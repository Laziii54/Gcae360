using System.Security.Cryptography;
using System.Text;
using CarScanner.Api.Services;
using CarScanner.Api.Services.Email;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CarScanner.Tests;

/// <summary>
/// Google / Apple sign-in, server side. Every token here is signed by a throw-away RSA key generated inside the test and
/// "published" through a fake key source: no network, no real Google/Apple account, nothing about a real provider is
/// simulated beyond the documented token shape (issuer, audience, sub, email, email_verified, nonce, exp).
/// </summary>
public sealed class ExternalSignInTests : IDisposable
{
    private const string GoogleClient = "test-google-client.apps.googleusercontent.com";
    private const string AppleClient = "com.example.test.app";
    private const string Device1 = "11111111111111111111111111111111";
    private const string Device2 = "22222222222222222222222222222222";
    private const string Password = "correct-horse-battery";
    private const string Nonce = "nonce-nonce-nonce-nonce-0123456789";

    private static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gcar360-extauth-tests", Guid.NewGuid().ToString("N"));
    private readonly EmailClock _clock = new() { Now = Start };
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);
    private readonly FakeKeys _keys;
    private readonly AccountStore _accounts;
    private readonly EmailVerificationStore _verification;
    private readonly CapturingSender _sender = new();
    private readonly LoginCodeService _loginCodes;
    private readonly ExternalIdentityVerifier _verifier;
    private readonly ExternalAuthService _service;
    private int _tokenCounter;

    public ExternalSignInTests()
    {
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = _directory })
            .Build();
        _accounts = new AccountStore(new TestEnvironment(_directory), configuration);
        _verification = new EmailVerificationStore($"Data Source={Path.Combine(_directory, "verify.db")}", TimeSpan.FromMinutes(15), _clock.Read);
        var codeStore = new LoginCodeStore($"Data Source={Path.Combine(_directory, "codes.db")}", _clock.Read);
        var options = new EmailOptions { Host = "smtp.example.test", FromAddress = "no-reply@example.test", SupportEmail = "support@example.test" };
        _loginCodes = new LoginCodeService(codeStore, _sender, options, new CapturingLoginLog(), _clock.Read);

        _keys = new FakeKeys(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = "k1" });
        _verifier = new ExternalIdentityVerifier(
            new ExternalAuthOptions { GoogleClientIds = new[] { GoogleClient }, AppleClientIds = new[] { AppleClient } },
            _keys,
            _clock.Read);
        _service = new ExternalAuthService(_verifier, _accounts, _verification, _loginCodes, _clock.Read);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _rsa.Dispose();
        _otherRsa.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------------------------------------
    // token factory

    private string Token(
        string provider = ExternalProviders.Google,
        string? subject = "google-sub-1",
        string? email = "driver@example.test",
        object? emailVerified = null,
        bool omitEmailVerified = false,
        string? audience = null,
        string? issuer = null,
        string? nonce = Nonce,
        TimeSpan? expiresIn = null,
        TimeSpan? notBeforeOffset = null,
        RSA? signWith = null,
        string algorithm = SecurityAlgorithms.RsaSha256,
        string keyId = "k1")
    {
        var claims = new Dictionary<string, object> { ["jti"] = "t" + Interlocked.Increment(ref _tokenCounter) };
        if (subject is not null) claims["sub"] = subject;
        if (email is not null) claims["email"] = email;
        if (!omitEmailVerified) claims["email_verified"] = emailVerified ?? true;
        if (nonce is not null) claims["nonce"] = nonce;

        var now = _clock.Now.UtcDateTime;
        var expires = now + (expiresIn ?? TimeSpan.FromHours(1));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? (provider == ExternalProviders.Google ? "https://accounts.google.com" : "https://appleid.apple.com"),
            Audience = audience ?? (provider == ExternalProviders.Google ? GoogleClient : AppleClient),
            Claims = claims,
            IssuedAt = now,
            NotBefore = now + (notBeforeOffset ?? TimeSpan.Zero) - TimeSpan.FromMinutes(1),
            Expires = expires > now ? expires : now - TimeSpan.FromSeconds(1),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(signWith ?? _rsa) { KeyId = keyId }, algorithm)
        };
        if (expiresIn is { } past && past < TimeSpan.Zero)
        {
            // JsonWebTokenHandler refuses exp < nbf, so move the whole validity window into the past.
            descriptor.IssuedAt = now + past - TimeSpan.FromHours(1);
            descriptor.NotBefore = now + past - TimeSpan.FromHours(1);
            descriptor.Expires = now + past;
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private Task<ExternalAuthOutcome> SignInAsync(string token, string provider = ExternalProviders.Google, string? nonce = Nonce, string device = Device1, bool move = false) =>
        _service.SignInAsync(provider, token, nonce, device, move, CancellationToken.None);

    private async Task<AccountSessionResult> RegisterPasswordAccountAsync(string email = "driver@example.test", string device = Device1)
    {
        var account = await _accounts.RegisterAsync(email, Password, device, CancellationToken.None);
        Assert.NotNull(account);
        return account!;
    }

    // ---------------------------------------------------------------------------------------------
    // token validation

    [Fact]
    public async Task AValidGoogleToken_YieldsTheSignedClaims()
    {
        var result = await _verifier.VerifyAsync(ExternalProviders.Google, Token(), Nonce, default);

        Assert.Equal(ExternalVerifyStatus.Ok, result.Status);
        Assert.Equal("google-sub-1", result.Claims!.Subject);
        Assert.Equal("driver@example.test", result.Claims.Email);
        Assert.True(result.Claims.EmailVerified);
    }

    [Fact]
    public async Task AnExpiredToken_IsRefused()
    {
        var token = Token(expiresIn: TimeSpan.FromHours(-3));
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task ATokenJustPastExpiry_IsStillAcceptedInsideTheClockSkew_ButNotBeyondIt()
    {
        var token = Token(expiresIn: TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(ExternalVerifyStatus.Ok, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task ATokenThatIsNotValidYet_IsRefused()
    {
        var token = Token(notBeforeOffset: TimeSpan.FromHours(2));
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task ATokenForAnotherApp_WrongAudience_IsRefused()
    {
        var token = Token(audience: "someone-elses-client.apps.googleusercontent.com");
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task AGoogleTokenPresentedAsApple_IsRefused()
    {
        // Same signing key, but issuer and audience belong to Google.
        var token = Token(ExternalProviders.Google);
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Apple, token, null, default)).Status);
    }

    [Fact]
    public async Task AWrongIssuer_IsRefused()
    {
        var token = Token(issuer: "https://evil.example.test");
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task ATokenSignedByAnUnknownKey_IsRefused()
    {
        var token = Token(signWith: _otherRsa, keyId: "k-unknown");
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task ATokenWithATamperedPayload_IsRefused()
    {
        var parts = Token().Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        var forged = payload.Replace("driver@example.test", "victim@example.test");
        var token = $"{parts[0]}.{Base64UrlEncoder.Encode(forged)}.{parts[2]}";

        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task AnUnsignedToken_AlgNone_IsRefused()
    {
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncoder.Encode(
            "{\"iss\":\"https://accounts.google.com\",\"aud\":\"" + GoogleClient + "\",\"sub\":\"x\",\"email\":\"a@example.test\",\"email_verified\":true,\"nonce\":\"" + Nonce + "\",\"exp\":" +
            (_clock.Now.ToUnixTimeSeconds() + 3600) + "}");

        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, $"{header}.{payload}.", Nonce, default)).Status);
    }

    [Fact]
    public async Task AnHmacToken_SignedWithThePublicKeyAsSecret_IsRefused()
    {
        // The classic algorithm-confusion attack: only RS256 is ever accepted.
        var secret = new SymmetricSecurityKey(_rsa.ExportSubjectPublicKeyInfo()) { KeyId = "k1" };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "https://accounts.google.com",
            Audience = GoogleClient,
            Claims = new Dictionary<string, object> { ["sub"] = "x", ["email"] = "a@example.test", ["email_verified"] = true, ["nonce"] = Nonce },
            Expires = _clock.Now.UtcDateTime.AddHours(1),
            SigningCredentials = new SigningCredentials(secret, SecurityAlgorithms.HmacSha256)
        };
        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    public async Task GarbageInput_IsRefused_WithoutThrowing(string? token)
    {
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task ATokenWithoutASubject_IsRefused()
    {
        var token = Token(subject: null);
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default)).Status);
    }

    [Fact]
    public async Task Google_NonceIsMandatory_AndMustMatch()
    {
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, Token(), null, default)).Status);
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, Token(), "another-nonce-another-nonce-1234", default)).Status);
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Google, Token(nonce: null), Nonce, default)).Status);
        Assert.Equal(ExternalVerifyStatus.Ok, (await _verifier.VerifyAsync(ExternalProviders.Google, Token(), Nonce, default)).Status);
    }

    [Fact]
    public async Task Apple_TokenWithoutNonce_IsAccepted_ButOneBoundToAnotherNonceIsNot()
    {
        var plain = Token(ExternalProviders.Apple, "apple-sub-1", "x@privaterelay.appleid.com", "true", nonce: null);
        var ok = await _verifier.VerifyAsync(ExternalProviders.Apple, plain, null, default);
        Assert.Equal(ExternalVerifyStatus.Ok, ok.Status);
        Assert.True(ok.Claims!.EmailVerified);   // Apple sends "true" as text

        var bound = Token(ExternalProviders.Apple, "apple-sub-1", "x@privaterelay.appleid.com", "true", nonce: "server-side-nonce-1234567890");
        Assert.Equal(ExternalVerifyStatus.Invalid, (await _verifier.VerifyAsync(ExternalProviders.Apple, bound, "different-nonce-1234567890", default)).Status);
        Assert.Equal(ExternalVerifyStatus.Ok, (await _verifier.VerifyAsync(ExternalProviders.Apple, bound, "server-side-nonce-1234567890", default)).Status);
    }

    [Fact]
    public async Task AProviderWithNoConfiguredClientIds_FailsSafe()
    {
        var verifier = new ExternalIdentityVerifier(new ExternalAuthOptions(), _keys, _clock.Read);
        Assert.Equal(ExternalVerifyStatus.NotConfigured, (await verifier.VerifyAsync(ExternalProviders.Google, Token(), Nonce, default)).Status);
        Assert.False(verifier.IsConfigured(ExternalProviders.Apple));

        var service = new ExternalAuthService(verifier, _accounts, _verification, _loginCodes, _clock.Read);
        var outcome = await service.SignInAsync(ExternalProviders.Google, Token(), Nonce, Device1, false, default);
        Assert.Equal(ExternalAuthStatus.NotConfigured, outcome.Status);
        Assert.Null(outcome.Session);
    }

    [Fact]
    public void ClientIds_ComeFromConfigurationOrTheEnvironmentVariableName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Google:ClientIds"] = "a.apps.googleusercontent.com, b.apps.googleusercontent.com", ["Auth:Apple:ClientIds"] = "com.example.one" })
            .Build();
        var options = ExternalAuthOptions.From(configuration);

        Assert.Equal(new[] { "a.apps.googleusercontent.com", "b.apps.googleusercontent.com" }, options.GoogleClientIds);
        Assert.Equal(new[] { "com.example.one" }, options.AppleClientIds);
    }

    [Fact]
    public async Task APublishedKeyRotation_TriggersOneRefresh()
    {
        _keys.Hidden = true;   // first fetch returns only an unrelated key
        var token = Token();
        var first = await _verifier.VerifyAsync(ExternalProviders.Google, token, Nonce, default);
        Assert.Equal(ExternalVerifyStatus.Invalid, first.Status);
        Assert.Equal(2, _keys.Calls);   // normal fetch + exactly one forced refresh

        _keys.Hidden = false;
        Assert.Equal(ExternalVerifyStatus.Ok, (await _verifier.VerifyAsync(ExternalProviders.Google, Token(), Nonce, default)).Status);
    }

    // ---------------------------------------------------------------------------------------------
    // first-time sign-in (new account)

    [Fact]
    public async Task FirstGoogleSignIn_CreatesAnAccount_WithoutAPassword_AndAWorkingSession()
    {
        var outcome = await SignInAsync(Token());

        Assert.Equal(ExternalAuthStatus.SignedIn, outcome.Status);
        Assert.True(outcome.IsNewAccount);
        var session = outcome.Session!;
        Assert.Equal("driver@example.test", session.Email);

        // The session is a normal app session on this phone.
        var resolved = await _accounts.ResolveAsync("Bearer " + session.AccessToken, Device1, default);
        Assert.Equal(session.AccountId, resolved!.AccountId);
        // ...and not on another phone.
        Assert.Null(await _accounts.ResolveAsync("Bearer " + session.AccessToken, Device2, default));

        // The provider vouched for the address: no verification mail needed.
        Assert.True(await _verification.IsVerifiedAsync(session.AccountId, default));

        // The random password nobody knows cannot be used.
        Assert.Null(await _accounts.VerifyCredentialsAsync("driver@example.test", "", default));
        Assert.Null(await _accounts.VerifyCredentialsAsync("driver@example.test", Password, default));
    }

    [Fact]
    public async Task SecondSignIn_WithTheSameProviderIdentity_ReturnsTheSameAccountId()
    {
        var first = await SignInAsync(Token());
        var second = await SignInAsync(Token());

        Assert.Equal(ExternalAuthStatus.SignedIn, second.Status);
        Assert.False(second.IsNewAccount);
        Assert.Equal(first.Session!.AccountId, second.Session!.AccountId);
    }

    [Fact]
    public async Task ChangingTheEmailAtTheProvider_DoesNotCreateASecondAccount()
    {
        var first = await SignInAsync(Token());
        var second = await SignInAsync(Token(email: "renamed@example.test"));

        Assert.Equal(first.Session!.AccountId, second.Session!.AccountId);
    }

    [Fact]
    public async Task AUsedToken_CannotBeReplayed()
    {
        var token = Token();
        Assert.Equal(ExternalAuthStatus.SignedIn, (await SignInAsync(token)).Status);

        var replay = await SignInAsync(token);
        Assert.Equal(ExternalAuthStatus.Rejected, replay.Status);
        Assert.Null(replay.Session);
    }

    [Fact]
    public async Task AnAppleSignIn_WithTheHiddenRelayEmail_Works()
    {
        var token = Token(ExternalProviders.Apple, "apple-sub-9", "abc123@privaterelay.appleid.com", "true", nonce: null);
        var outcome = await SignInAsync(token, ExternalProviders.Apple, nonce: null);

        Assert.Equal(ExternalAuthStatus.SignedIn, outcome.Status);
        Assert.Equal("abc123@privaterelay.appleid.com", outcome.Session!.Email);
    }

    [Fact]
    public async Task AnUnverifiedOrMissingEmail_NeverCreatesAnAccount()
    {
        var unverified = await SignInAsync(Token(emailVerified: false));
        Assert.Equal(ExternalAuthStatus.Rejected, unverified.Status);
        Assert.Equal("email_not_verified", unverified.Reason);

        var missing = await SignInAsync(Token(email: null, omitEmailVerified: true));
        Assert.Equal(ExternalAuthStatus.Rejected, missing.Status);
        Assert.Equal("email_missing", missing.Reason);

        Assert.Null(await _accounts.FindByEmailAsync("driver@example.test", default));
    }

    [Fact]
    public async Task ARejectedDeviceId_IsABadRequest_NotACrash()
    {
        var outcome = await SignInAsync(Token(), device: "not-a-device-id");
        Assert.Equal(ExternalAuthStatus.Rejected, outcome.Status);
        Assert.Equal("invalid_request", outcome.Reason);
    }

    // ---------------------------------------------------------------------------------------------
    // existing accounts and secure linking

    [Fact]
    public async Task ASameEmailPasswordAccount_IsNeverMergedOnEmailAlone()
    {
        var existing = await RegisterPasswordAccountAsync();

        var outcome = await SignInAsync(Token());

        Assert.Equal(ExternalAuthStatus.LinkRequired, outcome.Status);
        Assert.Null(outcome.Session);
        Assert.False(string.IsNullOrEmpty(outcome.LinkTicket));
        // Nothing was linked and no second account exists.
        Assert.Null(await _accounts.FindByExternalIdentityAsync(ExternalProviders.Google, "google-sub-1", default));
        Assert.Equal(existing.AccountId, (await _accounts.FindByEmailAsync("driver@example.test", default))!.AccountId);
    }

    [Fact]
    public async Task Linking_NeedsThePassword_AndTheEmailedCode()
    {
        var existing = await RegisterPasswordAccountAsync();
        var ticket = (await SignInAsync(Token())).LinkTicket;

        // Wrong password.
        var wrong = await _service.LinkAsync(ticket, "wrong-password-123", null, Device1, false, "en", default);
        Assert.Equal(ExternalAuthStatus.Rejected, wrong.Status);
        Assert.Null(await _accounts.FindByExternalIdentityAsync(ExternalProviders.Google, "google-sub-1", default));

        // Right password: a code is mailed to the account's address first.
        var needsCode = await _service.LinkAsync(ticket, Password, null, Device1, false, "en", default);
        Assert.Equal(ExternalAuthStatus.NeedsCode, needsCode.Status);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("driver@example.test", mail.To);
        Assert.Null(await _accounts.FindByExternalIdentityAsync(ExternalProviders.Google, "google-sub-1", default));

        // A wrong code does not link.
        var badCode = await _service.LinkAsync(ticket, Password, "000000", Device1, false, "en", default);
        Assert.Equal(ExternalAuthStatus.Rejected, badCode.Status);

        // The real code links, signs in, and keeps the account id.
        var code = System.Text.RegularExpressions.Regex.Match(mail.Html, "<span class=\"code\"[^>]*>(\\d{6})</span>").Groups[1].Value;
        var linked = await _service.LinkAsync(ticket, Password, code, Device1, false, "en", default);
        Assert.Equal(ExternalAuthStatus.SignedIn, linked.Status);
        Assert.Equal(existing.AccountId, linked.Session!.AccountId);

        // From now on Google alone signs in to that same account.
        var later = await SignInAsync(Token());
        Assert.Equal(ExternalAuthStatus.SignedIn, later.Status);
        Assert.Equal(existing.AccountId, later.Session!.AccountId);
        Assert.False(later.IsNewAccount);
    }

    [Fact]
    public async Task Linking_WithoutAMailServer_FallsBackToPasswordOnly_LikeTheNormalLogin()
    {
        _sender.IsConfigured = false;
        var existing = await RegisterPasswordAccountAsync();
        var ticket = (await SignInAsync(Token())).LinkTicket;

        var linked = await _service.LinkAsync(ticket, Password, null, Device1, false, "en", default);

        Assert.Equal(ExternalAuthStatus.SignedIn, linked.Status);
        Assert.Equal(existing.AccountId, linked.Session!.AccountId);
    }

    [Fact]
    public async Task ALinkTicket_IsSingleUse_AndBoundToTheDeviceThatGotIt()
    {
        _sender.IsConfigured = false;
        await RegisterPasswordAccountAsync();
        var ticket = (await SignInAsync(Token())).LinkTicket;

        var otherDevice = await _service.LinkAsync(ticket, Password, null, Device2, false, "en", default);
        Assert.Equal(ExternalAuthStatus.Rejected, otherDevice.Status);   // and the stolen ticket is burned

        var ticket2 = (await SignInAsync(Token())).LinkTicket;
        Assert.Equal(ExternalAuthStatus.SignedIn, (await _service.LinkAsync(ticket2, Password, null, Device1, false, "en", default)).Status);
        Assert.Equal(ExternalAuthStatus.Rejected, (await _service.LinkAsync(ticket2, Password, null, Device1, false, "en", default)).Status);
    }

    [Fact]
    public async Task ALinkTicket_Expires_AndTooManyWrongPasswordsBurnIt()
    {
        _sender.IsConfigured = false;
        await RegisterPasswordAccountAsync();

        var expiring = (await SignInAsync(Token())).LinkTicket;
        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(ExternalAuthStatus.Rejected, (await _service.LinkAsync(expiring, Password, null, Device1, false, "en", default)).Status);

        var guessed = (await SignInAsync(Token())).LinkTicket;
        for (var i = 0; i < 8; i++)
            await _service.LinkAsync(guessed, "wrong-password-" + i, null, Device1, false, "en", default);
        Assert.Equal(ExternalAuthStatus.Rejected, (await _service.LinkAsync(guessed, Password, null, Device1, false, "en", default)).Status);
    }

    [Fact]
    public async Task AGarbageLinkTicket_IsRejected()
    {
        foreach (var ticket in new[] { null, "", "nope", new string('x', 500) })
            Assert.Equal(ExternalAuthStatus.Rejected, (await _service.LinkAsync(ticket, Password, null, Device1, false, "en", default)).Status);
    }

    [Fact]
    public async Task AProviderIdentity_CanOnlyBelongToOneAccount()
    {
        var first = await SignInAsync(Token());                           // creates account A with identity google-sub-1
        var other = await RegisterPasswordAccountAsync("other@example.test", Device2);

        var linkedAgain = await _accounts.LinkExternalIdentityAsync(other.AccountId, ExternalProviders.Google, "google-sub-1", "other@example.test", default);

        Assert.False(linkedAgain);
        Assert.Equal(first.Session!.AccountId, (await _accounts.FindByExternalIdentityAsync(ExternalProviders.Google, "google-sub-1", default))!.AccountId);
    }

    [Fact]
    public async Task TheSameSubject_AtTwoProviders_AreTwoDifferentIdentities()
    {
        var google = await SignInAsync(Token(subject: "same-id", email: "a@example.test"));
        var apple = await SignInAsync(Token(ExternalProviders.Apple, "same-id", "b@example.test", "true", nonce: null), ExternalProviders.Apple, nonce: null, device: Device2);

        Assert.NotEqual(google.Session!.AccountId, apple.Session!.AccountId);
    }

    // ---------------------------------------------------------------------------------------------
    // device binding, bans, deletion — the rules every account already has

    [Fact]
    public async Task AnAccountBoundToOnePhone_RefusesAnotherUntilTheOwnerMovesIt()
    {
        await SignInAsync(Token(), device: Device1);

        var token = Token();
        var blocked = await SignInAsync(token, device: Device2);
        Assert.Equal(ExternalAuthStatus.DeviceBound, blocked.Status);

        // The same token still works for the follow-up "move to this phone" because the refusal did not use it up.
        var moved = await SignInAsync(token, device: Device2, move: true);
        Assert.Equal(ExternalAuthStatus.SignedIn, moved.Status);

        // Moving again straight away hits the 7-day cooldown.
        var again = await SignInAsync(Token(), device: Device1, move: true);
        Assert.Equal(ExternalAuthStatus.Cooldown, again.Status);
    }

    [Fact]
    public async Task ABannedAccount_CannotSignInWithGoogle()
    {
        var first = await SignInAsync(Token());
        await _accounts.BanAsync(first.Session!.AccountId, "test", "tests", default);

        var outcome = await SignInAsync(Token());

        Assert.Equal(ExternalAuthStatus.Rejected, outcome.Status);
        Assert.Null(outcome.Session);
    }

    [Fact]
    public async Task DeletingTheAccount_FreesTheProviderIdentity()
    {
        var first = await SignInAsync(Token());
        await _accounts.DeleteAsync(first.Session!.AccountId, default);

        Assert.Null(await _accounts.FindByExternalIdentityAsync(ExternalProviders.Google, "google-sub-1", default));
        var again = await SignInAsync(Token());
        Assert.Equal(ExternalAuthStatus.SignedIn, again.Status);
        Assert.True(again.IsNewAccount);
        Assert.NotEqual(first.Session.AccountId, again.Session!.AccountId);
    }

    [Fact]
    public async Task EmailAndPassword_StillWork_ExactlyAsBefore()
    {
        var registered = await RegisterPasswordAccountAsync();

        var login = await _accounts.LoginAsync("driver@example.test", Password, Device1, false, default);
        Assert.Equal(registered.AccountId, login!.AccountId);
        Assert.Null(await _accounts.LoginAsync("driver@example.test", "wrong-password", Device1, false, default));

        await Assert.ThrowsAsync<AccountDeviceMismatchException>(() => _accounts.LoginAsync("driver@example.test", Password, Device2, false, default));
        var moved = await _accounts.LoginAsync("driver@example.test", Password, Device2, true, default);
        Assert.Equal(registered.AccountId, moved!.AccountId);
    }

    [Fact]
    public async Task SigningInWithGoogle_DoesNotTouchPasswordCredentials_OfALinkedAccount()
    {
        _sender.IsConfigured = false;
        var existing = await RegisterPasswordAccountAsync();
        var ticket = (await SignInAsync(Token())).LinkTicket;
        await _service.LinkAsync(ticket, Password, null, Device1, false, "en", default);

        var password = await _accounts.LoginAsync("driver@example.test", Password, Device1, false, default);
        Assert.Equal(existing.AccountId, password!.AccountId);
    }

    // ---------------------------------------------------------------------------------------------

    private sealed class FakeKeys : IExternalSigningKeys
    {
        private readonly SecurityKey _key;
        public FakeKeys(SecurityKey key) => _key = key;
        public bool Hidden { get; set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SecurityKey>> GetKeysAsync(string provider, bool forceRefresh, CancellationToken ct)
        {
            Calls++;
            IReadOnlyList<SecurityKey> keys = Hidden
                ? new SecurityKey[] { new RsaSecurityKey(RSA.Create(2048).ExportParameters(false)) { KeyId = "unrelated" } }
                : new[] { _key };
            return Task.FromResult(keys);
        }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CarScanner.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
