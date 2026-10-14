using System.Text.RegularExpressions;
using CarScanner.Api.Services.Email;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

internal sealed class CapturingPasswordResetLog : ILogger<PasswordResetService>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception));
}

/// <summary>
/// The "forgot password" code: same proven shape as LoginCodeStore (single use, expiry, attempts, resend
/// limits), but its own table — issuing one must never disturb a pending login 2FA code for the same account,
/// and vice versa.
/// </summary>
public sealed class PasswordResetStoreTests : IDisposable
{
    private const string Account = "21111111-1111-1111-1111-111111111111";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-pwreset-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EmailClock _clock = new();
    private readonly PasswordResetStore _store;

    public PasswordResetStoreTests() =>
        _store = new PasswordResetStore($"Data Source={_file}", _clock.Read);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private async Task<IssuedPasswordResetCode> IssueAsync(string account = Account) => await _store.IssueAsync(account, CancellationToken.None);

    [Fact]
    public async Task TheIssuedCode_IsSixDigits_AndIsTheOneTheVerifierAccepts()
    {
        var issued = await IssueAsync();
        Assert.Equal(PasswordResetIssueStatus.Issued, issued.Status);
        Assert.Matches("^[0-9]{6}$", issued.Code!);
        Assert.Equal(PasswordResetVerifyResult.Verified, await _store.VerifyAsync(Account, issued.Code, default));
    }

    [Fact]
    public async Task ACode_IsSingleUse()
    {
        var issued = await IssueAsync();
        Assert.Equal(PasswordResetVerifyResult.Verified, await _store.VerifyAsync(Account, issued.Code, default));
        Assert.Equal(PasswordResetVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, issued.Code, default));
    }

    [Fact]
    public async Task AWrongCode_IsInvalid_AndFiveWrongOnesBurnTheCode()
    {
        var issued = await IssueAsync();
        var wrong = issued.Code == "000000" ? "111111" : "000000";

        for (var i = 1; i < PasswordResetStore.MaxAttempts; i++)
            Assert.Equal(PasswordResetVerifyResult.Invalid, await _store.VerifyAsync(Account, wrong, default));
        Assert.Equal(PasswordResetVerifyResult.TooManyAttempts, await _store.VerifyAsync(Account, wrong, default));

        Assert.Equal(PasswordResetVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, issued.Code, default));   // even the right one
    }

    [Fact]
    public async Task NothingPending_MeansNoCode()
    {
        Assert.Equal(PasswordResetVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, "123456", default));
    }

    [Fact]
    public async Task TheCode_ExpiresAfterTenMinutes()
    {
        var issued = await IssueAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(PasswordResetVerifyResult.Expired, await _store.VerifyAsync(Account, issued.Code, default));
    }

    [Fact]
    public async Task SendingAgain_ReplacesTheOldCode_AfterTheCooldown()
    {
        var first = await IssueAsync();

        var tooSoon = await IssueAsync();
        Assert.Equal(PasswordResetIssueStatus.Cooldown, tooSoon.Status);
        Assert.Equal(PasswordResetVerifyResult.Verified, await _store.VerifyAsync(Account, first.Code, default));   // the first code still works until replaced
    }

    [Fact]
    public async Task AtMostFiveEmailsAnHour_AreIssued_ThenTheWindowReopens()
    {
        for (var i = 0; i < PasswordResetStore.MaxSendsPerHour; i++)
        {
            Assert.Equal(PasswordResetIssueStatus.Issued, (await IssueAsync()).Status);
            _clock.Advance(PasswordResetStore.ResendCooldown + TimeSpan.FromSeconds(1));
        }

        var blocked = await IssueAsync();
        Assert.Equal(PasswordResetIssueStatus.HourlyLimit, blocked.Status);

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(PasswordResetIssueStatus.Issued, (await IssueAsync()).Status);
    }

    [Fact]
    public async Task TheDatabase_HoldsNoCodeInClearText()
    {
        var issued = await IssueAsync();
        SqliteConnection.ClearAllPools();

        await using var c = new SqliteConnection($"Data Source={_file}");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT AccountId||'|'||CodeSalt||'|'||CodeHash||'|'||ExpiresAt FROM PasswordResetCodes";
        var row = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.DoesNotContain("|" + issued.Code + "|", row);
        Assert.Matches("\\|[0-9A-F]{64}\\|", row);
    }

    [Fact]
    public async Task DeletingTheAccount_RemovesThePendingCode()
    {
        var issued = await IssueAsync();
        await _store.DeleteAsync(Account, default);
        Assert.Equal(PasswordResetVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, issued.Code, default));
    }

    [Fact]
    public async Task APendingLoginCode_ForTheSameAccount_IsNotDisturbed()
    {
        // The two stores are independent tables — issuing a password-reset code must not touch a concurrent
        // login 2FA code for the same account, and vice versa.
        var loginCodes = new LoginCodeStore($"Data Source={_file}", _clock.Read);
        var loginCode = await loginCodes.IssueAsync(Account, default);

        var resetCode = await IssueAsync();

        Assert.Equal(LoginCodeVerifyResult.Verified, await loginCodes.VerifyAsync(Account, loginCode.Code, default));
        Assert.Equal(PasswordResetVerifyResult.Verified, await _store.VerifyAsync(Account, resetCode.Code, default));
    }
}

/// <summary>Issue + render + send as one path, reusing the registration email template with no confirmation link.</summary>
public sealed class PasswordResetServiceTests : IDisposable
{
    private const string Account = "25555555-5555-5555-5555-555555555555";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-pwresetsvc-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EmailClock _clock = new();
    private readonly CapturingSender _sender = new();
    private readonly CapturingPasswordResetLog _log = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private PasswordResetService Create()
    {
        var store = new PasswordResetStore($"Data Source={_file}", _clock.Read);
        var options = new EmailOptions { Host = "smtp.example.test", FromAddress = "no-reply@example.test", PublicBaseUrl = "https://api.example.test", SupportEmail = "support@example.test" };
        return new PasswordResetService(store, _sender, options, _log, _clock.Read);
    }

    private static string CodeIn(EmailMessage m) => Regex.Match(m.Html, "<span class=\"code\"[^>]*>(\\d{6})</span>").Groups[1].Value;

    [Fact]
    public async Task TheEmailCarriesTheRealCode_AndNeverAConfirmationLink()
    {
        var service = Create();
        var outcome = await service.SendAsync(Account, "driver@example.test", "ka", default);

        Assert.Equal(PasswordResetSendStatus.Sent, outcome.Status);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("driver@example.test", mail.To);

        var code = CodeIn(mail);
        Assert.Matches("^[0-9]{6}$", code);
        Assert.DoesNotContain("<a ", mail.Html);
        Assert.DoesNotContain("href=", mail.Html);

        Assert.Equal(PasswordResetVerifyResult.Verified, await service.Store.VerifyAsync(Account, code, default));
    }

    [Fact]
    public async Task WhenEmailIsNotConfigured_NothingIsIssuedOrSent()
    {
        _sender.IsConfigured = false;
        var service = Create();
        Assert.False(service.Enabled);

        var outcome = await service.SendAsync(Account, "driver@example.test", "en", default);

        Assert.Equal(PasswordResetSendStatus.NotConfigured, outcome.Status);
        Assert.Empty(_sender.Sent);
        Assert.Equal(PasswordResetVerifyResult.NoPendingCode, await service.Store.VerifyAsync(Account, "123456", default));
    }

    [Fact]
    public async Task AFailingMailServer_DoesNotThrow_AndNothingSecretIsLogged()
    {
        _sender.Fail = new InvalidOperationException("535 auth failed for secret-user:S3cretPassw0rd at smtp.example.test");
        var service = Create();

        var outcome = await service.SendAsync(Account, "driver@example.test", "en", default);

        Assert.Equal(PasswordResetSendStatus.SendFailed, outcome.Status);
        var logged = string.Join("\n", _log.Lines);
        Assert.NotEmpty(logged);
        Assert.DoesNotContain("S3cretPassw0rd", logged);
        Assert.DoesNotContain("driver@example.test", logged);
        Assert.DoesNotMatch("\\b\\d{6}\\b", logged);
    }

    [Fact]
    public async Task ResendProtection_AppliesToPasswordResetCodesToo()
    {
        var service = Create();
        Assert.Equal(PasswordResetSendStatus.Sent, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);

        var second = await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.Equal(PasswordResetSendStatus.Cooldown, second.Status);
        Assert.Single(_sender.Sent);
    }
}

/// <summary>The wiring into the real HTTP routes: never reveals whether an e-mail is registered, never skips the server-side password check.</summary>
public sealed class PasswordResetWiringTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Program() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend", "CarScanner.Api", "Program.cs")).Replace("\r\n", "\n");

    /// <summary>The source of one route's handler: from its path string up to the next route.</summary>
    private static string Route(string program, string path)
    {
        var at = program.IndexOf("\"" + path + "\"", StringComparison.Ordinal);
        Assert.True(at >= 0, path + " not found");
        var rest = program[at..];
        var next = Regex.Match(rest[10..], @"\napp\.Map(Get|Post|Delete|Put)\(");
        return next.Success ? rest[..(next.Index + 10)] : rest;
    }

    [Fact]
    public void ForgotPassword_AlwaysAnswersTheSameWay_RegardlessOfWhetherTheEmailExists()
    {
        var route = Route(Program(), "/api/accounts/password/forgot");
        // Exactly one Results.Ok(...) in the whole handler, reached whether or not FindByEmailAsync found anything —
        // i.e. no branch returns a different shape/status for "unknown email".
        var okCount = Regex.Matches(route, "Results\\.Ok\\(").Count;
        Assert.Equal(1, okCount);
        Assert.Contains("identity is not null", route);
    }

    [Fact]
    public void ResetPassword_TreatsAnUnknownEmail_TheSameAsAWrongCode()
    {
        var route = Route(Program(), "/api/accounts/password/reset");
        Assert.Contains("identity is null || accounts.IsBanned(identity.AccountId)\n            ? PasswordResetVerifyResult.Invalid", route);
    }

    [Fact]
    public void ResetPassword_EndsEverySession_LikeTheAdminTemporaryPasswordAction()
    {
        var route = Route(Program(), "/api/accounts/password/reset");
        Assert.Contains("accounts.SetPasswordAsync(identity!.AccountId, request?.NewPassword", route);
        Assert.Contains("mustChange: false", route);
    }

    [Fact]
    public void BothRoutes_RateLimitByIp()
    {
        var forgot = Route(Program(), "/api/accounts/password/forgot");
        Assert.Contains("password-forgot-ip:", forgot);
        var reset = Route(Program(), "/api/accounts/password/reset");
        Assert.Contains("password-reset-ip:", reset);
    }

    [Fact]
    public void AccountDeletion_RemovesAnyPendingPasswordResetCode()
    {
        Assert.Contains("PasswordResetStore>().DeleteAsync(accountId, ct)", Program());
    }

    [Fact]
    public void TheMobileApp_NeverClaimsWhetherTheEmailIsRegistered()
    {
        var accountService = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services", "AccountService.cs"));
        Assert.Contains("ForgotPasswordAsync", accountService);
        Assert.Contains("ResetPasswordAsync", accountService);

        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "AccountPage.xaml.cs"));
        Assert.Contains("ForgotPasswordAsync", page);
        Assert.Contains("ResetPasswordAsync", page);
        // The prompt's cancel button must be a real label — DisplayPromptAsync is not documented as accepting
        // a null cancel (unlike DisplayAlertAsync) and crashes on iOS when passed null.
        Assert.DoesNotContain("cancel: null", page);
    }
}
