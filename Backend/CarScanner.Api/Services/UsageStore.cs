using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Anonymous usage of app installations (from the app's heartbeat):
/// first/last time seen, app and Android version, language, whether
/// notifications are allowed and which account is signed in. The device id
/// is stored only as a SHA-256 hash.
/// </summary>
public sealed class UsageStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UsageStore(IHostEnvironment env, IConfiguration config)
    {
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, config));

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Devices (
DeviceHash TEXT PRIMARY KEY,
FirstSeen TEXT NOT NULL,
LastSeen TEXT NOT NULL,
Opens INTEGER NOT NULL DEFAULT 1,
AppVersion TEXT NULL,
Platform TEXT NULL,
OsVersion TEXT NULL,
Model TEXT NULL,
Language TEXT NULL,
NotificationsAllowed INTEGER NOT NULL DEFAULT 0,
RemindersOn INTEGER NOT NULL DEFAULT 0,
AccountId TEXT NULL);
CREATE INDEX IF NOT EXISTS IX_Devices_LastSeen ON Devices(LastSeen);
CREATE INDEX IF NOT EXISTS IX_Devices_Account ON Devices(AccountId);
CREATE TABLE IF NOT EXISTS DailyCounters (
CounterKey TEXT NOT NULL,
Day TEXT NOT NULL,
Count INTEGER NOT NULL,
PRIMARY KEY(CounterKey, Day));";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Persistent per-day counter (UTC day), e.g. AI requests per account.
    /// Counts one use and returns false when the limit is already reached.
    /// Old days are removed on the way.
    /// </summary>
    public async Task<(bool Allowed, int Used)> TryConsumeDailyAsync(string key, int limit, CancellationToken ct)
    {
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT Count FROM DailyCounters WHERE CounterKey=$k AND Day=$d";
                read.Parameters.AddWithValue("$k", key);
                read.Parameters.AddWithValue("$d", day);
                var current = await read.ExecuteScalarAsync(ct);
                var used = current is null or DBNull ? 0 : Convert.ToInt32(current, CultureInfo.InvariantCulture);
                if (used >= limit)
                {
                    await tx.CommitAsync(ct);
                    return (false, used);
                }
            }

            int now;
            await using (var write = c.CreateCommand())
            {
                write.Transaction = tx;
                write.CommandText = @"DELETE FROM DailyCounters WHERE Day < $old;
INSERT INTO DailyCounters(CounterKey,Day,Count) VALUES($k,$d,1)
ON CONFLICT(CounterKey,Day) DO UPDATE SET Count=Count+1;
SELECT Count FROM DailyCounters WHERE CounterKey=$k AND Day=$d;";
                write.Parameters.AddWithValue("$old", DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                write.Parameters.AddWithValue("$k", key);
                write.Parameters.AddWithValue("$d", day);
                now = Convert.ToInt32(await write.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            }

            await tx.CommitAsync(ct);
            return (true, now);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Gives back one use of today's counter (a request that could not be served). Never goes below zero.</summary>
    public async Task RefundDailyAsync(string key, CancellationToken ct)
    {
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE DailyCounters SET Count=Count-1 WHERE CounterKey=$k AND Day=$d AND Count>0";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$d", day);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Account deletion: installations lose the account link; its counters go.</summary>
    public async Task ForgetAccountAsync(string accountId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"UPDATE Devices SET AccountId=NULL WHERE AccountId=$a;
DELETE FROM DailyCounters WHERE CounterKey LIKE '%:' || $a;";
            cmd.Parameters.AddWithValue("$a", accountId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public static string HashDevice(string deviceId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("gcs-device:" + deviceId.Trim().ToLowerInvariant())));

    public async Task RecordAsync(string deviceId, HeartbeatRequest h, string? accountId, CancellationToken ct)
    {
        static string? Cut(string? v, int max) =>
            string.IsNullOrWhiteSpace(v) ? null : (v.Trim().Length > max ? v.Trim()[..max] : v.Trim());

        var now = DateTimeOffset.UtcNow.ToString("O");
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO Devices(DeviceHash,FirstSeen,LastSeen,Opens,AppVersion,Platform,OsVersion,Model,Language,NotificationsAllowed,RemindersOn,AccountId)
VALUES($d,$now,$now,1,$v,$p,$os,$m,$l,$n,$r,$a)
ON CONFLICT(DeviceHash) DO UPDATE SET LastSeen=$now, Opens=Opens+1, AppVersion=$v, Platform=$p, OsVersion=$os, Model=$m,
Language=$l, NotificationsAllowed=$n, RemindersOn=$r, AccountId=COALESCE($a, AccountId)";
            cmd.Parameters.AddWithValue("$d", HashDevice(deviceId));
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$v", (object?)Cut(h.AppVersion, 32) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$p", (object?)Cut(h.Platform, 24) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$os", (object?)Cut(h.OsVersion, 24) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$m", (object?)Cut(h.Model, 60) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$l", (object?)Cut(h.Language, 16) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$n", h.NotificationsAllowed ? 1 : 0);
            cmd.Parameters.AddWithValue("$r", h.RemindersOn ? 1 : 0);
            cmd.Parameters.AddWithValue("$a", (object?)accountId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }
}

public sealed record HeartbeatRequest(
    string? AppVersion,
    string? Build,
    string? Platform,
    string? OsVersion,
    string? Model,
    string? Language,
    bool NotificationsAllowed,
    bool RemindersOn);
