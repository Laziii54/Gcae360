using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Recordings from the app's reset recorder (the adapter only listened while
/// a driver or a workshop tool reset oil / Service due). The owner reviews
/// the candidate commands here; nothing is executed automatically.
/// Only the first 11 VIN characters are kept.
/// </summary>
public sealed class ResetRecordingStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ResetRecordingStore(IHostEnvironment env, IConfiguration config)
    {
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ResetRecordings (
Id INTEGER PRIMARY KEY AUTOINCREMENT,
CreatedAt TEXT NOT NULL,
DeviceHash TEXT NOT NULL,
AccountId TEXT NULL,
Make TEXT NOT NULL,
Model TEXT NOT NULL,
Year TEXT NOT NULL,
VinPrefix TEXT NULL,
Mode TEXT NOT NULL,
Item TEXT NOT NULL,
Ext29 INTEGER NOT NULL,
Overflow INTEGER NOT NULL,
ValueBefore REAL NULL,
ValueAfter REAL NULL,
Candidates TEXT NOT NULL,
Frames TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    public async Task<long> AddAsync(ResetRecordingRequest r, string deviceHash, string? accountId, CancellationToken ct)
    {
        static string Cut(string? v, int max) =>
            string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();

        var candidates = (r.Candidates ?? new List<ResetRecordingCandidate>()).Take(100)
            .Select(c => new ResetRecordingCandidate(Cut(c.Kind, 40), Cut(c.Id, 8), Cut(c.Data, 16), Cut(c.Note, 120)))
            .ToList();
        var frames = string.Join("\n", (r.Frames ?? new List<string>()).Take(6000).Select(f => Cut(f, 60)));

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO ResetRecordings(CreatedAt,DeviceHash,AccountId,Make,Model,Year,VinPrefix,Mode,Item,Ext29,Overflow,ValueBefore,ValueAfter,Candidates,Frames)
VALUES($at,$d,$a,$mk,$md,$y,$v,$mo,$it,$e,$o,$b,$af,$c,$f); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$d", deviceHash);
            cmd.Parameters.AddWithValue("$a", (object?)accountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mk", Cut(r.Make, 40));
            cmd.Parameters.AddWithValue("$md", Cut(r.Model, 60));
            cmd.Parameters.AddWithValue("$y", Cut(r.Year, 8));
            cmd.Parameters.AddWithValue("$v", Cut(r.VinPrefix, 11).ToUpperInvariant());
            cmd.Parameters.AddWithValue("$mo", r.Mode == "ToolSession" ? "ToolSession" : "MenuReset");
            cmd.Parameters.AddWithValue("$it", Cut(r.Item, 60));
            cmd.Parameters.AddWithValue("$e", r.Extended29Bit ? 1 : 0);
            cmd.Parameters.AddWithValue("$o", r.BufferOverflow ? 1 : 0);
            cmd.Parameters.AddWithValue("$b", r.ValueBefore is { } b ? b : DBNull.Value);
            cmd.Parameters.AddWithValue("$af", r.ValueAfter is { } a ? a : DBNull.Value);
            cmd.Parameters.AddWithValue("$c", JsonSerializer.Serialize(candidates));
            cmd.Parameters.AddWithValue("$f", frames);
            return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ResetRecordingRow>> LatestAsync(int count, CancellationToken ct)
    {
        var rows = new List<ResetRecordingRow>();
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT Id,CreatedAt,Make,Model,Year,VinPrefix,Mode,Item,Ext29,Overflow,ValueBefore,ValueAfter,Candidates,
length(Frames) - length(replace(Frames, char(10), '')) + (Frames <> '')
FROM ResetRecordings ORDER BY Id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", Math.Clamp(count, 1, 300));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                List<ResetRecordingCandidate> candidates;
                try { candidates = JsonSerializer.Deserialize<List<ResetRecordingCandidate>>(r.GetString(12)) ?? new(); }
                catch (JsonException) { candidates = new(); }
                rows.Add(new ResetRecordingRow(
                    r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6), r.GetString(7),
                    r.GetInt32(8) == 1, r.GetInt32(9) == 1,
                    r.IsDBNull(10) ? null : r.GetDouble(10), r.IsDBNull(11) ? null : r.GetDouble(11),
                    candidates, r.GetInt32(13)));
            }
        }
        finally { _gate.Release(); }
        return rows;
    }

    public async Task<string?> FramesAsync(long id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Make||' '||Model||' '||Year||' · '||Mode||' · '||Item||char(10)||Frames FROM ResetRecordings WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            return await cmd.ExecuteScalarAsync(ct) as string;
        }
        finally { _gate.Release(); }
    }
}

public sealed record ResetRecordingRequest(
    string? Make, string? Model, string? Year, string? VinPrefix, string? Mode, string? Item,
    bool Extended29Bit, bool BufferOverflow, double? ValueBefore, double? ValueAfter, string? AppVersion,
    List<ResetRecordingCandidate>? Candidates, List<string>? Frames);

public sealed record ResetRecordingCandidate(string? Kind, string? Id, string? Data, string? Note);

public sealed record ResetRecordingRow(
    long Id, string CreatedAt, string Make, string Model, string Year, string? VinPrefix, string Mode, string Item,
    bool Ext29, bool Overflow, double? ValueBefore, double? ValueAfter, List<ResetRecordingCandidate> Candidates, int FrameCount);
