using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace CarScanner.Api.Services;

public sealed class AccountStore
{
    private const int PasswordIterations = 210_000;

    private static readonly TimeSpan SessionLifetime =
        TimeSpan.FromDays(90);

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AccountStore(
        IHostEnvironment env,
        IConfiguration configuration)
    {
        var path =
            BackendDataPath.ResolveDatabasePath(
                env,
                configuration);

        _connectionString =
            BackendDataPath.CreateConnectionString(
                path);

        Initialize();
        InitializeBans();
    }

    // ============================================================
    // BANS (admin page) – a banned account cannot sign in and all
    // its sessions stop working.
    // ============================================================

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _banned =
        new(StringComparer.OrdinalIgnoreCase);

    private void InitializeBans()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"CREATE TABLE IF NOT EXISTS BannedAccounts (
AccountId TEXT PRIMARY KEY,
Reason TEXT NULL,
BannedBy TEXT NULL,
BannedAt TEXT NOT NULL);";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT AccountId FROM BannedAccounts";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            _banned[reader.GetString(0)] = true;
    }

    public bool IsBanned(string accountId) => _banned.ContainsKey(accountId);

    public async Task BanAsync(string accountId, string? reason, string bannedBy, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT OR REPLACE INTO BannedAccounts(AccountId,Reason,BannedBy,BannedAt) VALUES($id,$r,$by,$at);
DELETE FROM AccountSessions WHERE AccountId=$id;";
            command.Parameters.AddWithValue("$id", accountId);
            command.Parameters.AddWithValue("$r", (object?)reason ?? DBNull.Value);
            command.Parameters.AddWithValue("$by", bannedBy);
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            _banned[accountId] = true;
        }
        finally { _gate.Release(); }
    }

    public async Task UnbanAsync(string accountId, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM BannedAccounts WHERE AccountId=$id";
            command.Parameters.AddWithValue("$id", accountId);
            command.ExecuteNonQuery();
            _banned.TryRemove(accountId, out _);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Admin: releases the device binding so the account can sign in on a
    /// new phone (or after a reinstall). The next login binds it again.
    /// </summary>
    public async Task ResetDeviceAsync(string accountId, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"UPDATE Accounts SET DeviceHash=NULL WHERE AccountId=$id;
DELETE FROM AccountSessions WHERE AccountId=$id;";
            command.Parameters.AddWithValue("$id", accountId);
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    private async Task DropSessionsAsync(string accountId, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM AccountSessions WHERE AccountId=$id";
            command.Parameters.AddWithValue("$id", accountId);
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    public async Task<AccountSessionResult?> LoginAsync(string email, string password, CancellationToken token)
    {
        var result = await LoginCoreAsync(email, password, token);
        if (result is not null && IsBanned(result.AccountId))
        {
            await DropSessionsAsync(result.AccountId, token);
            return null;
        }
        return result;
    }

    /// <summary>Self-service device move: at most once per this period.</summary>
    public static readonly TimeSpan RebindCooldown = TimeSpan.FromDays(7);

    public Task<AccountSessionResult?> LoginAsync(string email, string password, string deviceId, CancellationToken token) =>
        LoginAsync(email, password, deviceId, moveDevice: false, token);

    /// <summary>
    /// Device-bound login. With <paramref name="moveDevice"/> and the right
    /// password, an account bound to another phone moves to this one (all
    /// other sessions end); allowed once per <see cref="RebindCooldown"/>,
    /// otherwise <see cref="AccountRebindCooldownException"/>.
    /// </summary>
    public async Task<AccountSessionResult?> LoginAsync(string email, string password, string deviceId, bool moveDevice, CancellationToken token)
    {
        var result = await LoginCoreAsync(email, password, deviceId, moveDevice, token);
        if (result is not null && IsBanned(result.AccountId))
        {
            await DropSessionsAsync(result.AccountId, token);
            return null;
        }
        return result;
    }

    public async Task<AccountIdentity?> VerifyCredentialsAsync(string email, string password, CancellationToken token)
    {
        var result = await VerifyCredentialsCoreAsync(email, password, token);
        return result is not null && IsBanned(result.AccountId) ? null : result;
    }

    /// <summary>
    /// The account id for an e-mail, without checking any password — "forgot password" needs this since the
    /// whole point is that the password is not known. Null for an unknown e-mail (the caller must still answer
    /// the same way either way, so this alone never reveals whether an address is registered).
    /// </summary>
    public async Task<AccountIdentity?> FindByEmailAsync(string email, CancellationToken token)
    {
        string normalized;
        try
        {
            normalized = NormalizeEmail(email);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var stored = await ReadCredentialsAsync(normalized, null, token);
        return stored is null ? null : new AccountIdentity(stored.AccountId, stored.DisplayEmail);
    }

    public async Task<AccountIdentity?> ResolveAsync(string? authorization, CancellationToken token)
    {
        var result = await ResolveCoreAsync(authorization, token);
        return result is not null && IsBanned(result.AccountId) ? null : result;
    }

    public async Task<AccountIdentity?> ResolveAsync(string? authorization, string? deviceId, CancellationToken token)
    {
        var result = await ResolveCoreAsync(authorization, deviceId, token);
        return result is not null && IsBanned(result.AccountId) ? null : result;
    }

    private void Initialize()
    {
        using var connection =
            new SqliteConnection(
                _connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
CREATE TABLE IF NOT EXISTS Accounts (
    AccountId TEXT PRIMARY KEY,
    EmailNormalized TEXT NOT NULL UNIQUE,
    EmailDisplay TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    DeviceHash TEXT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AccountSessions (
    TokenHash TEXT PRIMARY KEY,
    AccountId TEXT NOT NULL,
    DeviceHash TEXT NULL,
    ExpiresAt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_AccountSessions_AccountId
    ON AccountSessions(AccountId);

CREATE TABLE IF NOT EXISTS AccountExternalIdentities (
    Provider TEXT NOT NULL,
    Subject TEXT NOT NULL,
    AccountId TEXT NOT NULL,
    EmailAtLink TEXT NULL,
    CreatedAt TEXT NOT NULL,
    PRIMARY KEY (Provider, Subject)
);

CREATE INDEX IF NOT EXISTS IX_AccountExternalIdentities_AccountId
    ON AccountExternalIdentities(AccountId);
""";

        command.ExecuteNonQuery();

        // Existing production databases may have been created
        // before device binding was introduced.
        EnsureColumn(
            connection,
            "Accounts",
            "DeviceHash",
            "TEXT NULL");

        EnsureColumn(
            connection,
            "AccountSessions",
            "DeviceHash",
            "TEXT NULL");

        // Last self-service device move (7-day cooldown).
        EnsureColumn(
            connection,
            "Accounts",
            "LastRebindAt",
            "TEXT NULL");

        // Set by the admin "temporary password"; cleared by POST /api/accounts/password.
        EnsureColumn(
            connection,
            "Accounts",
            "MustChangePassword",
            "INTEGER NOT NULL DEFAULT 0");
    }

    // ============================================================
    // REGISTER - LEGACY
    // ============================================================

    public Task<AccountSessionResult?> RegisterAsync(
        string email,
        string password,
        CancellationToken token)
    {
        // Temporary compatibility for the existing Program.cs.
        // Device-aware Program.cs will use the overload below.
        return RegisterCoreAsync(
            email,
            password,
            deviceHash: null,
            token);
    }

    // ============================================================
    // REGISTER - DEVICE BOUND
    // ============================================================

    public Task<AccountSessionResult?> RegisterAsync(
        string email,
        string password,
        string deviceId,
        CancellationToken token)
    {
        var normalizedDeviceId =
            NormalizeDeviceId(deviceId);

        var deviceHash =
            HashDeviceId(
                normalizedDeviceId);

        return RegisterCoreAsync(
            email,
            password,
            deviceHash,
            token);
    }

    private async Task<AccountSessionResult?> RegisterCoreAsync(
        string email,
        string password,
        string? deviceHash,
        CancellationToken token)
    {
        var normalized =
            NormalizeEmail(email);

        ValidatePassword(password);

        var accountId =
            Guid.NewGuid()
                .ToString("N");

        var salt =
            RandomNumberGenerator.GetBytes(16);

        var hash =
            HashPassword(
                password,
                salt);

        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var transaction =
                connection.BeginTransaction();

            using var insert =
                connection.CreateCommand();

            insert.Transaction =
                transaction;

            insert.CommandText = """
INSERT INTO Accounts(
    AccountId,
    EmailNormalized,
    EmailDisplay,
    PasswordSalt,
    PasswordHash,
    DeviceHash,
    CreatedAt
)
VALUES(
    $id,
    $normalized,
    $display,
    $salt,
    $hash,
    $device,
    $created
);
""";

            insert.Parameters.AddWithValue(
                "$id",
                accountId);

            insert.Parameters.AddWithValue(
                "$normalized",
                normalized);

            insert.Parameters.AddWithValue(
                "$display",
                email.Trim());

            insert.Parameters.AddWithValue(
                "$salt",
                Convert.ToBase64String(salt));

            insert.Parameters.AddWithValue(
                "$hash",
                Convert.ToBase64String(hash));

            insert.Parameters.AddWithValue(
                "$device",
                deviceHash is null
                    ? DBNull.Value
                    : deviceHash);

            insert.Parameters.AddWithValue(
                "$created",
                DateTimeOffset.UtcNow
                    .ToString("O"));

            try
            {
                insert.ExecuteNonQuery();
            }
            catch (SqliteException ex)
                when (ex.SqliteErrorCode == 19)
            {
                transaction.Rollback();
                return null;
            }

            var result =
                CreateSession(
                    connection,
                    transaction,
                    accountId,
                    email.Trim(),
                    deviceHash);

            transaction.Commit();

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // CREDENTIALS (PBKDF2 runs OUTSIDE the database gate; only the
    // reads and writes are serialized)
    // ============================================================

    private sealed record StoredCredentials(
        string AccountId,
        string DisplayEmail,
        string Salt,
        string Hash,
        string? DeviceHash,
        string? LastRebindAt,
        bool MustChangePassword);

    // Unknown e-mail: the same PBKDF2 work is done against these, so the
    // answer takes as long as for an existing account.
    private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(16);
    private static readonly byte[] DummyHash = RandomNumberGenerator.GetBytes(32);

    private async Task<StoredCredentials?> ReadCredentialsAsync(
        string? normalizedEmail,
        string? accountId,
        CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = normalizedEmail is not null
                ? """
SELECT AccountId, EmailDisplay, PasswordSalt, PasswordHash, DeviceHash, LastRebindAt, MustChangePassword
FROM Accounts
WHERE EmailNormalized=$key
LIMIT 1;
"""
                : """
SELECT AccountId, EmailDisplay, PasswordSalt, PasswordHash, DeviceHash, LastRebindAt, MustChangePassword
FROM Accounts
WHERE AccountId=$key
LIMIT 1;
""";
            query.Parameters.AddWithValue("$key", (object?)normalizedEmail ?? accountId ?? string.Empty);
            using var reader = query.ExecuteReader();
            if (!reader.Read())
                return null;

            return new StoredCredentials(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                !reader.IsDBNull(6) && reader.GetInt64(6) != 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Constant-work password check (dummy PBKDF2 when the account does not exist).</summary>
    private static bool PasswordMatches(StoredCredentials? stored, string password)
    {
        if (stored is null)
        {
            CryptographicOperations.FixedTimeEquals(HashPassword(password, DummySalt), DummyHash);
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(stored.Salt);
            expected = Convert.FromBase64String(stored.Hash);
        }
        catch (FormatException)
        {
            CryptographicOperations.FixedTimeEquals(HashPassword(password, DummySalt), DummyHash);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, HashPassword(password, salt));
    }

    /// <summary>
    /// Inside a transaction: re-reads the account and confirms the password
    /// hash is still the one that was verified (it may have changed while
    /// PBKDF2 ran outside the gate). Returns the fresh row or null.
    /// </summary>
    private static StoredCredentials? Reread(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StoredCredentials verified)
    {
        using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
SELECT AccountId, EmailDisplay, PasswordSalt, PasswordHash, DeviceHash, LastRebindAt, MustChangePassword
FROM Accounts
WHERE AccountId=$id
LIMIT 1;
""";
        query.Parameters.AddWithValue("$id", verified.AccountId);
        using var reader = query.ExecuteReader();
        if (!reader.Read() || !string.Equals(reader.GetString(3), verified.Hash, StringComparison.Ordinal))
            return null;

        return new StoredCredentials(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            !reader.IsDBNull(6) && reader.GetInt64(6) != 0);
    }

    // ============================================================
    // LOGIN - LEGACY
    // ============================================================

    private async Task<AccountSessionResult?> LoginCoreAsync(
        string email,
        string password,
        CancellationToken token)
    {
        var normalized =
            NormalizeEmail(email);

        if (string.IsNullOrEmpty(password) || password.Length > 128)
            return null;

        var stored = await ReadCredentialsAsync(normalized, null, token);
        if (!PasswordMatches(stored, password))
            return null;

        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var transaction =
                connection.BeginTransaction();

            var current = Reread(connection, transaction, stored!);

            // Once an account is device-bound,
            // legacy login is no longer allowed.
            if (current is null ||
                !string.IsNullOrWhiteSpace(
                    current.DeviceHash))
            {
                transaction.Rollback();
                return null;
            }

            DeleteSessions(
                connection,
                transaction,
                current.AccountId);

            var result =
                CreateSession(
                    connection,
                    transaction,
                    current.AccountId,
                    current.DisplayEmail,
                    deviceHash: null) with { MustChangePassword = current.MustChangePassword };

            transaction.Commit();

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // CREDENTIAL CHECK (web payment page)
    // ============================================================

    /// <summary>
    /// Checks e-mail and password without creating a session, so the
    /// web payment page does not touch the phone's device binding.
    /// </summary>
    private async Task<AccountIdentity?> VerifyCredentialsCoreAsync(
        string email,
        string password,
        CancellationToken token)
    {
        string normalized;
        try
        {
            normalized = NormalizeEmail(email);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(password) || password.Length > 128)
            return null;

        var stored = await ReadCredentialsAsync(normalized, null, token);
        return PasswordMatches(stored, password)
            ? new AccountIdentity(stored!.AccountId, stored.DisplayEmail)
            : null;
    }

    // ============================================================
    // LOGIN - DEVICE BOUND
    // ============================================================

    private async Task<AccountSessionResult?> LoginCoreAsync(
        string email,
        string password,
        string deviceId,
        bool moveDevice,
        CancellationToken token)
    {
        var normalized =
            NormalizeEmail(email);

        var normalizedDeviceId =
            NormalizeDeviceId(deviceId);

        var currentDeviceHash =
            HashDeviceId(
                normalizedDeviceId);

        if (string.IsNullOrEmpty(password) || password.Length > 128)
            return null;

        // Password is checked before device status,
        // so an attacker cannot use this endpoint to
        // discover whether an account is device-bound.
        var stored = await ReadCredentialsAsync(normalized, null, token);
        if (!PasswordMatches(stored, password))
            return null;

        return await CompleteDeviceBoundSignInAsync(stored!, currentDeviceHash, moveDevice, token);
    }

    /// <summary>
    /// The device-bound part of a sign-in, after the caller has proved the identity (password, or a verified
    /// external identity). First sign-in binds the device, another device needs <paramref name="moveDevice"/>
    /// (once per <see cref="RebindCooldown"/>), the newest session replaces older ones.
    /// </summary>
    private async Task<AccountSessionResult?> CompleteDeviceBoundSignInAsync(
        StoredCredentials stored,
        string currentDeviceHash,
        bool moveDevice,
        CancellationToken token)
    {
        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var transaction =
                connection.BeginTransaction();

            var current = Reread(connection, transaction, stored);
            if (current is null)
            {
                transaction.Rollback();
                return null;
            }

            var accountId =
                current.AccountId;

            var storedDeviceHash =
                current.DeviceHash;

            var lastRebind =
                current.LastRebindAt is { } rebindText &&
                DateTimeOffset.TryParse(
                    rebindText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var rebindAt)
                    ? rebindAt
                    : (DateTimeOffset?)null;

            // Existing account from the old version:
            // first successful device-aware login binds it.
            if (string.IsNullOrWhiteSpace(
                    storedDeviceHash))
            {
                using var bind =
                    connection.CreateCommand();

                bind.Transaction =
                    transaction;

                bind.CommandText = """
UPDATE Accounts
SET DeviceHash=$device
WHERE AccountId=$id
  AND DeviceHash IS NULL;
""";

                bind.Parameters.AddWithValue(
                    "$device",
                    currentDeviceHash);

                bind.Parameters.AddWithValue(
                    "$id",
                    accountId);

                bind.ExecuteNonQuery();
            }
            else if (!FixedTimeEqualsText(
                         storedDeviceHash,
                         currentDeviceHash))
            {
                if (!moveDevice)
                {
                    transaction.Rollback();

                    throw new AccountDeviceMismatchException();
                }

                var now = DateTimeOffset.UtcNow;
                if (lastRebind is { } previous &&
                    previous + RebindCooldown > now)
                {
                    transaction.Rollback();

                    throw new AccountRebindCooldownException(
                        previous + RebindCooldown);
                }

                // Self-service move to this phone (password verified above).
                // The old phone's sessions are deleted below.
                using var move =
                    connection.CreateCommand();

                move.Transaction =
                    transaction;

                move.CommandText = """
UPDATE Accounts
SET DeviceHash=$device,
    LastRebindAt=$at
WHERE AccountId=$id;
""";

                move.Parameters.AddWithValue(
                    "$device",
                    currentDeviceHash);

                move.Parameters.AddWithValue(
                    "$at",
                    now.ToString("O"));

                move.Parameters.AddWithValue(
                    "$id",
                    accountId);

                move.ExecuteNonQuery();
            }

            // Keep only the newest session for this account.
            DeleteSessions(
                connection,
                transaction,
                accountId);

            var result =
                CreateSession(
                    connection,
                    transaction,
                    accountId,
                    current.DisplayEmail,
                    currentDeviceHash) with { MustChangePassword = current.MustChangePassword };

            transaction.Commit();

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // EXTERNAL IDENTITIES (Google / Apple): the provider proves who the person is; device binding, session
    // length and bans stay exactly as for a password sign-in.
    // ============================================================

    /// <summary>Signs in an account whose identity was already proved by a verified external identity.</summary>
    public async Task<AccountSessionResult?> SignInVerifiedAccountAsync(
        string accountId,
        string deviceId,
        bool moveDevice,
        CancellationToken token)
    {
        var deviceHash = HashDeviceId(NormalizeDeviceId(deviceId));
        var stored = await ReadCredentialsAsync(null, accountId, token);
        if (stored is null)
            return null;

        var result = await CompleteDeviceBoundSignInAsync(stored, deviceHash, moveDevice, token);
        if (result is not null && IsBanned(result.AccountId))
        {
            await DropSessionsAsync(result.AccountId, token);
            return null;
        }

        return result;
    }

    /// <summary>The account linked to this provider identity, or null. Provider: "google" or "apple".</summary>
    public async Task<AccountIdentity?> FindByExternalIdentityAsync(string provider, string subject, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = """
SELECT a.AccountId, a.EmailDisplay
FROM AccountExternalIdentities i
JOIN Accounts a ON a.AccountId = i.AccountId
WHERE i.Provider=$p AND i.Subject=$s
LIMIT 1;
""";
            query.Parameters.AddWithValue("$p", provider);
            query.Parameters.AddWithValue("$s", subject);
            using var reader = query.ExecuteReader();
            return reader.Read() ? new AccountIdentity(reader.GetString(0), reader.GetString(1)) : null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Creates a new account for a first-time external sign-in, linked to the provider identity, in one transaction.
    /// The account gets a random password nobody knows (it can set one later through the normal password flow), and is
    /// bound to this device like any new account. Null when the identity or the e-mail address is already taken
    /// (a concurrent first sign-in): the caller looks the identity up again.
    /// </summary>
    public async Task<AccountSessionResult?> CreateExternalAccountAsync(
        string provider,
        string subject,
        string email,
        string deviceId,
        CancellationToken token)
    {
        var normalized = NormalizeEmail(email);
        var deviceHash = HashDeviceId(NormalizeDeviceId(deviceId));
        var accountId = Guid.NewGuid().ToString("N");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), salt);

        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
INSERT INTO Accounts(AccountId, EmailNormalized, EmailDisplay, PasswordSalt, PasswordHash, DeviceHash, CreatedAt)
VALUES($id, $normalized, $display, $salt, $hash, $device, $created);
""";
                insert.Parameters.AddWithValue("$id", accountId);
                insert.Parameters.AddWithValue("$normalized", normalized);
                insert.Parameters.AddWithValue("$display", email.Trim());
                insert.Parameters.AddWithValue("$salt", Convert.ToBase64String(salt));
                insert.Parameters.AddWithValue("$hash", Convert.ToBase64String(hash));
                insert.Parameters.AddWithValue("$device", deviceHash);
                insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                try
                {
                    insert.ExecuteNonQuery();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    transaction.Rollback();
                    return null;
                }
            }

            if (!InsertExternalIdentity(connection, transaction, accountId, provider, subject, email.Trim()))
            {
                transaction.Rollback();
                return null;
            }

            var result = CreateSession(connection, transaction, accountId, email.Trim(), deviceHash);
            transaction.Commit();
            return result;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Links a provider identity to an existing account (after the caller proved control of that account).</summary>
    public async Task<bool> LinkExternalIdentityAsync(string accountId, string provider, string subject, string? email, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var ok = InsertExternalIdentity(connection, transaction, accountId, provider, subject, email);
            if (ok)
                transaction.Commit();
            else
                transaction.Rollback();
            return ok;
        }
        finally { _gate.Release(); }
    }

    private static bool InsertExternalIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountId,
        string provider,
        string subject,
        string? email)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO AccountExternalIdentities(Provider, Subject, AccountId, EmailAtLink, CreatedAt)
VALUES($p, $s, $a, $e, $c);
""";
        command.Parameters.AddWithValue("$p", provider);
        command.Parameters.AddWithValue("$s", subject);
        command.Parameters.AddWithValue("$a", accountId);
        command.Parameters.AddWithValue("$e", (object?)email ?? DBNull.Value);
        command.Parameters.AddWithValue("$c", DateTimeOffset.UtcNow.ToString("O"));
        try
        {
            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;   // this provider identity is already linked to an account
        }
    }

    // ============================================================
    // CHANGE PASSWORD (signed-in user; clears MustChangePassword)
    // ============================================================

    /// <summary>
    /// Changes the password of a signed-in account after checking the
    /// current one. Every other session ends; the session in
    /// <paramref name="authorization"/> stays. Returns false when the
    /// current password is wrong. Throws ArgumentException when the new
    /// password breaks the policy.
    /// </summary>
    public async Task<bool> ChangePasswordAsync(
        string accountId,
        string? authorization,
        string currentPassword,
        string newPassword,
        CancellationToken token)
    {
        ValidatePassword(newPassword);

        var rawToken = ExtractBearerToken(authorization);
        if (rawToken is null)
            return false;

        if (string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 128)
            return false;

        var stored = await ReadCredentialsAsync(null, accountId, token);
        if (stored is null || !PasswordMatches(stored, currentPassword))
            return false;

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(newPassword, salt);

        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                // Only if the password was not changed meanwhile.
                update.CommandText = """
UPDATE Accounts
SET PasswordSalt=$salt,
    PasswordHash=$hash,
    MustChangePassword=0
WHERE AccountId=$id
  AND PasswordHash=$old;
""";
                update.Parameters.AddWithValue("$salt", Convert.ToBase64String(salt));
                update.Parameters.AddWithValue("$hash", Convert.ToBase64String(hash));
                update.Parameters.AddWithValue("$id", accountId);
                update.Parameters.AddWithValue("$old", stored.Hash);
                if (update.ExecuteNonQuery() == 0)
                {
                    transaction.Rollback();
                    return false;
                }
            }

            using (var sessions = connection.CreateCommand())
            {
                sessions.Transaction = transaction;
                // Other sessions end; the current one stays.
                sessions.CommandText = """
DELETE FROM AccountSessions
WHERE AccountId=$id
  AND TokenHash<>$current;
""";
                sessions.Parameters.AddWithValue("$id", accountId);
                sessions.Parameters.AddWithValue("$current", HashToken(rawToken));
                sessions.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // RESOLVE - LEGACY
    // ============================================================

    private async Task<AccountIdentity?> ResolveCoreAsync(
        string? authorization,
        CancellationToken token)
    {
        var rawToken =
            ExtractBearerToken(
                authorization);

        if (rawToken is null)
            return null;

        var tokenHash =
            HashToken(rawToken);

        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var query =
                connection.CreateCommand();

            query.CommandText = """
SELECT
    a.AccountId,
    a.EmailDisplay,
    s.ExpiresAt,
    a.DeviceHash,
    s.DeviceHash
FROM AccountSessions s
JOIN Accounts a
    ON a.AccountId=s.AccountId
WHERE s.TokenHash=$hash
LIMIT 1;
""";

            query.Parameters.AddWithValue(
                "$hash",
                tokenHash);

            using var reader =
                query.ExecuteReader();

            if (!reader.Read() ||
                !DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out var expires) ||
                expires <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            var accountDevice =
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3);

            var sessionDevice =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4);

            // Legacy resolution cannot bypass an account
            // that has already been device-bound.
            if (!string.IsNullOrWhiteSpace(accountDevice) ||
                !string.IsNullOrWhiteSpace(sessionDevice))
            {
                return null;
            }

            return new AccountIdentity(
                reader.GetString(0),
                reader.GetString(1));
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // RESOLVE - DEVICE BOUND
    // ============================================================

    private async Task<AccountIdentity?> ResolveCoreAsync(
        string? authorization,
        string? deviceId,
        CancellationToken token)
    {
        var rawToken =
            ExtractBearerToken(
                authorization);

        if (rawToken is null)
            return null;

        string normalizedDeviceId;

        try
        {
            normalizedDeviceId =
                NormalizeDeviceId(
                    deviceId ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var currentDeviceHash =
            HashDeviceId(
                normalizedDeviceId);

        var tokenHash =
            HashToken(rawToken);

        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var query =
                connection.CreateCommand();

            query.CommandText = """
SELECT
    a.AccountId,
    a.EmailDisplay,
    s.ExpiresAt,
    a.DeviceHash,
    s.DeviceHash
FROM AccountSessions s
JOIN Accounts a
    ON a.AccountId=s.AccountId
WHERE s.TokenHash=$hash
LIMIT 1;
""";

            query.Parameters.AddWithValue(
                "$hash",
                tokenHash);

            using var reader =
                query.ExecuteReader();

            if (!reader.Read() ||
                !DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out var expires) ||
                expires <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            if (reader.IsDBNull(3) ||
                reader.IsDBNull(4))
            {
                return null;
            }

            var accountDeviceHash =
                reader.GetString(3);

            var sessionDeviceHash =
                reader.GetString(4);

            if (!FixedTimeEqualsText(
                    accountDeviceHash,
                    currentDeviceHash) ||
                !FixedTimeEqualsText(
                    sessionDeviceHash,
                    currentDeviceHash))
            {
                return null;
            }

            return new AccountIdentity(
                reader.GetString(0),
                reader.GetString(1));
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // LOGOUT
    // ============================================================

    public async Task LogoutAsync(
        string? authorization,
        CancellationToken token)
    {
        var rawToken =
            ExtractBearerToken(
                authorization);

        if (rawToken is null)
            return;

        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var command =
                connection.CreateCommand();

            command.CommandText = """
DELETE FROM AccountSessions
WHERE TokenHash=$hash;
""";

            command.Parameters.AddWithValue(
                "$hash",
                HashToken(rawToken));

            command.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // TEMPORARY PASSWORD (admin page; there is no e-mail sender)
    // ============================================================

    /// <summary>
    /// Admin: sets a new password (shown once to the owner, who passes it to
    /// the user). Every session ends; the device binding is released so the
    /// user can sign in on a new phone.
    /// </summary>
    public async Task<bool> SetPasswordAsync(
        string accountId,
        string newPassword,
        CancellationToken token,
        bool mustChange = true)
    {
        ValidatePassword(newPassword);

        var salt =
            RandomNumberGenerator.GetBytes(16);

        var hash =
            HashPassword(
                newPassword,
                salt);

        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var command =
                connection.CreateCommand();

            command.CommandText = """
UPDATE Accounts
SET PasswordSalt=$salt,
    PasswordHash=$hash,
    DeviceHash=NULL,
    MustChangePassword=$must
WHERE AccountId=$id;
DELETE FROM AccountSessions
WHERE AccountId=$id;
""";

            command.Parameters.AddWithValue(
                "$salt",
                Convert.ToBase64String(salt));

            command.Parameters.AddWithValue(
                "$hash",
                Convert.ToBase64String(hash));

            command.Parameters.AddWithValue(
                "$id",
                accountId);

            command.Parameters.AddWithValue(
                "$must",
                mustChange ? 1 : 0);

            return command.ExecuteNonQuery() > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Random 14-character password without look-alike characters.</summary>
    public static string NewTemporaryPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[14];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    // ============================================================
    // DELETE ACCOUNT
    // ============================================================

    public async Task DeleteAsync(
        string accountId,
        CancellationToken token)
    {
        await _gate.WaitAsync(token);

        try
        {
            using var connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            using var transaction =
                connection.BeginTransaction();

            using var sessions =
                connection.CreateCommand();

            sessions.Transaction =
                transaction;

            sessions.CommandText = """
DELETE FROM AccountSessions
WHERE AccountId=$id;
""";

            sessions.Parameters.AddWithValue(
                "$id",
                accountId);

            sessions.ExecuteNonQuery();

            // A deleted account must free its provider identities, otherwise the same Google/Apple user
            // could never sign up again.
            using var identities =
                connection.CreateCommand();

            identities.Transaction =
                transaction;

            identities.CommandText = """
DELETE FROM AccountExternalIdentities
WHERE AccountId=$id;
""";

            identities.Parameters.AddWithValue(
                "$id",
                accountId);

            identities.ExecuteNonQuery();

            using var account =
                connection.CreateCommand();

            account.Transaction =
                transaction;

            account.CommandText = """
DELETE FROM Accounts
WHERE AccountId=$id;
""";

            account.Parameters.AddWithValue(
                "$id",
                accountId);

            account.ExecuteNonQuery();

            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // SESSION
    // ============================================================

    private static AccountSessionResult CreateSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountId,
        string email,
        string? deviceHash)
    {
        var rawToken =
            Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(48))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        var expires =
            DateTimeOffset.UtcNow
                .Add(SessionLifetime);

        using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText = """
INSERT INTO AccountSessions(
    TokenHash,
    AccountId,
    DeviceHash,
    ExpiresAt,
    CreatedAt
)
VALUES(
    $hash,
    $account,
    $device,
    $expires,
    $created
);
""";

        command.Parameters.AddWithValue(
            "$hash",
            HashToken(rawToken));

        command.Parameters.AddWithValue(
            "$account",
            accountId);

        command.Parameters.AddWithValue(
            "$device",
            deviceHash is null
                ? DBNull.Value
                : deviceHash);

        command.Parameters.AddWithValue(
            "$expires",
            expires.ToString("O"));

        command.Parameters.AddWithValue(
            "$created",
            DateTimeOffset.UtcNow
                .ToString("O"));

        command.ExecuteNonQuery();

        return new AccountSessionResult(
            accountId,
            email,
            rawToken,
            expires);
    }

    private static void DeleteSessions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountId)
    {
        using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText = """
DELETE FROM AccountSessions
WHERE AccountId=$id;
""";

        command.Parameters.AddWithValue(
            "$id",
            accountId);

        command.ExecuteNonQuery();
    }

    // ============================================================
    // VALIDATION
    // ============================================================

    private static string NormalizeEmail(
        string email)
    {
        email =
            (email ?? string.Empty)
            .Trim();

        if (email.Length is < 5 or > 254 ||
            !System.Net.Mail.MailAddress.TryCreate(
                email,
                out var parsed) ||
            !string.Equals(
                parsed.Address,
                email,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A valid email address is required.");
        }

        return email.ToUpperInvariant();
    }

    private static void ValidatePassword(
        string password)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length is < 8 or > 128)
        {
            throw new ArgumentException(
                "Password must contain 8 to 128 characters.");
        }
    }

    private static string NormalizeDeviceId(
        string deviceId)
    {
        var value =
            (deviceId ?? string.Empty)
            .Trim();

        if (!Guid.TryParseExact(
                value,
                "N",
                out var parsed))
        {
            throw new ArgumentException(
                "A valid device ID is required.");
        }

        return parsed.ToString("N");
    }

    // ============================================================
    // HASHING
    // ============================================================

    private static byte[] HashPassword(
        string password,
        byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            32);

    private static string HashToken(
        string token) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    token)));

    private static string HashDeviceId(
        string deviceId) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    deviceId)));

    private static bool FixedTimeEqualsText(
        string first,
        string second)
    {
        var a =
            Encoding.UTF8.GetBytes(
                first);

        var b =
            Encoding.UTF8.GetBytes(
                second);

        return a.Length == b.Length &&
               CryptographicOperations.FixedTimeEquals(
                   a,
                   b);
    }

    private static string? ExtractBearerToken(
        string? authorization)
    {
        if (string.IsNullOrWhiteSpace(
                authorization) ||
            !authorization.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rawToken =
            authorization[7..]
                .Trim();

        if (rawToken.Length is < 32 or > 256)
            return null;

        return rawToken;
    }

    // ============================================================
    // DATABASE MIGRATION
    // ============================================================

    private static void EnsureColumn(
        SqliteConnection connection,
        string table,
        string column,
        string definition)
    {
        using var check =
            connection.CreateCommand();

        check.CommandText =
            $"PRAGMA table_info({table});";

        using var reader =
            check.ExecuteReader();

        while (reader.Read())
        {
            var existingName =
                reader.GetString(1);

            if (string.Equals(
                    existingName,
                    column,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();

        using var alter =
            connection.CreateCommand();

        alter.CommandText =
            $"ALTER TABLE {table} ADD COLUMN {column} {definition};";

        alter.ExecuteNonQuery();
    }
}

public sealed record AccountIdentity(
    string AccountId,
    string Email);

public sealed record AccountSessionResult(
    string AccountId,
    string Email,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    // "mustChangePassword": true only after an admin temporary password.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool MustChangePassword = false);

public sealed class AccountRebindCooldownException
    : Exception
{
    public AccountRebindCooldownException(
        DateTimeOffset nextAllowedAt)
        : base(
            "The account was moved to another device less than 7 days ago.")
    {
        NextAllowedAt = nextAllowedAt;
    }

    public DateTimeOffset NextAllowedAt { get; }
}

public sealed class AccountDeviceMismatchException
    : Exception
{
    public AccountDeviceMismatchException()
        : base(
            "This account is already linked to another device.")
    {
    }
}