using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Counts opened VIN reports per account.
///  • VIN PRO / Full PRO: 10 reports per subscription month.
///  • Extra credits (packs / single report) never expire.
///  • A VIN opened in the last 30 days opens again for free.
/// </summary>
public sealed class VinQuotaStore
{
    public static readonly TimeSpan ReopenWindow = TimeSpan.FromDays(30);

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VinQuotaStore(IHostEnvironment env, IConfiguration configuration)
    {
        _connectionString = BackendDataPath.CreateConnectionString(
            BackendDataPath.ResolveDatabasePath(env, configuration));
        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS VinUnlocks (
Id INTEGER PRIMARY KEY AUTOINCREMENT,
UserId TEXT NOT NULL,
Vin TEXT NOT NULL,
Kind TEXT NOT NULL,
UnlockedAt TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS IX_VinUnlocks_User ON VinUnlocks(UserId, UnlockedAt);
CREATE TABLE IF NOT EXISTS VinCredits (
UserId TEXT PRIMARY KEY,
Credits INTEGER NOT NULL,
UpdatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS VinCreditGrants (
Source TEXT NOT NULL,
TransactionId TEXT NOT NULL,
UserId TEXT NOT NULL,
ProductId TEXT NOT NULL,
Credits INTEGER NOT NULL,
GrantedAt TEXT NOT NULL,
PRIMARY KEY(Source, TransactionId));";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The current subscription month: it ends when the subscription
    /// renews. Without a usable renewal date the calendar month is used.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) CurrentPeriod(AccessInfo access, DateTimeOffset now)
    {
        if (access.VinExpiresAt is { } end && end > now && end - now <= TimeSpan.FromDays(32))
            return (end.AddMonths(-1), end);

        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (start, start.AddMonths(1));
    }

    public async Task<VinQuota> GetQuotaAsync(string userId, AccessInfo access, string? vin, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var (start, end) = CurrentPeriod(access, now);

        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            return ReadQuota(c, userId, access, vin, start, end, now);
        }
        finally { _gate.Release(); }
    }

    private static VinQuota ReadQuota(
        SqliteConnection c, string userId, AccessInfo access, string? vin,
        DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, SqliteTransaction? tx = null)
    {
        using var used = c.CreateCommand();
        used.Transaction = tx;
        used.CommandText = "SELECT COUNT(*) FROM VinUnlocks WHERE UserId=$u AND Kind='monthly' AND UnlockedAt >= $start";
        used.Parameters.AddWithValue("$u", userId);
        used.Parameters.AddWithValue("$start", start.ToString("O"));
        var monthlyUsed = Convert.ToInt32(used.ExecuteScalar() ?? 0);

        using var credits = c.CreateCommand();
        credits.Transaction = tx;
        credits.CommandText = "SELECT Credits FROM VinCredits WHERE UserId=$u";
        credits.Parameters.AddWithValue("$u", userId);
        var creditCount = Convert.ToInt32(credits.ExecuteScalar() ?? 0);

        var unlocked = false;
        if (!string.IsNullOrWhiteSpace(vin))
        {
            using var open = c.CreateCommand();
            open.Transaction = tx;
            open.CommandText = "SELECT COUNT(*) FROM VinUnlocks WHERE UserId=$u AND Vin=$v AND UnlockedAt >= $since";
            open.Parameters.AddWithValue("$u", userId);
            open.Parameters.AddWithValue("$v", vin);
            open.Parameters.AddWithValue("$since", (now - ReopenWindow).ToString("O"));
            unlocked = Convert.ToInt32(open.ExecuteScalar() ?? 0) > 0;
        }

        var limit = access.HasVin ? ProductCatalog.VinMonthlyQuota : 0;
        return new VinQuota(
            access.HasVin,
            limit,
            Math.Min(monthlyUsed, limit),
            Math.Max(0, limit - monthlyUsed),
            creditCount,
            access.HasVin ? end : null,
            unlocked);
    }

    /// <summary>
    /// Opens one VIN: first from the monthly allowance, then from
    /// credits. Opening an already opened VIN costs nothing.
    /// </summary>
    public async Task<VinUnlockResult> TryUnlockAsync(string userId, string vin, AccessInfo access, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var (start, end) = CurrentPeriod(access, now);

        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var tx = c.BeginTransaction();
            var quota = ReadQuota(c, userId, access, vin, start, end, now, tx);
            if (quota.Unlocked)
                return new VinUnlockResult(true, "already", quota);

            string kind;
            if (quota.HasVinPlan && quota.Remaining > 0)
            {
                kind = "monthly";
            }
            else if (quota.Credits > 0)
            {
                kind = "credit";
                using var spend = c.CreateCommand();
                spend.Transaction = tx;
                spend.CommandText = "UPDATE VinCredits SET Credits=Credits-1, UpdatedAt=$at WHERE UserId=$u AND Credits > 0";
                spend.Parameters.AddWithValue("$u", userId);
                spend.Parameters.AddWithValue("$at", now.ToString("O"));
                if (spend.ExecuteNonQuery() != 1)
                    return new VinUnlockResult(false, quota.HasVinPlan ? "quota" : "subscribe", quota);
            }
            else
            {
                return new VinUnlockResult(false, quota.HasVinPlan ? "quota" : "subscribe", quota);
            }

            using var insert = c.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO VinUnlocks(UserId,Vin,Kind,UnlockedAt) VALUES($u,$v,$k,$at)";
            insert.Parameters.AddWithValue("$u", userId);
            insert.Parameters.AddWithValue("$v", vin);
            insert.Parameters.AddWithValue("$k", kind);
            insert.Parameters.AddWithValue("$at", now.ToString("O"));
            insert.ExecuteNonQuery();
            var after = ReadQuota(c, userId, access, vin, start, end, now, tx);
            tx.Commit();

            return new VinUnlockResult(true, kind, after);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Adds the credits of one verified store purchase exactly once.
    /// Returns false when the purchase already belongs to another account.
    /// </summary>
    public async Task<bool> GrantCreditsAsync(
        string source, string transactionId, string userId, string productId, int credits, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var tx = c.BeginTransaction();

            using var claim = c.CreateCommand();
            claim.Transaction = tx;
            claim.CommandText = @"INSERT OR IGNORE INTO VinCreditGrants(Source,TransactionId,UserId,ProductId,Credits,GrantedAt)
VALUES($s,$t,$u,$p,$n,$at)";
            claim.Parameters.AddWithValue("$s", source);
            claim.Parameters.AddWithValue("$t", transactionId);
            claim.Parameters.AddWithValue("$u", userId);
            claim.Parameters.AddWithValue("$p", productId);
            claim.Parameters.AddWithValue("$n", credits);
            claim.Parameters.AddWithValue("$at", now);

            if (claim.ExecuteNonQuery() == 0)
            {
                // Already granted: fine for the same account, refused otherwise.
                using var owner = c.CreateCommand();
                owner.Transaction = tx;
                owner.CommandText = "SELECT UserId FROM VinCreditGrants WHERE Source=$s AND TransactionId=$t";
                owner.Parameters.AddWithValue("$s", source);
                owner.Parameters.AddWithValue("$t", transactionId);
                var existing = owner.ExecuteScalar() as string;
                tx.Commit();
                return string.Equals(existing, userId, StringComparison.OrdinalIgnoreCase);
            }

            using var add = c.CreateCommand();
            add.Transaction = tx;
            add.CommandText = @"INSERT INTO VinCredits(UserId,Credits,UpdatedAt) VALUES($u,$n,$at)
ON CONFLICT(UserId) DO UPDATE SET Credits=Credits+$n, UpdatedAt=$at";
            add.Parameters.AddWithValue("$u", userId);
            add.Parameters.AddWithValue("$n", credits);
            add.Parameters.AddWithValue("$at", now);
            add.ExecuteNonQuery();
            tx.Commit();
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteUserDataAsync(string userId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"DELETE FROM VinUnlocks WHERE UserId=$u;
DELETE FROM VinCredits WHERE UserId=$u;
UPDATE VinCreditGrants SET UserId='deleted' WHERE UserId=$u;";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }
}

public sealed record VinQuota(
    bool HasVinPlan,
    int MonthlyLimit,
    int MonthlyUsed,
    int Remaining,
    int Credits,
    DateTimeOffset? ResetsAt,
    bool Unlocked);

public sealed record VinUnlockResult(bool Success, string Reason, VinQuota Quota);
