using System.Text.RegularExpressions;
using CarScanner.Api.Services.Email;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

internal sealed class CapturingLoginLog : ILogger<LoginCodeService>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception));
}

/// <summary>
/// The sign-in code: single use, expiry, attempts, resend limits — but unlike EmailVerificationStore, it is never
/// a one-time check. There is no "AlreadyVerified" state here: a fresh code can always be issued and checked
/// again, because every login needs one, not only the first.
/// </summary>
public sealed class LoginCodeStoreTests : IDisposable
{
    private const string Account = "11111111-1111-1111-1111-111111111111";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-logincode-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EmailClock _clock = new();
    private readonly LoginCodeStore _store;

    public LoginCodeStoreTests() =>
        _store = new LoginCodeStore($"Data Source={_file}", _clock.Read);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private async Task<IssuedLoginCode> IssueAsync(string account = Account) => await _store.IssueAsync(account, CancellationToken.None);

    [Fact]
    public async Task TheIssuedCode_IsSixDigits_AndIsTheOneTheVerifierAccepts()
    {
        var issued = await IssueAsync();
        Assert.Equal(LoginCodeIssueStatus.Issued, issued.Status);
        Assert.Matches("^[0-9]{6}$", issued.Code!);
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, issued.Code, default));
    }

    [Fact]
    public async Task ACodeTypedWithSpaces_StillWorks()
    {
        var issued = await IssueAsync();
        var spaced = issued.Code![..3] + " " + issued.Code![3..];
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, spaced, default));
    }

    [Fact]
    public async Task ACode_IsSingleUse()
    {
        var issued = await IssueAsync();
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, issued.Code, default));
        Assert.Equal(LoginCodeVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, issued.Code, default));
    }

    [Fact]
    public async Task UnlikeEmailVerification_ANewCodeCanAlwaysBeIssuedAndCheckedAgain()
    {
        // first login
        var first = await IssueAsync();
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, first.Code, default));

        // a later login: a fresh code is issued and required again — nothing is "remembered" as already done
        _clock.Advance(LoginCodeStore.ResendCooldown + TimeSpan.FromSeconds(1));
        var second = await IssueAsync();
        Assert.Equal(LoginCodeIssueStatus.Issued, second.Status);
        if (first.Code != second.Code)
            Assert.Equal(LoginCodeVerifyResult.Invalid, await _store.VerifyAsync(Account, first.Code, default));   // the old one no longer matches
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, second.Code, default));
    }

    [Fact]
    public async Task AWrongCode_IsInvalid_AndFiveWrongOnesBurnTheCode()
    {
        var issued = await IssueAsync();
        var wrong = issued.Code == "000000" ? "111111" : "000000";

        for (var i = 1; i < LoginCodeStore.MaxAttempts; i++)
            Assert.Equal(LoginCodeVerifyResult.Invalid, await _store.VerifyAsync(Account, wrong, default));
        Assert.Equal(LoginCodeVerifyResult.TooManyAttempts, await _store.VerifyAsync(Account, wrong, default));

        Assert.Equal(LoginCodeVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, issued.Code, default));   // even the right one
    }

    [Theory]
    [InlineData("")] [InlineData("12345")] [InlineData("1234567")] [InlineData("abcdef")] [InlineData(null)]
    public async Task MalformedCodes_AreInvalid(string? typed)
    {
        await IssueAsync();
        Assert.Equal(LoginCodeVerifyResult.Invalid, await _store.VerifyAsync(Account, typed, default));
    }

    [Fact]
    public async Task NothingPending_MeansNoCode()
    {
        Assert.Equal(LoginCodeVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, "123456", default));
    }

    [Fact]
    public async Task TheCode_ExpiresAfterTenMinutes()
    {
        var issued = await IssueAsync();
        Assert.Equal(_clock.Now + LoginCodeStore.Lifetime, issued.ExpiresAt);

        _clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, issued.Code, default));

        var late = await _store.IssueAsync("33333333-3333-3333-3333-333333333333", default);
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(LoginCodeVerifyResult.Expired, await _store.VerifyAsync("33333333-3333-3333-3333-333333333333", late.Code, default));
    }

    [Fact]
    public async Task SendingAgain_ReplacesTheOldCode_AfterTheCooldown()
    {
        var first = await IssueAsync();

        var tooSoon = await IssueAsync();
        Assert.Equal(LoginCodeIssueStatus.Cooldown, tooSoon.Status);
        Assert.Equal(_clock.Now + LoginCodeStore.ResendCooldown, tooSoon.RetryAfter);
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, first.Code, default));   // the first code still works until replaced
    }

    [Fact]
    public async Task AtMostFiveEmailsAnHour_AreIssued_ThenTheWindowReopens()
    {
        for (var i = 0; i < LoginCodeStore.MaxSendsPerHour; i++)
        {
            Assert.Equal(LoginCodeIssueStatus.Issued, (await IssueAsync()).Status);
            _clock.Advance(LoginCodeStore.ResendCooldown + TimeSpan.FromSeconds(1));
        }

        var blocked = await IssueAsync();
        Assert.Equal(LoginCodeIssueStatus.HourlyLimit, blocked.Status);
        Assert.NotNull(blocked.RetryAfter);

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(LoginCodeIssueStatus.Issued, (await IssueAsync()).Status);
    }

    [Fact]
    public async Task AccountsAreIndependent()
    {
        var a = await IssueAsync(Account);
        var b = await IssueAsync("44444444-4444-4444-4444-444444444444");
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync("44444444-4444-4444-4444-444444444444", b.Code, default));
        Assert.Equal(LoginCodeVerifyResult.Verified, await _store.VerifyAsync(Account, a.Code, default));
    }

    [Fact]
    public async Task TheDatabase_HoldsNoCodeInClearText()
    {
        var issued = await IssueAsync();
        SqliteConnection.ClearAllPools();

        await using var c = new SqliteConnection($"Data Source={_file}");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT AccountId||'|'||CodeSalt||'|'||CodeHash||'|'||ExpiresAt||'|'||LastSentAt FROM LoginCodes";
        var row = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.DoesNotContain("|" + issued.Code + "|", row);
        Assert.Matches("\\|[0-9A-F]{64}\\|", row);                             // hashes only
    }

    [Fact]
    public async Task DeletingTheAccount_RemovesThePendingCode()
    {
        var issued = await IssueAsync();
        await _store.DeleteAsync(Account, default);
        Assert.Equal(LoginCodeVerifyResult.NoPendingCode, await _store.VerifyAsync(Account, issued.Code, default));
    }
}

/// <summary>Issue + render + send as one path, reusing the registration email template with no confirmation link.</summary>
public sealed class LoginCodeServiceTests : IDisposable
{
    private const string Account = "55555555-5555-5555-5555-555555555555";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-logincodesvc-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EmailClock _clock = new();
    private readonly CapturingSender _sender = new();
    private readonly CapturingLoginLog _log = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private LoginCodeService Create(string? baseUrl = "https://api.example.test")
    {
        var store = new LoginCodeStore($"Data Source={_file}", _clock.Read);
        var options = new EmailOptions { Host = "smtp.example.test", FromAddress = "no-reply@example.test", PublicBaseUrl = baseUrl, SupportEmail = "support@example.test" };
        return new LoginCodeService(store, _sender, options, _log, _clock.Read);
    }

    private static string CodeIn(EmailMessage m) => Regex.Match(m.Html, "<span class=\"code\"[^>]*>(\\d{6})</span>").Groups[1].Value;

    [Fact]
    public async Task TheEmailCarriesTheRealCode_AndNeverAConfirmationLink()
    {
        var service = Create();
        var outcome = await service.SendAsync(Account, "driver@example.test", "ka", default);

        Assert.Equal(LoginCodeSendStatus.Sent, outcome.Status);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("driver@example.test", mail.To);

        var code = CodeIn(mail);
        Assert.Matches("^[0-9]{6}$", code);
        Assert.DoesNotContain("<a ", mail.Html);                               // sign-in codes are typed in the app only
        Assert.DoesNotContain("href=", mail.Html);

        Assert.Equal(LoginCodeVerifyResult.Verified, await service.Store.VerifyAsync(Account, code, default));
    }

    [Fact]
    public async Task ACodeCanBeSentAgainAfterIt_WasAlreadyUsed()
    {
        var service = Create();
        await service.SendAsync(Account, "driver@example.test", "en", default);
        var code = CodeIn(_sender.Sent[0]);
        Assert.Equal(LoginCodeVerifyResult.Verified, await service.Store.VerifyAsync(Account, code, default));

        _clock.Advance(TimeSpan.FromHours(2));
        var again = await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.Equal(LoginCodeSendStatus.Sent, again.Status);                  // not "AlreadyVerified" — there is no such state
        Assert.Equal(2, _sender.Sent.Count);
    }

    [Fact]
    public async Task WhenEmailIsNotConfigured_NothingIsIssuedOrSent()
    {
        _sender.IsConfigured = false;
        var service = Create();
        Assert.False(service.Enabled);

        var outcome = await service.SendAsync(Account, "driver@example.test", "en", default);

        Assert.Equal(LoginCodeSendStatus.NotConfigured, outcome.Status);
        Assert.Empty(_sender.Sent);
        Assert.Equal(LoginCodeVerifyResult.NoPendingCode, await service.Store.VerifyAsync(Account, "123456", default));
    }

    [Fact]
    public async Task AFailingMailServer_DoesNotThrow_AndNothingSecretIsLogged()
    {
        _sender.Fail = new InvalidOperationException("535 auth failed for secret-user:S3cretPassw0rd at smtp.example.test");
        var service = Create();

        var outcome = await service.SendAsync(Account, "driver@example.test", "en", default);

        Assert.Equal(LoginCodeSendStatus.SendFailed, outcome.Status);
        var logged = string.Join("\n", _log.Lines);
        Assert.NotEmpty(logged);
        Assert.DoesNotContain("S3cretPassw0rd", logged);
        Assert.DoesNotContain("driver@example.test", logged);
        Assert.DoesNotMatch("\\b\\d{6}\\b", logged);
    }

    [Fact]
    public async Task ResendProtection_AppliesToLoginCodesToo()
    {
        var service = Create();
        Assert.Equal(LoginCodeSendStatus.Sent, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);

        var second = await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.Equal(LoginCodeSendStatus.Cooldown, second.Status);
        Assert.Single(_sender.Sent);

        _clock.Advance(LoginCodeStore.ResendCooldown + TimeSpan.FromSeconds(1));
        Assert.Equal(LoginCodeSendStatus.Sent, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);
        Assert.Equal(2, _sender.Sent.Count);
    }
}

/// <summary>The wiring into the real login flow: password first, code second, nothing skipped.</summary>
public sealed class LoginCodeWiringTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void Login_ChecksThePasswordBeforeEverSendingACode()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Program.cs"));
        var login = program[program.IndexOf("\"/api/accounts/login\"", StringComparison.Ordinal)..];
        login = login[..login.IndexOf("/api/accounts/login/verify-code", StringComparison.Ordinal)];

        var verifyPassword = login.IndexOf("VerifyCredentialsAsync", StringComparison.Ordinal);
        var sendCode = login.IndexOf("loginCodes.SendAsync", StringComparison.Ordinal);
        Assert.True(verifyPassword > 0 && sendCode > verifyPassword, "the password must be checked before any code is sent");
        Assert.Contains("needsCode = true", login);
    }

    [Fact]
    public void VerifyCode_RechecksThePassword_AndNeverTrustsTheCodeAlone()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Program.cs"));
        var verify = program[program.IndexOf("\"/api/accounts/login/verify-code\"", StringComparison.Ordinal)..];
        verify = verify[..verify.IndexOf("FinishLoginAsync(AccountRequest", StringComparison.Ordinal)];

        var verifyPassword = verify.IndexOf("VerifyCredentialsAsync", StringComparison.Ordinal);
        var verifyCode = verify.IndexOf("loginCodes.Store.VerifyAsync", StringComparison.Ordinal);
        Assert.True(verifyPassword > 0 && verifyPassword < verifyCode, "the password is re-checked before the code is checked");
        Assert.Contains("RateWindow.TryConsume", verify);
    }

    [Fact]
    public void AMailOutage_FallsBackToSingleStepLogin_InsteadOfLockingEveryoneOut()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Program.cs"));
        var login = program[program.IndexOf("\"/api/accounts/login\"", StringComparison.Ordinal)..];
        login = login[..login.IndexOf("/api/accounts/login/verify-code", StringComparison.Ordinal)];
        Assert.Contains("if (loginCodes.Enabled)", login);
        Assert.Contains("FinishLoginAsync(request, http, accounts)", login);
    }

    [Fact]
    public void TheApp_AsksForTheCodeOnEveryLogin_NotOnlyRegistration()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services/AccountService.cs"));
        Assert.Contains("NeedsLoginCodeCode", service);
        Assert.Contains("VerifyLoginCodeAsync", service);

        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views/AccountPage.xaml.cs"));
        Assert.Contains("AccountService.NeedsLoginCodeCode", page);
        Assert.Contains("RunLoginCodeAsync", page);
        Assert.DoesNotContain("\"Later\"", page);                              // no skip button on the login code prompt either
    }

    [Fact]
    public void AccountDeletion_RemovesAnyPendingLoginCode()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Program.cs"));
        Assert.Contains("LoginCodeStore>().DeleteAsync(accountId, ct)", program);
    }
}
