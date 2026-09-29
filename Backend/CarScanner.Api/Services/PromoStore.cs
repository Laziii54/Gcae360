using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace CarScanner.Api.Services;

/// <summary>
/// Stores server-side promo codes and one-time redemptions. Plain promo codes
/// are never persisted; deployment seeds them from the GCS_PROMO_CODES secret.
/// </summary>
public sealed class PromoStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IConfiguration _configuration;

    public PromoStore(IHostEnvironment environment, IConfiguration configuration)
    {
        _configuration = configuration;
        var databasePath = BackendDataPath.ResolveDatabasePath(
            environment,
            configuration);
        _connectionString =
            BackendDataPath.CreateConnectionString(databasePath);
        Initialize();
    }

    /// <summary>
    /// Expected format: CODE|PRO_DAYS|MAX_REDEMPTIONS|YYYY-MM-DD, separated by commas.
    /// Example: WELCOME30|30|500|2027-01-31
    /// </summary>
    public async Task SeedFromEnvironmentAsync(CancellationToken token = default)
    {
        // Environment variables take precedence in production.  The
        // configuration fallback is intentionally useful only for local
        // Development builds and is not required in a deployed environment.
        var configured = Environment.GetEnvironmentVariable("GCS_PROMO_CODES")
            ?? _configuration["Promo:Codes"];
        if (string.IsNullOrWhiteSpace(configured))
            return;

        foreach (var item in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 4 || string.IsNullOrWhiteSpace(parts[0]) ||
                !int.TryParse(parts[1], out var durationDays) || durationDays <= 0 ||
                !int.TryParse(parts[2], out var maximumRedemptions) || maximumRedemptions <= 0 ||
                !DateTimeOffset.TryParse(parts[3], out var expiresAt))
            {
                continue;
            }

            await _gate.WaitAsync(token);
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO PromoCodes
                    (CodeHash, ProDays, MaxRedemptions, RedemptionCount, ExpiresAt)
                    VALUES ($hash, $days, $maximum, 0, $expiresAt)
                    ON CONFLICT(CodeHash) DO UPDATE SET
                        ProDays=excluded.ProDays,
                        MaxRedemptions=excluded.MaxRedemptions,
                        ExpiresAt=excluded.ExpiresAt;";
                command.Parameters.AddWithValue("$hash", Hash(parts[0]));
                command.Parameters.AddWithValue("$days", durationDays);
                command.Parameters.AddWithValue("$maximum", maximumRedemptions);
                command.Parameters.AddWithValue("$expiresAt", expiresAt.UtcDateTime.ToString("O"));
                command.ExecuteNonQuery();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<PromoRedemptionResult> RedeemAsync(string userId, string code, CancellationToken token = default)
    {
        var normalizedUserId = userId.Trim();
        var codeHash = Hash(code);
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            // The process-wide gate serializes redemptions in this deployment.
            using var transaction = connection.BeginTransaction();

            using var promoQuery = connection.CreateCommand();
            promoQuery.Transaction = transaction;
            promoQuery.CommandText = "SELECT ProDays, MaxRedemptions, RedemptionCount, ExpiresAt FROM PromoCodes WHERE CodeHash=$hash;";
            promoQuery.Parameters.AddWithValue("$hash", codeHash);
            using var reader = promoQuery.ExecuteReader();
            if (!reader.Read())
            {
                transaction.Rollback();
                return PromoRedemptionResult.Invalid;
            }

            var days = reader.GetInt32(0);
            var maximum = reader.GetInt32(1);
            var redemptions = reader.GetInt32(2);
            var expiresAt = DateTimeOffset.Parse(reader.GetString(3));
            if (expiresAt <= DateTimeOffset.UtcNow || redemptions >= maximum)
            {
                transaction.Rollback();
                return PromoRedemptionResult.Invalid;
            }
            reader.Close();

            using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT 1 FROM PromoRedemptions WHERE CodeHash=$hash AND UserId=$userId LIMIT 1;";
            existing.Parameters.AddWithValue("$hash", codeHash);
            existing.Parameters.AddWithValue("$userId", normalizedUserId);
            if (existing.ExecuteScalar() is not null)
            {
                transaction.Rollback();
                return PromoRedemptionResult.AlreadyUsed;
            }

            using var increment = connection.CreateCommand();
            increment.Transaction = transaction;
            increment.CommandText = "UPDATE PromoCodes SET RedemptionCount=RedemptionCount+1 WHERE CodeHash=$hash AND RedemptionCount < MaxRedemptions;";
            increment.Parameters.AddWithValue("$hash", codeHash);
            if (increment.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return PromoRedemptionResult.Invalid;
            }

            using var redemption = connection.CreateCommand();
            redemption.Transaction = transaction;
            redemption.CommandText = @"INSERT INTO PromoRedemptions(CodeHash, UserId, RedeemedAt)
                VALUES ($hash, $userId, $redeemedAt);";
            redemption.Parameters.AddWithValue("$hash", codeHash);
            redemption.Parameters.AddWithValue("$userId", normalizedUserId);
            redemption.Parameters.AddWithValue("$redeemedAt", DateTimeOffset.UtcNow.ToString("O"));
            redemption.ExecuteNonQuery();
            transaction.Commit();

            return new PromoRedemptionResult(true, false, days);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"CREATE TABLE IF NOT EXISTS PromoCodes (
            CodeHash TEXT PRIMARY KEY,
            ProDays INTEGER NOT NULL,
            MaxRedemptions INTEGER NOT NULL,
            RedemptionCount INTEGER NOT NULL DEFAULT 0,
            ExpiresAt TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS PromoRedemptions (
            CodeHash TEXT NOT NULL,
            UserId TEXT NOT NULL,
            RedeemedAt TEXT NOT NULL,
            PRIMARY KEY (CodeHash, UserId)
        );";
        command.ExecuteNonQuery();
    }

    private static string Hash(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}

public sealed record PromoRedemptionResult(bool Success, bool AlreadyRedeemed, int ProDays)
{
    public static PromoRedemptionResult Invalid { get; } = new(false, false, 0);
    public static PromoRedemptionResult AlreadyUsed { get; } = new(false, true, 0);
}
