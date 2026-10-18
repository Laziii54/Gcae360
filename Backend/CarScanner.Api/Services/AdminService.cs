using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Owner page (/admin). Security:
///  • only e-mails in GCS_ADMIN_EMAILS, with their app password;
///  • 2-step code from an authenticator app — MANDATORY: without
///    GCS_ADMIN_TOTP_SECRET nobody can sign in;
///  • admin perks (free PRO / VIN) only for accounts that completed at
///    least one successful 2-step sign-in (AdminTotpLogins);
///  • optional IP allow-list GCS_ADMIN_IPS;
///  • encrypted cookie (Data Protection) bound to the browser, 4 hours;
///  • CSRF token on every form, strict security headers;
///  • failed sign-ins are rate limited and every action is logged.
/// </summary>
public sealed class AdminService
{
    public const string CookieName = "gcs_admin";
    private static readonly TimeSpan SessionAge = TimeSpan.FromHours(4);

    private readonly string _connectionString;
    private readonly IDataProtector _protector;
    private readonly ConcurrentDictionary<string, long> _usedTotpSteps = new(StringComparer.OrdinalIgnoreCase);

    public AdminService(IHostEnvironment env, IConfiguration config, IDataProtectionProvider protection)
    {
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));
        _protector = protection.CreateProtector("GCar360.Admin.v2");

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS AdminAudit (
Id INTEGER PRIMARY KEY AUTOINCREMENT,
At TEXT NOT NULL,
Email TEXT NULL,
Action TEXT NOT NULL,
Target TEXT NULL,
Ip TEXT NULL);
CREATE TABLE IF NOT EXISTS AdminTotpLogins (
AccountId TEXT PRIMARY KEY,
Email TEXT NOT NULL,
FirstAt TEXT NOT NULL,
LastAt TEXT NOT NULL);";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT AccountId FROM AdminTotpLogins";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            VerifiedAccounts[reader.GetString(0)] = true;
    }

    // Accounts that signed in to /admin with password + authenticator code.
    private static readonly ConcurrentDictionary<string, bool> VerifiedAccounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when this account has completed at least one successful
    /// 2-step admin sign-in. Admin perks (AdminAccess) require it, so an
    /// account registered with an admin e-mail by someone else gets nothing.
    /// </summary>
    public static bool IsTotpVerified(string? accountId) =>
        !string.IsNullOrWhiteSpace(accountId) && VerifiedAccounts.ContainsKey(accountId);

    public async Task MarkTotpLoginAsync(string accountId, string email, CancellationToken ct)
    {
        await using var c = new SqliteConnection(_connectionString);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO AdminTotpLogins(AccountId,Email,FirstAt,LastAt) VALUES($id,$e,$at,$at)
ON CONFLICT(AccountId) DO UPDATE SET Email=excluded.Email, LastAt=excluded.LastAt";
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.Parameters.AddWithValue("$e", email);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        VerifiedAccounts[accountId] = true;
    }

    /// <summary>Account deletion: the perk flag goes with the account.</summary>
    public async Task ForgetAccountAsync(string accountId, CancellationToken ct)
    {
        await using var c = new SqliteConnection(_connectionString);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM AdminTotpLogins WHERE AccountId=$id";
        cmd.Parameters.AddWithValue("$id", accountId);
        await cmd.ExecuteNonQueryAsync(ct);
        VerifiedAccounts.TryRemove(accountId, out _);
    }

    // ============================================================
    // SECURITY
    // ============================================================

    public sealed record Session(string Email, string Csrf);

    public static string? TotpSecret
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("GCS_ADMIN_TOTP_SECRET");
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Replace(" ", "").Trim().ToUpperInvariant();
        }
    }

    public static bool TwoStepEnabled => TotpSecret is not null;

    /// <summary>True when GCS_ADMIN_IPS is empty or contains the caller's IP.</summary>
    public static bool IpAllowed(HttpContext http)
    {
        var list = Environment.GetEnvironmentVariable("GCS_ADMIN_IPS");
        if (string.IsNullOrWhiteSpace(list))
            return true;
        var ip = ClientIp(http);
        return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(allowed => allowed == ip);
    }

    /// <summary>
    /// The one client-IP helper for every rate limit, IP hash and audit line.
    /// X-Forwarded-For is used only when the request came through the Azure
    /// front end (ARR sets X-ARR-LOG-ID / X-ARR-SSL). ARR APPENDS the real
    /// client address, so its LAST entry is taken — earlier entries are sent
    /// by the client and can be forged. Otherwise the connection address
    /// (after UseForwardedHeaders) is used. Ports are stripped.
    /// </summary>
    public static string ClientIp(HttpContext http)
    {
        var headers = http.Request.Headers;
        if (headers.ContainsKey("X-ARR-LOG-ID") || headers.ContainsKey("X-ARR-SSL"))
        {
            var last = headers["X-Forwarded-For"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(last) && System.Net.IPEndPoint.TryParse(last, out var endpoint))
                return Normalize(endpoint.Address);
        }

        var remote = http.Connection.RemoteIpAddress;
        return remote is null ? "unknown" : Normalize(remote);
    }

    private static string Normalize(System.Net.IPAddress address) =>
        (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();

    private static string AgentHash(HttpRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Headers.UserAgent.ToString())))[..16];

    public string CreateCookie(string email, HttpRequest request)
    {
        var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expires = DateTimeOffset.UtcNow.Add(SessionAge).ToUnixTimeSeconds();
        return _protector.Protect($"{email}\n{expires}\n{csrf}\n{AgentHash(request)}");
    }

    public Session? ReadSession(HttpRequest request, Func<string?, bool> isAdmin)
    {
        var raw = request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            var parts = _protector.Unprotect(raw).Split('\n');
            if (parts.Length != 4 ||
                !long.TryParse(parts[1], out var expires) ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires ||
                parts[3] != AgentHash(request) ||
                !isAdmin(parts[0]))
                return null;
            return new Session(parts[0], parts[2]);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/admin",
        MaxAge = SessionAge,
        IsEssential = true
    };

    public static void SecureHeaders(HttpResponse response)
    {
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        // The only script on admin pages is AdminPages.ConfirmScript (static,
        // no user data); it is allowed by its hash, inline handlers are not.
        response.Headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; script-src '" + ConfirmScriptHash + "'; img-src 'self' data:; media-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    }

    private static readonly string ConfirmScriptHash =
        "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(AdminPages.ConfirmScript)));

    /// <summary>
    /// Checks a 6-digit authenticator code (±30 s), each code once.
    /// Without GCS_ADMIN_TOTP_SECRET every sign-in is refused.
    /// </summary>
    public bool VerifyTotp(string email, string? code)
    {
        var secret = TotpSecret;
        if (secret is null)
            return false;
        code = (code ?? "").Replace(" ", "");
        if (code.Length != 6 || !code.All(char.IsDigit))
            return false;

        byte[] key;
        try { key = Base32Decode(secret); }
        catch (FormatException) { return false; }

        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            var s = step + offset;
            if (Totp(key, s) != code)
                continue;
            if (_usedTotpSteps.TryGetValue(email, out var used) && used >= s)
                return false;
            _usedTotpSteps[email] = s;
            return true;
        }
        return false;
    }

    private static string Totp(byte[] key, long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static string NewTotpSecret()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = RandomNumberGenerator.GetBytes(20);
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var ch in text.TrimEnd('='))
        {
            var value = alphabet.IndexOf(ch);
            if (value < 0) throw new FormatException("Bad base32");
            buffer = (buffer << 5) | value; bits += 5;
            if (bits >= 8) { output.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return output.ToArray();
    }

    public async Task AuditAsync(string? email, string action, string? target, string ip, CancellationToken ct)
    {
        await using var c = new SqliteConnection(_connectionString);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO AdminAudit(At,Email,Action,Target,Ip) VALUES($at,$e,$a,$t,$ip)";
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$e", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", action);
        cmd.Parameters.AddWithValue("$t", (object?)target ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ip", ip);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ============================================================
    // DATA
    // ============================================================

    public sealed record Overview(
        int Accounts, int NewAccounts7, int Banned,
        int Devices, int Active1, int Active7, int Active30, int Inactive30,
        int NotificationsAllowed, int RemindersOn, int SignedInDevices,
        int ActiveSubscriptions, int SubsGoogle, int SubsWeb, int SubsOther, int SubsObd, int SubsVin, int SubsFull, int Expiring7,
        decimal RevenueMonth, int PaidMonth, decimal RevenueTotal, int PaidTotal, int FullReportsMonth, int Problems,
        int VinUnlocksMonth, int VinCreditsLeft, int Crashes7,
        List<(string Label, int Count)> Versions, List<(string Label, int Count)> Languages,
        List<(string Label, int Count)> Os, List<(string Label, int Count)> Models,
        List<(DateOnly Day, int Count)> ActivePerDay);

    public sealed record UserRow(
        string AccountId, string Email, DateTimeOffset CreatedAt, DateTimeOffset? LastSeen, int Devices,
        string Plans, int Credits, bool Banned, bool NotificationsAllowed);

    public sealed record SubscriptionRow(string AccountId, string? Email, string ProductId, string Source, DateTimeOffset? ExpiresAt, DateTimeOffset UpdatedAt);

    public sealed record OrderRow(
        string OrderId, string? Email, string ProductId, string Provider, decimal Amount,
        string Status, bool Applied, DateTimeOffset CreatedAt, string? ReportStatus);

    public sealed record AuditRow(DateTimeOffset At, string? Email, string Action, string? Target, string? Ip);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqliteConnection(_connectionString);
        await c.OpenAsync(ct);
        return c;
    }

    private static async Task<bool> Has(SqliteConnection c, string table, CancellationToken ct) =>
        await Int(c, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$p0", ct, table) > 0;

    private static async Task<int> Int(SqliteConnection c, string sql, CancellationToken ct, params object[] args)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("$p" + i, args[i]);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<List<(string, int)>> Groups(SqliteConnection c, string sql, CancellationToken ct, params object[] args)
    {
        var list = new List<(string, int)>();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("$p" + i, args[i]);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add((r.IsDBNull(0) ? "?" : r.GetString(0), r.GetInt32(1)));
        return list;
    }

    private static string Ago(int days) => DateTimeOffset.UtcNow.AddDays(-days).ToString("O");

    private static decimal Money(string raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public async Task<Overview> OverviewAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero).ToString("O");

        int accounts = 0, new7 = 0, banned = 0;
        if (await Has(c, "Accounts", ct))
        {
            accounts = await Int(c, "SELECT COUNT(*) FROM Accounts", ct);
            new7 = await Int(c, "SELECT COUNT(*) FROM Accounts WHERE CreatedAt > $p0", ct, Ago(7));
        }
        if (await Has(c, "BannedAccounts", ct))
            banned = await Int(c, "SELECT COUNT(*) FROM BannedAccounts", ct);

        int devices = 0, a1 = 0, a7 = 0, a30 = 0, notif = 0, remind = 0, signed = 0;
        List<(string, int)> versions = [], languages = [], os = [], models = [];
        var perDay = new List<(DateOnly, int)>();
        if (await Has(c, "Devices", ct))
        {
            devices = await Int(c, "SELECT COUNT(*) FROM Devices", ct);
            a1 = await Int(c, "SELECT COUNT(*) FROM Devices WHERE LastSeen > $p0", ct, Ago(1));
            a7 = await Int(c, "SELECT COUNT(*) FROM Devices WHERE LastSeen > $p0", ct, Ago(7));
            a30 = await Int(c, "SELECT COUNT(*) FROM Devices WHERE LastSeen > $p0", ct, Ago(30));
            notif = await Int(c, "SELECT COUNT(*) FROM Devices WHERE NotificationsAllowed=1 AND LastSeen > $p0", ct, Ago(30));
            remind = await Int(c, "SELECT COUNT(*) FROM Devices WHERE RemindersOn=1 AND LastSeen > $p0", ct, Ago(30));
            signed = await Int(c, "SELECT COUNT(*) FROM Devices WHERE AccountId IS NOT NULL AND LastSeen > $p0", ct, Ago(30));
            versions = await Groups(c, "SELECT AppVersion, COUNT(*) FROM Devices WHERE LastSeen > $p0 GROUP BY AppVersion ORDER BY 2 DESC LIMIT 8", ct, Ago(30));
            languages = await Groups(c, "SELECT Language, COUNT(*) FROM Devices WHERE LastSeen > $p0 GROUP BY Language ORDER BY 2 DESC LIMIT 6", ct, Ago(30));
            os = await Groups(c, "SELECT Platform || ' ' || COALESCE(OsVersion,''), COUNT(*) FROM Devices WHERE LastSeen > $p0 GROUP BY 1 ORDER BY 2 DESC LIMIT 8", ct, Ago(30));
            models = await Groups(c, "SELECT Model, COUNT(*) FROM Devices WHERE LastSeen > $p0 GROUP BY Model ORDER BY 2 DESC LIMIT 8", ct, Ago(30));
            // First seen per day (new installs that opened the app), last 30 days.
            var firsts = await Groups(c, "SELECT substr(FirstSeen,1,10), COUNT(*) FROM Devices WHERE FirstSeen > $p0 GROUP BY 1", ct, Ago(30));
            var map = firsts.ToDictionary(f => f.Item1, f => f.Item2);
            for (var i = 29; i >= 0; i--)
            {
                var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i));
                perDay.Add((day, map.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var n) ? n : 0));
            }
        }

        int subs = 0, google = 0, web = 0, other = 0, obd = 0, vin = 0, full = 0, exp7 = 0;
        if (await Has(c, "Subscriptions", ct))
        {
            const string active = "Active=1 AND (ExpiresAt IS NULL OR ExpiresAt > $p0)";
            subs = await Int(c, $"SELECT COUNT(*) FROM Subscriptions WHERE {active}", ct, now);
            google = await Int(c, $"SELECT COUNT(*) FROM Subscriptions WHERE {active} AND Source IN ('google','apple')", ct, now);
            web = await Int(c, $"SELECT COUNT(*) FROM Subscriptions WHERE {active} AND Source='web'", ct, now);
            other = subs - google - web;
            obd = await Int(c, $"SELECT COUNT(*) FROM Subscriptions WHERE {active} AND ProductId=$p1", ct, now, ProductCatalog.ObdMonthly);
            vin = await Int(c, $"SELECT COUNT(*) FROM Subscriptions WHERE {active} AND ProductId=$p1", ct, now, ProductCatalog.VinMonthly);
            full = await Int(c, $"SELECT COUNT(*) FROM Subscriptions WHERE {active} AND ProductId=$p1", ct, now, ProductCatalog.FullMonthly);
            exp7 = await Int(c, $"SELECT COUNT(*) FROM Subscriptions WHERE {active} AND ExpiresAt < $p1", ct, now, DateTimeOffset.UtcNow.AddDays(7).ToString("O"));
        }

        decimal revMonth = 0, revTotal = 0;
        int paidMonth = 0, paidTotal = 0, fullMonth = 0, problems = 0;
        if (await Has(c, "WebOrders", ct))
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Amount, CreatedAt, ProductId FROM WebOrders WHERE Status='paid'";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var amount = Money(r.GetString(0));
                paidTotal++; revTotal += amount;
                if (string.CompareOrdinal(r.GetString(1), monthStart) >= 0)
                {
                    paidMonth++; revMonth += amount;
                    if (FullHistoryService.IsFullProduct(r.GetString(2))) fullMonth++;
                }
            }
            if (await Has(c, "FullReports", ct))
                problems = await Int(c, @"SELECT COUNT(*) FROM WebOrders o LEFT JOIN FullReports r ON r.AccountId=o.UserId AND o.ProductId='vin_full:'||r.Vin
WHERE o.Status='paid' AND o.ProductId LIKE 'vin_full:%' AND (r.Status IS NULL OR r.Status<>'ready')", ct);
        }

        var unlocks = await Has(c, "VinUnlocks", ct) ? await Int(c, "SELECT COUNT(*) FROM VinUnlocks WHERE UnlockedAt > $p0", ct, monthStart) : 0;
        var credits = await Has(c, "VinCredits", ct) ? await Int(c, "SELECT COALESCE(SUM(Credits),0) FROM VinCredits", ct) : 0;
        var crashes = await Has(c, "CrashReports", ct) ? await Int(c, "SELECT COUNT(*) FROM CrashReports WHERE ReceivedAt > $p0", ct, Ago(7)) : 0;

        return new Overview(accounts, new7, banned, devices, a1, a7, a30, devices - a30, notif, remind, signed,
            subs, google, web, other, obd, vin, full, exp7,
            revMonth, paidMonth, revTotal, paidTotal, fullMonth, problems, unlocks, credits, crashes,
            versions, languages, os, models, perDay);
    }

    public async Task<List<UserRow>> UsersAsync(string? search, CancellationToken ct)
    {
        var rows = new List<UserRow>();
        await using var c = await OpenAsync(ct);
        if (!await Has(c, "Accounts", ct))
            return rows;

        var hasDevices = await Has(c, "Devices", ct);
        var hasSubs = await Has(c, "Subscriptions", ct);
        var hasCredits = await Has(c, "VinCredits", ct);
        var hasBans = await Has(c, "BannedAccounts", ct);
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var cmd = c.CreateCommand();
        cmd.CommandText = $@"SELECT a.AccountId, a.EmailDisplay, a.CreatedAt,
{(hasDevices ? "(SELECT MAX(LastSeen) FROM Devices d WHERE d.AccountId=a.AccountId)" : "NULL")},
{(hasDevices ? "(SELECT COUNT(*) FROM Devices d WHERE d.AccountId=a.AccountId)" : "0")},
{(hasSubs ? "(SELECT group_concat(ProductId || '|' || Source, ';') FROM Subscriptions s WHERE s.UserId=a.AccountId AND s.Active=1 AND (s.ExpiresAt IS NULL OR s.ExpiresAt > $now))" : "NULL")},
{(hasCredits ? "(SELECT Credits FROM VinCredits v WHERE v.UserId=a.AccountId)" : "0")},
{(hasBans ? "(SELECT COUNT(*) FROM BannedAccounts b WHERE b.AccountId=a.AccountId)" : "0")},
{(hasDevices ? "(SELECT MAX(NotificationsAllowed) FROM Devices d WHERE d.AccountId=a.AccountId)" : "0")}
FROM Accounts a
WHERE $q = '' OR a.EmailNormalized LIKE $like OR a.AccountId = $q
ORDER BY a.CreatedAt DESC LIMIT 300";
        cmd.Parameters.AddWithValue("$now", now);
        var q = (search ?? "").Trim().ToLowerInvariant();
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$like", "%" + q.Replace("%", "").Replace("_", "") + "%");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var plans = r.IsDBNull(5) ? "" : string.Join(", ", r.GetString(5).Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => { var x = p.Split('|'); return PlanName(x[0]) + (x.Length > 1 ? " (" + SourceName(x[1]) + ")" : ""); }));
            rows.Add(new UserRow(
                r.GetString(0), r.GetString(1),
                DateTimeOffset.TryParse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ? created : DateTimeOffset.MinValue,
                !r.IsDBNull(3) && DateTimeOffset.TryParse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.None, out var seen) ? seen : null,
                r.GetInt32(4), plans, r.IsDBNull(6) ? 0 : r.GetInt32(6), r.GetInt32(7) > 0, !r.IsDBNull(8) && r.GetInt32(8) == 1));
        }
        return rows;
    }

    public async Task<List<SubscriptionRow>> SubscriptionsAsync(CancellationToken ct)
    {
        var rows = new List<SubscriptionRow>();
        await using var c = await OpenAsync(ct);
        if (!await Has(c, "Subscriptions", ct))
            return rows;
        var hasAccounts = await Has(c, "Accounts", ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $@"SELECT s.UserId, {(hasAccounts ? "a.EmailDisplay" : "NULL")}, s.ProductId, s.Source, s.ExpiresAt, s.UpdatedAt
FROM Subscriptions s {(hasAccounts ? "LEFT JOIN Accounts a ON a.AccountId=s.UserId" : "")}
WHERE s.Active=1 AND (s.ExpiresAt IS NULL OR s.ExpiresAt > $now)
ORDER BY s.ExpiresAt ASC LIMIT 500";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new SubscriptionRow(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetString(3),
                !r.IsDBNull(4) && DateTimeOffset.TryParse(r.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : null,
                DateTimeOffset.TryParse(r.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.None, out var u) ? u : DateTimeOffset.MinValue));
        return rows;
    }

    public async Task<List<OrderRow>> OrdersAsync(int limit, CancellationToken ct)
    {
        var rows = new List<OrderRow>();
        await using var c = await OpenAsync(ct);
        if (!await Has(c, "WebOrders", ct))
            return rows;
        var hasReports = await Has(c, "FullReports", ct);
        var hasAccounts = await Has(c, "Accounts", ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $@"SELECT o.OrderId, {(hasAccounts ? "a.EmailDisplay" : "NULL")}, o.ProductId, o.Provider, o.Amount, o.Status, o.Applied, o.CreatedAt,
{(hasReports ? "r.Status" : "NULL")}
FROM WebOrders o
{(hasAccounts ? "LEFT JOIN Accounts a ON a.AccountId = o.UserId" : "")}
{(hasReports ? "LEFT JOIN FullReports r ON r.AccountId = o.UserId AND o.ProductId = 'vin_full:' || r.Vin" : "")}
WHERE o.Status <> 'created' OR o.CreatedAt > $recent
ORDER BY o.CreatedAt DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        cmd.Parameters.AddWithValue("$recent", Ago(2));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new OrderRow(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetString(3), Money(r.GetString(4)),
                r.GetString(5), r.GetInt32(6) != 0,
                DateTimeOffset.TryParse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : DateTimeOffset.MinValue,
                r.IsDBNull(8) ? null : r.GetString(8)));
        return rows;
    }

    public async Task<List<AuditRow>> AuditLogAsync(CancellationToken ct)
    {
        var rows = new List<AuditRow>();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT At, Email, Action, Target, Ip FROM AdminAudit ORDER BY Id DESC LIMIT 200";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new AuditRow(
                DateTimeOffset.TryParse(r.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : DateTimeOffset.MinValue,
                r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return rows;
    }

    public async Task<string?> AccountEmailAsync(string accountId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        if (!await Has(c, "Accounts", ct)) return null;
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT EmailDisplay FROM Accounts WHERE AccountId=$id";
        cmd.Parameters.AddWithValue("$id", accountId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    // ============================================================
    // NAMES
    // ============================================================

    public static string PlanName(string productId) => productId switch
    {
        ProductCatalog.ObdMonthly => "OBD PRO",
        ProductCatalog.VinMonthly => "VIN PRO",
        ProductCatalog.FullMonthly => "სრული PRO",
        ProductCatalog.VinPack5 => "+5 VIN",
        ProductCatalog.VinPack10 => "+10 VIN",
        ProductCatalog.VinSingle => "1 VIN ანგარიში",
        _ when FullHistoryService.IsFullProduct(productId) => "სრული ისტორია · " + FullHistoryService.VinOf(productId),
        _ => productId
    };

    public static string SourceName(string source) => source switch
    {
        "google" => "Google Play · ავტო-განახლებადი",
        "apple" => "App Store · ავტო-განახლებადი",
        "web" => "ბარათი · 30 დღე",
        "promo" => "პრომო-კოდი",
        "admin" => "ადმინმა მისცა",
        _ => source
    };

    public static string StatusName(string status) => status switch
    {
        "paid" => "გადახდილი",
        "failed" => "ვერ შესრულდა",
        "refunded" => "დაბრუნებული",
        "created" => "ელოდება",
        _ => status
    };

    public static string ActionName(string action) => action switch
    {
        "login" => "შესვლა",
        "login_failed" => "შესვლის წარუმატებელი მცდელობა",
        "login_blocked_ip" => "შესვლა დაბლოკილი IP-დან",
        "logout" => "გასვლა",
        "ban" => "დაბლოკვა",
        "unban" => "განბლოკვა",
        "grant" => "PRO-ს მიცემა",
        "refund" => "თანხის დაბრუნება",
        "refund_failed" => "დაბრუნება ვერ მოხერხდა",
        "retry" => "რეპორტის ხელახლა ცდა",
        "login_no_totp" => "შესვლა უარყოფილია — GCS_ADMIN_TOTP_SECRET არ არის",
        "temp_password" => "დროებითი პაროლი",
        "unbind" => "მოწყობილობის მოხსნა",
        "ecu_verify" => "ECU პასუხის დამოწმება",
        "ecu_unverify" => "ECU დამოწმების მოხსნა",
        _ => action
    };
}
