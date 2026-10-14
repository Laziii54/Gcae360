using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Card payments on the OBD360 web page through a Georgian bank:
///  • TBC  – TBC Checkout (tpay),
///  • BOG  – Bank of Georgia Payments.
/// The bank is chosen with Payments:Provider (tbc / bog). Credentials
/// are read only from the server configuration.
///
/// A payment is never trusted from the browser or from the callback
/// body: the order status is always read back from the bank API.
/// </summary>
public sealed partial class WebPaymentService
{
    /// <summary>Days of access bought with one "monthly" web purchase.</summary>
    public const int PlanDays = 30;

    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _config;
    private readonly ILogger<WebPaymentService> _log;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new();

    public WebPaymentService(
        IHttpClientFactory clients,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<WebPaymentService> log)
    {
        _clients = clients;
        _config = config;
        _log = log;
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS WebOrders (
OrderId TEXT PRIMARY KEY,
UserId TEXT NOT NULL,
ProductId TEXT NOT NULL,
Provider TEXT NOT NULL,
ProviderPaymentId TEXT NULL,
Amount TEXT NOT NULL,
Status TEXT NOT NULL,
Applied INTEGER NOT NULL DEFAULT 0,
CreatedAt TEXT NOT NULL,
PaidAt TEXT NULL);
CREATE INDEX IF NOT EXISTS IX_WebOrders_Provider ON WebOrders(Provider, ProviderPaymentId);";
        cmd.ExecuteNonQuery();

        // Monthly auto-renewal (BOG saved card): columns added later.
        AddColumnIfMissing(c, "WebOrders", "Recurring", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "WebOrders", "ParentOrderId", "TEXT NULL");
        AddColumnIfMissing(c, "WebOrders", "Currency", "TEXT NOT NULL DEFAULT 'GEL'");
        CreateSubscriptionSchema(c);
        AddColumnIfMissing(c, "WebSubscriptions", "Currency", "TEXT NOT NULL DEFAULT 'GEL'");
    }

    private static void AddColumnIfMissing(SqliteConnection c, string table, string column, string definition)
    {
        using var check = c.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$n";
        check.Parameters.AddWithValue("$n", column);
        if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            return;

        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    // ============================================================
    // PRODUCTS / PRICES (GEL)
    // ============================================================

    public static decimal? PriceFor(string? productId) => productId switch
    {
        ProductCatalog.ObdMonthly => 4.99m,
        ProductCatalog.VinMonthly => 5.99m,
        ProductCatalog.FullMonthly => 8.99m,
        ProductCatalog.VinPack5 => 3.99m,
        ProductCatalog.VinPack10 => 6.99m,
        ProductCatalog.VinSingle => 2.99m,
        _ => null
    };

    /// <summary>
    /// Website prices in US dollars for visitors outside Georgia (charged in
    /// USD through BOG). Same numbers as in lari, set for foreign markets.
    /// </summary>
    public static decimal? UsdPriceFor(string? productId) => productId switch
    {
        ProductCatalog.ObdMonthly => 4.99m,
        ProductCatalog.VinMonthly => 5.99m,
        ProductCatalog.FullMonthly => 8.99m,
        ProductCatalog.VinPack5 => 3.99m,
        ProductCatalog.VinPack10 => 6.99m,
        ProductCatalog.VinSingle => 2.99m,
        _ => null
    };

    public static decimal? PriceFor(string? productId, string currency) =>
        currency == "USD" ? UsdPriceFor(productId) : PriceFor(productId);

    /// <summary>"USD" or "GEL" (anything else is lari).</summary>
    public static string NormalizeCurrency(string? currency) =>
        string.Equals(currency?.Trim(), "USD", StringComparison.OrdinalIgnoreCase) ? "USD" : "GEL";

    public static string FormatPrice(decimal amount, string currency) =>
        currency == "USD"
            ? "$" + amount.ToString("0.00", CultureInfo.InvariantCulture)
            : amount.ToString("0.00", CultureInfo.InvariantCulture) + " ₾";

    // ============================================================
    // CONFIG
    // ============================================================

    private string? Setting(string key)
    {
        var value = _config[key];
        return string.IsNullOrWhiteSpace(value) || value == "SET_ON_SERVER_ONLY" ? null : value.Trim();
    }

    public bool IsConfigured(string provider) => provider switch
    {
        "tbc" => Setting("Payments:Tbc:ApiKey") is not null &&
                 Setting("Payments:Tbc:ClientId") is not null &&
                 Setting("Payments:Tbc:ClientSecret") is not null,
        "bog" => Setting("Payments:Bog:ClientId") is not null &&
                 Setting("Payments:Bog:ClientSecret") is not null,
        _ => false
    };

    /// <summary>Banks that are configured, the default one first.</summary>
    public IReadOnlyList<string> AvailableProviders
    {
        get
        {
            var preferred = (Setting("Payments:Provider") ?? "tbc").ToLowerInvariant();
            return new[] { preferred, "tbc", "bog" }
                .Distinct()
                .Where(IsConfigured)
                .ToList();
        }
    }

    // ============================================================
    // CHECKOUT
    // ============================================================

    public async Task<CheckoutResult> CreateCheckoutAsync(
        string userId,
        string productId,
        string provider,
        string publicBaseUrl,
        CancellationToken ct,
        decimal? priceOverride = null,
        string resultPath = "/pro/result",
        string description = "GCar360 PRO",
        bool recurring = false,
        string currency = "GEL")
    {
        currency = NormalizeCurrency(currency);
        var price = priceOverride ?? PriceFor(productId, currency);
        // Dollar payments go through BOG only.
        if (price is null || !IsConfigured(provider) || (currency == "USD" && provider != "bog"))
            return CheckoutResult.Fail("NOT_AVAILABLE");

        var orderId = Guid.NewGuid().ToString("N");
        var returnUrl = $"{publicBaseUrl}{resultPath}?order={orderId}";
        var callbackUrl = $"{publicBaseUrl}/api/web/payments/{provider}/callback";

        // Auto-renewal only for monthly plans paid through BOG.
        recurring = recurring && provider == "bog" && ProductCatalog.IsSubscription(productId);
        await InsertOrderAsync(orderId, userId, productId, provider, price.Value, ct, recurring, currency: currency);

        try
        {
            var (paymentId, redirect) = provider == "tbc"
                ? await CreateTbcAsync(orderId, price.Value, returnUrl, callbackUrl, description, ct)
                : await CreateBogAsync(orderId, productId, price.Value, returnUrl, callbackUrl, ct, currency);

            if (paymentId is null || redirect is null)
            {
                await SetStatusAsync(orderId, "failed", null, ct);
                return CheckoutResult.Fail("BANK_UNAVAILABLE");
            }

            await SetProviderPaymentIdAsync(orderId, paymentId, ct);

            // The card must be marked for automatic payments before the
            // customer is sent to the bank page.
            if (recurring && !await EnableBogSavedCardAsync(paymentId, ct))
            {
                await SetStatusAsync(orderId, "failed", null, ct);
                return CheckoutResult.Fail("AUTO_RENEW_UNAVAILABLE");
            }

            return new CheckoutResult(true, orderId, redirect, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning("Checkout with {Provider} failed: {Message}", provider, ex.Message);
            await SetStatusAsync(orderId, "failed", null, ct);
            return CheckoutResult.Fail("BANK_UNAVAILABLE");
        }
    }

    /// <summary>Reads the order status from the bank and stores it.</summary>
    public async Task<WebOrder?> RefreshAsync(string orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order is null || order.Status is "paid" or "refunded" || string.IsNullOrWhiteSpace(order.ProviderPaymentId))
            return order;

        try
        {
            var (status, amount) = order.Provider == "tbc"
                ? await ReadTbcStatusAsync(order.ProviderPaymentId, ct)
                : await ReadBogStatusAsync(order.ProviderPaymentId, ct);

            string? newStatus = status switch
            {
                "paid" when amount is not null && amount.Value >= order.Amount => "paid",
                "paid" => "failed", // amount does not match the order
                "failed" => "failed",
                _ => null
            };

            if (newStatus is not null && newStatus != order.Status)
            {
                await SetStatusAsync(orderId, newStatus, newStatus == "paid" ? DateTimeOffset.UtcNow : null, ct);
                order = order with { Status = newStatus };
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning("Status check for order {Order} failed: {Message}", orderId, ex.Message);
        }

        return order;
    }

    // ============================================================
    // TBC CHECKOUT (tpay)
    // ============================================================

    private const string TbcBase = "https://api.tbcbank.ge/v1/tpay";

    private async Task<string?> TbcTokenAsync(CancellationToken ct) =>
        await CachedTokenAsync("tbc", async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TbcBase + "/access-token");
            request.Headers.Add("apikey", Setting("Payments:Tbc:ApiKey"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Setting("Payments:Tbc:ClientId") ?? "",
                ["client_secret"] = Setting("Payments:Tbc:ClientSecret") ?? ""
            });
            return await client.SendAsync(request, ct);
        }, ct);

    private async Task<HttpRequestMessage> TbcRequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var token = await TbcTokenAsync(ct) ?? throw new HttpRequestException("TBC token unavailable");
        var request = new HttpRequestMessage(method, TbcBase + path);
        request.Headers.Add("apikey", Setting("Payments:Tbc:ApiKey"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<(string? PaymentId, string? Redirect)> CreateTbcAsync(
        string orderId, decimal price, string returnUrl, string callbackUrl, string description, CancellationToken ct)
    {
        var body = new
        {
            amount = new { currency = "GEL", total = price, subTotal = price, tax = 0, shipping = 0 },
            returnurl = returnUrl,
            callbackUrl,
            merchantPaymentId = orderId,
            expirationMinutes = 15,
            language = "KA",
            description
        };

        using var request = await TbcRequestAsync(HttpMethod.Post, "/payments", ct);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _clients.CreateClient().SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("TBC create payment answered {Status}", (int)response.StatusCode);
            return (null, null);
        }

        var node = JsonNode.Parse(raw);
        var payId = node?["payId"]?.GetValue<string>();
        string? redirect = null;
        if (node?["links"] is JsonArray links)
        {
            redirect = links
                .OfType<JsonObject>()
                .FirstOrDefault(link => string.Equals(link["rel"]?.GetValue<string>(), "approval_url", StringComparison.OrdinalIgnoreCase))
                ?["uri"]?.GetValue<string>();
            redirect ??= links
                .OfType<JsonObject>()
                .FirstOrDefault(link => string.Equals(link["rel"]?.GetValue<string>(), "approval_url", StringComparison.OrdinalIgnoreCase))
                ?["href"]?.GetValue<string>();
        }

        return (payId, redirect);
    }

    private async Task<(string Status, decimal? Amount)> ReadTbcStatusAsync(string payId, CancellationToken ct)
    {
        using var request = await TbcRequestAsync(HttpMethod.Get, "/payments/" + Uri.EscapeDataString(payId), ct);
        using var response = await _clients.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return ("pending", null);

        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var status = node?["status"]?.GetValue<string>();
        var amount = Decimal(node?["amount"]);
        return status switch
        {
            "Succeeded" => ("paid", amount),
            "Failed" or "Expired" or "Returned" => ("failed", amount),
            _ => ("pending", amount)
        };
    }

    // ============================================================
    // BANK OF GEORGIA PAYMENTS
    // ============================================================

    private async Task<string?> BogTokenAsync(CancellationToken ct) =>
        await CachedTokenAsync("bog", async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{Setting("Payments:Bog:ClientId")}:{Setting("Payments:Bog:ClientSecret")}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            return await client.SendAsync(request, ct);
        }, ct);

    private async Task<(string? PaymentId, string? Redirect)> CreateBogAsync(
        string orderId, string productId, decimal price, string returnUrl, string callbackUrl, CancellationToken ct,
        string currency = "GEL")
    {
        var token = await BogTokenAsync(ct);
        if (token is null)
            return (null, null);

        var body = new
        {
            callback_url = callbackUrl,
            external_order_id = orderId,
            purchase_units = new
            {
                currency,
                total_amount = price,
                basket = new[] { new { product_id = productId, quantity = 1, unit_price = price } }
            },
            redirect_urls = new { success = returnUrl, fail = returnUrl },
            ttl = 15
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.bog.ge/payments/v1/ecommerce/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Bank page in English for foreign (dollar) customers.
        request.Headers.Add("Accept-Language", currency == "USD" ? "en" : "ka");
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _clients.CreateClient().SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("BOG create order answered {Status}", (int)response.StatusCode);
            return (null, null);
        }

        var node = JsonNode.Parse(raw);
        return (node?["id"]?.GetValue<string>(), node?["_links"]?["redirect"]?["href"]?.GetValue<string>());
    }

    private async Task<(string Status, decimal? Amount)> ReadBogStatusAsync(string bogOrderId, CancellationToken ct)
    {
        var token = await BogTokenAsync(ct);
        if (token is null)
            return ("pending", null);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.bog.ge/payments/v1/receipt/" + Uri.EscapeDataString(bogOrderId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _clients.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return ("pending", null);

        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var status = node?["order_status"]?["key"]?.GetValue<string>();
        var amount = Decimal(node?["purchase_units"]?["transfer_amount"]) ?? Decimal(node?["purchase_units"]?["request_amount"]);
        return status switch
        {
            "completed" => ("paid", amount),
            "rejected" or "refunded" or "refunded_partially" => ("failed", amount),
            _ => ("pending", amount)
        };
    }

    // ============================================================
    // REFUND (owner, from the admin page)
    // ============================================================

    /// <summary>
    /// Returns the whole amount of a paid order to the card through the
    /// bank API. The order is marked "refunded" only when the bank accepts.
    /// </summary>
    public async Task<(bool Ok, string Message)> RefundAsync(string orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order is null)
            return (false, "შეკვეთა ვერ მოიძებნა");
        if (order.Status != "paid")
            return (false, "დაბრუნება შეიძლება მხოლოდ გადახდილ შეკვეთაზე");
        if (string.IsNullOrWhiteSpace(order.ProviderPaymentId))
            return (false, "ბანკის გადახდის ნომერი არ არის");

        try
        {
            HttpResponseMessage response;
            if (order.Provider == "bog")
            {
                var token = await BogTokenAsync(ct);
                if (token is null)
                    return (false, "BOG-ის ავტორიზაცია ვერ მოხერხდა");
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://api.bog.ge/payments/v1/payment/refund/" + Uri.EscapeDataString(order.ProviderPaymentId));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { amount = order.Amount }), Encoding.UTF8, "application/json");
                response = await _clients.CreateClient().SendAsync(request, ct);
            }
            else
            {
                using var request = await TbcRequestAsync(HttpMethod.Post,
                    "/payments/" + Uri.EscapeDataString(order.ProviderPaymentId) + "/cancel", ct);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { amount = order.Amount }), Encoding.UTF8, "application/json");
                response = await _clients.CreateClient().SendAsync(request, ct);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _log.LogWarning("Refund of {Order} answered {Status}: {Body}", orderId, (int)response.StatusCode,
                        body.Length > 300 ? body[..300] : body);
                    return (false, $"ბანკმა უარი თქვა ({(int)response.StatusCode})");
                }
            }

            await ExecuteAsync("UPDATE WebOrders SET Status=$v WHERE OrderId=$o", orderId, "refunded", ct);
            return (true, "თანხა დაბრუნდა");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning("Refund of {Order} failed: {Message}", orderId, ex.Message);
            return (false, "ბანკთან კავშირი ვერ მოხერხდა");
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private async Task<string?> CachedTokenAsync(
        string provider,
        Func<HttpClient, Task<HttpResponseMessage>> fetch,
        CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_tokens.TryGetValue(provider, out var cached) && cached.Expires > DateTimeOffset.UtcNow.AddMinutes(1))
                return cached.Token;

            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            using var response = await fetch(client);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("{Provider} token request answered {Status}", provider, (int)response.StatusCode);
                return null;
            }

            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            var token = node?["access_token"]?.GetValue<string>();
            var seconds = Decimal(node?["expires_in"]) ?? 300;
            if (string.IsNullOrWhiteSpace(token))
                return null;

            _tokens[provider] = (token, DateTimeOffset.UtcNow.AddSeconds((double)seconds));
            return token;
        }
        finally { _tokenGate.Release(); }
    }

    private static decimal? Decimal(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<decimal>(out var d)) return d;
        if (value.TryGetValue<double>(out var f)) return (decimal)f;
        if (value.TryGetValue<string>(out var s) &&
            decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    // ============================================================
    // ORDERS
    // ============================================================

    private async Task InsertOrderAsync(
        string orderId, string userId, string productId, string provider, decimal amount, CancellationToken ct,
        bool recurring = false, string? parentOrderId = null, string currency = "GEL")
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO WebOrders(OrderId,UserId,ProductId,Provider,Amount,Status,Applied,CreatedAt,Recurring,ParentOrderId,Currency)
VALUES($o,$u,$p,$pr,$a,'created',0,$at,$rc,$po,$cur)";
            cmd.Parameters.AddWithValue("$cur", NormalizeCurrency(currency));
            cmd.Parameters.AddWithValue("$rc", recurring ? 1 : 0);
            cmd.Parameters.AddWithValue("$po", (object?)parentOrderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$o", orderId);
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$p", productId);
            cmd.Parameters.AddWithValue("$pr", provider);
            cmd.Parameters.AddWithValue("$a", amount.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    private Task SetProviderPaymentIdAsync(string orderId, string paymentId, CancellationToken ct) =>
        ExecuteAsync("UPDATE WebOrders SET ProviderPaymentId=$v WHERE OrderId=$o", orderId, paymentId, ct);

    private async Task SetStatusAsync(string orderId, string status, DateTimeOffset? paidAt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE WebOrders SET Status=$s, PaidAt=COALESCE($at, PaidAt) WHERE OrderId=$o AND Status<>'paid'";
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$at", paidAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$o", orderId);
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Marks a paid order as applied. True only for the first caller.</summary>
    public async Task<bool> TryMarkAppliedAsync(string orderId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE WebOrders SET Applied=1 WHERE OrderId=$o AND Status='paid' AND Applied=0";
            cmd.Parameters.AddWithValue("$o", orderId);
            return cmd.ExecuteNonQuery() == 1;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Undo of <see cref="TryMarkAppliedAsync"/> when applying failed.</summary>
    public Task UnmarkAppliedAsync(string orderId, CancellationToken ct) =>
        ExecuteAsync("UPDATE WebOrders SET Applied=0 WHERE OrderId=$o AND Status=$v", orderId, "paid", ct);

    private async Task ExecuteAsync(string sql, string orderId, string value, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$o", orderId);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    /// <summary>The newest paid order of a user for a product, or null.</summary>
    public async Task<WebOrder?> FindPaidAsync(string userId, string productId, CancellationToken ct)
    {
        string? id;
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT OrderId FROM WebOrders WHERE UserId=$u AND ProductId=$p AND Status='paid' ORDER BY CreatedAt DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$p", productId);
            id = cmd.ExecuteScalar() as string;
        }
        finally { _gate.Release(); }
        return id is null ? null : await GetOrderAsync(id, ct);
    }

    /// <summary>The newest unfinished order of a user for a product (to re-check after the bank page).</summary>
    public async Task<WebOrder?> FindOpenAsync(string userId, string productId, CancellationToken ct)
    {
        string? id;
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT OrderId FROM WebOrders WHERE UserId=$u AND ProductId=$p AND Status='created' AND CreatedAt>$since ORDER BY CreatedAt DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$p", productId);
            cmd.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
            id = cmd.ExecuteScalar() as string;
        }
        finally { _gate.Release(); }
        return id is null ? null : await GetOrderAsync(id, ct);
    }

    public async Task<WebOrder?> GetOrderAsync(string orderId, CancellationToken ct) =>
        await QueryOrderAsync("OrderId=$v", orderId, ct);

    public async Task<WebOrder?> FindByProviderPaymentIdAsync(string provider, string paymentId, CancellationToken ct) =>
        await QueryOrderAsync("Provider='" + (provider == "bog" ? "bog" : "tbc") + "' AND ProviderPaymentId=$v", paymentId, ct);

    private async Task<WebOrder?> QueryOrderAsync(string where, string value, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT OrderId,UserId,ProductId,Provider,ProviderPaymentId,Amount,Status,Applied,Recurring,ParentOrderId,Currency FROM WebOrders WHERE " + where + " LIMIT 1";
            cmd.Parameters.AddWithValue("$v", value);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new WebOrder(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                decimal.Parse(r.GetString(5), CultureInfo.InvariantCulture),
                r.GetString(6),
                r.GetInt32(7) != 0,
                r.GetInt32(8) != 0,
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? "GEL" : r.GetString(10));
        }
        finally { _gate.Release(); }
    }
}

public sealed record WebOrder(
    string OrderId,
    string UserId,
    string ProductId,
    string Provider,
    string? ProviderPaymentId,
    decimal Amount,
    string Status,
    bool Applied,
    bool Recurring = false,
    string? ParentOrderId = null,
    string Currency = "GEL");

public sealed record CheckoutResult(bool Success, string? OrderId, string? RedirectUrl, string? Error)
{
    public static CheckoutResult Fail(string error) => new(false, null, null, error);
}
