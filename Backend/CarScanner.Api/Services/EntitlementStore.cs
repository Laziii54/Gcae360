using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

public sealed class EntitlementStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EntitlementStore(
        IHostEnvironment env,
        IConfiguration configuration)
    {
        var path = BackendDataPath.ResolveDatabasePath(
            env,
            configuration);
        _connectionString =
            BackendDataPath.CreateConnectionString(path);
        Initialize();
        MigrateLegacyJson(env);
    }

    private void MigrateLegacyJson(IHostEnvironment env)
    {
        try
        {
            var legacy=Path.Combine(env.ContentRootPath,"data","entitlements.json");
            if(!File.Exists(legacy)) return;
            var json=File.ReadAllText(legacy);
            var records=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,EntitlementRecord>>(json);
            if(records==null) return;
            foreach(var r in records.Values) UpsertAsync(r).GetAwaiter().GetResult();
            File.Move(legacy, legacy+".migrated", true);
        }
        catch (Exception ex)
        {
            // Visible in the Azure log stream; the legacy file stays in place for a retry.
            Console.Error.WriteLine($"[EntitlementStore] Legacy entitlements.json migration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Initialize()
    {
        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Entitlements (UserId TEXT PRIMARY KEY, IsPro INTEGER NOT NULL, Source TEXT NOT NULL, ProductId TEXT NULL, ExpiresAt TEXT NULL, UpdatedAt TEXT NOT NULL, TransactionId TEXT NULL);";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Entitlements_ExpiresAt ON Entitlements(ExpiresAt);";
        cmd.ExecuteNonQuery();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS PurchaseClaims (
Source TEXT NOT NULL,
TransactionId TEXT NOT NULL,
UserId TEXT NOT NULL,
ClaimedAt TEXT NOT NULL,
PRIMARY KEY(Source, TransactionId));";
        cmd.ExecuteNonQuery();
        cmd.CommandText = @"INSERT OR IGNORE INTO PurchaseClaims(Source,TransactionId,UserId,ClaimedAt)
SELECT Source,TransactionId,UserId,UpdatedAt FROM Entitlements
WHERE TransactionId IS NOT NULL AND length(trim(TransactionId)) > 0;";
        cmd.ExecuteNonQuery();
        // One row per user and subscription product, so OBD PRO, VIN PRO
        // and Full PRO can be active at the same time and are combined.
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Subscriptions (
UserId TEXT NOT NULL,
ProductId TEXT NOT NULL,
Source TEXT NOT NULL,
ExpiresAt TEXT NULL,
UpdatedAt TEXT NOT NULL,
TransactionId TEXT NULL,
Active INTEGER NOT NULL,
PRIMARY KEY(UserId, ProductId));";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Stores one verified subscription and mirrors it into the legacy row.</summary>
    public async Task UpsertSubscriptionAsync(EntitlementRecord record, CancellationToken token = default)
    {
        if (!string.IsNullOrWhiteSpace(record.ProductId))
        {
            // Keep a subscription that only exists in the older single-row
            // table before that row is overwritten by the new product.
            var legacy = await GetAsync(record.UserId, token);

            await _gate.WaitAsync(token);
            try
            {
                using var c = new SqliteConnection(_connectionString); c.Open();
                if (legacy is { IsPro: true, ProductId: not null } &&
                    legacy.Source is "google" or "apple" &&
                    !string.Equals(legacy.ProductId, record.ProductId, StringComparison.Ordinal))
                {
                    using var keep = c.CreateCommand();
                    keep.CommandText = @"INSERT OR IGNORE INTO Subscriptions(UserId,ProductId,Source,ExpiresAt,UpdatedAt,TransactionId,Active)
VALUES($u,$pi,$s,$e,$up,$t,1);";
                    keep.Parameters.AddWithValue("$u", legacy.UserId);
                    keep.Parameters.AddWithValue("$pi", legacy.ProductId);
                    keep.Parameters.AddWithValue("$s", legacy.Source);
                    keep.Parameters.AddWithValue("$e", legacy.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
                    keep.Parameters.AddWithValue("$up", legacy.UpdatedAt.ToString("O"));
                    keep.Parameters.AddWithValue("$t", (object?)legacy.TransactionId ?? DBNull.Value);
                    keep.ExecuteNonQuery();
                }

                using var cmd = c.CreateCommand();
                cmd.CommandText = @"INSERT INTO Subscriptions(UserId,ProductId,Source,ExpiresAt,UpdatedAt,TransactionId,Active)
VALUES($u,$pi,$s,$e,$up,$t,$a)
ON CONFLICT(UserId,ProductId) DO UPDATE SET Source=$s,ExpiresAt=$e,UpdatedAt=$up,TransactionId=$t,Active=$a;";
                cmd.Parameters.AddWithValue("$u", record.UserId);
                cmd.Parameters.AddWithValue("$pi", record.ProductId);
                cmd.Parameters.AddWithValue("$s", record.Source);
                cmd.Parameters.AddWithValue("$e", record.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$up", record.UpdatedAt.ToString("O"));
                cmd.Parameters.AddWithValue("$t", (object?)record.TransactionId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$a", record.IsPro ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
            finally { _gate.Release(); }
        }

        await UpsertAsync(record, token);
    }

    /// <summary>
    /// Refund of one web-paid period: the plan end moves back by that period.
    /// When nothing paid is left, the plan is switched off.
    /// </summary>
    public async Task RemoveWebPeriodAsync(string userId, string productId, int days, string transactionId, CancellationToken token = default)
    {
        var expiry = await GetSubscriptionExpiryAsync(userId, productId, token);
        if (expiry is null)
            return;

        var now = DateTimeOffset.UtcNow;
        var newEnd = expiry.Value.AddDays(-days);
        var stillPaid = newEnd > now;
        await UpsertSubscriptionAsync(
            new EntitlementRecord(userId, stillPaid, "web", productId, stillPaid ? newEnd : now, now, transactionId),
            token);
    }

    /// <summary>Expiry of one active subscription row (null when none).</summary>
    public async Task<DateTimeOffset?> GetSubscriptionExpiryAsync(string userId, string productId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT ExpiresAt FROM Subscriptions WHERE UserId=$u AND ProductId=$p AND Active=1";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$p", productId);
            var value = cmd.ExecuteScalar() as string;
            return DateTimeOffset.TryParse(value, out var expires) ? expires : null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Turns off the subscription that belongs to one store transaction.</summary>
    public async Task<bool> RevokeSubscriptionAsync(string userId, string source, string transactionId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        int changed;
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"UPDATE Subscriptions SET Active=0, UpdatedAt=$at
WHERE UserId=$u AND Source=$s AND TransactionId=$t;";
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$s", source);
            cmd.Parameters.AddWithValue("$t", transactionId);
            changed = cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }

        var legacy = await GetAsync(userId, token);
        if (legacy is not null &&
            string.Equals(legacy.Source, source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(legacy.TransactionId, transactionId, StringComparison.Ordinal))
        {
            await RevokeAsync(userId, token);
            changed++;
        }

        return changed > 0;
    }

    /// <summary>Combines every active subscription of the account.</summary>
    public async Task<AccessInfo> GetAccessAsync(string userId, CancellationToken token = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<EntitlementRecord>();

        var legacy = await GetAsync(userId, token);
        if (legacy is not null)
            rows.Add(legacy);

        await _gate.WaitAsync(token);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT UserId, Active, Source, ProductId, ExpiresAt, UpdatedAt, TransactionId FROM Subscriptions WHERE UserId=$id";
            cmd.Parameters.AddWithValue("$id", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(Read(r));
        }
        finally { _gate.Release(); }

        bool obd = false, vin = false;
        DateTimeOffset? obdExp = null, vinExp = null;
        foreach (var row in rows)
        {
            if (!row.IsPro || (row.ExpiresAt.HasValue && row.ExpiresAt <= now))
                continue;

            if (ProductCatalog.GrantsObd(row.ProductId, row.Source))
            {
                obd = true;
                obdExp = Later(obdExp, row.ExpiresAt);
            }

            if (ProductCatalog.GrantsVin(row.ProductId, row.Source))
            {
                vin = true;
                vinExp = Later(vinExp, row.ExpiresAt);
            }
        }

        var tier = obd && vin ? "full" : obd ? "obd" : vin ? "vin" : "none";
        return new AccessInfo(obd, vin, tier, obdExp, vinExp);
    }

    private static DateTimeOffset? Later(DateTimeOffset? current, DateTimeOffset? candidate)
    {
        // A missing expiry means "no end date" and always wins.
        if (!candidate.HasValue) return current ?? DateTimeOffset.UtcNow.AddYears(10);
        if (!current.HasValue) return candidate;
        return candidate > current ? candidate : current;
    }

    public async Task<EntitlementRecord?> GetAsync(string userId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT UserId, IsPro, Source, ProductId, ExpiresAt, UpdatedAt, TransactionId FROM Entitlements WHERE UserId=$id";
            cmd.Parameters.AddWithValue("$id", userId);
            using var r = cmd.ExecuteReader();
            return !r.Read() ? null : Read(r);
        }
        finally { _gate.Release(); }
    }

    public async Task UpsertAsync(EntitlementRecord record, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var c = new SqliteConnection(_connectionString); c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO Entitlements(UserId,IsPro,Source,ProductId,ExpiresAt,UpdatedAt,TransactionId)
VALUES($u,$p,$s,$pi,$e,$up,$t)
ON CONFLICT(UserId) DO UPDATE SET IsPro=$p,Source=$s,ProductId=$pi,ExpiresAt=$e,UpdatedAt=$up,TransactionId=$t;";
            cmd.Parameters.AddWithValue("$u", record.UserId);
            cmd.Parameters.AddWithValue("$p", record.IsPro ? 1 : 0);
            cmd.Parameters.AddWithValue("$s", record.Source);
            cmd.Parameters.AddWithValue("$pi", (object?)record.ProductId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$e", record.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$up", record.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$t", (object?)record.TransactionId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteUserDataAsync(string userId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var c = new SqliteConnection(_connectionString);
            c.Open();
            using var transaction = c.BeginTransaction();
            using var claims = c.CreateCommand();
            claims.Transaction = transaction;
            claims.CommandText = "DELETE FROM PurchaseClaims WHERE UserId=$id;";
            claims.Parameters.AddWithValue("$id", userId);
            claims.ExecuteNonQuery();
            using var entitlement = c.CreateCommand();
            entitlement.Transaction = transaction;
            entitlement.CommandText = "DELETE FROM Entitlements WHERE UserId=$id;";
            entitlement.Parameters.AddWithValue("$id", userId);
            entitlement.ExecuteNonQuery();
            using var subscriptions = c.CreateCommand();
            subscriptions.Transaction = transaction;
            subscriptions.CommandText = "DELETE FROM Subscriptions WHERE UserId=$id;";
            subscriptions.Parameters.AddWithValue("$id", userId);
            subscriptions.ExecuteNonQuery();
            transaction.Commit();
        }
        finally { _gate.Release(); }
    }

    public Task<bool> TryClaimTransactionAsync(
        string source,
        string transactionId,
        string userId,
        CancellationToken token = default) =>
        TryClaimTransactionAsync(
            source,
            transactionId,
            userId,
            allowAuthenticatedTransfer: false,
            token);

    public async Task<bool> TryClaimTransactionAsync(
        string source,
        string transactionId,
        string userId,
        bool allowAuthenticatedTransfer,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(transactionId) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        source = source.Trim().ToLowerInvariant();
        transactionId = transactionId.Trim();

        await _gate.WaitAsync(token);
        try
        {
            using var c = new SqliteConnection(_connectionString);
            c.Open();

            using var insert = c.CreateCommand();
            insert.CommandText = @"INSERT OR IGNORE INTO PurchaseClaims(Source,TransactionId,UserId,ClaimedAt)
VALUES($s,$t,$u,$at);";
            insert.Parameters.AddWithValue("$s", source);
            insert.Parameters.AddWithValue("$t", transactionId);
            insert.Parameters.AddWithValue("$u", userId);
            insert.Parameters.AddWithValue(
                "$at",
                DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();

            using var query = c.CreateCommand();
            query.CommandText = @"SELECT UserId FROM PurchaseClaims
WHERE Source=$s AND TransactionId=$t LIMIT 1;";
            query.Parameters.AddWithValue("$s", source);
            query.Parameters.AddWithValue("$t", transactionId);

            var owner = query.ExecuteScalar() as string;
            if (string.Equals(
                owner,
                userId,
                StringComparison.Ordinal))
                return true;

            if (!allowAuthenticatedTransfer || string.IsNullOrWhiteSpace(owner))
                return false;

            // A verified store transaction can move from an anonymous
            // installation to a signed-in account. The previous entitlement
            // is revoked in the same database transaction so one purchase
            // cannot remain active for two identities.
            using var transaction = c.BeginTransaction();
            using var transfer = c.CreateCommand();
            transfer.Transaction = transaction;
            transfer.CommandText = @"UPDATE PurchaseClaims
SET UserId=$newOwner, ClaimedAt=$at
WHERE Source=$s AND TransactionId=$t AND UserId=$oldOwner;";
            transfer.Parameters.AddWithValue("$newOwner", userId);
            transfer.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            transfer.Parameters.AddWithValue("$s", source);
            transfer.Parameters.AddWithValue("$t", transactionId);
            transfer.Parameters.AddWithValue("$oldOwner", owner);
            if (transfer.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }

            using var revoke = c.CreateCommand();
            revoke.Transaction = transaction;
            revoke.CommandText = @"UPDATE Entitlements
SET IsPro=0, Source='transferred', ExpiresAt=NULL, UpdatedAt=$at
WHERE UserId=$oldOwner AND Source=$s AND TransactionId=$t;";
            revoke.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            revoke.Parameters.AddWithValue("$oldOwner", owner);
            revoke.Parameters.AddWithValue("$s", source);
            revoke.Parameters.AddWithValue("$t", transactionId);
            revoke.ExecuteNonQuery();
            using var revokeSubscription = c.CreateCommand();
            revokeSubscription.Transaction = transaction;
            revokeSubscription.CommandText = @"UPDATE Subscriptions
SET Active=0, UpdatedAt=$at
WHERE UserId=$oldOwner AND Source=$s AND TransactionId=$t;";
            revokeSubscription.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            revokeSubscription.Parameters.AddWithValue("$oldOwner", owner);
            revokeSubscription.Parameters.AddWithValue("$s", source);
            revokeSubscription.Parameters.AddWithValue("$t", transactionId);
            revokeSubscription.ExecuteNonQuery();
            transaction.Commit();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RevokeAsync(string userId, CancellationToken token = default)
        => UpsertAsync(new EntitlementRecord(userId, false, "revoked", null, null, DateTimeOffset.UtcNow, null), token);

    private static EntitlementRecord Read(SqliteDataReader r)
    {
        var expText = r.IsDBNull(4) ? null : r.GetString(4);
        var updatedText = r.IsDBNull(5) ? DateTimeOffset.UtcNow.ToString("O") : r.GetString(5);
        return new EntitlementRecord(
            r.GetString(0), r.GetInt32(1) != 0, r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
            DateTimeOffset.TryParse(expText, out var exp) ? exp : null,
            DateTimeOffset.TryParse(updatedText, out var upd) ? upd : DateTimeOffset.UtcNow,
            r.IsDBNull(6) ? null : r.GetString(6));
    }
}

public sealed record EntitlementRecord(
    string UserId, bool IsPro, string Source, string? ProductId, DateTimeOffset? ExpiresAt,
    DateTimeOffset UpdatedAt, string? TransactionId);
