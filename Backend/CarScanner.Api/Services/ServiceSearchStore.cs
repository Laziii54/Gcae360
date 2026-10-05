using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Results of the app's Service "Search" (read-only). Each report says which
/// catalog sources answered on a car and which of them equalled the value on
/// the dashboard. A source becomes "known" for a make/model/year when two
/// different installations (or one owner/test account) confirmed it; the app
/// then uses it without searching.
/// Only the first 11 VIN characters are kept (no serial number).
/// </summary>
public sealed class ServiceSearchStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServiceSearchStore(IHostEnvironment env, IConfiguration config)
    {
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ServiceSearchReports (
Id INTEGER PRIMARY KEY AUTOINCREMENT,
CreatedAt TEXT NOT NULL,
DeviceHash TEXT NOT NULL,
AccountId TEXT NULL,
Trusted INTEGER NOT NULL DEFAULT 0,
Make TEXT NOT NULL,
Model TEXT NOT NULL,
Year TEXT NOT NULL,
VinPrefix TEXT NULL,
DashboardPercent REAL NULL,
Tried INTEGER NOT NULL,
Answered INTEGER NOT NULL,
MatchedKeys TEXT NOT NULL,
Payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS IX_ServiceSearch_Model ON ServiceSearchReports(Make, Model, Year);";
        cmd.ExecuteNonQuery();
    }

    public static string Norm(string? value) =>
        new string((value ?? string.Empty).Trim().ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());

    public async Task AddAsync(ServiceSearchReport report, string deviceHash, string? accountId, bool trusted, CancellationToken ct)
    {
        static string Cut(string? v, int max) =>
            string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();

        var hits = (report.Hits ?? new List<ServiceSearchReportHit>()).Take(60).ToList();
        var matched = hits.Where(h => h.Matched && !string.IsNullOrWhiteSpace(h.SourceKey))
            .Select(h => Cut(h.SourceKey, 300)).Distinct().Take(8).ToArray();

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO ServiceSearchReports(CreatedAt,DeviceHash,AccountId,Trusted,Make,Model,Year,VinPrefix,DashboardPercent,Tried,Answered,MatchedKeys,Payload)
VALUES($at,$d,$a,$t,$mk,$md,$y,$v,$p,$tr,$an,$m,$pl)";
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$d", deviceHash);
            cmd.Parameters.AddWithValue("$a", (object?)accountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", trusted ? 1 : 0);
            cmd.Parameters.AddWithValue("$mk", Norm(report.Make));
            cmd.Parameters.AddWithValue("$md", Norm(report.Model));
            cmd.Parameters.AddWithValue("$y", Norm(report.Year));
            cmd.Parameters.AddWithValue("$v", (object?)Cut(report.VinPrefix, 11).ToUpperInvariant() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$p", report.DashboardPercent is { } p && p is >= 0 and <= 100 ? p : DBNull.Value);
            cmd.Parameters.AddWithValue("$tr", Math.Clamp(report.Tried, 0, 1000));
            cmd.Parameters.AddWithValue("$an", Math.Clamp(report.Answered, 0, 1000));
            cmd.Parameters.AddWithValue("$m", JsonSerializer.Serialize(matched));
            cmd.Parameters.AddWithValue("$pl", JsonSerializer.Serialize(hits));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Sources confirmed for this make/model/year (≥2 installations, or 1 trusted).</summary>
    public async Task<IReadOnlyList<string>> KnownSourcesAsync(string make, string model, string year, CancellationToken ct)
    {
        var votes = new Dictionary<string, (HashSet<string> Devices, bool Trusted)>(StringComparer.Ordinal);
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT DeviceHash, Trusted, MatchedKeys FROM ServiceSearchReports
WHERE Make=$mk AND Model=$md AND Year=$y AND MatchedKeys<>'[]' ORDER BY Id DESC LIMIT 500";
            cmd.Parameters.AddWithValue("$mk", Norm(make));
            cmd.Parameters.AddWithValue("$md", Norm(model));
            cmd.Parameters.AddWithValue("$y", Norm(year));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string[] keys;
                try { keys = JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? Array.Empty<string>(); }
                catch (JsonException) { continue; }

                foreach (var key in keys)
                {
                    if (!votes.TryGetValue(key, out var vote))
                        vote = (new HashSet<string>(StringComparer.Ordinal), false);
                    vote.Devices.Add(reader.GetString(0));
                    votes[key] = (vote.Devices, vote.Trusted || reader.GetInt32(1) == 1);
                }
            }
        }
        finally { _gate.Release(); }

        return votes
            .Where(v => v.Value.Trusted || v.Value.Devices.Count >= 2)
            .OrderByDescending(v => v.Value.Devices.Count)
            .Select(v => v.Key)
            .Take(8)
            .ToArray();
    }

    /// <summary>Latest reports for the admin page.</summary>
    public async Task<IReadOnlyList<ServiceSearchRow>> LatestAsync(int count, CancellationToken ct)
    {
        var rows = new List<ServiceSearchRow>();
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT CreatedAt,Make,Model,Year,VinPrefix,DashboardPercent,Tried,Answered,MatchedKeys,Trusted
FROM ServiceSearchReports ORDER BY Id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", Math.Clamp(count, 1, 500));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ServiceSearchRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8), reader.GetInt32(9) == 1));
            }
        }
        finally { _gate.Release(); }
        return rows;
    }
}

public sealed record ServiceSearchReport(
    string? Make,
    string? Model,
    string? Year,
    string? VinPrefix,
    double? DashboardPercent,
    int Tried,
    int Answered,
    string? AppVersion,
    List<ServiceSearchReportHit>? Hits);

public sealed record ServiceSearchReportHit(
    string? SourceKey,
    string? Kind,
    double Value,
    string? Unit,
    string? Request,
    string? Module,
    bool Matched);

public sealed record ServiceSearchRow(
    string CreatedAt, string Make, string Model, string Year, string? VinPrefix,
    double? DashboardPercent, int Tried, int Answered, string MatchedKeys, bool Trusted);
