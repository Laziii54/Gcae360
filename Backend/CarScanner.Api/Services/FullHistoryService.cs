using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Paid "full history" of a VIN from VinAudit (official US NMVTIS title
/// data: title records with odometer, title brands such as salvage /
/// flood / rebuilt, junk-salvage-insurance records).
///
/// Flow: query (free, tells whether VinAudit has the car) → the user pays
/// with a Georgian bank card → order (VinAudit charges us) → generate →
/// report. The report is kept per account, so a paid report can always be
/// opened again. A VinAudit order is recorded before it is placed, so a
/// retry never pays twice.
///
/// Settings (Azure): Vin__VinAuditKey, Vin__VinAuditUser, Vin__VinAuditPass,
/// optional Vin__VinAuditMode (= test while testing) and
/// GCS_FULL_REPORT_PRICE / Vin__FullReportPrice (GEL, default 6.19).
/// </summary>
public sealed class FullHistoryService
{
    public const string ProductPrefix = "vin_full:";
    private const string Api = "https://api.vinaudit.com/";
    private static readonly TimeSpan CheckCacheAge = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _config;
    private readonly ILogger<FullHistoryService> _log;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _dbGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _buyGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, FullAvailability Result)> _checks = new();

    public FullHistoryService(
        IHttpClientFactory clients,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<FullHistoryService> log)
    {
        _clients = clients;
        _config = config;
        _log = log;
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS FullReports (
AccountId TEXT NOT NULL,
Vin TEXT NOT NULL,
OrderId TEXT NOT NULL,
ProviderId TEXT NULL,
Status TEXT NOT NULL,
Json TEXT NULL,
CreatedAt TEXT NOT NULL,
PRIMARY KEY (AccountId, Vin));";
        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // CONFIG
    // ============================================================

    private string? Setting(string key, string? env = null)
    {
        var value = _config[key] ?? (env is null ? null : Environment.GetEnvironmentVariable(env));
        return string.IsNullOrWhiteSpace(value) || value == "SET_ON_SERVER_ONLY" ? null : value.Trim();
    }

    private string? Key => Setting("Vin:VinAuditKey", "GCS_VINAUDIT_KEY");
    private string? User => Setting("Vin:VinAuditUser", "GCS_VINAUDIT_USER");
    private string? Pass => Setting("Vin:VinAuditPass", "GCS_VINAUDIT_PASS");
    private string? Mode => Setting("Vin:VinAuditMode", "GCS_VINAUDIT_MODE");

    public bool IsConfigured => Key is not null && User is not null && Pass is not null;

    /// <summary>Price of one full report in GEL.</summary>
    public decimal Price
    {
        get
        {
            var raw = Setting("Vin:FullReportPrice", "GCS_FULL_REPORT_PRICE");
            return raw is not null &&
                   decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var price) &&
                   price is > 0 and < 1000
                ? Math.Round(price, 2)
                : 6.19m;
        }
    }

    public static string ProductFor(string vin) => ProductPrefix + vin;

    public static bool IsFullProduct(string? productId) =>
        productId is not null && productId.StartsWith(ProductPrefix, StringComparison.Ordinal);

    public static string? VinOf(string? productId) =>
        IsFullProduct(productId) ? productId![ProductPrefix.Length..] : null;

    // ============================================================
    // AVAILABILITY (free)
    // ============================================================

    /// <summary>Whether VinAudit has records for this VIN (no charge).</summary>
    public async Task<FullAvailability> CheckAsync(string vin, CancellationToken ct)
    {
        if (!IsConfigured)
            return new FullAvailability(false, "not_configured", null);

        if (_checks.TryGetValue(vin, out var hit) && DateTimeOffset.UtcNow - hit.At < CheckCacheAge)
            return hit.Result;

        var (ok, root) = await GetAsync("query.php", new()
        {
            ["vin"] = vin,
            ["key"] = Key!,
            ["format"] = "json"
        }, ct);

        FullAvailability result;
        if (!ok || root is null)
        {
            // Provider unreachable: do not cache, try again next time.
            return new FullAvailability(false, "provider_unavailable", null);
        }

        var success = Bool(root["success"]);
        var error = Str(root["error"]);
        var id = Str(root["id"]);
        result = success && id is not null
            ? new FullAvailability(true, null, id)
            : new FullAvailability(false, error ?? "no_records", null);

        if (result.Available || error is "no_records" or "invalid_vin")
            _checks[vin] = (DateTimeOffset.UtcNow, result);
        return result;
    }

    // ============================================================
    // PURCHASE (after payment)
    // ============================================================

    /// <summary>
    /// Buys the report from VinAudit for an account (once) and stores it.
    /// Safe to call again: a stored report is returned as is, and an
    /// already placed VinAudit order is not placed a second time.
    /// </summary>
    public async Task<FullPurchase> PurchaseAsync(string accountId, string vin, string orderId, CancellationToken ct)
    {
        if (!IsConfigured)
            return FullPurchase.Fail("not_configured");

        var gate = _buyGates.GetOrAdd(accountId + "|" + vin, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var row = await ReadRowAsync(accountId, vin, ct);
            if (row is { Status: "ready", Json: not null })
                return FullPurchase.Ok(Build(row.Json));

            var providerId = row?.ProviderId;
            if (row is null || row.Status == "new" || providerId is null)
            {
                var check = await CheckAsync(vin, ct);
                if (!check.Available || check.QueryId is null)
                    return FullPurchase.Fail(check.Reason ?? "no_records");
                providerId = check.QueryId;

                // Recorded before ordering: a retry will not order again.
                await UpsertRowAsync(accountId, vin, orderId, providerId, "ordering", null, ct);

                var orderArgs = new Dictionary<string, string>
                {
                    ["id"] = providerId,
                    ["user"] = User!,
                    ["pass"] = Pass!,
                    ["vin"] = vin,
                    ["key"] = Key!,
                    ["format"] = "json"
                };
                if (Mode is { } mode)
                    orderArgs["mode"] = mode;

                var (ok, root) = await GetAsync("order.php", orderArgs, ct);
                if (!ok || root is null || !Bool(root["success"]))
                {
                    var error = Str(root?["error"]) ?? "order_failed";
                    _log.LogWarning("VinAudit order for {Vin} failed: {Error}", vin, error);
                    // Nothing was charged when the order was refused.
                    if (root is not null)
                        await UpsertRowAsync(accountId, vin, orderId, null, "new", null, ct);
                    return FullPurchase.Fail(error);
                }

                await UpsertRowAsync(accountId, vin, orderId, providerId, "ordered", null, ct);
            }

            // Generate, then read the report (it can take a few seconds).
            for (var attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(TimeSpan.FromSeconds(2 + attempt), ct);

                await GetAsync("generate.php", new()
                {
                    ["id"] = providerId,
                    ["key"] = Key!,
                    ["format"] = "json"
                }, ct);

                var (ok, root) = await GetAsync("report.php", new()
                {
                    ["id"] = providerId,
                    ["key"] = Key!,
                    ["format"] = "json"
                }, ct);

                if (ok && root is not null && Bool(root["success"]))
                {
                    var json = root.ToJsonString();
                    await UpsertRowAsync(accountId, vin, orderId, providerId, "ready", json, ct);
                    return FullPurchase.Ok(Build(json));
                }

                var error = Str(root?["error"]);
                if (error is not (null or "not_ready"))
                {
                    _log.LogWarning("VinAudit report for {Vin} answered {Error}", vin, error);
                    if (error is "expired" or "no_records" or "invalid_vin")
                        return FullPurchase.Fail(error);
                }
            }

            return FullPurchase.Fail("not_ready");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or SqliteException)
        {
            _log.LogWarning("Full history for {Vin} failed: {Message}", vin, ex.Message);
            return FullPurchase.Fail("provider_unavailable");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A report this account already has, or null.</summary>
    public async Task<FullReportDto?> GetSavedAsync(string accountId, string vin, CancellationToken ct)
    {
        var row = await ReadRowAsync(accountId, vin, ct);
        return row is { Status: "ready", Json: not null } ? Build(row.Json) : null;
    }

    // ============================================================
    // REPORT → DISPLAY
    // ============================================================

    /// <summary>
    /// Turns the VinAudit report into ready-to-show sections (Georgian).
    /// Unknown lists are shown too, so no paid data is lost.
    /// </summary>
    public static FullReportDto Build(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        var sections = new List<FullSectionDto>();
        var warnings = 0;
        int? lastMeter = null;
        string? lastUnit = null;

        // Title records (state, date, odometer).
        if (root["titles"] is JsonArray titles && titles.Count > 0)
        {
            var items = new List<FullItemDto>();
            foreach (var t in titles.OfType<JsonObject>().OrderByDescending(t => Str(t["date"])))
            {
                var meter = Str(t["meter"]);
                var unit = Str(t["meterUnit"]) is "K" or "k" ? "კმ" : "მილი";
                var current = Bool(t["current"]);
                if (int.TryParse(meter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0 && lastMeter is null)
                {
                    lastMeter = m;
                    lastUnit = unit;
                }

                items.Add(new FullItemDto(
                    Date(Str(t["date"])),
                    "ტიტული · " + (Str(t["state"]) ?? "?") + (current ? " (მიმდინარე)" : ""),
                    meter is null or "0" ? null : $"გარბენი: {Thousands(meter)} {unit}"));
            }
            sections.Add(new FullSectionDto("ტიტულის ჩანაწერები და გარბენი", false, items));
        }

        // Title brands (salvage, flood, rebuilt, junk ...).
        if (root["checks"] is JsonArray checks && checks.Count > 0)
        {
            var items = checks.OfType<JsonObject>().Select(c => new FullItemDto(
                Date(Str(c["date"])),
                BrandName(c),
                Join(Str(c["branderName"]), Str(c["disposition"])))).ToList();
            warnings += items.Count;
            sections.Add(new FullSectionDto("ტიტულის ბრენდები (salvage, flood, rebuilt…)", true, items));
        }

        // Junk / salvage / insurance total-loss records.
        if (root["jsi"] is JsonArray jsi && jsi.Count > 0)
        {
            var items = jsi.OfType<JsonObject>().Select(j => new FullItemDto(
                Date(Str(j["date"])),
                RecordType(Str(j["recordType"])),
                Join(Str(j["branderName"]), Str(j["branderState"]), Str(j["disposition"]), Str(j["vehicleDisposition"])))).ToList();
            warnings += items.Count;
            sections.Add(new FullSectionDto("ზარალი / Junk / Salvage ჩანაწერები", true, items));
        }

        // Any other list VinAudit sends (thefts, accidents, liens, sales ...).
        foreach (var (name, value) in root)
        {
            if (name is "titles" or "checks" or "jsi" or "specs" or "attributes" || value is not JsonArray list || list.Count == 0)
                continue;

            var items = list.OfType<JsonObject>().Select(o =>
            {
                var date = Date(Str(o["date"]));
                var text = string.Join(" · ", o
                    .Where(p => p.Key != "date" && p.Value is JsonValue)
                    .Select(p => Str(p.Value))
                    .Where(s => s is not null)
                    .Take(5));
                return new FullItemDto(date, string.IsNullOrWhiteSpace(text) ? name : text, null);
            }).ToList();

            if (items.Count == 0)
                continue;
            var bad = name.Contains("theft", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("accident", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("lien", StringComparison.OrdinalIgnoreCase);
            if (bad) warnings += items.Count;
            sections.Add(new FullSectionDto(OtherTitle(name), bad, items));
        }

        var summary = warnings == 0
            ? "სახიფათო ჩანაწერი (salvage, flood, junk, ზარალი) ოფიციალურ ბაზაში არ მოიძებნა."
            : $"ოფიციალურ ბაზაში მოიძებნა {warnings} საყურადღებო ჩანაწერი — იხილე ქვემოთ.";

        return new FullReportDto(
            warnings == 0,
            summary,
            lastMeter is null ? null : $"{Thousands(lastMeter.Value.ToString(CultureInfo.InvariantCulture))} {lastUnit}",
            (root["titles"] as JsonArray)?.Count ?? 0,
            sections);
    }

    private static string BrandName(JsonObject c)
    {
        var name = Str(c["brandTitle"]) ?? Str(c["brandName"]) ?? Str(c["description"]) ?? Str(c["title"]);
        var code = Str(c["brandCode"]);
        return name ?? (code is null ? "ტიტულის ბრენდი" : "ტიტულის ბრენდი · კოდი " + code);
    }

    private static string RecordType(string? type) => type?.ToUpperInvariant() switch
    {
        "J" or "JUNK" => "Junk (ჯართი)",
        "S" or "SALVAGE" => "Salvage (აღსადგენი)",
        "I" or "INSURANCE" => "სადაზღვევო — სრული ზარალი",
        null => "ჩანაწერი",
        _ => type!
    };

    private static string OtherTitle(string name) => name.ToLowerInvariant() switch
    {
        "thefts" or "theft" => "ქურდობის ჩანაწერები",
        "accidents" or "accident" => "ავარიები",
        "liens" or "lien" => "გირავნობა (lien)",
        "sales" or "sale" => "გაყიდვები",
        _ => name
    };

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }

    private static string? Date(string? raw)
    {
        if (raw is null) return null;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : raw;
    }

    private static string Thousands(string number) =>
        long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ')
            : number;

    // ============================================================
    // HTTP / DB HELPERS
    // ============================================================

    private async Task<(bool Ok, JsonObject? Root)> GetAsync(string path, Dictionary<string, string> args, CancellationToken ct)
    {
        var query = string.Join("&", args.Select(a => a.Key + "=" + Uri.EscapeDataString(a.Value)));
        var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(40);
        using var response = await client.GetAsync(Api + path + "?" + query, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("VinAudit {Path} answered {Status}", path, (int)response.StatusCode);
            return (false, null);
        }

        try { return (true, JsonNode.Parse(body) as JsonObject); }
        catch (System.Text.Json.JsonException) { return (false, null); }
    }

    private sealed record Row(string OrderId, string? ProviderId, string Status, string? Json);

    private async Task<Row?> ReadRowAsync(string accountId, string vin, CancellationToken ct)
    {
        await _dbGate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT OrderId, ProviderId, Status, Json FROM FullReports WHERE AccountId=$a AND Vin=$v";
            cmd.Parameters.AddWithValue("$a", accountId);
            cmd.Parameters.AddWithValue("$v", vin);
            using var r = cmd.ExecuteReader();
            return r.Read()
                ? new Row(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3))
                : null;
        }
        finally { _dbGate.Release(); }
    }

    private async Task UpsertRowAsync(string accountId, string vin, string orderId, string? providerId, string status, string? json, CancellationToken ct)
    {
        await _dbGate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO FullReports(AccountId,Vin,OrderId,ProviderId,Status,Json,CreatedAt)
VALUES($a,$v,$o,$p,$s,$j,$at)
ON CONFLICT(AccountId,Vin) DO UPDATE SET OrderId=$o, ProviderId=$p, Status=$s, Json=$j";
            cmd.Parameters.AddWithValue("$a", accountId);
            cmd.Parameters.AddWithValue("$v", vin);
            cmd.Parameters.AddWithValue("$o", orderId);
            cmd.Parameters.AddWithValue("$p", providerId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$j", json ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally { _dbGate.Release(); }
    }

    private static string? Str(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        JsonValue v when v.TryGetValue<long>(out var l) => l.ToString(CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        _ => null
    };

    private static bool Bool(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) => s is "true" or "1",
        JsonValue v when v.TryGetValue<int>(out var i) => i != 0,
        _ => false
    };
}

public sealed record FullAvailability(bool Available, string? Reason, string? QueryId);

public sealed record FullPurchase(bool Success, FullReportDto? Report, string? Error)
{
    public static FullPurchase Ok(FullReportDto report) => new(true, report, null);
    public static FullPurchase Fail(string error) => new(false, null, error);
}

/// <summary>The paid report as the app shows it.</summary>
public sealed record FullReportDto(
    bool Clean,
    string Summary,
    string? LastOdometer,
    int TitleRecords,
    List<FullSectionDto> Sections);

public sealed record FullSectionDto(string Title, bool Warning, List<FullItemDto> Items);

public sealed record FullItemDto(string? Date, string Title, string? Detail);
