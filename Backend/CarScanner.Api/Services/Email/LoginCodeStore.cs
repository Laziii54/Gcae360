using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services.Email;

public enum LoginCodeIssueStatus
{
    Issued,
    Cooldown,
    HourlyLimit
}

public enum LoginCodeVerifyResult
{
    Verified,
    Invalid,
    Expired,
    TooManyAttempts,
    NoPendingCode
}

/// <summary>A freshly issued sign-in code. The plain code exists only here, for the one email; the database keeps a hash.</summary>
public sealed record IssuedLoginCode(
    LoginCodeIssueStatus Status,
    string? Code,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RetryAfter);

/// <summary>
/// A short-lived sign-in code required on every login, not a one-time check: unlike <see cref="EmailVerificationStore"/>
/// (which remembers a past success forever), this store never short-circuits — a fresh code is issued and checked
/// every time, so a stolen password alone is not enough to sign in.
///
///  • Single use: a code is cleared the moment it is checked, right or wrong five times.
///  • Expires after <see cref="Lifetime"/> (10 minutes).
///  • The code is stored only as a salted SHA-256 hash and compared in constant time; five wrong codes burn it.
///  • Sending again replaces the old code; at most one email per minute and five per hour per account.
///  • Nothing is stored in clear text and nothing here is ever logged.
/// </summary>
public sealed class LoginCodeStore
{
    public const int MaxAttempts = 5;
    public const int MaxSendsPerHour = 5;
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _clock;

    public LoginCodeStore(IHostEnvironment env, IConfiguration config)
        : this(BackendDataPath.CreateConnectionString(BackendDataPath.ResolveDatabasePath(env, config)))
    {
    }

    public LoginCodeStore(string connectionString, Func<DateTimeOffset>? clock = null)
    {
        _connectionString = connectionString;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS LoginCodes (
AccountId TEXT PRIMARY KEY,
CodeSalt TEXT NOT NULL,
CodeHash TEXT NOT NULL,
ExpiresAt TEXT NOT NULL,
Attempts INTEGER NOT NULL DEFAULT 0,
LastSentAt TEXT NOT NULL,
WindowStart TEXT NOT NULL,
SendsInWindow INTEGER NOT NULL DEFAULT 1);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Creates a new pending code (replacing any older one), unless throttled.</summary>
    public async Task<IssuedLoginCode> IssueAsync(string accountId, CancellationToken ct)
    {
        var now = _clock();
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

            DateTimeOffset? lastSent = null, windowStart = null;
            var sends = 0;
            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT LastSentAt, WindowStart, SendsInWindow FROM LoginCodes WHERE AccountId=$a";
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
                return new IssuedLoginCode(LoginCodeIssueStatus.Cooldown, null, null, lastSent + ResendCooldown);

            if (windowStart is null || now - windowStart >= TimeSpan.FromHours(1))
            {
                windowStart = now;
                sends = 0;
            }
            else if (sends >= MaxSendsPerHour)
            {
                return new IssuedLoginCode(LoginCodeIssueStatus.HourlyLimit, null, null, windowStart + TimeSpan.FromHours(1));
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var expires = now + Lifetime;

            await using (var upsert = c.CreateCommand())
            {
                upsert.Transaction = tx;
                upsert.CommandText = @"INSERT INTO LoginCodes(AccountId,CodeSalt,CodeHash,ExpiresAt,Attempts,LastSentAt,WindowStart,SendsInWindow)
VALUES($a,$s,$ch,$e,0,$now,$w,$n)
ON CONFLICT(AccountId) DO UPDATE SET CodeSalt=$s,CodeHash=$ch,ExpiresAt=$e,Attempts=0,LastSentAt=$now,WindowStart=$w,SendsInWindow=$n";
                upsert.Parameters.AddWithValue("$a", accountId);
                upsert.Parameters.AddWithValue("$s", salt);
                upsert.Parameters.AddWithValue("$ch", HashCode(salt, code));
                upsert.Parameters.AddWithValue("$e", expires.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$w", windowStart.Value.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$n", sends + 1);
                await upsert.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return new IssuedLoginCode(LoginCodeIssueStatus.Issued, code, expires, null);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The email for the pending code could not be sent: lift the cooldown so the user can retry at once.</summary>
    public async Task MarkSendFailedAsync(string accountId, CancellationToken ct)
    {
        var now = _clock();
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE LoginCodes SET ExpiresAt=$e, LastSentAt=$l WHERE AccountId=$a";
            cmd.Parameters.AddWithValue("$e", now.UtcDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("$l", (now - ResendCooldown).UtcDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("$a", accountId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The 6-digit code typed in the app. Verifying (right or wrong five times) always clears the pending code.</summary>
    public async Task<LoginCodeVerifyResult> VerifyAsync(string accountId, string? code, CancellationToken ct)
    {
        var now = _clock();
        var typed = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

            string? salt = null, hash = null;
            DateTimeOffset expires = default;
            var attempts = 0;
            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT CodeSalt, CodeHash, ExpiresAt, Attempts FROM LoginCodes WHERE AccountId=$a";
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
                return LoginCodeVerifyResult.NoPendingCode;

            if (now >= expires)
            {
                await DeletePendingAsync(c, tx, accountId, ct);
                await tx.CommitAsync(ct);
                return LoginCodeVerifyResult.Expired;
            }

            if (attempts >= MaxAttempts)
            {
                await DeletePendingAsync(c, tx, accountId, ct);
                await tx.CommitAsync(ct);
                return LoginCodeVerifyResult.TooManyAttempts;
            }

            var ok = typed.Length == 6 &&
                     CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashCode(salt, typed)), Encoding.ASCII.GetBytes(hash));
            if (ok)
            {
                await DeletePendingAsync(c, tx, accountId, ct);
                await tx.CommitAsync(ct);
                return LoginCodeVerifyResult.Verified;
            }

            attempts++;
            if (attempts >= MaxAttempts)
            {
                await DeletePendingAsync(c, tx, accountId, ct);      // the code is burned
                await tx.CommitAsync(ct);
                return LoginCodeVerifyResult.TooManyAttempts;
            }

            await using (var bump = c.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE LoginCodes SET Attempts=$n WHERE AccountId=$a";
                bump.Parameters.AddWithValue("$n", attempts);
                bump.Parameters.AddWithValue("$a", accountId);
                await bump.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return LoginCodeVerifyResult.Invalid;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Removes any pending code (account deletion).</summary>
    public async Task DeleteAsync(string accountId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM LoginCodes WHERE AccountId=$a";
            cmd.Parameters.AddWithValue("$a", accountId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqliteConnection(_connectionString);
        await c.OpenAsync(ct);
        return c;
    }

    private static async Task DeletePendingAsync(SqliteConnection c, SqliteTransaction tx, string accountId, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM LoginCodes WHERE AccountId=$a";
        cmd.Parameters.AddWithValue("$a", accountId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DateTimeOffset? Parse(string value) =>
        DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string HashCode(string salt, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("login-code:" + salt + ":" + code)));
}
