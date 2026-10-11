using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services.Email;

public enum IssueStatus
{
    Issued,
    AlreadyVerified,
    Cooldown,
    HourlyLimit
}

public enum VerifyResult
{
    Verified,
    Invalid,
    Expired,
    TooManyAttempts,
    NoPendingCode,
    AlreadyVerified
}

/// <summary>A freshly issued code and link token. The plain values exist only here, for the one email; the database keeps hashes.</summary>
public sealed record IssuedVerification(
    IssueStatus Status,
    string? Code,
    string? LinkToken,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RetryAfter);

/// <summary>
/// Email-address verification: a 6-digit code (typed in the app) and a long link token (the email button), both
/// for one pending verification per account.
///
///  • Single use: whichever is used first completes the verification and removes both.
///  • Expires after the configured time (default 15 minutes, GCS_EMAIL_CODE_MINUTES / Email:VerificationMinutes).
///  • The code is stored only as a salted SHA-256 hash and compared in constant time; five wrong codes burn it.
///  • Sending again replaces the old code; at most one email per minute and five per hour per account.
///  • Nothing is stored in clear text and nothing here is ever logged.
/// </summary>
public sealed class EmailVerificationStore
{
    public const int MaxAttempts = 5;
    public const int MaxSendsPerHour = 5;
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _clock;

    public TimeSpan Lifetime { get; }

    public EmailVerificationStore(IHostEnvironment env, IConfiguration config)
        : this(BackendDataPath.CreateConnectionString(BackendDataPath.ResolveDatabasePath(env, config)), ReadLifetime(config))
    {
    }

    public EmailVerificationStore(string connectionString, TimeSpan lifetime, Func<DateTimeOffset>? clock = null)
    {
        _connectionString = connectionString;
        Lifetime = lifetime;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS EmailVerifications (
AccountId TEXT PRIMARY KEY,
CodeSalt TEXT NOT NULL,
CodeHash TEXT NOT NULL,
LinkHash TEXT NOT NULL,
ExpiresAt TEXT NOT NULL,
Attempts INTEGER NOT NULL DEFAULT 0,
LastSentAt TEXT NOT NULL,
WindowStart TEXT NOT NULL,
SendsInWindow INTEGER NOT NULL DEFAULT 1);
CREATE UNIQUE INDEX IF NOT EXISTS IX_EmailVerifications_LinkHash ON EmailVerifications(LinkHash);
CREATE TABLE IF NOT EXISTS EmailVerified (
AccountId TEXT PRIMARY KEY,
VerifiedAt TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Lifetime in minutes from Email:VerificationMinutes or GCS_EMAIL_CODE_MINUTES; 5 to 60, default 15.</summary>
    public static TimeSpan ReadLifetime(IConfiguration config)
    {
        var text = config["Email:VerificationMinutes"] ?? Environment.GetEnvironmentVariable("GCS_EMAIL_CODE_MINUTES");
        return int.TryParse(text, out var minutes) && minutes is >= 5 and <= 60
            ? TimeSpan.FromMinutes(minutes)
            : DefaultLifetime;
    }

    public async Task<bool> IsVerifiedAsync(string accountId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            return await IsVerifiedCoreAsync(c, null, accountId, ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Creates a new pending verification (replacing any older one), unless throttled or already verified.</summary>
    public async Task<IssuedVerification> IssueAsync(string accountId, CancellationToken ct)
    {
        var now = _clock();
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

            if (await IsVerifiedCoreAsync(c, tx, accountId, ct))
                return new IssuedVerification(IssueStatus.AlreadyVerified, null, null, null, null);

            DateTimeOffset? lastSent = null, windowStart = null;
            var sends = 0;
            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT LastSentAt, WindowStart, SendsInWindow FROM EmailVerifications WHERE AccountId=$a";
                read.Parameters.AddWithValue("$a", accountId);
                await using var r = await read.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    lastSent = Parse(r.GetString(0));
                    windowStart = Parse(r.GetString(1));
                    sends = r.GetInt32(2);
                }
            }

            if (lastSent is not null && now - lastSent < ResendCooldown)
                return new IssuedVerification(IssueStatus.Cooldown, null, null, null, lastSent + ResendCooldown);

            if (windowStart is null || now - windowStart >= TimeSpan.FromHours(1))
            {
                windowStart = now;
                sends = 0;
            }
            else if (sends >= MaxSendsPerHour)
            {
                return new IssuedVerification(IssueStatus.HourlyLimit, null, null, null, windowStart + TimeSpan.FromHours(1));
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var linkToken = Base64Url(RandomNumberGenerator.GetBytes(32));
            var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var expires = now + Lifetime;

            await using (var upsert = c.CreateCommand())
            {
                upsert.Transaction = tx;
                upsert.CommandText = @"INSERT INTO EmailVerifications(AccountId,CodeSalt,CodeHash,LinkHash,ExpiresAt,Attempts,LastSentAt,WindowStart,SendsInWindow)
VALUES($a,$s,$ch,$lh,$e,0,$now,$w,$n)
ON CONFLICT(AccountId) DO UPDATE SET CodeSalt=$s,CodeHash=$ch,LinkHash=$lh,ExpiresAt=$e,Attempts=0,LastSentAt=$now,WindowStart=$w,SendsInWindow=$n";
                upsert.Parameters.AddWithValue("$a", accountId);
                upsert.Parameters.AddWithValue("$s", salt);
                upsert.Parameters.AddWithValue("$ch", HashCode(salt, code));
                upsert.Parameters.AddWithValue("$lh", HashToken(linkToken));
                upsert.Parameters.AddWithValue("$e", expires.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$w", windowStart.Value.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$n", sends + 1);
                await upsert.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return new IssuedVerification(IssueStatus.Issued, code, linkToken, expires, null);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The email for the pending code could not be sent: the code is useless (it expires now) and the resend
    /// cooldown is lifted so the user can retry at once. The hourly count is kept, so retries stay limited.
    /// </summary>
    public async Task MarkSendFailedAsync(string accountId, CancellationToken ct)
    {
        var now = _clock();
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE EmailVerifications SET ExpiresAt=$e, LastSentAt=$l WHERE AccountId=$a";
            cmd.Parameters.AddWithValue("$e", now.UtcDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("$l", (now - ResendCooldown).UtcDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("$a", accountId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The 6-digit code typed in the app.</summary>
    public async Task<VerifyResult> VerifyCodeAsync(string accountId, string? code, CancellationToken ct)
    {
        var now = _clock();
        var typed = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

            if (await IsVerifiedCoreAsync(c, tx, accountId, ct))
                return VerifyResult.AlreadyVerified;

            string? salt = null, hash = null;
            DateTimeOffset expires = default;
            var attempts = 0;
            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT CodeSalt, CodeHash, ExpiresAt, Attempts FROM EmailVerifications WHERE AccountId=$a";
                read.Parameters.AddWithValue("$a", accountId);
                await using var r = await read.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    salt = r.GetString(0);
                    hash = r.GetString(1);
                    expires = Parse(r.GetString(2)) ?? default;
                    attempts = r.GetInt32(3);
                }
            }

            if (salt is null || hash is null)
                return VerifyResult.NoPendingCode;

            if (now >= expires)
            {
                await DeletePendingAsync(c, tx, accountId, ct);
                await tx.CommitAsync(ct);
                return VerifyResult.Expired;
            }

            if (attempts >= MaxAttempts)
            {
                await DeletePendingAsync(c, tx, accountId, ct);
                await tx.CommitAsync(ct);
                return VerifyResult.TooManyAttempts;
            }

            var ok = typed.Length == 6 &&
                     CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashCode(salt, typed)), Encoding.ASCII.GetBytes(hash));
            if (ok)
            {
                await CompleteAsync(c, tx, accountId, now, ct);
                await tx.CommitAsync(ct);
                return VerifyResult.Verified;
            }

            attempts++;
            if (attempts >= MaxAttempts)
            {
                await DeletePendingAsync(c, tx, accountId, ct);      // the code is burned
                await tx.CommitAsync(ct);
                return VerifyResult.TooManyAttempts;
            }

            await using (var bump = c.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE EmailVerifications SET Attempts=$n WHERE AccountId=$a";
                bump.Parameters.AddWithValue("$n", attempts);
                bump.Parameters.AddWithValue("$a", accountId);
                await bump.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return VerifyResult.Invalid;
        }
        finally { _gate.Release(); }
    }

    /// <summary>The link in the email (a 256-bit random token, so no attempt counting is needed).</summary>
    public async Task<VerifyResult> VerifyLinkAsync(string? token, CancellationToken ct)
    {
        var now = _clock();
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 100)
            return VerifyResult.Invalid;

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

            string? accountId = null;
            DateTimeOffset expires = default;
            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT AccountId, ExpiresAt FROM EmailVerifications WHERE LinkHash=$h";
                read.Parameters.AddWithValue("$h", HashToken(token));
                await using var r = await read.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    accountId = r.GetString(0);
                    expires = Parse(r.GetString(1)) ?? default;
                }
            }

            if (accountId is null)
                return VerifyResult.Invalid;                          // unknown, already used or replaced

            if (now >= expires)
            {
                await DeletePendingAsync(c, tx, accountId, ct);
                await tx.CommitAsync(ct);
                return VerifyResult.Expired;
            }

            await CompleteAsync(c, tx, accountId, now, ct);
            await tx.CommitAsync(ct);
            return VerifyResult.Verified;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Removes everything about an account (account deletion).</summary>
    public async Task DeleteAsync(string accountId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM EmailVerifications WHERE AccountId=$a; DELETE FROM EmailVerified WHERE AccountId=$a;";
            cmd.Parameters.AddWithValue("$a", accountId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqliteConnection(_connectionString);
        await c.OpenAsync(ct);
        return c;
    }

    private static async Task<bool> IsVerifiedCoreAsync(SqliteConnection c, SqliteTransaction? tx, string accountId, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM EmailVerified WHERE AccountId=$a";
        cmd.Parameters.AddWithValue("$a", accountId);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task CompleteAsync(SqliteConnection c, SqliteTransaction tx, string accountId, DateTimeOffset now, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR IGNORE INTO EmailVerified(AccountId,VerifiedAt) VALUES($a,$t);
DELETE FROM EmailVerifications WHERE AccountId=$a;";
        cmd.Parameters.AddWithValue("$a", accountId);
        cmd.Parameters.AddWithValue("$t", now.UtcDateTime.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeletePendingAsync(SqliteConnection c, SqliteTransaction tx, string accountId, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM EmailVerifications WHERE AccountId=$a";
        cmd.Parameters.AddWithValue("$a", accountId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DateTimeOffset? Parse(string value) =>
        DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string HashCode(string salt, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("email-code:" + salt + ":" + code)));

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("email-link:" + token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
