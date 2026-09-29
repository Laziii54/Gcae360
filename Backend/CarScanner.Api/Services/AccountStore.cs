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

    public async Task<AccountSessionResult?> LoginAsync(string email, string password, string deviceId, CancellationToken token)
    {
        var result = await LoginCoreAsync(email, password, deviceId, token);
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
    // LOGIN - LEGACY
    // ============================================================

    private async Task<AccountSessionResult?> LoginCoreAsync(
        string email,
        string password,
        CancellationToken token)
    {
        var normalized =
            NormalizeEmail(email);

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
    AccountId,
    EmailDisplay,
    PasswordSalt,
    PasswordHash,
    DeviceHash
FROM Accounts
WHERE EmailNormalized=$email
LIMIT 1;
""";

            query.Parameters.AddWithValue(
                "$email",
                normalized);

            using var reader =
                query.ExecuteReader();

            if (!reader.Read())
                return null;

            var accountId =
                reader.GetString(0);

            var displayEmail =
                reader.GetString(1);

            var salt =
                Convert.FromBase64String(
                    reader.GetString(2));

            var expected =
                Convert.FromBase64String(
                    reader.GetString(3));

            var storedDeviceHash =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4);

            var actual =
                HashPassword(
                    password,
                    salt);

            if (!CryptographicOperations.FixedTimeEquals(
                    expected,
                    actual))
            {
                return null;
            }

            // Once an account is device-bound,
            // legacy login is no longer allowed.
            if (!string.IsNullOrWhiteSpace(
                    storedDeviceHash))
            {
                return null;
            }

            reader.Close();

            using var transaction =
                connection.BeginTransaction();

            DeleteSessions(
                connection,
                transaction,
                accountId);

            var result =
                CreateSession(
                    connection,
                    transaction,
                    accountId,
                    displayEmail,
                    deviceHash: null);

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

        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = """
SELECT AccountId, EmailDisplay, PasswordSalt, PasswordHash
FROM Accounts
WHERE EmailNormalized=$email
LIMIT 1;
""";
            query.Parameters.AddWithValue("$email", normalized);
            using var reader = query.ExecuteReader();
            if (!reader.Read())
                return null;

            var expected = Convert.FromBase64String(reader.GetString(3));
            var actual = HashPassword(password, Convert.FromBase64String(reader.GetString(2)));
            return CryptographicOperations.FixedTimeEquals(expected, actual)
                ? new AccountIdentity(reader.GetString(0), reader.GetString(1))
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ============================================================
    // LOGIN - DEVICE BOUND
    // ============================================================

    private async Task<AccountSessionResult?> LoginCoreAsync(
        string email,
        string password,
        string deviceId,
        CancellationToken token)
    {
        var normalized =
            NormalizeEmail(email);

        var normalizedDeviceId =
            NormalizeDeviceId(deviceId);

        var currentDeviceHash =
            HashDeviceId(
                normalizedDeviceId);

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
    AccountId,
    EmailDisplay,
    PasswordSalt,
    PasswordHash,
    DeviceHash
FROM Accounts
WHERE EmailNormalized=$email
LIMIT 1;
""";

            query.Parameters.AddWithValue(
                "$email",
                normalized);

            using var reader =
                query.ExecuteReader();

            if (!reader.Read())
                return null;

            var accountId =
                reader.GetString(0);

            var displayEmail =
                reader.GetString(1);

            var salt =
                Convert.FromBase64String(
                    reader.GetString(2));

            var expected =
                Convert.FromBase64String(
                    reader.GetString(3));

            var storedDeviceHash =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4);

            var actual =
                HashPassword(
                    password,
                    salt);

            // Password is checked before device status,
            // so an attacker cannot use this endpoint to
            // discover whether an account is device-bound.
            if (!CryptographicOperations.FixedTimeEquals(
                    expected,
                    actual))
            {
                return null;
            }

            reader.Close();

            using var transaction =
                connection.BeginTransaction();

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
                transaction.Rollback();

                throw new AccountDeviceMismatchException();
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
                    displayEmail,
                    currentDeviceHash);

            transaction.Commit();

            return result;
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
            password.Length is < 10 or > 128)
        {
            throw new ArgumentException(
                "Password must contain 10 to 128 characters.");
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
    DateTimeOffset ExpiresAt);

public sealed class AccountDeviceMismatchException
    : Exception
{
    public AccountDeviceMismatchException()
        : base(
            "This account is already linked to another device.")
    {
    }
}