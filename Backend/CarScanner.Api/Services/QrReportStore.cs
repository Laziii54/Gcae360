using System.Net;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

public sealed class QrReportStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public QrReportStore(IHostEnvironment env, IConfiguration config)
    {
        _connectionString = BackendDataPath.CreateConnectionString(BackendDataPath.ResolveDatabasePath(env, config));
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"CREATE TABLE IF NOT EXISTS QrReports (
TokenHash TEXT PRIMARY KEY,
OwnerHash TEXT NOT NULL,
Vehicle TEXT NOT NULL,
VinMasked TEXT NULL,
CheckedAt TEXT NOT NULL,
ExpiresAt TEXT NOT NULL,
Status TEXT NOT NULL,
Summary TEXT NOT NULL,
DtcSummary TEXT NOT NULL,
ReadinessSummary TEXT NOT NULL,
CreatedAt TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS IX_QrReports_ExpiresAt ON QrReports(ExpiresAt);";
        command.ExecuteNonQuery();

        // Added later: owner-entered service history (older databases lack the column).
        using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('QrReports') WHERE name='ServiceHistory'";
        if (Convert.ToInt64(columns.ExecuteScalar()) == 0)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE QrReports ADD COLUMN ServiceHistory TEXT NULL";
            alter.ExecuteNonQuery();
        }
    }

    public const int MaxVehicle = 160;
    public const int MaxStatus = 120;
    public const int MaxSummary = 4000;
    public const int MaxDtcSummary = 8000;
    public const int MaxReadiness = 4000;
    public const int MaxServiceHistory = 4000;

    /// <summary>
    /// Only the inspection date can make a report invalid. Oversized text
    /// fields are trimmed (see <see cref="CreateAsync"/>) and an unknown
    /// vehicle gets a default name instead of a 400.
    /// </summary>
    public static string? Validate(QrReportCreateRequest? request)
    {
        if (request is null) return "invalid report";
        if (request.CheckedAt == default ||
            request.CheckedAt.ToUniversalTime() < DateTime.UtcNow.AddYears(-2) ||
            request.CheckedAt.ToUniversalTime() > DateTime.UtcNow.AddMinutes(10)) return "invalid inspection date";
        return null;
    }

    /// <summary>Vehicle shown on the report: the app's name, else the masked VIN, else "GCar360".</summary>
    public static string VehicleName(QrReportCreateRequest request)
    {
        var vehicle = (request.Vehicle ?? string.Empty).Trim();
        if (vehicle.Length > 0)
            return vehicle.Length > MaxVehicle ? vehicle[..MaxVehicle] : vehicle;
        var vin = (request.Vin ?? string.Empty).Trim().ToUpperInvariant();
        return vin.Length == 17 ? $"GCar360 · VIN ***{vin[^6..]}" : "GCar360";
    }

    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(string ownerKey, QrReportCreateRequest request, CancellationToken token)
    {
        var rawToken = Base64Url(RandomNumberGenerator.GetBytes(24));
        var expires = DateTime.UtcNow.AddDays(30);
        var vin = (request.Vin ?? string.Empty).Trim().ToUpperInvariant();
        var masked = vin.Length == 17 ? $"***{vin[^6..]}" : string.Empty;

        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = @"DELETE FROM QrReports WHERE ExpiresAt < $now;
INSERT INTO QrReports(TokenHash,OwnerHash,Vehicle,VinMasked,CheckedAt,ExpiresAt,Status,Summary,DtcSummary,ReadinessSummary,CreatedAt,ServiceHistory)
VALUES($t,$o,$v,$vin,$checked,$expires,$status,$summary,$dtc,$ready,$now,$service);";
            command.Parameters.AddWithValue("$t", Hash(rawToken));
            command.Parameters.AddWithValue("$o", Hash(ownerKey));
            command.Parameters.AddWithValue("$v", VehicleName(request));
            command.Parameters.AddWithValue("$vin", masked);
            command.Parameters.AddWithValue("$checked", request.CheckedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$expires", expires.ToString("O"));
            command.Parameters.AddWithValue("$status", Cut(request.Status, MaxStatus));
            command.Parameters.AddWithValue("$summary", Cut(request.Summary, MaxSummary));
            command.Parameters.AddWithValue("$dtc", Cut(request.DtcSummary, MaxDtcSummary));
            command.Parameters.AddWithValue("$ready", Cut(request.ReadinessSummary, MaxReadiness));
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$service", string.IsNullOrWhiteSpace(request.ServiceHistory) ? DBNull.Value : Cut(request.ServiceHistory, MaxServiceHistory));
            await command.ExecuteNonQueryAsync(token);
        }
        finally { _gate.Release(); }

        return (rawToken, expires);
    }

    /// <summary>Account deletion: removes every QR report the account created.</summary>
    public async Task<int> DeleteOwnerAsync(string ownerKey, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM QrReports WHERE OwnerHash=$o";
            command.Parameters.AddWithValue("$o", Hash(ownerKey));
            return await command.ExecuteNonQueryAsync(token);
        }
        finally { _gate.Release(); }
    }

    public async Task<QrPublicReport?> GetAsync(string tokenValue, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(tokenValue) || tokenValue.Length is < 24 or > 80)
            return null;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT Vehicle,VinMasked,CheckedAt,ExpiresAt,Status,Summary,DtcSummary,ReadinessSummary,ServiceHistory
FROM QrReports WHERE TokenHash=$t LIMIT 1";
        command.Parameters.AddWithValue("$t", Hash(tokenValue));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return new QrPublicReport(
            reader.GetString(0), reader.GetString(1), DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    public static string RenderHtml(QrPublicReport report)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty).Replace("\n", "<br>");
        var expired = report.ExpiresAt <= DateTime.UtcNow;
        // The content is sent by the app (owner's phone), not verified by
        // GCar360: neutral wording, no "valid" badge.
        var accent = expired ? "#ef4444" : "#facc15";
        var state = expired
            ? "ბმულს ვადა გაუვიდა / Link expired"
            : "ავტომობილის მფლობელის მიერ GCar360-ით შექმნილი ანგარიში — დამოუკიდებლად გადამოწმებული არ არის<br>Report created by the vehicle owner with GCar360 — not independently verified";
        var vinLine = string.IsNullOrWhiteSpace(report.VinMasked) ? string.Empty : "<br>VIN: " + H(report.VinMasked);
        return """
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>GCar360 — QR Report</title><style>
body{margin:0;background:#07111f;color:#eef5ff;font-family:system-ui,-apple-system,Segoe UI,sans-serif}main{max-width:760px;margin:auto;padding:22px}
.brand{font-weight:800;color:#38bdf8;font-size:20px}.card{background:#101c2d;border:1px solid #24364d;border-radius:18px;padding:18px;margin-top:14px}
.state{color:__ACCENT__;font-weight:800;font-size:18px}h1{font-size:24px;margin:10px 0}h2{font-size:15px;color:#9fb1c7;margin:0 0 8px}p{line-height:1.55;margin:0}.meta{color:#9fb1c7;font-size:13px}
</style></head><body><main><div class="brand">GCar360</div><div class="card"><div class="state">__STATE__</div><h1>__VEHICLE__</h1>
<p class="meta">წაკითხვის დრო (აპის მონაცემით) / Read at (as reported by the app): __CHECKED__<br>ბმული ხელმისაწვდომია / Link available until: __EXPIRES____VIN__</p></div>
<div class="card"><h2>სტატუსი / Status</h2><p>__STATUS__</p></div>
<div class="card"><h2>სკანის შეჯამება / Scan summary</h2><p>__SUMMARY__</p></div>
<div class="card"><h2>შეცდომის კოდები / Trouble codes</h2><p>__DTC__</p></div>
<div class="card"><h2>Check Engine / Readiness</h2><p>__READY__</p></div>
__SERVICE__
<p class="meta" style="margin-top:16px;text-align:center">ანგარიში შექმნა ავტომობილის მფლობელმა GCar360 აპით და ასახავს მხოლოდ მის მიერ გამოგზავნილ მონაცემებს; GCar360 მათ დამოუკიდებლად არ ამოწმებს. / This report was created by the vehicle owner with the GCar360 app and shows only the data the owner's app sent; GCar360 does not verify it independently.</p>
</main></body></html>
"""
            .Replace("__ACCENT__", accent)
            .Replace("__STATE__", state)
            .Replace("__VEHICLE__", H(report.Vehicle))
            .Replace("__CHECKED__", report.CheckedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
            .Replace("__EXPIRES__", report.ExpiresAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
            .Replace("__VIN__", vinLine)
            .Replace("__STATUS__", H(report.Status))
            .Replace("__SUMMARY__", H(report.Summary))
            .Replace("__DTC__", H(report.DtcSummary))
            .Replace("__READY__", H(report.ReadinessSummary))
            .Replace("__SERVICE__", string.IsNullOrWhiteSpace(report.ServiceHistory)
                ? string.Empty
                : "<div class=\"card\"><h2>სერვისის ისტორია / Service history</h2><p>" + H(report.ServiceHistory) +
                  "</p><p class=\"meta\" style=\"margin-top:8px\">მფლობელის მიერ შეყვანილი, სერვისცენტრის მიერ დაუდასტურებელი. / Entered by the owner, not confirmed by a service centre.</p></div>");
    }

    public static string RenderExpiredHtml() => """
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>GCar360 — Link expired</title><style>body{margin:0;background:#07111f;color:#eef5ff;font-family:system-ui,-apple-system,Segoe UI,sans-serif}main{max-width:620px;margin:12vh auto;padding:24px}.card{background:#101c2d;border:1px solid #24364d;border-radius:18px;padding:22px}h1{color:#ef4444;font-size:24px}p{line-height:1.6;color:#b6c5d8}</style></head>
<body><main><div class="card"><div style="font-weight:800;color:#38bdf8">GCar360</div><h1>ბმულს ვადა გაუვიდა</h1><p>QR ანგარიშის ბმული ხელმისაწვდომია 30 დღე, რადგან ძველი ანგარიში აღარ ასახავს ავტომობილის მიმდინარე მდგომარეობას.</p><h1>Link expired</h1><p>A QR report link is available for 30 days because an older report no longer reflects the vehicle's current condition.</p></div></main></body></html>
""";

    private static string Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var text = value.Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("gcar360-qr:" + value)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record QrReportCreateRequest(string? Vehicle, string? Vin, DateTime CheckedAt, string? Status, string? Summary, string? DtcSummary, string? ReadinessSummary, string? ServiceHistory = null);
public sealed record QrPublicReport(string Vehicle, string VinMasked, DateTime CheckedAt, DateTime ExpiresAt, string Status, string Summary, string DtcSummary, string ReadinessSummary, string? ServiceHistory = null);
