using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;

namespace CarScanner.Api.Services;

/// <summary>
/// Photos of a VIN found on the open web (the same pictures a Google search
/// shows: auction archives, dealer pages, forums), through the official Brave
/// Search API. Two requests per VIN:
///  1. web search for the exact VIN - pages about this car and their preview
///     picture (usually the car's main photo);
///  2. image search - pictures whose page, title or address has the VIN, or
///     that come from one of the pages found in step 1.
/// Nothing from other cars is shown. Results are kept in memory a few hours.
/// </summary>
public sealed class WebImageSearchService
{
    private const string WebEndpoint = "https://api.search.brave.com/res/v1/web/search";
    private const string ImageEndpoint = "https://api.search.brave.com/res/v1/images/search";
    private static readonly TimeSpan CacheAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan EmptyCacheAge = TimeSpan.FromMinutes(30);

    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _config;
    private readonly ILogger<WebImageSearchService> _log;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, List<WebPhotoDto> Photos)> _cache = new();
    private readonly object _budgetGate = new();
    private string _budgetMonth = "";
    private int _budgetUsed;

    public WebImageSearchService(IHttpClientFactory clients, IConfiguration config, ILogger<WebImageSearchService> log)
    {
        _clients = clients;
        _config = config;
        _log = log;
    }

    private string? ApiKey
    {
        get
        {
            var key = _config["Vin:BraveApiKey"] ?? Environment.GetEnvironmentVariable("GCS_BRAVE_API_KEY");
            return string.IsNullOrWhiteSpace(key) || key == "SET_ON_SERVER_ONLY" ? null : key.Trim();
        }
    }

    public bool IsConfigured => ApiKey is not null;

    public bool IsAnyConfigured => ApiKey is not null || SerperKey is not null;

    /// <summary>Brave requests per month (Brave gives $5 = 1000 requests free each month).</summary>
    private int MonthlyBudget =>
        int.TryParse(_config["Vin:BraveMonthlyBudget"], out var value) && value > 0 ? value : 900;

    // ---------------- Serper.dev (Google results) ----------------
    private const string SerperSearch = "https://google.serper.dev/search";
    private const string SerperImages = "https://google.serper.dev/images";
    private readonly object _serperGate = new();
    private string _serperMonth = "";
    private int _serperUsed;

    private string? SerperKey
    {
        get
        {
            var key = _config["Vin:SerperApiKey"] ?? Environment.GetEnvironmentVariable("GCS_SERPER_API_KEY");
            return string.IsNullOrWhiteSpace(key) || key == "SET_ON_SERVER_ONLY" ? null : key.Trim();
        }
    }

    private int SerperBudget =>
        int.TryParse(_config["Vin:SerperMonthlyBudget"], out var value) && value > 0 ? value : 1000;

    /// <summary>
    /// Photos of the VIN from anywhere on the web. Google results (Serper) are
    /// tried first when a key is set, Brave after that when nothing was found.
    /// </summary>
    public async Task<List<WebPhotoDto>> FindAsync(string vin, CancellationToken ct)
    {
        if (_cache.TryGetValue(vin, out var hit) &&
            DateTimeOffset.UtcNow - hit.At < (hit.Photos.Count > 0 ? CacheAge : EmptyCacheAge))
        {
            VinDataService.Trace("web images (saved)", 200, hit.Photos.Count);
            return hit.Photos;
        }

        var photos = new List<WebPhotoDto>();
        if (SerperKey is not null)
            photos = await FindWithSerperAsync(vin, ct);
        else
            VinDataService.Trace("google (Serper)", 0, 0, "key not set in Azure");

        if (photos.Count == 0)
            photos = await FindWithBraveAsync(vin, ct);

        photos = photos
            .GroupBy(p => p.ImageUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(40)
            .ToList();
        _cache[vin] = (DateTimeOffset.UtcNow, photos);
        return photos;
    }

    private async Task<List<WebPhotoDto>> FindWithSerperAsync(string vin, CancellationToken ct)
    {
        var photos = new List<WebPhotoDto>();
        var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TrySerper())
        {
            var (status, root) = await PostSerperAsync(SerperSearch, new JsonObject { ["q"] = vin }, ct);
            if (root is null && status == 400)
                (status, root) = await PostSerperAsync(SerperSearch, new JsonObject { ["q"] = "VIN " + vin }, ct);
            var raw = 0;
            if (root?["organic"] is JsonArray organic)
            {
                foreach (var item in organic.OfType<JsonObject>())
                {
                    raw++;
                    var link = Str(item["link"]);
                    if (link is null || !(Has(link, vin) || Has(Str(item["title"]), vin) || Has(Str(item["snippet"]), vin)))
                        continue;
                    pages.Add(Normalize(link));
                    var image = Str(item["imageUrl"]);
                    if (image is not null)
                        photos.Add(new WebPhotoDto(image, image, link, Host(link)));
                }
            }
            VinDataService.Trace("google pages (Serper)", status, pages.Count, $"{raw} results");
        }
        else
        {
            VinDataService.Trace("google pages (Serper)", 0, 0, "monthly budget used up");
        }

        if (TrySerper())
        {
            var (status, root) = await PostSerperAsync(SerperImages, new JsonObject { ["q"] = vin, ["num"] = 100 }, ct);
            var raw = 0;
            var kept = 0;
            if (root?["images"] is JsonArray images)
            {
                foreach (var item in images.OfType<JsonObject>())
                {
                    raw++;
                    var full = Str(item["imageUrl"]);
                    var thumb = Str(item["thumbnailUrl"]) ?? full;
                    var link = Str(item["link"]);
                    if (full is null || thumb is null)
                        continue;
                    var ok = Has(Str(item["title"]), vin) || Has(link, vin) || Has(full, vin) ||
                             (link is not null && pages.Contains(Normalize(link)));
                    if (!ok)
                        continue;
                    kept++;
                    photos.Add(new WebPhotoDto(thumb, full, link, Str(item["domain"]) ?? Host(link ?? full)));
                }
            }
            VinDataService.Trace("google images (Serper)", status, kept, $"{raw} results");
        }

        return photos;
    }

    private async Task<(int Status, JsonNode? Root)> PostSerperAsync(string url, JsonObject body, CancellationToken ct)
    {
        try
        {
            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-API-KEY", SerperKey);
            using var response = await client.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Serper answered {Status}: {Body}", status, error.Length > 300 ? error[..300] : error);
                return (status, null);
            }
            return (status, JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _log.LogWarning("Serper failed: {Message}", ex.Message);
            return (503, null);
        }
    }

    private bool TrySerper()
    {
        lock (_serperGate)
        {
            var month = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (month != _serperMonth)
            {
                _serperMonth = month;
                _serperUsed = 0;
            }
            if (_serperUsed >= SerperBudget)
                return false;
            _serperUsed++;
            return true;
        }
    }

    // ---------------- Brave Search ----------------
    private async Task<List<WebPhotoDto>> FindWithBraveAsync(string vin, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            VinDataService.Trace("web images (Brave)", 0, 0, "key not set in Azure");
            return [];
        }

        var photos = new List<WebPhotoDto>();
        var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Pages that mention this exact VIN (and their preview picture).
        if (TryReserve())
        {
            var url = $"{WebEndpoint}?q={Uri.EscapeDataString("\"" + vin + "\"")}&count=20&spellcheck=false&extra_snippets=true&safesearch=strict";
            var (status, root) = await GetAsync(url, ct);
            var raw = 0;
            if (root?["web"]?["results"] is JsonArray results)
            {
                foreach (var item in results)
                {
                    if (item is not JsonObject r)
                        continue;
                    raw++;
                    var page = Str(r["url"]);
                    var snippets = r["extra_snippets"] is JsonArray ex ? string.Join(" ", ex.Select(Str)) : null;
                    if (page is null || !(Has(page, vin) || Has(Str(r["title"]), vin) ||
                                          Has(Str(r["description"]), vin) || Has(snippets, vin)))
                        continue;

                    pages.Add(Normalize(page));
                    var isLogo = r["thumbnail"]?["logo"] is JsonValue lv && lv.TryGetValue<bool>(out var logo) && logo;
                    var original = Str(r["thumbnail"]?["original"]);
                    var thumb = Str(r["thumbnail"]?["src"]) ?? original;
                    if (!isLogo && thumb is not null)
                        photos.Add(new WebPhotoDto(thumb, original ?? thumb, page, Host(page)));
                }
            }
            VinDataService.Trace("web pages (Brave)", status, pages.Count, $"{raw} results, {photos.Count} pictures");
        }
        else
        {
            VinDataService.Trace("web pages (Brave)", 0, 0, "monthly budget used up");
        }

        // 2. Pictures of this VIN, or from the pages found above.
        if (TryReserve())
        {
            var url = $"{ImageEndpoint}?q={Uri.EscapeDataString(vin)}&count=100&spellcheck=false&safesearch=strict";
            var (status, root) = await GetAsync(url, ct);
            var raw = 0;
            var kept = 0;
            if (root?["results"] is JsonArray results)
            {
                foreach (var item in results)
                {
                    if (item is not JsonObject r)
                        continue;
                    raw++;
                    var page = Str(r["url"]);
                    var full = Str(r["properties"]?["url"]);
                    var thumb = Str(r["thumbnail"]?["src"]) ?? full;
                    if (thumb is null || full is null)
                        continue;

                    var ok = Has(Str(r["title"]), vin) || Has(page, vin) || Has(full, vin) ||
                             (page is not null && pages.Contains(Normalize(page)));
                    if (!ok)
                        continue;
                    kept++;
                    photos.Add(new WebPhotoDto(thumb, full, page, Host(page ?? full)));
                }
            }
            VinDataService.Trace("web images (Brave)", status, kept, $"{raw} results");
        }
        else
        {
            VinDataService.Trace("web images (Brave)", 0, 0, "monthly budget used up");
        }

        return photos;
    }

    private async Task<(int Status, JsonNode? Root)> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Subscription-Token", ApiKey);
            using var response = await client.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Brave search answered {Status}", status);
                return (status, null);
            }
            return (status, JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _log.LogWarning("Brave search failed: {Message}", ex.Message);
            return (503, null);
        }
    }

    private bool TryReserve()
    {
        lock (_budgetGate)
        {
            var month = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (month != _budgetMonth)
            {
                _budgetMonth = month;
                _budgetUsed = 0;
            }

            if (_budgetUsed >= MonthlyBudget)
                return false;
            _budgetUsed++;
            return true;
        }
    }

    private static string Normalize(string url) =>
        url.Split('#')[0].TrimEnd('/').Replace("://www.", "://", StringComparison.OrdinalIgnoreCase);

    private static string? Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "") : null;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static bool Has(string? text, string vin) =>
        text is not null && text.Contains(vin, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A picture found on the web, with the page it comes from.</summary>
public sealed record WebPhotoDto(string ThumbnailUrl, string ImageUrl, string? PageUrl, string? Source);
