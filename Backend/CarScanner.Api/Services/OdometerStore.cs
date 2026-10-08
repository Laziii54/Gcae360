using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Odometer places found by the app's "odometer finder" (read-only search).
/// A report holds only the place (module address, identifier, bytes, scale) —
/// never the distance — plus make, model, year and the first 11 VIN characters.
/// A place is used by everyone with the same model when the owner approves it,
/// when a verified owner/test account found it, or when at least two different
/// SIGNED-IN accounts found it from different phones AND different networks
/// (IP hashes). Reports from anonymous installations are kept for the owner
/// but never enable a place on their own. Rejected places are never used.
/// </summary>
public sealed class OdometerStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OdometerStore(IHostEnvironment env, IConfiguration config)
    {
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS OdometerReports (
Id INTEGER PRIMARY KEY AUTOINCREMENT,
CreatedAt TEXT NOT NULL,
DeviceHash TEXT NOT NULL,
AccountId TEXT NULL,
Trusted INTEGER NOT NULL DEFAULT 0,
Make TEXT NOT NULL,
Model TEXT NOT NULL,
Year TEXT NOT NULL,
VinPrefix TEXT NULL,
AppVersion TEXT NULL,
Locations TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS IX_Odometer_Model ON OdometerReports(Make, Model);
CREATE TABLE IF NOT EXISTS OdometerDecisions (
Id INTEGER PRIMARY KEY AUTOINCREMENT,
Make TEXT NOT NULL,
Model TEXT NOT NULL,
LocationKey TEXT NOT NULL,
Status TEXT NOT NULL,
DecidedBy TEXT NULL,
DecidedAt TEXT NULL,
UNIQUE(Make, Model, LocationKey));";
        cmd.ExecuteNonQuery();

        // Added later: hashed network address of the reporter (crowd check).
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('OdometerReports') WHERE name='IpHash'";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            cmd.CommandText = "ALTER TABLE OdometerReports ADD COLUMN IpHash TEXT NULL";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>One-way hash of the client IP (no address is stored).</summary>
    public static string HashIp(string ip) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("gcs-odo-ip:" + (ip ?? string.Empty).Trim())))[..32];

    /// <summary>
    /// Crowd rule: ≥2 distinct signed-in accounts whose reports came from
    /// ≥2 distinct phones and ≥2 distinct networks.
    /// </summary>
    public static bool CrowdConfirmed(OdometerGroup g) =>
        g.Accounts >= 2 && g.AccountDevices >= 2 && g.AccountIps >= 2;

    public static bool AutoEnabled(OdometerGroup g) =>
        g.Status == "pending" && (g.Trusted || CrowdConfirmed(g));

    public static string Norm(string? value) =>
        new string((value ?? string.Empty).Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool ValidKey(string key) =>
        key.Length is > 20 and <= 400 && key.StartsWith("svc=", StringComparison.Ordinal) &&
        (key.StartsWith("svc=22;", StringComparison.Ordinal) || key.StartsWith("svc=1A;", StringComparison.Ordinal)) &&
        !key.Any(char.IsControl);

    public async Task AddAsync(OdometerReport report, string deviceHash, string? accountId, bool trusted, CancellationToken ct, string? ipHash = null)
    {
        static string Cut(string? v, int max) =>
            string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();

        var keys = (report.Locations ?? new List<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Where(ValidKey).Distinct().Take(10).ToArray();
        if (keys.Length == 0)
            return;

        var make = Norm(report.Make);
        var model = Norm(report.Model);

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO OdometerReports(CreatedAt,DeviceHash,AccountId,Trusted,Make,Model,Year,VinPrefix,AppVersion,Locations,IpHash)
VALUES($at,$d,$a,$t,$mk,$md,$y,$v,$ver,$l,$ip)";
                cmd.Parameters.AddWithValue("$ip", (object?)ipHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$d", deviceHash);
                cmd.Parameters.AddWithValue("$a", (object?)accountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$t", trusted ? 1 : 0);
                cmd.Parameters.AddWithValue("$mk", make);
                cmd.Parameters.AddWithValue("$md", model);
                cmd.Parameters.AddWithValue("$y", Norm(report.Year));
                cmd.Parameters.AddWithValue("$v", Cut(report.VinPrefix, 11).ToUpperInvariant());
                cmd.Parameters.AddWithValue("$ver", Cut(report.AppVersion, 30));
                cmd.Parameters.AddWithValue("$l", JsonSerializer.Serialize(keys));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var key in keys)
            {
                await using var insert = c.CreateCommand();
                insert.CommandText = "INSERT OR IGNORE INTO OdometerDecisions(Make,Model,LocationKey,Status) VALUES($mk,$md,$k,'pending')";
                insert.Parameters.AddWithValue("$mk", make);
                insert.Parameters.AddWithValue("$md", model);
                insert.Parameters.AddWithValue("$k", key);
                await insert.ExecuteNonQueryAsync(ct);
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Places the app may use for this model (same year first, then any year of the model).</summary>
    public async Task<IReadOnlyList<string>> KnownAsync(string make, string model, string year, CancellationToken ct)
    {
        var groups = await GroupsAsync(Norm(make), Norm(model), ct);
        var usable = groups.Where(g => g.Status == "approved" || AutoEnabled(g)).ToList();
        var normYear = Norm(year);
        return usable
            .OrderByDescending(g => g.Years.Contains(normYear))
            .ThenByDescending(g => g.Status == "approved")
            .ThenByDescending(g => g.Devices)
            .Select(g => g.Key)
            .Take(8)
            .ToArray();
    }

    /// <summary>All places (admin page), or those of one model.</summary>
    public async Task<IReadOnlyList<OdometerGroup>> GroupsAsync(string? make, string? model, CancellationToken ct)
    {
        var decisions = new Dictionary<(string, string, string), (long Id, string Status)>();
        var votes = new Dictionary<(string, string, string), Vote>();

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = make is null
                    ? "SELECT Id,Make,Model,LocationKey,Status FROM OdometerDecisions"
                    : "SELECT Id,Make,Model,LocationKey,Status FROM OdometerDecisions WHERE Make=$mk AND Model=$md";
                if (make is not null)
                {
                    cmd.Parameters.AddWithValue("$mk", make);
                    cmd.Parameters.AddWithValue("$md", model ?? string.Empty);
                }
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    decisions[(r.GetString(1), r.GetString(2), r.GetString(3))] = (r.GetInt64(0), r.GetString(4));
            }

            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = make is null
                    ? "SELECT DeviceHash,Trusted,Make,Model,Year,Locations,CreatedAt,AccountId,IpHash FROM OdometerReports ORDER BY Id DESC LIMIT 3000"
                    : "SELECT DeviceHash,Trusted,Make,Model,Year,Locations,CreatedAt,AccountId,IpHash FROM OdometerReports WHERE Make=$mk AND Model=$md ORDER BY Id DESC LIMIT 1000";
                if (make is not null)
                {
                    cmd.Parameters.AddWithValue("$mk", make);
                    cmd.Parameters.AddWithValue("$md", model ?? string.Empty);
                }
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    string[] keys;
                    try { keys = JsonSerializer.Deserialize<string[]>(r.GetString(5)) ?? Array.Empty<string>(); }
                    catch (JsonException) { continue; }
                    var device = r.GetString(0);
                    var account = r.IsDBNull(7) ? null : r.GetString(7);
                    var ipHash = r.IsDBNull(8) ? null : r.GetString(8);
                    foreach (var key in keys)
                    {
                        var id = (r.GetString(2), r.GetString(3), key);
                        if (!votes.TryGetValue(id, out var vote))
                        {
                            vote = new Vote(r.GetString(6));
                            votes[id] = vote;
                        }
                        vote.Devices.Add(device);
                        vote.Years.Add(r.GetString(4));
                        vote.Trusted |= r.GetInt32(1) == 1;
                        // Only signed-in reports count for the crowd rule.
                        if (!string.IsNullOrWhiteSpace(account))
                        {
                            vote.Accounts.Add(account);
                            vote.AccountDevices.Add(device);
                            if (!string.IsNullOrWhiteSpace(ipHash))
                                vote.AccountIps.Add(ipHash);
                        }
                    }
                }
            }
        }
        finally { _gate.Release(); }

        return decisions
            .Select(d =>
            {
                votes.TryGetValue(d.Key, out var vote);
                return new OdometerGroup(
                    d.Value.Id, d.Key.Item1, d.Key.Item2, d.Key.Item3, d.Value.Status,
                    vote?.Devices.Count ?? 0, vote?.Trusted ?? false,
                    vote?.Years.OrderBy(y => y).ToArray() ?? Array.Empty<string>(),
                    vote?.Last ?? string.Empty,
                    vote?.Accounts.Count ?? 0,
                    vote?.AccountDevices.Count ?? 0,
                    vote?.AccountIps.Count ?? 0);
            })
            .OrderBy(g => g.Status == "pending" ? 0 : g.Status == "approved" ? 1 : 2)
            .ThenBy(g => g.Make).ThenBy(g => g.Model)
            .ToList();
    }

    private sealed class Vote(string last)
    {
        public HashSet<string> Devices { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Years { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AccountDevices { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AccountIps { get; } = new(StringComparer.Ordinal);
        public bool Trusted { get; set; }
        public string Last { get; } = last;
    }

    /// <summary>Account deletion: the reports stay (they hold no personal data) but lose the account link.</summary>
    public async Task ForgetAccountAsync(string accountId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE OdometerReports SET AccountId=NULL, Trusted=0 WHERE AccountId=$a";
            cmd.Parameters.AddWithValue("$a", accountId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DecideAsync(long id, string status, string by, CancellationToken ct)
    {
        if (status is not ("approved" or "rejected" or "pending"))
            return false;
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE OdometerDecisions SET Status=$s, DecidedBy=$b, DecidedAt=$at WHERE Id=$id";
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$b", by);
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", id);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally { _gate.Release(); }
    }
}

public sealed record OdometerReport(
    string? Make, string? Model, string? Year, string? VinPrefix, string? AppVersion, List<string>? Locations);

public sealed record OdometerGroup(
    long Id, string Make, string Model, string Key, string Status, int Devices, bool Trusted, string[] Years, string LastAt,
    int Accounts = 0, int AccountDevices = 0, int AccountIps = 0);
