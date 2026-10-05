using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Monthly auto-renewal of website plans through a BOG saved card.
///
///  1. The first payment is a normal BOG order. Before the customer is sent
///     to the bank page the order is marked for automatic payments
///     (PUT /payments/v1/orders/{id}/subscriptions).
///  2. When that order is paid a WebSubscriptions row is created.
///  3. A day before the paid period ends the server charges the saved card
///     (POST /payments/v1/ecommerce/orders/{parent}/subscribe). BOG uses the
///     amount of the first order.
///  4. A failed charge is retried after 2 and 5 days. During that time PRO
///     stays on for <see cref="GraceDays"/> days after the period end. After
///     the last failed try the subscription ends and the card is deleted.
///  5. Cancelling stops the charges and deletes the saved card at BOG; the
///     paid period is kept.
///
/// Status values: active, past_due, canceled, ended.
/// </summary>
public sealed partial class WebPaymentService
{
    /// <summary>The renewal is charged this many days before the period ends.</summary>
    public const int RenewDaysBefore = 1;

    /// <summary>PRO keeps working this many days after the period end while retrying.</summary>
    public const int GraceDays = 7;

    /// <summary>Days to wait before the 2nd and 3rd try.</summary>
    private static readonly int[] RetryAfterDays = { 2, 3 };

    private const int MaxAttempts = 3;

    private static void CreateSubscriptionSchema(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS WebSubscriptions (
SubscriptionId TEXT PRIMARY KEY,
UserId TEXT NOT NULL,
ProductId TEXT NOT NULL,
Provider TEXT NOT NULL,
ParentOrderId TEXT NOT NULL,
ParentPaymentId TEXT NOT NULL,
Amount TEXT NOT NULL,
Status TEXT NOT NULL,
PeriodEnd TEXT NOT NULL,
NextChargeAt TEXT NULL,
Attempts INTEGER NOT NULL DEFAULT 0,
PendingOrderId TEXT NULL,
CardMask TEXT NULL,
LastError TEXT NULL,
CreatedAt TEXT NOT NULL,
CanceledAt TEXT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS UX_WebSubscriptions_Parent ON WebSubscriptions(ParentOrderId);
CREATE INDEX IF NOT EXISTS IX_WebSubscriptions_User ON WebSubscriptions(UserId, Status);
CREATE INDEX IF NOT EXISTS IX_WebSubscriptions_Due ON WebSubscriptions(Status, NextChargeAt);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>True when the website may offer the monthly automatic payment.</summary>
    public bool AutoRenewAvailable =>
        IsConfigured("bog") &&
        !string.Equals(Setting("Payments:Bog:AutoRenew"), "false", StringComparison.OrdinalIgnoreCase);

    // ============================================================
    // BOG CALLS
    // ============================================================

    private async Task<HttpRequestMessage?> BogRequestAsync(HttpMethod method, string url, CancellationToken ct, string? idempotencyKey = null)
    {
        var token = await BogTokenAsync(ct);
        if (token is null)
            return null;

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return request;
    }

    /// <summary>Marks a new BOG order so its card can be charged automatically later.</summary>
    private async Task<bool> EnableBogSavedCardAsync(string bogOrderId, CancellationToken ct)
    {
        using var request = await BogRequestAsync(HttpMethod.Put,
            "https://api.bog.ge/payments/v1/orders/" + Uri.EscapeDataString(bogOrderId) + "/subscriptions", ct);
        if (request is null)
            return false;

        using var response = await _clients.CreateClient().SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return true;

        _log.LogWarning("BOG saved-card request for {Order} answered {Status}", bogOrderId, (int)response.StatusCode);
        return false;
    }

    /// <summary>Deletes the saved card at BOG (best effort).</summary>
    private async Task DeleteBogSavedCardAsync(string bogOrderId, CancellationToken ct)
    {
        try
        {
            using var request = await BogRequestAsync(HttpMethod.Delete,
                "https://api.bog.ge/payments/v1/charges/card/" + Uri.EscapeDataString(bogOrderId), ct);
            if (request is null)
                return;

            using var response = await _clients.CreateClient().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                _log.LogWarning("BOG card delete for {Order} answered {Status}", bogOrderId, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning("BOG card delete for {Order} failed: {Message}", bogOrderId, ex.Message);
        }
    }

    /// <summary>"Mastercard •••• 4417" from the BOG receipt, or null.</summary>
    private async Task<string?> ReadBogCardMaskAsync(string bogOrderId, CancellationToken ct)
    {
        try
        {
            using var request = await BogRequestAsync(HttpMethod.Get,
                "https://api.bog.ge/payments/v1/receipt/" + Uri.EscapeDataString(bogOrderId), ct);
            if (request is null)
                return null;

            using var response = await _clients.CreateClient().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            var detail = node?["payment_detail"];
            var pan = detail?["payer_identifier"]?.GetValue<string>() ?? string.Empty;
            var digits = new string(pan.Where(char.IsDigit).ToArray());
            if (digits.Length < 4)
                return null;

            var type = (detail?["card_type"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant();
            var brand = type switch
            {
                "mc" or "mastercard" => "Mastercard",
                "visa" => "Visa",
                "amex" => "Amex",
                _ => pan.StartsWith('4') ? "Visa" : pan.StartsWith('5') || pan.StartsWith('2') ? "Mastercard" : "Card"
            };
            return $"{brand} •••• {digits[^4..]}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // ============================================================
    // START / CANCEL
    // ============================================================

    /// <summary>
    /// Creates the auto-renewal after the first recurring order was paid
    /// and applied. Only one auto-renewal per account: older ones are
    /// cancelled (their paid time is kept).
    /// </summary>
    public async Task StartSubscriptionAsync(WebOrder order, DateTimeOffset periodEnd, CancellationToken ct)
    {
        if (!order.Recurring || order.Status != "paid" || order.ParentOrderId is not null ||
            string.IsNullOrWhiteSpace(order.ProviderPaymentId) || order.Provider != "bog")
            return;

        if (await GetSubscriptionByParentAsync(order.OrderId, ct) is not null)
            return;

        foreach (var old in await ListOpenSubscriptionsAsync(order.UserId, ct))
            await CancelCoreAsync(old, ct);

        var card = await ReadBogCardMaskAsync(order.ProviderPaymentId, ct);

        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT OR IGNORE INTO WebSubscriptions
(SubscriptionId,UserId,ProductId,Provider,ParentOrderId,ParentPaymentId,Amount,Status,PeriodEnd,NextChargeAt,Attempts,CardMask,CreatedAt,Currency)
VALUES($id,$u,$p,'bog',$po,$pp,$a,'active',$end,$next,0,$card,$at,$cur)";
            cmd.Parameters.AddWithValue("$cur", order.Currency);
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$u", order.UserId);
            cmd.Parameters.AddWithValue("$p", order.ProductId);
            cmd.Parameters.AddWithValue("$po", order.OrderId);
            cmd.Parameters.AddWithValue("$pp", order.ProviderPaymentId);
            cmd.Parameters.AddWithValue("$a", order.Amount.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$end", periodEnd.ToString("O"));
            cmd.Parameters.AddWithValue("$next", periodEnd.AddDays(-RenewDaysBefore).ToString("O"));
            cmd.Parameters.AddWithValue("$card", (object?)card ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Cancels the account's auto-renewal. The paid period is kept.</summary>
    public async Task<bool> CancelSubscriptionAsync(string userId, CancellationToken ct)
    {
        var open = await ListOpenSubscriptionsAsync(userId, ct);
        foreach (var sub in open)
            await CancelCoreAsync(sub, ct);
        return open.Count > 0;
    }

    private async Task CancelCoreAsync(WebSubscription sub, CancellationToken ct)
    {
        await RunAsync(@"UPDATE WebSubscriptions SET Status='canceled', NextChargeAt=NULL, CanceledAt=$now
WHERE SubscriptionId=$id AND Status IN ('active','past_due')", ct,
            ("$id", sub.SubscriptionId), ("$now", DateTimeOffset.UtcNow.ToString("O")));
        await DeleteBogSavedCardAsync(sub.ParentPaymentId, ct);
    }

    /// <summary>"Pay now" on the website for a past-due subscription.</summary>
    public async Task<bool> RetryNowAsync(string userId, EntitlementStore store, CancellationToken ct)
    {
        var sub = (await ListOpenSubscriptionsAsync(userId, ct)).FirstOrDefault(s => s.Status == "past_due");
        if (sub is null || sub.PendingOrderId is not null)
            return false;

        await ChargeAsync(sub, store, ct);
        return true;
    }

    // ============================================================
    // RENEWAL RUN (background worker)
    // ============================================================

    /// <summary>Charges every due subscription and settles open renewals.</summary>
    public async Task ProcessRenewalsAsync(EntitlementStore store, CancellationToken ct)
    {
        if (!IsConfigured("bog"))
            return;

        // 1. Renewals that are still waiting for the bank answer.
        foreach (var sub in await QuerySubscriptionsAsync("PendingOrderId IS NOT NULL", ct))
        {
            var order = await RefreshAsync(sub.PendingOrderId!, ct);
            if (order is null)
            {
                await RunAsync("UPDATE WebSubscriptions SET PendingOrderId=NULL WHERE SubscriptionId=$id", ct, ("$id", sub.SubscriptionId));
                continue;
            }

            if (order.Status == "created" && await OrderAgeAsync(order.OrderId, ct) > TimeSpan.FromHours(2))
            {
                await SetStatusAsync(order.OrderId, "failed", null, ct);
                order = order with { Status = "failed" };
            }

            await HandleRenewalOrderAsync(order, store, ct);
        }

        // 2. Subscriptions that are due now.
        foreach (var sub in await QuerySubscriptionsAsync(
                     "Status IN ('active','past_due') AND PendingOrderId IS NULL AND NextChargeAt IS NOT NULL AND NextChargeAt<=$now", ct,
                     ("$now", DateTimeOffset.UtcNow.ToString("O"))))
        {
            ct.ThrowIfCancellationRequested();
            await ChargeAsync(sub, store, ct);
        }
    }

    private async Task ChargeAsync(WebSubscription sub, EntitlementStore store, CancellationToken ct)
    {
        var orderId = Guid.NewGuid().ToString("N");
        await InsertOrderAsync(orderId, sub.UserId, sub.ProductId, "bog", sub.Amount, ct, parentOrderId: sub.ParentOrderId, currency: sub.Currency);

        // Claim the subscription so two runs never charge it twice.
        var claimed = await RunAsync(
            "UPDATE WebSubscriptions SET PendingOrderId=$o WHERE SubscriptionId=$id AND PendingOrderId IS NULL AND Status IN ('active','past_due')",
            ct, ("$o", orderId), ("$id", sub.SubscriptionId));
        if (claimed != 1)
        {
            await SetStatusAsync(orderId, "failed", null, ct);
            return;
        }

        var publicBase = Setting("Payments:PublicBaseUrl")?.TrimEnd('/');
        string? bogId = null;
        try
        {
            var body = new Dictionary<string, object> { ["external_order_id"] = orderId };
            if (publicBase is not null && publicBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                body["callback_url"] = publicBase + "/api/web/payments/bog/callback";

            using var request = await BogRequestAsync(HttpMethod.Post,
                "https://api.bog.ge/payments/v1/ecommerce/orders/" + Uri.EscapeDataString(sub.ParentPaymentId) + "/subscribe",
                ct, Guid.ParseExact(orderId, "N").ToString("D"));
            if (request is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var response = await _clients.CreateClient().SendAsync(request, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    bogId = JsonNode.Parse(raw)?["id"]?.GetValue<string>();
                else
                    _log.LogWarning("BOG renewal for {Sub} answered {Status}", sub.SubscriptionId, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _log.LogWarning("BOG renewal for {Sub} failed: {Message}", sub.SubscriptionId, ex.Message);
        }

        if (bogId is null)
        {
            await SetStatusAsync(orderId, "failed", null, ct);
            await HandleRenewalOrderAsync((await GetOrderAsync(orderId, ct))!, store, ct);
            return;
        }

        await SetProviderPaymentIdAsync(orderId, bogId, ct);
        var order = await RefreshAsync(orderId, ct);
        if (order is not null)
            await HandleRenewalOrderAsync(order, store, ct);
    }

    /// <summary>
    /// Settles a renewal order (from the worker or the bank callback):
    /// paid → 30 more days; failed → retry later or end.
    /// </summary>
    public async Task HandleRenewalOrderAsync(WebOrder order, EntitlementStore store, CancellationToken ct)
    {
        if (order.ParentOrderId is null || order.Status is not ("paid" or "failed"))
            return;

        var sub = await GetSubscriptionByParentAsync(order.ParentOrderId, ct);
        if (sub is null || sub.PendingOrderId != order.OrderId)
            return;

        var now = DateTimeOffset.UtcNow;
        if (order.Status == "paid")
        {
            if (!await TryMarkAppliedAsync(order.OrderId, ct))
                return;

            var newEnd = sub.PeriodEnd.AddDays(PlanDays);
            if (newEnd < now)
                newEnd = now.AddDays(PlanDays);

            await store.UpsertSubscriptionAsync(
                new EntitlementRecord(sub.UserId, true, "web", sub.ProductId, newEnd, now, "web:" + order.OrderId), ct);

            var keepRenewing = sub.Status is "active" or "past_due";
            await RunAsync(@"UPDATE WebSubscriptions SET Status=$st, PeriodEnd=$end, NextChargeAt=$next, Attempts=0,
PendingOrderId=NULL, LastError=NULL WHERE SubscriptionId=$id", ct,
                ("$st", keepRenewing ? "active" : sub.Status),
                ("$end", newEnd.ToString("O")),
                ("$next", keepRenewing ? newEnd.AddDays(-RenewDaysBefore).ToString("O") : null),
                ("$id", sub.SubscriptionId));
            return;
        }

        // Failed charge.
        if (sub.Status is not ("active" or "past_due"))
        {
            await RunAsync("UPDATE WebSubscriptions SET PendingOrderId=NULL WHERE SubscriptionId=$id AND PendingOrderId=$o", ct,
                ("$id", sub.SubscriptionId), ("$o", order.OrderId));
            return;
        }

        var attempts = sub.Attempts + 1;
        if (attempts == 1)
        {
            // Grace period: PRO keeps working while the card is retried.
            var graceEnd = sub.PeriodEnd.AddDays(GraceDays);
            var current = await store.GetSubscriptionExpiryAsync(sub.UserId, sub.ProductId, ct);
            if (current is null || current < graceEnd)
                await store.UpsertSubscriptionAsync(
                    new EntitlementRecord(sub.UserId, true, "web", sub.ProductId, graceEnd, now, "web:grace:" + sub.SubscriptionId), ct);
        }

        if (attempts >= MaxAttempts)
        {
            var ended = await RunAsync(@"UPDATE WebSubscriptions SET Status='ended', NextChargeAt=NULL, Attempts=$n, PendingOrderId=NULL,
LastError='payment_failed' WHERE SubscriptionId=$id AND PendingOrderId=$o", ct,
                ("$n", attempts.ToString(CultureInfo.InvariantCulture)), ("$id", sub.SubscriptionId), ("$o", order.OrderId));
            if (ended == 1)
                await DeleteBogSavedCardAsync(sub.ParentPaymentId, ct);
            return;
        }

        await RunAsync(@"UPDATE WebSubscriptions SET Status='past_due', NextChargeAt=$next, Attempts=$n, PendingOrderId=NULL,
LastError='payment_failed' WHERE SubscriptionId=$id AND PendingOrderId=$o", ct,
            ("$next", now.AddDays(RetryAfterDays[attempts - 1]).ToString("O")),
            ("$n", attempts.ToString(CultureInfo.InvariantCulture)),
            ("$id", sub.SubscriptionId),
            ("$o", order.OrderId));
    }

    // ============================================================
    // STATUS (website "My subscription" page and the app)
    // ============================================================

    public async Task<WebSubscriptionStatus> GetSubscriptionStatusAsync(string userId, EntitlementStore store, CancellationToken ct)
    {
        var subs = await QuerySubscriptionsAsync("UserId=$u ORDER BY CreatedAt DESC LIMIT 1", ct, ("$u", userId));
        var sub = subs.FirstOrDefault();

        DateTimeOffset? activeUntil = sub is null ? null : await store.GetSubscriptionExpiryAsync(userId, sub.ProductId, ct);
        var history = await GetPaymentHistoryAsync(userId, ct);

        return new WebSubscriptionStatus(
            sub is not null,
            sub?.ProductId,
            sub?.Amount,
            sub?.Status,
            sub is { Status: "active" or "past_due" } ? sub.NextChargeAt : null,
            activeUntil,
            sub?.CardMask,
            sub?.Currency ?? history.FirstOrDefault()?.Currency ?? "GEL",
            sub?.Status is "active" or "past_due",
            sub?.Status == "past_due",
            history);
    }

    private async Task<IReadOnlyList<WebPaymentLine>> GetPaymentHistoryAsync(string userId, CancellationToken ct)
    {
        var lines = new List<WebPaymentLine>();
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT ProductId,Amount,Status,COALESCE(PaidAt,CreatedAt),Currency FROM WebOrders
WHERE UserId=$u AND (Status IN ('paid','refunded') OR (Status='failed' AND ParentOrderId IS NOT NULL))
ORDER BY CreatedAt DESC LIMIT 12";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                DateTimeOffset.TryParse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at);
                lines.Add(new WebPaymentLine(
                    r.GetString(0),
                    decimal.Parse(r.GetString(1), CultureInfo.InvariantCulture),
                    r.GetString(2),
                    at,
                    r.IsDBNull(4) ? "GEL" : r.GetString(4)));
            }
        }
        finally { _gate.Release(); }
        return lines;
    }

    // ============================================================
    // DATA
    // ============================================================

    private async Task<WebSubscription?> GetSubscriptionByParentAsync(string parentOrderId, CancellationToken ct) =>
        (await QuerySubscriptionsAsync("ParentOrderId=$p", ct, ("$p", parentOrderId))).FirstOrDefault();

    private async Task<IReadOnlyList<WebSubscription>> ListOpenSubscriptionsAsync(string userId, CancellationToken ct) =>
        await QuerySubscriptionsAsync("UserId=$u AND Status IN ('active','past_due') ORDER BY CreatedAt DESC", ct, ("$u", userId));

    private async Task<IReadOnlyList<WebSubscription>> QuerySubscriptionsAsync(
        string where, CancellationToken ct, params (string Name, string? Value)[] args)
    {
        var list = new List<WebSubscription>();
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT SubscriptionId,UserId,ProductId,ParentOrderId,ParentPaymentId,Amount,Status,PeriodEnd,
NextChargeAt,Attempts,PendingOrderId,CardMask,Currency FROM WebSubscriptions WHERE " + where;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new WebSubscription(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    decimal.Parse(r.GetString(5), CultureInfo.InvariantCulture),
                    r.GetString(6),
                    ParseDate(r.GetString(7)) ?? DateTimeOffset.UtcNow,
                    r.IsDBNull(8) ? null : ParseDate(r.GetString(8)),
                    r.GetInt32(9),
                    r.IsDBNull(10) ? null : r.GetString(10),
                    r.IsDBNull(11) ? null : r.GetString(11),
                    r.IsDBNull(12) ? "GEL" : r.GetString(12)));
            }
        }
        finally { _gate.Release(); }
        return list;
    }

    private async Task<TimeSpan> OrderAgeAsync(string orderId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CreatedAt FROM WebOrders WHERE OrderId=$o";
            cmd.Parameters.AddWithValue("$o", orderId);
            return ParseDate(cmd.ExecuteScalar() as string) is { } at ? DateTimeOffset.UtcNow - at : TimeSpan.Zero;
        }
        finally { _gate.Release(); }
    }

    private async Task<int> RunAsync(string sql, CancellationToken ct, params (string Name, string? Value)[] args)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
}

public sealed record WebSubscription(
    string SubscriptionId,
    string UserId,
    string ProductId,
    string ParentOrderId,
    string ParentPaymentId,
    decimal Amount,
    string Status,
    DateTimeOffset PeriodEnd,
    DateTimeOffset? NextChargeAt,
    int Attempts,
    string? PendingOrderId,
    string? CardMask,
    string Currency = "GEL");

public sealed record WebPaymentLine(string ProductId, decimal Amount, string Status, DateTimeOffset At, string Currency = "GEL");

public sealed record WebSubscriptionStatus(
    bool HasSubscription,
    string? ProductId,
    decimal? Amount,
    string? Status,
    DateTimeOffset? NextChargeAt,
    DateTimeOffset? ActiveUntil,
    string? CardMask,
    string Currency,
    bool AutoRenew,
    bool PaymentFailed,
    IReadOnlyList<WebPaymentLine> History);

/// <summary>Runs the monthly renewals every 20 minutes.</summary>
public sealed class WebRenewalWorker : BackgroundService
{
    private readonly WebPaymentService _payments;
    private readonly EntitlementStore _store;
    private readonly ILogger<WebRenewalWorker> _log;

    public WebRenewalWorker(WebPaymentService payments, EntitlementStore store, ILogger<WebRenewalWorker> log)
    {
        _payments = payments;
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(20));
        do
        {
            try
            {
                await _payments.ProcessRenewalsAsync(_store, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Web renewal run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
