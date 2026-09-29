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
CREATE INDEX IF NOT EXISTS IX_Devices_Account ON Devices(AccountId);";
        cmd.ExecuteNonQuery();
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
