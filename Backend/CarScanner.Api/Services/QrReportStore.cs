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

    /// <summary>Vehicle shown on the report: the app's name, else the masked VIN, else "OBD360".</summary>
    public static string VehicleName(QrReportCreateRequest request)
    {
        var vehicle = (request.Vehicle ?? string.Empty).Trim();
        if (vehicle.Length > 0)
            return vehicle.Length > MaxVehicle ? vehicle[..MaxVehicle] : vehicle;
        var vin = (request.Vin ?? string.Empty).Trim().ToUpperInvariant();
        return vin.Length == 17 ? $"OBD360 · VIN ***{vin[^6..]}" : "OBD360";
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
        // OBD360: neutral wording, no "valid" badge.
        var note = expired
            ? "<div class=\"note bad\">ბმულს ვადა გაუვიდა<br>Link expired</div>"
            : "<div class=\"note\">ანგარიში შექმნა ავტომობილის მფლობელმა OBD360-ით და დამოუკიდებლად გადამოწმებული არ არის.<br>Created by the vehicle owner with OBD360 — not independently verified.</div>";
        var vinLine = string.IsNullOrWhiteSpace(report.VinMasked) ? string.Empty : "<br>VIN: " + H(report.VinMasked);
        return """
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light dark">
<title>OBD360 — QR Report</title>
<link rel="icon" type="image/png" href="/assets/favicon.png"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png">
<style>
:root{--bg:#fafaf8;--paper:#fff;--ink:#1c2128;--muted:#667085;--line:#e4e7ec;--mark:#d99a00}
@media (prefers-color-scheme:dark){:root{--bg:#14171b;--paper:#1b1f24;--ink:#eceff3;--muted:#9aa4b2;--line:#2c333b;--mark:#e6b422}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.65 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;-webkit-text-size-adjust:100%}
.wrap{max-width:680px;margin:0 auto;padding:20px 18px 44px}
header{display:flex;align-items:center;gap:10px;padding-bottom:14px;border-bottom:1px solid var(--line)}
header img{height:26px;width:auto}
header b{font-size:17px}
h1{font-size:26px;line-height:1.25;margin:22px 0 6px;font-weight:650}
.sub{color:var(--muted);font-size:14px;line-height:1.6}
.note{margin:16px 0 0;padding:10px 14px;border-left:3px solid var(--mark);background:var(--paper);color:var(--muted);font-size:13.5px;line-height:1.55}
.note.bad{border-left-color:#c0392b;color:var(--ink);font-weight:600}
section{margin-top:22px;padding-top:20px;border-top:1px solid var(--line)}
h2{font-size:14px;font-weight:650;margin:0 0 6px;color:var(--muted)}
section p{margin:0;font-size:16.5px;overflow-wrap:anywhere}
section p.small{margin-top:8px;font-size:13px;color:var(--muted)}
.foot{margin-top:30px;padding-top:16px;border-top:1px solid var(--line);color:var(--muted);font-size:12.5px;line-height:1.6}
</style></head><body><div class="wrap">
<header><img src="/assets/mark.png" alt=""><b>OBD360</b></header>
<h1>__VEHICLE__</h1>
<div class="sub">წაკითხვის დრო / Read at: __CHECKED__<br>ბმული ძალაშია / Link available until: __EXPIRES____VIN__</div>
__NOTE__
<section><h2>სტატუსი / Status</h2><p>__STATUS__</p></section>
<section><h2>სკანის შეჯამება / Scan summary</h2><p>__SUMMARY__</p></section>
<section><h2>შეცდომის კოდები / Trouble codes</h2><p>__DTC__</p></section>
<section><h2>Check Engine / Readiness</h2><p>__READY__</p></section>
__SERVICE__
<div class="foot">ანგარიში შექმნა ავტომობილის მფლობელმა OBD360 აპით და ასახავს მხოლოდ მის მიერ გამოგზავნილ მონაცემებს; OBD360 მათ დამოუკიდებლად არ ამოწმებს.<br>This report was created by the vehicle owner with the OBD360 app and shows only the data the owner's app sent; OBD360 does not verify it independently.</div>
</div></body></html>
"""
            .Replace("__NOTE__", note)
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
                : "<section><h2>სერვისის ისტორია / Service history</h2><p>" + H(report.ServiceHistory) +
                  "</p><p class=\"small\">მფლობელის მიერ შეყვანილი, სერვისცენტრის მიერ დაუდასტურებელი. / Entered by the owner, not confirmed by a service centre.</p></section>");
    }

    public static string RenderExpiredHtml() => """
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light dark">
<title>OBD360 — Link expired</title>
<link rel="icon" type="image/png" href="/assets/favicon.png"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png">
<style>
:root{--bg:#fafaf8;--ink:#1c2128;--muted:#667085;--line:#e4e7ec}
@media (prefers-color-scheme:dark){:root{--bg:#14171b;--ink:#eceff3;--muted:#9aa4b2;--line:#2c333b}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.65 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
.wrap{max-width:560px;margin:0 auto;padding:20px 18px 44px}
header{display:flex;align-items:center;gap:10px;padding-bottom:14px;border-bottom:1px solid var(--line)}
header img{height:26px;width:auto}header b{font-size:17px}
h1{font-size:22px;margin:24px 0 6px}p{margin:0;color:var(--muted)}h1+p{margin-bottom:22px}
</style></head>
<body><div class="wrap"><header><img src="/assets/mark.png" alt=""><b>OBD360</b></header>
<h1>ბმულს ვადა გაუვიდა</h1><p>QR ანგარიშის ბმული ხელმისაწვდომია 30 დღე, რადგან ძველი ანგარიში აღარ ასახავს ავტომობილის მიმდინარე მდგომარეობას.</p>
<h1>Link expired</h1><p>A QR report link is available for 30 days because an older report no longer reflects the vehicle's current condition.</p></div></body></html>
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
