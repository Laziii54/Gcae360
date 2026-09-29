using System.Net.Http.Json;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Collects VIN data from the providers:
///  • NHTSA vPIC – free specifications,
///  • NHTSA recalls – free recall campaigns,
///  • Apibara – paid Copart / IAAI auction history and photos.
/// Every answer is cached so one VIN costs at most one paid request a
/// week, and paid requests are capped by a monthly budget.
/// The Apibara key is read only from the server configuration.
/// </summary>
public sealed class VinDataService
{
    private const string ApibaraBase = "https://apibara.tech/api/v1/vehicle-auction";
    private static readonly TimeSpan AuctionCacheAge = TimeSpan.FromDays(7);
    // "Nothing found" is re-checked sooner: the provider keeps adding lots.
    private static readonly TimeSpan EmptyAuctionCacheAge = TimeSpan.FromHours(12);
    // v3: history + detail + VIN search fallback (older empty answers are not reused).
    private const string AuctionCacheKey = "auction3:";
    private static readonly TimeSpan SpecCacheAge = TimeSpan.FromDays(60);
    private static readonly TimeSpan RecallCacheAge = TimeSpan.FromDays(7);

    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _config;
    private readonly ILogger<VinDataService> _log;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VinDataService(
        IHttpClientFactory clients,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<VinDataService> log)
    {
        _clients = clients;
        _config = config;
        _log = log;
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));
        Initialize();
    }

    private void Initialize()
    {
        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS VinCache (
CacheKey TEXT PRIMARY KEY,
Json TEXT NOT NULL,
FetchedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS VinProviderUsage (
Month TEXT PRIMARY KEY,
Requests INTEGER NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // CONFIG
    // ============================================================

    private string? ApibaraKey
    {
        get
        {
            var key = _config["Vin:ApibaraApiKey"] ?? Environment.GetEnvironmentVariable("GCS_APIBARA_API_KEY");
            return string.IsNullOrWhiteSpace(key) || key == "SET_ON_SERVER_ONLY" ? null : key.Trim();
        }
    }

    /// <summary>Paid requests allowed per calendar month (100 on the free Apibara plan).</summary>
    public int MonthlyBudget =>
        int.TryParse(_config["Vin:ProviderMonthlyRequestBudget"], out var value) && value > 0 ? value : 100;

    /// <summary>Share of the budget that free (preview) lookups may use; the rest is kept for paid reports.</summary>
    public double PreviewShare =>
        double.TryParse(_config["Vin:PreviewBudgetShare"], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value is > 0 and <= 1 ? value : 0.6;

    public bool IsAuctionProviderConfigured => ApibaraKey is not null;

    // ============================================================
    // PER-REQUEST TRACE (owner diagnostics in the app)
    // ============================================================

    private static readonly AsyncLocal<List<ProviderTrace>?> CurrentTrace = new();

    /// <summary>Starts recording which providers answered what, for this request.</summary>
    public static List<ProviderTrace> BeginTrace()
    {
        var list = new List<ProviderTrace>();
        CurrentTrace.Value = list;
        return list;
    }

    internal static void Trace(string provider, int status, int lots, string? note = null)
    {
        var list = CurrentTrace.Value;
        if (list is null) return;
        lock (list) list.Add(new ProviderTrace(provider, status, lots, note));
    }

    /// <summary>Number of lots found in an answer of any provider (for the trace).</summary>
    private static int ParseAny(string body)
    {
        try
        {
            var root = JsonNode.Parse(body);
            var data = root?["data"] ?? root;
            return data switch
            {
                JsonArray a => a.Count,
                JsonObject o when o.ContainsKey("vin") || o.ContainsKey("lot_number") => 1,
                JsonObject o => o.Count(p => p.Value is JsonArray arr && arr.Count > 0),
                _ => 0
            };
        }
        catch (JsonException) { return 0; }
    }

    // ============================================================
    // SECOND PROVIDER: VehicleDatabases Auction API (deeper archive)
    // Used only when Apibara has nothing for the VIN.
    // ============================================================

    private const string VdbBase = "https://api.vehicledatabases.com/auction/";

    private string? VdbKey
    {
        get
        {
            var key = _config["Vin:VehicleDatabasesApiKey"] ?? Environment.GetEnvironmentVariable("GCS_VDB_API_KEY");
            return string.IsNullOrWhiteSpace(key) || key == "SET_ON_SERVER_ONLY" ? null : key.Trim();
        }
    }

    public bool IsSecondProviderConfigured => VdbKey is not null;

    // ============================================================
    // THIRD PROVIDER: API Auctions (apiauctions.io, archive from 2018)
    // ============================================================

    private const string ApaUrl = "https://api.apiauctions.io/api/v2/get-cars";

    private string? ApaToken
    {
        get
        {
            var key = _config["Vin:ApiAuctionsToken"] ?? Environment.GetEnvironmentVariable("GCS_APIAUCTIONS_TOKEN");
            return string.IsNullOrWhiteSpace(key) || key == "SET_ON_SERVER_ONLY" ? null : key.Trim();
        }
    }

    public bool IsApiAuctionsConfigured => ApaToken is not null;

    /// <summary>API Auctions requests per month ($0.01 each).</summary>
    public int ApaMonthlyBudget =>
        int.TryParse(_config["Vin:ApiAuctionsMonthlyBudget"], out var value) && value > 0 ? value : 300;

    /// <summary>Any extra archive (VehicleDatabases / API Auctions) is set up.</summary>
    public bool HasExtraProviders => IsSecondProviderConfigured || IsApiAuctionsConfigured;

    public async Task<AuctionLookup> GetApiAuctionsAsync(string vin, CancellationToken ct)
    {
        if (!IsApiAuctionsConfigured)
            return new AuctionLookup(AuctionLookupState.Unavailable, []);

        var cached = await ReadCacheAsync("apa:" + vin, TimeSpan.FromDays(3), ct);
        if (cached is null)
        {
            if (!await TryReserveAsync("apa", ApaMonthlyBudget, ct))
            {
                Trace("apiauctions", 0, 0, "monthly budget used up");
                return new AuctionLookup(AuctionLookupState.Deferred, []);
            }

            var (status, body) = await CallApiAuctionsAsync(vin, ct);
            if (body is null && IsProviderFailure(status) && status != HttpStatusCode.NotFound)
                return new AuctionLookup(AuctionLookupState.Unavailable, []);

            cached = body is not null && ParseApaLots(body, vin).Count > 0 ? body : "{\"result\":true,\"data\":[]}";
            await WriteCacheAsync("apa:" + vin, cached, ct);
        }

        var apaLots = ParseApaLots(cached, vin);
        Trace("apiauctions (saved)", 200, apaLots.Count);
        return new AuctionLookup(AuctionLookupState.Ready, apaLots);
    }

    private async Task<(HttpStatusCode Status, string? Body)> CallApiAuctionsAsync(string vin, CancellationToken ct)
    {
        var (status, body) = await CallApiAuctionsCoreAsync(vin, ct);
        Trace("apiauctions", (int)status, body is null ? 0 : ParseAny(body));
        return (status, body);
    }

    private async Task<(HttpStatusCode Status, string? Body)> CallApiAuctionsCoreAsync(string vin, CancellationToken ct)
    {
        try
        {
            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApaUrl + "?vin=" + Uri.EscapeDataString(vin))
            {
                Content = JsonContent.Create(new { vin })
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApaToken);
            request.Headers.Add("Accept", "application/json");
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("API Auctions answered {Status}", (int)response.StatusCode);
                return (response.StatusCode, null);
            }
            var body = await response.Content.ReadAsStringAsync(ct);
            LogShape("apiauctions", body);
            return (response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning("API Auctions request failed: {Message}", ex.Message);
            return (HttpStatusCode.ServiceUnavailable, null);
        }
    }

    /// <summary>API Auctions lots: each record plus its embedded earlier sales.</summary>
    internal static List<AuctionLotDto> ParseApaLots(string raw, string vin)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(raw); }
        catch (JsonException) { return []; }

        var data = root?["data"] ?? root;
        var items = new JsonArray();
        void Add(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                var copy = JsonNode.Parse(obj.ToJsonString())!.AsObject();
                if (!copy.ContainsKey("vin")) copy["vin"] = vin;
                items.Add(copy);
                if (obj["history"] is JsonArray past)
                    foreach (var sale in past) Add(sale);
            }
        }
        if (data is JsonArray list) foreach (var item in list) Add(item);
        else Add(data);

        return ParseLots(new JsonObject { ["data"] = items }.ToJsonString(), vin);
    }

    /// <summary>
    /// Both extra archives at once (for an opened history), merged.
    /// </summary>
    public async Task<List<AuctionLotDto>> GetExtraLotsAsync(CancellationToken ct, string vin)
    {
        if (!IsSecondProviderConfigured) Trace("vehicledatabases", 0, 0, "key not set in Azure");
        if (!IsApiAuctionsConfigured) Trace("apiauctions", 0, 0, "key not set in Azure");
        var vdb = IsSecondProviderConfigured ? GetSecondaryAuctionAsync(vin, ct) : Task.FromResult(new AuctionLookup(AuctionLookupState.Unavailable, []));
        var apa = IsApiAuctionsConfigured ? GetApiAuctionsAsync(vin, ct) : Task.FromResult(new AuctionLookup(AuctionLookupState.Unavailable, []));
        await Task.WhenAll(vdb, apa);
        return MergeLots(apa.Result.Lots, vdb.Result.Lots);
    }

    private static string LotKey(string? lot) =>
        lot is null ? "" : new string(lot.Where(char.IsDigit).ToArray()).TrimStart('0');

    /// <summary>
    /// One list from several sources: the same sale (same lot number, or
    /// same auction day) is joined — photos added up, empty values filled.
    /// </summary>
    public static List<AuctionLotDto> MergeLots(List<AuctionLotDto> primary, List<AuctionLotDto> extra)
    {
        var result = primary.ToList();
        foreach (var lot in extra)
        {
            var key = LotKey(lot.LotNumber);
            var match = result.FirstOrDefault(r =>
                (key.Length >= 5 && LotKey(r.LotNumber) == key) ||
                (r.SaleDate is { } a && lot.SaleDate is { } b && Math.Abs((a.Date - b.Date).TotalDays) <= 3 &&
                 (r.AuctionName is null || lot.AuctionName is null ||
                  string.Equals(r.AuctionName, lot.AuctionName, StringComparison.OrdinalIgnoreCase))));

            if (match is null)
            {
                result.Add(lot);
                continue;
            }

            var photos = match.ImageUrls.Concat(lot.ImageUrls).Distinct(StringComparer.OrdinalIgnoreCase).Take(80).ToList();
            Merge(match, lot);
            match.ImageUrls = photos;
            match.PhotoCount = Math.Max(match.PhotoCount, photos.Count);
        }

        return result.OrderByDescending(l => l.SaleDate ?? DateTime.MinValue).ToList();
    }

    /// <summary>Paid VehicleDatabases lookups per month (their credits are limited).</summary>
    public int VdbMonthlyBudget =>
        int.TryParse(_config["Vin:VdbMonthlyBudget"], out var value) && value > 0 ? value : 15;

    /// <summary>
    /// Auction lots from VehicleDatabases. Photo links there are signed and
    /// expire, so a found answer is kept only for a few hours.
    /// </summary>
    public async Task<AuctionLookup> GetSecondaryAuctionAsync(string vin, CancellationToken ct)
    {
        if (!IsSecondProviderConfigured)
            return new AuctionLookup(AuctionLookupState.Unavailable, []);

        if (!IsSecondProviderConfigured) Trace("vehicledatabases", 0, 0, "no key");
        var cached = await ReadCacheAsync("vdb:" + vin, TimeSpan.FromHours(6), ct);
        if (cached is not null && ParseVdbLots(cached, vin).Count == 0)
            cached = await ReadCacheAsync("vdb:" + vin, TimeSpan.FromDays(3), ct);

        if (cached is null)
        {
            if (!await TryReserveAsync("vdb", VdbMonthlyBudget, ct))
            {
                Trace("vehicledatabases", 0, 0, "monthly budget used up");
                return new AuctionLookup(AuctionLookupState.Deferred, []);
            }

            var (status, body) = await CallVdbAsync(vin, ct);
            if (body is null && status is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound))
                return new AuctionLookup(AuctionLookupState.Unavailable, []);

            cached = body is not null && ParseVdbLots(body, vin).Count > 0 ? body : "{\"status\":\"none\",\"data\":[]}";
            await WriteCacheAsync("vdb:" + vin, cached, ct);
        }

        var vdbLots = ParseVdbLots(cached, vin);
        Trace("vehicledatabases (saved)", 200, vdbLots.Count);
        return new AuctionLookup(AuctionLookupState.Ready, vdbLots);
    }

    private async Task<(HttpStatusCode Status, string? Body)> CallVdbAsync(string vin, CancellationToken ct)
    {
        var (status, body) = await CallVdbCoreAsync(vin, ct);
        Trace("vehicledatabases", (int)status, body is null ? 0 : ParseAny(body));
        return (status, body);
    }

    private async Task<(HttpStatusCode Status, string? Body)> CallVdbCoreAsync(string vin, CancellationToken ct)
    {
        try
        {
            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var request = new HttpRequestMessage(HttpMethod.Get, VdbBase + Uri.EscapeDataString(vin));
            request.Headers.Add("x-authkey", VdbKey);
            request.Headers.Add("Accept", "application/json");
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("VehicleDatabases answered {Status}", (int)response.StatusCode);
                return (response.StatusCode, null);
            }
            return (response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning("VehicleDatabases request failed: {Message}", ex.Message);
            return (HttpStatusCode.ServiceUnavailable, null);
        }
    }

    internal static List<AuctionLotDto> ParseVdbLots(string raw, string vin)
    {
        var lots = new List<AuctionLotDto>();
        JsonNode? root;
        try { root = JsonNode.Parse(raw); }
        catch (JsonException) { return lots; }

        if (root?["data"] is not JsonArray items)
            return lots;

        static string? Get(JsonNode? node, string group, string key)
        {
            var value = (group.Length == 0 ? node?[key] : node?[group]?[key]) as JsonValue;
            if (value is null || !value.TryGetValue<string>(out var text))
                return null;
            text = text.Trim();
            return text.Length == 0 || text.Equals("N/A", StringComparison.OrdinalIgnoreCase) ? null : text;
        }

        static decimal? Number(string? text)
        {
            if (text is null) return null;
            var digits = new string(text.TakeWhile(c => char.IsDigit(c) || c is ',' or '.' or '$' or ' ').Where(c => char.IsDigit(c) || c == '.').ToArray());
            return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : null;
        }

        static bool? YesNo(string? text) => text?.ToLowerInvariant() switch
        {
            "yes" or "true" => true,
            "no" or "false" => false,
            _ => null
        };

        foreach (var item in items)
        {
            if (item is not JsonObject lot)
                continue;

            var lotVin = Get(lot, "", "vin") ?? Get(lot, "title-and-condition", "VIN");
            if (!string.IsNullOrWhiteSpace(lotVin) && !lotVin.Equals(vin, StringComparison.OrdinalIgnoreCase))
                continue;

            var images = new List<string>();
            if (lot["images"] is JsonArray pictures)
                foreach (var picture in pictures)
                    if (picture is JsonValue v && v.TryGetValue<string>(out var url) && Uri.TryCreate(url, UriKind.Absolute, out _))
                        images.Add(url);

            DateTime? date = null;
            var dateText = Get(lot, "sale-date-location", "Auction Date");
            if (DateTime.TryParseExact(dateText, new[] { "MM-dd-yyyy", "M-d-yyyy", "yyyy-MM-dd", "MM/dd/yyyy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                date = parsed;

            var odometerText = Get(lot, "technical-specs", "Odometer");
            var odometerKm = odometerText is not null && !odometerText.Contains("mi", StringComparison.OrdinalIgnoreCase);

            lots.Add(new AuctionLotDto
            {
                AuctionName = null,
                LotNumber = Get(lot, "", "lot-number"),
                SaleDate = date,
                Location = Get(lot, "sale-date-location", "Location"),
                Seller = Get(lot, "sale-date-location", "Seller Type"),
                SaleType = Get(lot, "", "sale status"),
                PrimaryDamage = Get(lot, "title-and-condition", "Primary Damage"),
                SecondaryDamage = Get(lot, "title-and-condition", "Secondary Damage"),
                OdometerValue = Number(odometerText),
                OdometerUnit = odometerText is null ? null : odometerKm ? "km" : "mi",
                SoldPrice = Money(Number(Get(lot, "", "price"))),
                RepairCost = Money(Number(Get(lot, "technical-specs", "Estimated Repair Cost"))),
                EstimatedRetailValue = Money(Number(Get(lot, "technical-specs", "Avg. Estimated Retail Value"))),
                KeysAvailable = YesNo(Get(lot, "car-features", "Has_Keys")),
                RunAndDrive = YesNo(Get(lot, "car-features", "Runs_Drives")),
                EngineStarts = YesNo(Get(lot, "car-features", "Engine_Starts")),
                TitleStatus = Get(lot, "title-and-condition", "Title Type"),
                Vin = vin,
                ImageUrls = images,
                PhotoCount = images.Count
            });
        }

        return lots.OrderByDescending(lot => lot.SaleDate ?? DateTime.MinValue).ToList();
    }

    /// <summary>Monthly request counter for one provider ("vdb").</summary>
    private async Task<bool> TryReserveAsync(string provider, int limit, CancellationToken ct)
    {
        var month = provider + ":" + DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO VinProviderUsage(Month,Requests) VALUES($m,0) ON CONFLICT(Month) DO NOTHING;
UPDATE VinProviderUsage SET Requests=Requests+1 WHERE Month=$m AND Requests < $limit;";
            cmd.Parameters.AddWithValue("$m", month);
            cmd.Parameters.AddWithValue("$limit", limit);
            var reserved = cmd.ExecuteNonQuery() > 0;
            if (!reserved)
                _log.LogWarning("VehicleDatabases budget reached for {Month}", month);
            return reserved;
        }
        finally { _gate.Release(); }
    }

    // ============================================================
    // SPECIFICATIONS (NHTSA vPIC)
    // ============================================================

    public async Task<Dictionary<string, string>?> GetSpecsAsync(string vin, CancellationToken ct)
    {
        var raw = await GetCachedOrFetchAsync(
            "spec:" + vin,
            SpecCacheAge,
            paid: false,
            preview: false,
            async client =>
            {
                using var response = await client.GetAsync(
                    $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValuesExtended/{Uri.EscapeDataString(vin)}?format=json", ct);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
            },
            ct);

        if (raw is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("Results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
                return null;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in results[0].EnumerateObject())
            {
                var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()?.Trim() : null;
                if (!string.IsNullOrWhiteSpace(text) && text != "Not Applicable" && text != "0")
                    values[property.Name] = text;
            }

            return values;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ============================================================
    // RECALLS (NHTSA)
    // ============================================================

    public async Task<List<VinRecallDto>> GetRecallsAsync(string? make, string? model, int? year, CancellationToken ct)
    {
        var list = new List<VinRecallDto>();
        if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model) || year is null)
            return list;

        var key = $"recall:{make}|{model}|{year}".ToUpperInvariant();
        var raw = await GetCachedOrFetchAsync(
            key,
            RecallCacheAge,
            paid: false,
            preview: false,
            async client =>
            {
                var url = "https://api.nhtsa.gov/recalls/recallsByVehicle?make=" + Uri.EscapeDataString(make) +
                          "&model=" + Uri.EscapeDataString(model) + "&modelYear=" + year;
                using var response = await client.GetAsync(url, ct);
                // NHTSA answers 400 when it has no data for the model.
                if (response.StatusCode == HttpStatusCode.BadRequest) return "{\"results\":[]}";
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
            },
            ct);

        if (raw is null)
            return list;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in results.EnumerateArray())
            {
                DateTime? date = null;
                var dateText = Str(item, "ReportReceivedDate");
                if (DateTime.TryParseExact(dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ||
                    DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    date = d;

                list.Add(new VinRecallDto(
                    Str(item, "NHTSACampaignNumber"),
                    date,
                    Str(item, "Component"),
                    Str(item, "Summary"),
                    Str(item, "Manufacturer"),
                    Str(item, "Remedy")));
            }
        }
        catch (JsonException)
        {
        }

        return list;
    }

    // ============================================================
    // AUCTION HISTORY (Apibara)
    // ============================================================

    /// <summary>
    /// Auction lots of the VIN. <paramref name="preview"/> marks a free
    /// lookup, which may only use part of the monthly budget.
    /// </summary>
    public async Task<AuctionLookup> GetAuctionHistoryAsync(string vin, bool preview, CancellationToken ct)
    {
        var cached = await ReadCacheAsync(AuctionCacheKey + vin, AuctionCacheAge, ct);
        if (cached is not null && ParseLots(cached, vin).Count == 0)
        {
            // An empty answer is kept for a shorter time.
            var fresh = await ReadCacheAsync(AuctionCacheKey + vin, EmptyAuctionCacheAge, ct);
            if (fresh is null)
                cached = null;
        }

        if (cached is null)
        {
            if (!IsAuctionProviderConfigured)
            {
                Trace("apibara", 0, 0, "key not set in Azure");
                return new AuctionLookup(AuctionLookupState.Unavailable, []);
            }

            if (!await TryReserveRequestAsync(preview, ct))
            {
                Trace("apibara", 0, 0, "monthly budget used up");
                return new AuctionLookup(AuctionLookupState.Deferred, []);
            }

            var (status, body) = await CallApibaraAsync($"/vehicles/{Uri.EscapeDataString(vin)}/history?per_page=20", ct);
            if (body is null && IsProviderFailure(status))
                return new AuctionLookup(AuctionLookupState.Unavailable, []);

            LogShape("history", body);
            var lots = body is null ? [] : ParseLots(body, vin);

            // The history list can be empty while the vehicle page still
            // has the lot (current or retained). Ask the vehicle page too.
            if (lots.Count == 0 && await TryReserveRequestAsync(preview, ct))
            {
                var (detailStatus, detail) = await CallApibaraAsync(
                    $"/vehicles/{Uri.EscapeDataString(vin)}?include=history,vin_lots", ct);
                LogShape("detail", detail);
                if (detail is not null)
                {
                    var detailLots = ParseLots(detail, vin);
                    if (detailLots.Count > 0)
                    {
                        body = detail;
                        // The detail answer also carries the photos.
                        await WriteCacheAsync("detail:" + vin, detail, ct);
                    }
                }
                // A failed vehicle page is not final: the VIN search below still runs.
            }

            // Some provider records are searchable by VIN but are not returned by
            // the exact history/detail routes (usually older archived lots).
            // Use the provider's VIN/lot search as a final fallback.
            if ((body is null || ParseLots(body, vin).Count == 0) &&
                await TryReserveRequestAsync(preview, ct))
            {
                var (searchStatus, search) = await CallApibaraAsync(
                    $"/vehicles?s={Uri.EscapeDataString(vin)}&per_page=20", ct);
                LogShape("search", search);
                if (search is not null && ParseLots(search, vin).Count > 0)
                {
                    body = search;
                }
                else if (body is null && IsProviderFailure(searchStatus))
                {
                    return new AuctionLookup(AuctionLookupState.Unavailable, []);
                }
            }

            body ??= "{\"ok\":true,\"data\":[]}";
            await WriteCacheAsync(AuctionCacheKey + vin, body, ct);
            cached = body;
        }

        var apibaraLots = ParseLots(cached, vin);
        Trace("apibara (saved)", 200, apibaraLots.Count, $"{apibaraLots.Sum(l => l.ImageUrls.Count)} photos");
        return new AuctionLookup(AuctionLookupState.Ready, apibaraLots);
    }

    /// <summary>
    /// Owner diagnostics: asks every provider route for the VIN (no cache)
    /// and describes the answers (status, field names, lots, photos).
    /// </summary>
    public async Task<object> DiagnoseAsync(string vin, CancellationToken ct)
    {
        var used = await GetUsedRequestsAsync(ct);
        if (!IsAuctionProviderConfigured)
            return new { vin, apibaraKeyConfigured = false, budgetUsed = used, budget = MonthlyBudget };

        var routes = new[]
        {
            ("history", $"/vehicles/{Uri.EscapeDataString(vin)}/history?per_page=20"),
            ("detail", $"/vehicles/{Uri.EscapeDataString(vin)}?include=history,vin_lots"),
            ("search", $"/vehicles?s={Uri.EscapeDataString(vin)}&per_page=20")
        };

        var answers = new List<object>();
        foreach (var (kind, path) in routes)
        {
            if (!await TryReserveRequestAsync(false, ct))
            {
                answers.Add(new { kind, skipped = "monthly budget used up" });
                continue;
            }

            var (status, body) = await CallApibaraAsync(path, ct);
            var lots = body is null ? [] : ParseLots(body, vin);
            string? rootKeys = null, itemKeys = null;
            try
            {
                var root = body is null ? null : JsonNode.Parse(body);
                var dataNode = root?["data"] ?? root;
                var first = dataNode is JsonArray a && a.Count > 0 ? a[0] : dataNode;
                rootKeys = root is JsonObject r ? string.Join(",", r.Select(p => p.Key)) : null;
                itemKeys = first is JsonObject o ? string.Join(",", o.Select(p => p.Key).Take(60)) : first?.GetType().Name;
            }
            catch (JsonException) { }

            answers.Add(new
            {
                kind,
                status = (int)status,
                bytes = body?.Length ?? 0,
                rootKeys,
                itemKeys,
                lots = lots.Count,
                photos = lots.Sum(l => l.ImageUrls.Count),
                photoCountField = lots.Sum(l => l.PhotoCount),
                firstPhotoHost = lots.SelectMany(l => l.ImageUrls).Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Host : null).FirstOrDefault(),
                platforms = lots.Select(l => l.AuctionName).Distinct().ToArray()
            });
        }

        object? secondProvider = null;
        if (IsSecondProviderConfigured && await TryReserveAsync("vdb", VdbMonthlyBudget, ct))
        {
            var (vdbStatus, vdbBody) = await CallVdbAsync(vin, ct);
            var vdbLots = vdbBody is null ? [] : ParseVdbLots(vdbBody, vin);
            if (vdbBody is not null && vdbLots.Count > 0)
                await WriteCacheAsync("vdb:" + vin, vdbBody, ct);
            secondProvider = new
            {
                kind = "vehicledatabases",
                status = (int)vdbStatus,
                lots = vdbLots.Count,
                photos = vdbLots.Sum(l => l.ImageUrls.Count),
                firstPhotoHost = vdbLots.SelectMany(l => l.ImageUrls).Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Host : null).FirstOrDefault()
            };
        }

        object? apiAuctions = null;
        if (IsApiAuctionsConfigured && await TryReserveAsync("apa", ApaMonthlyBudget, ct))
        {
            var (apaStatus, apaBody) = await CallApiAuctionsAsync(vin, ct);
            var apaLots = apaBody is null ? [] : ParseApaLots(apaBody, vin);
            if (apaBody is not null && apaLots.Count > 0)
                await WriteCacheAsync("apa:" + vin, apaBody, ct);
            string? apaKeys = null;
            try
            {
                var root = apaBody is null ? null : JsonNode.Parse(apaBody);
                var first = (root?["data"] as JsonArray)?.FirstOrDefault() ?? root?["data"];
                apaKeys = first is JsonObject o ? string.Join(",", o.Select(p => p.Key).Take(60)) : null;
            }
            catch (JsonException) { }
            apiAuctions = new
            {
                kind = "apiauctions",
                status = (int)apaStatus,
                itemKeys = apaKeys,
                lots = apaLots.Count,
                photos = apaLots.Sum(l => l.ImageUrls.Count)
            };
        }

        return new
        {
            vin,
            apiAuctionsConfigured = IsApiAuctionsConfigured,
            apiAuctions,
            secondProviderConfigured = IsSecondProviderConfigured,
            secondProvider,
            apibaraKeyConfigured = true,
            budgetUsedBefore = used,
            budget = MonthlyBudget,
            answers
        };
    }

    /// <summary>
    /// A real outage (server error, key refused, rate limit). 400/404/422 only
    /// mean "nothing for this VIN" (e.g. a mistyped VIN) and are not failures.
    /// </summary>
    private static bool IsProviderFailure(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            or HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Logs only the field names of a provider answer (never values), so
    /// a changed answer format can be seen in the server log.
    /// </summary>
    private void LogShape(string kind, string? body)
    {
        if (body is null)
            return;
        try
        {
            var root = JsonNode.Parse(body);
            var data = root?["data"] ?? root;
            string Keys(JsonNode? node) => node is JsonObject o ? string.Join(",", o.Select(p => p.Key).Take(40)) : node?.GetType().Name ?? "null";
            var first = data is JsonArray a && a.Count > 0 ? a[0] : data;
            _log.LogInformation("VIN provider {Kind} answer: root[{Root}] data[{Data}] item[{Item}]",
                kind, Keys(root), data is JsonArray arr ? "array:" + arr.Count : Keys(data), Keys(first));
        }
        catch (JsonException)
        {
            _log.LogWarning("VIN provider {Kind} answer is not JSON", kind);
        }
    }

    /// <summary>
    /// Photos of the latest lot. The history list usually carries them;
    /// if it does not, one detail request is made (paid reports only).
    /// </summary>
    // ------------------------------------------------------------
    // Field names differ between provider answers (list vs. vehicle
    // page). Missing values are looked up anywhere in the lot by name.
    // ------------------------------------------------------------

    private static string NameKey(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>First value whose property name matches one of the names (depth ≤ 4).</summary>
    private static JsonNode? FindAny(JsonNode? node, HashSet<string> names, int depth = 0)
    {
        if (node is null || depth > 4)
            return null;

        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                if (property.Value is JsonValue && names.Contains(NameKey(property.Key)))
                    return property.Value;
            }
            foreach (var property in obj)
            {
                if (property.Key is "history" or "vin_lots" or "related" or "images" or "media" or "photos")
                    continue;
                if (property.Value is JsonObject or JsonArray &&
                    FindAny(property.Value, names, depth + 1) is { } found)
                    return found;
            }
        }
        else if (node is JsonArray array && depth > 0)
        {
            foreach (var item in array.Take(3))
                if (FindAny(item, names, depth + 1) is { } found)
                    return found;
        }

        return null;
    }

    private static string? FindText(JsonNode lot, params string[] names)
    {
        var value = FindAny(lot, names.Select(NameKey).ToHashSet()) as JsonValue;
        if (value is null) return null;
        if (value.TryGetValue<string>(out var s))
        {
            s = s.Trim();
            return s.Length == 0 || s.Equals("N/A", StringComparison.OrdinalIgnoreCase) || s == "-" ? null : s;
        }
        if (value.TryGetValue<long>(out var n)) return n.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<double>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<bool>(out var b)) return b ? "yes" : "no";
        return null;
    }

    private static decimal? FindNumber(JsonNode lot, params string[] names)
    {
        var text = FindText(lot, names);
        if (text is null) return null;
        var digits = new string(text.TakeWhile(c => char.IsDigit(c) || c is ',' or '.' or '$' or ' ').Where(c => char.IsDigit(c) || c == '.').ToArray());
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    private static bool? YesNoText(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "yes" or "true" or "y" or "present" or "1" => true,
        "no" or "false" or "n" or "missing" or "0" => false,
        _ => null
    };

    private static void FillFromAnyField(AuctionLotDto dto, JsonNode lot)
    {
        dto.AuctionName ??= Platform(FindText(lot, "platform", "auction", "auction_name", "source", "site"));
        dto.LotNumber ??= FindText(lot, "lot_number", "lot_id", "lotNumber", "stock_number", "stock");
        dto.PrimaryDamage ??= FindText(lot, "primary_damage", "primaryDamage", "damage_primary", "damage_description", "main_damage");
        dto.SecondaryDamage ??= FindText(lot, "secondary_damage", "secondaryDamage", "damage_secondary");
        dto.TitleStatus ??= FindText(lot, "sale_document", "sale_document_type", "saleDocument", "title_type", "title_status", "titleType", "document_type", "doc_type", "title_brand");
        dto.Condition ??= FindText(lot, "run_condition", "runCondition", "runs_drives", "condition_status", "highlights", "loss_type");
        dto.RunAndDrive ??= RunAndDrive(dto.Condition) ?? YesNoText(FindText(lot, "runs_drives", "run_and_drive", "runs_and_drives", "drivable"));
        dto.KeysAvailable ??= YesNoText(FindText(lot, "has_key", "has_keys", "keys", "keys_available", "key"));
        dto.EngineStarts ??= YesNoText(FindText(lot, "engine_starts", "starts"));
        dto.Location ??= FindText(lot, "location_name", "yard_name", "sale_location", "location");
        dto.Seller ??= FindText(lot, "seller_name", "seller_type", "seller");
        dto.OdometerStatus ??= FindText(lot, "odometer_status", "odometer_brand", "odometer_type");
        dto.SaleType ??= FindText(lot, "sale_status", "lot_status");

        if (dto.OdometerValue is null)
        {
            var km = FindNumber(lot, "odometer_km", "mileage_km", "km");
            var mi = FindNumber(lot, "odometer_mi", "odometer_miles", "mileage_mi", "miles", "mi", "odometer", "mileage");
            dto.OdometerValue = mi ?? km;
            dto.OdometerUnit = mi is not null ? "mi" : km is not null ? "km" : null;
        }

        if (dto.SaleDate is null)
        {
            var text = FindText(lot, "auction_at", "auction_date", "sale_date", "sold_at", "sale_time", "auctionDate", "saleDate");
            if (text is not null)
            {
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                    dto.SaleDate = date.UtcDateTime;
                else if (long.TryParse(text, out var unix) && unix > 1_000_000_000)
                    dto.SaleDate = DateTimeOffset.FromUnixTimeSeconds(unix > 10_000_000_000 ? unix / 1000 : unix).UtcDateTime;
            }
        }

        dto.SoldPrice ??= Money(FindNumber(lot, "sale_price_usd", "final_sale_price", "final_bid", "sold_price", "sale_price", "last_sold_price", "winning_bid"));
        dto.RepairCost ??= Money(FindNumber(lot, "repair_cost_usd", "repair_cost", "estimated_repair_cost"));
        dto.EstimatedRetailValue ??= Money(FindNumber(lot, "estimated_retail_value_usd", "estimated_retail_value", "retail_value", "acv"));
    }

    /// <summary>Fills the missing values of a lot from another copy of it.</summary>
    public static void Merge(AuctionLotDto target, AuctionLotDto source)
    {
        target.AuctionName ??= source.AuctionName;
        target.SaleDate ??= source.SaleDate;
        target.LotNumber ??= source.LotNumber;
        target.Location ??= source.Location;
        target.Seller ??= source.Seller;
        target.SaleType ??= source.SaleType;
        target.PrimaryDamage ??= source.PrimaryDamage;
        target.SecondaryDamage ??= source.SecondaryDamage;
        target.Condition ??= source.Condition;
        if (target.OdometerValue is null)
        {
            target.OdometerValue = source.OdometerValue;
            target.OdometerUnit = source.OdometerUnit;
        }
        target.OdometerStatus ??= source.OdometerStatus;
        target.EstimatedRetailValue ??= source.EstimatedRetailValue;
        target.RepairCost ??= source.RepairCost;
        target.SoldPrice ??= source.SoldPrice;
        target.KeysAvailable ??= source.KeysAvailable;
        target.RunAndDrive ??= source.RunAndDrive;
        target.EngineStarts ??= source.EngineStarts;
        target.TitleStatus ??= source.TitleStatus;
        if (target.ImageUrls.Count == 0 && source.ImageUrls.Count > 0)
            target.ImageUrls = source.ImageUrls;
        target.PhotoCount = Math.Max(target.PhotoCount, target.ImageUrls.Count);
    }

    /// <summary>
    /// The vehicle page of the latest lot (all details + photos). The
    /// history list is often short; this fills it in (paid reports only).
    /// </summary>
    public async Task<AuctionLotDto?> GetLatestDetailLotAsync(string vin, string? lotNumber, CancellationToken ct)
    {
        var raw = await ReadCacheAsync("detail:" + vin, AuctionCacheAge, ct);
        if (raw is null)
        {
            if (!IsAuctionProviderConfigured || !await TryReserveRequestAsync(false, ct))
                return null;

            var (status, body) = await CallApibaraAsync($"/vehicles/{Uri.EscapeDataString(vin)}", ct);
            if (body is null && IsProviderFailure(status))
                return null;

            raw = body ?? "{\"ok\":true,\"data\":{}}";
            await WriteCacheAsync("detail:" + vin, raw, ct);
        }

        var lots = ParseLots(raw, vin);
        var match = lots.FirstOrDefault(l => lotNumber is not null && l.LotNumber == lotNumber) ?? lots.FirstOrDefault();
        if (match is not null && match.ImageUrls.Count == 0)
        {
            var photos = await GetLatestPhotosAsync(vin, ct);
            match.ImageUrls = photos;
            match.PhotoCount = Math.Max(match.PhotoCount, photos.Count);
        }
        return match;
    }

    public async Task<List<string>> GetLatestPhotosAsync(string vin, CancellationToken ct)
    {
        var raw = await ReadCacheAsync("detail:" + vin, AuctionCacheAge, ct);
        if (raw is null)
        {
            if (!IsAuctionProviderConfigured || !await TryReserveRequestAsync(false, ct))
                return [];

            var (status, body) = await CallApibaraAsync($"/vehicles/{Uri.EscapeDataString(vin)}", ct);
            if (body is null && status != HttpStatusCode.NotFound)
                return [];

            raw = body ?? "{\"ok\":true,\"data\":{}}";
            await WriteCacheAsync("detail:" + vin, raw, ct);
        }

        try
        {
            var node = JsonNode.Parse(raw);
            var data = node?["data"] ?? node;
            var urls = new List<string>();
            if (data is not null)
                CollectImages(data, urls, 0);
            return urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(60).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<(HttpStatusCode Status, string? Body)> CallApibaraAsync(string path, CancellationToken ct)
    {
        var (status, body) = await CallApibaraCoreAsync(path, ct);
        Trace("apibara:" + (path.Split('?')[0].EndsWith("/history") ? "history" : path.StartsWith("/vehicles?") ? "search" : "vehicle") + "", (int)status, body is null ? 0 : ParseAny(body));
        return (status, body);
    }

    private async Task<(HttpStatusCode Status, string? Body)> CallApibaraCoreAsync(string path, CancellationToken ct)
    {
        try
        {
            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            using var request = new HttpRequestMessage(HttpMethod.Get, ApibaraBase + path);
            request.Headers.Add("X-API-Key", ApibaraKey);
            request.Headers.Add("Accept", "application/json");
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("VIN provider answered {Status} for {Path}", (int)response.StatusCode, path.Split('/')[1]);
                return (response.StatusCode, null);
            }

            return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning("VIN provider request failed: {Message}", ex.Message);
            return (HttpStatusCode.ServiceUnavailable, null);
        }
    }

    // ============================================================
    // PARSING (tolerant: field names differ between lots/providers)
    // ============================================================

    internal static List<AuctionLotDto> ParseLots(string raw, string vin)
    {
        var lots = new List<AuctionLotDto>();
        JsonNode? root;
        try { root = JsonNode.Parse(raw); }
        catch (JsonException) { return lots; }

        var data = root?["data"] ?? root;
        var items = FindLotArray(data);
        foreach (var item in items)
        {
            if (item is not JsonObject lot)
                continue;

            var images = new List<string>();
            CollectImages(lot, images, 0);
            images = images.Distinct(StringComparer.OrdinalIgnoreCase).Take(60).ToList();

            var photoCount = Int(lot, "media.thumbs_count", "media.images_count", "media.photos_count", "images_count", "photos_count");
            var lotVin = Text(lot, "vin");
            if (!string.IsNullOrWhiteSpace(lotVin) && !string.Equals(lotVin, vin, StringComparison.OrdinalIgnoreCase))
                continue;

            var odometerKm = Dec(lot, "odometer.km", "odometer_km");
            var odometerMi = Dec(lot, "odometer.mi", "odometer.miles", "odometer_mi", "odometer.value", "odometer");

            var dto = new AuctionLotDto
            {
                AuctionName = Platform(Text(lot, "platform", "source", "auction.platform")),
                LotNumber = Text(lot, "lot_number", "lot", "lotNumber"),
                SaleDate = Date(lot, "auction.auction_at", "auction_date", "sale_date", "sold_at", "auction.date"),
                Location = Text(lot, "location.display", "location.name", "location", "auction.location", "yard"),
                Seller = Text(lot, "seller.name", "seller.type", "seller_type", "seller"),
                SaleType = Text(lot, "auction.state", "lot_status", "status", "sale_status"),
                PrimaryDamage = Text(lot, "condition.primary_damage", "primary_damage", "damage.primary", "damage"),
                SecondaryDamage = Text(lot, "condition.secondary_damage", "secondary_damage", "damage.secondary"),
                Condition = Text(lot, "condition.run_condition", "run_condition", "condition.loss_type", "loss_type"),
                OdometerValue = odometerMi ?? odometerKm,
                OdometerUnit = odometerMi is not null ? "mi" : odometerKm is not null ? "km" : null,
                OdometerStatus = Text(lot, "odometer.status", "odometer_status", "odometer_brand"),
                SoldPrice = Money(Dec(lot, "pricing.sale_price_usd", "pricing.final_sale_price", "pricing.last_sold_price", "final_bid", "sale_price", "sold_price", "pricing.current_bid_usd", "current_bid")),
                EstimatedRetailValue = Money(Dec(lot, "pricing.estimated_retail_value_usd", "pricing.estimated_retail_value", "est_retail_value", "retail_value", "acv")),
                RepairCost = Money(Dec(lot, "pricing.repair_cost_usd", "pricing.repair_cost", "repair_cost")),
                KeysAvailable = Bool(lot, "condition.has_key", "has_key", "keys"),
                RunAndDrive = RunAndDrive(Text(lot, "condition.run_condition", "run_condition")),
                TitleStatus = Text(lot, "sale_document.title", "sale_document.type", "sale_document", "title_type", "document"),
                Vin = vin,
                ImageUrls = images,
                PhotoCount = Math.Max(photoCount ?? 0, images.Count)
            };
            FillFromAnyField(dto, lot);
            lots.Add(dto);
        }

        return lots
            .GroupBy(lot => (lot.AuctionName ?? "") + "|" + (lot.LotNumber ?? Guid.NewGuid().ToString()))
            .Select(group => group.OrderByDescending(lot => lot.ImageUrls.Count).First())
            .OrderByDescending(lot => lot.SaleDate ?? DateTime.MinValue)
            .ToList();
    }

    private static IEnumerable<JsonNode?> FindLotArray(JsonNode? data)
    {
        if (data is JsonArray array)
            return array;

        if (data is JsonObject obj)
        {
            if (obj.ContainsKey("lot_number") || obj.ContainsKey("vin"))
            {
                // Vehicle page: the lot itself plus its embedded history.
                var list = new List<JsonNode?> { obj };
                foreach (var name in new[] { "history", "vin_lots", "lots", "sales" })
                {
                    if (obj[name] is JsonArray embedded)
                        list.AddRange(embedded);
                    else if (obj[name] is JsonObject wrapper && wrapper["data"] is JsonArray wrapped)
                        list.AddRange(wrapped);
                }
                return list;
            }
            foreach (var name in new[] { "items", "history", "lots", "records", "sales", "results", "data", "vin_lots" })
            {
                if (obj[name] is JsonArray named && named.Count > 0)
                    return named;
            }

            foreach (var property in obj)
            {
                if (property.Value is JsonArray any && any.Count > 0 && any[0] is JsonObject first &&
                    (first.ContainsKey("lot_number") || first.ContainsKey("vin") || first.ContainsKey("platform")))
                    return any;
            }

        }

        return [];
    }

    private static void CollectImages(JsonNode node, List<string> urls, int depth)
    {
        if (depth > 6 || urls.Count >= 80)
            return;

        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (LooksLikeImage(text))
                    urls.Add(text);
                break;
            case JsonArray array:
                foreach (var child in array)
                    if (child is not null) CollectImages(child, urls, depth + 1);
                break;
            case JsonObject obj:
                // Prefer the large picture when an object has several sizes.
                var best = new[] { "full", "large", "hd", "high", "original", "url", "src", "image", "medium" }
                    .Select(key => obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && LooksLikeImage(s) ? s : null)
                    .FirstOrDefault(s => s is not null);
                if (best is not null)
                {
                    urls.Add(best);
                    break;
                }

                foreach (var property in obj)
                {
                    if (property.Value is null) continue;
                    if (property.Key.Contains("seller", StringComparison.OrdinalIgnoreCase) ||
                        property.Key.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
                        property.Key is "history" or "vin_lots" or "related" or "lots")
                        continue;
                    CollectImages(property.Value, urls, depth + 1);
                }
                break;
        }
    }

    private static bool LooksLikeImage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return false;

        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".png") || path.EndsWith(".webp") ||
               path.Contains("/image") || path.Contains("/photo") || path.Contains("/img") || path.Contains("/media") ||
               path.Contains("/resizer") || uri.Host.EndsWith("iaai.com", StringComparison.OrdinalIgnoreCase) && uri.Query.Contains("imageKeys", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonNode? At(JsonObject lot, string path)
    {
        JsonNode? node = lot;
        foreach (var part in path.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node))
                return null;
        }

        return node;
    }

    private static string? Text(JsonObject lot, params string[] paths)
    {
        foreach (var path in paths)
        {
            var node = At(lot, path);
            if (node is JsonValue value)
            {
                if (value.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    return s.Trim();
                if (value.TryGetValue<long>(out var n))
                    return n.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static decimal? Dec(JsonObject lot, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (At(lot, path) is not JsonValue value) continue;
            if (value.TryGetValue<decimal>(out var d) && d > 0) return d;
            if (value.TryGetValue<double>(out var f) && f > 0) return (decimal)f;
            if (value.TryGetValue<string>(out var s) &&
                decimal.TryParse(s.Replace(",", "").Replace("$", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out d) && d > 0)
                return d;
        }

        return null;
    }

    private static int? Int(JsonObject lot, params string[] paths)
    {
        var value = Dec(lot, paths);
        return value is null ? null : (int)value.Value;
    }

    private static bool? Bool(JsonObject lot, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (At(lot, path) is not JsonValue value) continue;
            if (value.TryGetValue<bool>(out var b)) return b;
            if (value.TryGetValue<string>(out var s))
            {
                s = s.Trim().ToLowerInvariant();
                if (s is "yes" or "true" or "present" or "y") return true;
                if (s is "no" or "false" or "missing" or "n") return false;
            }
        }

        return null;
    }

    private static DateTime? Date(JsonObject lot, params string[] paths)
    {
        foreach (var path in paths)
        {
            var text = Text(lot, path);
            if (text is null) continue;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                return date.UtcDateTime;
            if (long.TryParse(text, out var unix) && unix > 1_000_000_000)
                return DateTimeOffset.FromUnixTimeSeconds(unix > 10_000_000_000 ? unix / 1000 : unix).UtcDateTime;
        }

        return null;
    }

    private static MoneyDto? Money(decimal? amount) => amount is null ? null : new MoneyDto(amount.Value, "USD");

    private static bool? RunAndDrive(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return null;
        condition = condition.ToLowerInvariant();
        if (condition.Contains("run") && condition.Contains("drive")) return true;
        if (condition.Contains("stationary") || condition.Contains("won't") || condition.Contains("not start")) return false;
        return null;
    }

    private static string? Platform(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" => null,
        "copart" => "Copart",
        "iaai" or "iaa" => "IAAI",
        _ => value
    };

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    // ============================================================
    // CACHE + BUDGET
    // ============================================================

    private async Task<string?> GetCachedOrFetchAsync(
        string key,
        TimeSpan maxAge,
        bool paid,
        bool preview,
        Func<HttpClient, Task<string?>> fetch,
        CancellationToken ct)
    {
        var cached = await ReadCacheAsync(key, maxAge, ct);
        if (cached is not null)
            return cached;

        if (paid && !await TryReserveRequestAsync(preview, ct))
            return null;

        try
        {
            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var body = await fetch(client);
            if (body is not null)
                await WriteCacheAsync(key, body, ct);
            return body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // An older copy is better than nothing when the provider is down.
            return await ReadCacheAsync(key, TimeSpan.MaxValue, ct);
        }
    }

    private async Task<string?> ReadCacheAsync(string key, TimeSpan maxAge, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Json, FetchedAt FROM VinCache WHERE CacheKey=$k";
            cmd.Parameters.AddWithValue("$k", key);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            if (maxAge != TimeSpan.MaxValue &&
                (!DateTimeOffset.TryParse(r.GetString(1), out var fetched) || DateTimeOffset.UtcNow - fetched > maxAge))
                return null;
            return r.GetString(0);
        }
        finally { _gate.Release(); }
    }

    private async Task WriteCacheAsync(string key, string json, CancellationToken ct)
    {
        if (json.Length > 2_000_000) return;
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO VinCache(CacheKey,Json,FetchedAt) VALUES($k,$j,$at)
ON CONFLICT(CacheKey) DO UPDATE SET Json=$j, FetchedAt=$at;";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$j", json);
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Counts one paid request against this month's budget.</summary>
    private async Task<bool> TryReserveRequestAsync(bool preview, CancellationToken ct)
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var limit = preview ? (int)Math.Floor(MonthlyBudget * PreviewShare) : MonthlyBudget;

        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO VinProviderUsage(Month,Requests) VALUES($m,0) ON CONFLICT(Month) DO NOTHING;
UPDATE VinProviderUsage SET Requests=Requests+1 WHERE Month=$m AND Requests < $limit;";
            cmd.Parameters.AddWithValue("$m", month);
            cmd.Parameters.AddWithValue("$limit", limit);
            var reserved = cmd.ExecuteNonQuery() > 0;
            if (!reserved)
                _log.LogWarning("VIN provider budget reached for {Month} ({Kind})", month, preview ? "preview" : "report");
            return reserved;
        }
        finally { _gate.Release(); }
    }

    public async Task<int> GetUsedRequestsAsync(CancellationToken ct)
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Requests FROM VinProviderUsage WHERE Month=$m";
            cmd.Parameters.AddWithValue("$m", month);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        finally { _gate.Release(); }
    }
}

public enum AuctionLookupState
{
    /// <summary>Provider answered (the list may be empty).</summary>
    Ready,
    /// <summary>Free preview skipped to protect the monthly budget.</summary>
    Deferred,
    /// <summary>Provider not configured or not reachable.</summary>
    Unavailable
}

public sealed record ProviderTrace(string Provider, int Status, int Items, string? Note);

public sealed record AuctionLookup(AuctionLookupState State, List<AuctionLotDto> Lots);

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record VinRecallDto(
    string? RecallId,
    DateTime? Date,
    string? Component,
    string? Description,
    string? Manufacturer,
    string? Remedy);

public sealed class AuctionLotDto
{
    public string? AuctionName { get; set; }
    public DateTime? SaleDate { get; set; }
    public string? LotNumber { get; set; }
    public string? Location { get; set; }
    public string? Seller { get; set; }
    public string? SaleType { get; set; }
    public string? PrimaryDamage { get; set; }
    public string? SecondaryDamage { get; set; }
    public string? Condition { get; set; }
    public decimal? OdometerValue { get; set; }
    public string? OdometerUnit { get; set; }
    public string? OdometerStatus { get; set; }
    public MoneyDto? EstimatedRetailValue { get; set; }
    public MoneyDto? RepairCost { get; set; }
    public MoneyDto? SoldPrice { get; set; }
    public bool? KeysAvailable { get; set; }
    public bool? RunAndDrive { get; set; }
    public bool? EngineStarts { get; set; }
    public string? TitleStatus { get; set; }
    public string? Vin { get; set; }
    public List<string> ImageUrls { get; set; } = [];
    public int PhotoCount { get; set; }
}
