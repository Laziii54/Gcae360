using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarScanner.Api.Services;

/// <summary>
/// Google Play install statistics (installs, uninstalls, active devices)
/// from the monthly reports Google Play writes to the developer's Cloud
/// Storage bucket. Uses the same service account as purchase checks
/// (Stores:Google:ServiceAccountJson); it needs the Play Console permission
/// "View app information and download bulk reports". The bucket id is set
/// with GCS_PLAY_REPORTS_BUCKET (Play Console → Download reports →
/// "Copy Cloud Storage URI", e.g. pubsite_prod_rev_0123456789).
/// </summary>
public sealed class PlayStatsService
{
    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _config;
    private readonly ILogger<PlayStatsService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, PlayStats Stats)? _cache;

    public PlayStatsService(IHttpClientFactory clients, IConfiguration config, ILogger<PlayStatsService> log)
    {
        _clients = clients;
        _config = config;
        _log = log;
    }

    private string? ServiceJson =>
        _config["Stores:Google:ServiceAccountJson"] ?? Environment.GetEnvironmentVariable("GCS_GOOGLE_SERVICE_ACCOUNT_JSON");

    private string? Bucket
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("GCS_PLAY_REPORTS_BUCKET") ?? _config["Stores:Google:ReportsBucket"];
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            if (raw.StartsWith("gs://", StringComparison.OrdinalIgnoreCase)) raw = raw[5..];
            return raw.Split('/')[0];
        }
    }

    private string PackageName =>
        (_config["Stores:Google:PackageName"] ?? Environment.GetEnvironmentVariable("GCS_ANDROID_PACKAGE_NAME") ?? "com.obd360.app").Trim();

    public async Task<PlayStats> GetAsync(CancellationToken ct)
    {
        if (Bucket is null || string.IsNullOrWhiteSpace(ServiceJson))
            return PlayStats.NotConfigured("GCS_PLAY_REPORTS_BUCKET ან Google service account არ არის მითითებული");

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is { } hit && DateTimeOffset.UtcNow - hit.At < TimeSpan.FromHours(3))
                return hit.Stats;

            var token = await TokenAsync(ct);
            if (token is null)
                return PlayStats.NotConfigured("Google-ის ავტორიზაცია ვერ მოხერხდა (შეამოწმე service account)");

            var days = new SortedDictionary<DateOnly, PlayDay>();
            var now = DateTime.UtcNow;
            string? error = null;
            foreach (var month in new[] { now.AddMonths(-1), now })
            {
                var name = $"stats/installs/installs_{PackageName}_{month:yyyyMM}_overview.csv";
                var (ok, text, status) = await DownloadAsync(token, name, ct);
                if (!ok)
                {
                    if (status is 403 or 401) error = "Google-მა წვდომა არ მისცა (service account-ს Play Console-ში reports-ის უფლება სჭირდება)";
                    continue;
                }
                foreach (var day in Parse(text!))
                    days[day.Date] = day;
            }

            PlayStats stats;
            if (days.Count == 0)
            {
                stats = PlayStats.NotConfigured(error ?? "რეპორტები ჯერ არ არის (Google Play მათ დღეში ერთხელ ქმნის)");
            }
            else
            {
                var list = days.Values.ToList();
                var last = list[^1];
                var last30 = list.Where(d => d.Date > DateOnly.FromDateTime(now.AddDays(-30))).ToList();
                stats = new PlayStats(
                    true,
                    null,
                    last.Date,
                    last.TotalUserInstalls,
                    last.ActiveDeviceInstalls,
                    last30.Sum(d => d.DailyUserInstalls),
                    last30.Sum(d => d.DailyUserUninstalls),
                    last30.TakeLast(30).ToList());
            }

            _cache = (DateTimeOffset.UtcNow, stats);
            return stats;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or CryptographicException or FormatException)
        {
            _log.LogWarning("Play stats failed: {Message}", ex.Message);
            return PlayStats.NotConfigured("Google Play-ის სტატისტიკა ვერ წავიკითხე: " + ex.GetType().Name);
        }
        finally { _gate.Release(); }
    }

    private async Task<(bool Ok, string? Text, int Status)> DownloadAsync(string token, string objectName, CancellationToken ct)
    {
        var url = $"https://storage.googleapis.com/storage/v1/b/{Uri.EscapeDataString(Bucket!)}/o/{Uri.EscapeDataString(objectName)}?alt=media";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (false, null, (int)response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        // Play reports are UTF-16 LE with a BOM.
        var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
            : Encoding.UTF8.GetString(bytes);
        return (true, text, 200);
    }

    public static List<PlayDay> Parse(string csv)
    {
        var lines = csv.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return [];
        var header = lines[0].Split(',').Select(h => h.Trim().Trim('"')).ToList();
        int Col(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int date = Col("Date"), total = Col("Total User Installs"), active = Col("Active Device Installs"),
            installs = Col("Daily User Installs"), uninstalls = Col("Daily User Uninstalls");
        if (date < 0) return [];

        var result = new List<PlayDay>();
        foreach (var line in lines.Skip(1))
        {
            var f = line.Split(',').Select(v => v.Trim().Trim('"')).ToArray();
            if (f.Length <= date || !DateOnly.TryParse(f[date], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                continue;
            int N(int i) => i >= 0 && i < f.Length && int.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
            result.Add(new PlayDay(d, N(total), N(active), N(installs), N(uninstalls)));
        }
        return result;
    }

    private async Task<string?> TokenAsync(CancellationToken ct)
    {
        using var creds = JsonDocument.Parse(ServiceJson!);
        var email = creds.RootElement.GetProperty("client_email").GetString();
        var key = (creds.RootElement.GetProperty("private_key").GetString() ?? "").Replace("\\n", "\n");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key))
            return null;

        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var head = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" })));
        var claims = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = email,
            scope = "https://www.googleapis.com/auth/devstorage.read_only",
            aud = "https://oauth2.googleapis.com/token",
            iat = now,
            exp = now + 3600
        })));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key);
        var unsigned = head + "." + claims;
        var signature = B64(rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = unsigned + "." + signature
            })
        };
        using var response = await _clients.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }
}

public sealed record PlayDay(DateOnly Date, int TotalUserInstalls, int ActiveDeviceInstalls, int DailyUserInstalls, int DailyUserUninstalls);

public sealed record PlayStats(
    bool Available,
    string? Note,
    DateOnly? AsOf,
    int TotalInstalls,
    int ActiveDevices,
    int Installs30,
    int Uninstalls30,
    List<PlayDay> Days)
{
    public static PlayStats NotConfigured(string note) => new(false, note, null, 0, 0, 0, 0, []);
}
