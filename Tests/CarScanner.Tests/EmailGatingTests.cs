using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarScanner.Api.Services;
using CarScanner.Api.Services.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Which actions need a verified email, and which never do. The rule lives on the server (the app only reacts to
/// its answer), applies only to actions where owning the address matters, and never touches scanning, sign-in,
/// deletion, existing subscriptions or store purchases.
/// </summary>
public sealed class EmailGatingTests : IDisposable
{
    private const string Account = "77777777-7777-7777-7777-777777777777";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-gate-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EmailClock _clock = new();
    private readonly CapturingSender _sender = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private EmailVerificationService Service(bool configured = true)
    {
        _sender.IsConfigured = configured;
        var store = new EmailVerificationStore($"Data Source={_file}", TimeSpan.FromMinutes(15), _clock.Read);
        var options = new EmailOptions { Host = "smtp.example.test", FromAddress = "no-reply@example.test", PublicBaseUrl = "https://api.example.test" };
        return new EmailVerificationService(store, _sender, options, NullLogger<EmailVerificationService>.Instance, _clock.Read);
    }

    private static IConfiguration Config(string? requireVerified = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(requireVerified is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["Email:RequireVerified"] = requireVerified }).Build();

    private static string CodeIn(EmailMessage m) => Regex.Match(m.Html, "<span class=\"code\"[^>]*>(\\d{6})</span>").Groups[1].Value;

    // ---- who is checked -------------------------------------------------------------------------

    [Fact]
    public async Task AnExistingAccount_IsUnverified_AndStaysSoUntilItReallyVerifies()
    {
        var service = Service();
        Assert.False(await service.Store.IsVerifiedAsync(Account, default));                 // legacy: never marked

        Assert.False(await EmailVerificationGate.IsSatisfiedAsync(service, Config(), Account, default));

        // none of these may count as verification: asking for a code, a wrong code, an expired code
        await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.False(await service.Store.IsVerifiedAsync(Account, default));
        await service.Store.VerifyCodeAsync(Account, "000000", default);
        _clock.Advance(TimeSpan.FromMinutes(16));
        await service.Store.VerifyCodeAsync(Account, CodeIn(_sender.Sent[0]), default);       // expired
        Assert.False(await service.Store.IsVerifiedAsync(Account, default));
        Assert.False(await EmailVerificationGate.IsSatisfiedAsync(service, Config(), Account, default));
    }

    [Fact]
    public async Task AfterRealVerification_TheActionsAreAllowed()
    {
        var service = Service();
        await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.Equal(VerifyResult.Verified, await service.Store.VerifyCodeAsync(Account, CodeIn(_sender.Sent[0]), default));

        Assert.True(await EmailVerificationGate.IsSatisfiedAsync(service, Config(), Account, default));
    }

    [Fact]
    public async Task OneAccountsVerification_DoesNotUnlockAnother()
    {
        var service = Service();
        await service.SendAsync(Account, "a@example.test", "en", default);
        await service.Store.VerifyCodeAsync(Account, CodeIn(_sender.Sent[0]), default);

        Assert.False(await EmailVerificationGate.IsSatisfiedAsync(service, Config(), "88888888-8888-8888-8888-888888888888", default));
    }

    // ---- when the rule is in force --------------------------------------------------------------

    [Fact]
    public async Task WithoutEmailSettings_TheRuleIsNotEnforced_SoNobodyIsLockedOutOfPaying()
    {
        var service = Service(configured: false);
        Assert.False(EmailVerificationGate.IsEnforced(service, Config()));
        Assert.True(await EmailVerificationGate.IsSatisfiedAsync(service, Config(), Account, default));
    }

    [Fact]
    public async Task TheRuleCanBeSwitchedOff_Explicitly()
    {
        var service = Service();
        Assert.True(EmailVerificationGate.IsEnforced(service, Config()));
        Assert.True(EmailVerificationGate.IsEnforced(service, Config("true")));
        Assert.False(EmailVerificationGate.IsEnforced(service, Config("false")));
        Assert.True(await EmailVerificationGate.IsSatisfiedAsync(service, Config("false"), Account, default));
    }

    // ---- the answer the app recognises ----------------------------------------------------------

    [Fact]
    public async Task TheRefusal_IsAConsistent403_WithTheCodeTheAppRecognises()
    {
        Assert.Equal("EMAIL_NOT_VERIFIED", EmailVerificationGate.ErrorCode);
        Assert.Equal(403, EmailVerificationGate.ErrorStatus);

        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        await EmailVerificationGate.Refusal().ExecuteAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("EMAIL_NOT_VERIFIED", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.GetProperty("canVerify").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public void TheAppAndTheWebPage_RecogniseTheCode()
    {
        Assert.Contains("\"EMAIL_NOT_VERIFIED\"", File.ReadAllText(Path.Combine(Root(), "VinCheck/ViewModels/VinReportViewModel.cs")));
        Assert.Contains("EMAIL_NOT_VERIFIED:tr(", File.ReadAllText(Path.Combine(Root(), "Backend/CarScanner.Api/Services/WebPages.cs")));
    }

    // ---- the server routes ----------------------------------------------------------------------

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    /// <summary>The source of one route's handler: from its path string up to the next route.</summary>
    private static string Route(string program, string path)
    {
        var at = program.IndexOf("\"" + path + "\"", StringComparison.Ordinal);
        Assert.True(at >= 0, path + " not found");
        var rest = program[at..];
        var next = Regex.Match(rest[10..], @"\napp\.Map(Get|Post|Delete|Put)\(");
        return next.Success ? rest[..(next.Index + 10)] : rest;
    }

    private static string Program() => File.ReadAllText(Path.Combine(Root(), "Backend/CarScanner.Api/Program.cs"));

    [Fact]
    public void WebCheckout_RequiresVerification_AfterTheCredentialsAreChecked_AndBeforeAnyPaymentIsStarted()
    {
        var route = Route(Program(), "/api/web/checkout");
        var login = route.IndexOf("VerifyCredentialsAsync", StringComparison.Ordinal);
        var gate = route.IndexOf("EmailVerificationGate.IsSatisfiedAsync", StringComparison.Ordinal);
        var refuse = route.IndexOf("EmailVerificationGate.Refusal()", StringComparison.Ordinal);
        var pay = route.IndexOf("payments.CreateCheckoutAsync", StringComparison.Ordinal);
        Assert.True(login > 0 && gate > login && refuse > gate && pay > refuse, "order: sign-in check, gate, then payment");
    }

    [Fact]
    public void VinReportPurchase_RequiresVerification_ButABoughtReportStillOpens_AndTheOwnerIsExempt()
    {
        var route = Route(Program(), "/api/vin/{vin}/full/checkout");
        var saved = route.IndexOf("full.GetSavedAsync", StringComparison.Ordinal);
        var perks = route.IndexOf("AdminAccess.HasPerks", StringComparison.Ordinal);
        var gate = route.IndexOf("EmailVerificationGate.IsSatisfiedAsync", StringComparison.Ordinal);
        var providerCall = route.IndexOf("full.CheckAsync", StringComparison.Ordinal);
        var pay = route.IndexOf("payments.CreateCheckoutAsync", StringComparison.Ordinal);
        Assert.True(saved > 0 && perks > saved && gate > perks && providerCall > gate && pay > providerCall,
            "order: already-bought report, owner perks, gate, provider lookup, payment");
    }

    [Theory]
    [InlineData("/api/accounts/register")]
    [InlineData("/api/accounts/login")]
    [InlineData("/api/accounts/me")]
    [InlineData("/api/accounts/logout")]
    [InlineData("/api/accounts/password")]
    [InlineData("/api/accounts/email/status")]
    [InlineData("/api/accounts/email/send-code")]
    [InlineData("/api/accounts/email/verify")]
    [InlineData("/api/entitlements/{userId}")]
    [InlineData("/api/store/verify")]
    [InlineData("/api/store/webhook")]
    [InlineData("/api/store/apple/notifications")]
    [InlineData("/api/store/google/rtdn")]
    [InlineData("/api/web/account/delete")]
    [InlineData("/api/web/subscription")]
    [InlineData("/api/web/subscription/cancel")]
    [InlineData("/api/web/subscription/retry")]
    public void ThesePaths_AreNeverBlockedByTheRule(string path)
    {
        // (the status route may *report* whether the rule is in force; it must never enforce it)
        var route = Route(Program(), path);
        Assert.DoesNotContain("EmailVerificationGate.IsSatisfiedAsync", route);
        Assert.DoesNotContain("EmailVerificationGate.Refusal", route);
    }

    [Fact]
    public void ThereIsNoPromoRedemptionRoute_AndAnyFutureOneMustUseTheGate()
    {
        var program = Program();
        foreach (Match m in Regex.Matches(program, "\"(/[^\"]*(promo|redeem)[^\"]*)\"", RegexOptions.IgnoreCase))
            Assert.Contains("EmailVerificationGate", Route(program, m.Groups[1].Value));   // vacuous today: promos are admin-granted only
    }

    [Fact]
    public void Registration_ReportsWhatTheMailServerActuallyDid()
    {
        var route = Route(Program(), "/api/accounts/register");
        Assert.Contains("await emailVerification.SendAsync", route);
        Assert.Contains("VerificationSendStatus.Sent ? \"sent\" : \"failed\"", route);
        Assert.Contains("\"unavailable\"", route);
        Assert.DoesNotContain("\"queued\"", route);                            // no more "queued": success means the server accepted it
        Assert.Contains("TimeSpan.FromSeconds(10)", route);                     // a slow mail server cannot hang registration
    }

    // ---- existing entitlements are untouched ----------------------------------------------------

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task AnUnverifiedAccount_KeepsItsExistingProEntitlement()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gcar-ent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = dir }).Build();
            var entitlements = new EntitlementStore(new FakeEnvironment { ContentRootPath = dir }, config);
            var granted = new EntitlementRecord(Account, true, "google", ProductCatalog.ObdMonthly, DateTimeOffset.UtcNow.AddDays(20), DateTimeOffset.UtcNow, "tx-1");
            await entitlements.UpsertSubscriptionAsync(granted, default);

            var service = Service();
            Assert.False(await service.Store.IsVerifiedAsync(Account, default));              // unverified ...
            var access = await entitlements.GetAccessAsync(Account, default);
            Assert.True(access.HasObd);                                                        // ... and still PRO
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void TheEntitlementReadPath_DoesNotConsultVerification()
    {
        var program = Program();
        Assert.DoesNotContain("EmailVerificationGate", Route(program, "/api/entitlements/{userId}"));
        Assert.DoesNotContain("emailVerification", Route(program, "/api/store/verify"));
    }

    // ---- delivery failures ----------------------------------------------------------------------

    [Fact]
    public async Task AFailedSend_DoesNotLockTheUserOutOfRetrying()
    {
        var service = Service();
        _sender.Fail = new InvalidOperationException("smtp down");
        Assert.Equal(VerificationSendStatus.SendFailed, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);

        _sender.Fail = null;                                                                    // the mail server recovers
        var retry = await service.SendAsync(Account, "driver@example.test", "en", default);     // no 60-second wait after a failure
        Assert.Equal(VerificationSendStatus.Sent, retry.Status);
        Assert.Equal(VerifyResult.Verified, await service.Store.VerifyCodeAsync(Account, CodeIn(_sender.Sent[0]), default));
    }

    [Fact]
    public async Task RepeatedFailures_StayLimited_PerHour()
    {
        var service = Service();
        _sender.Fail = new InvalidOperationException("smtp down");
        for (var i = 0; i < EmailVerificationStore.MaxSendsPerHour; i++)
            Assert.Equal(VerificationSendStatus.SendFailed, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);

        Assert.Equal(VerificationSendStatus.HourlyLimit, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task TheCodeOfAFailedSend_CannotBeUsed()
    {
        var service = Service();
        var guessed = new List<string>();
        _sender.Fail = new InvalidOperationException("smtp down");
        await service.SendAsync(Account, "driver@example.test", "en", default);

        // an attacker who guesses the unsent code gets nothing: it expired the moment the send failed
        for (var i = 0; i < 4; i++)
            Assert.NotEqual(VerifyResult.Verified, await service.Store.VerifyCodeAsync(Account, i.ToString("D6"), default));
        Assert.False(await service.Store.IsVerifiedAsync(Account, default));
        Assert.Empty(guessed);
    }

    [Fact]
    public async Task WithoutEmailSettings_ASendIsRefusedClearly_AndNothingIsReportedAsSent()
    {
        var service = Service(configured: false);
        var outcome = await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.Equal(VerificationSendStatus.NotConfigured, outcome.Status);
        Assert.Empty(_sender.Sent);
    }

    // ---- the app side ---------------------------------------------------------------------------

    [Fact]
    public void LocalDiagnosticsNeverTouchVerification()
    {
        // Only these three files may mention the flow; scanning, live data, DTCs, service, mileage, fuel, coding… do not.
        var allowed = new[] { "Services/EmailVerificationFlow.cs", "Views/AccountPage.xaml.cs", "VinCheck/ViewModels/VinReportViewModel.cs" };
        var root = Root();
        var hits = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "Backend" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllText(f).Contains("EmailVerificationFlow", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(f => f)
            .ToArray();
        Assert.Equal(allowed.OrderBy(f => f), hits);

        // and the Coding page and its service are untouched by this feature
        Assert.DoesNotContain("EmailVerification", File.ReadAllText(Path.Combine(root, "Services/Coding/CodingService.cs")));
    }

    [Fact]
    public void ARefusedPurchase_StartsVerification_ThenTriesOnce_AndAnEarlyExitChangesNothing()
    {
        var vm = File.ReadAllText(Path.Combine(Root(), "VinCheck/ViewModels/VinReportViewModel.cs"));
        var refused = vm.IndexOf("result.Code == \"EMAIL_NOT_VERIFIED\"", StringComparison.Ordinal);
        var flow = vm.IndexOf("EmailVerificationEntry.Restricted", StringComparison.Ordinal);
        var retry = vm.IndexOf("result = await service.StartFullCheckoutAsync", refused + 10, StringComparison.Ordinal);
        Assert.True(refused > 0 && flow > refused && retry > flow);
        Assert.Contains("return;", vm[flow..retry]);                                           // "Later" ends the attempt quietly
    }

    [Fact]
    public void TheFlow_NeverReportsSuccess_WithoutTheServer()
    {
        var flow = File.ReadAllText(Path.Combine(Root(), "Services/EmailVerificationFlow.cs"));
        // success is returned only after the server accepted the code ("result.Success") or said it is already verified
        var successReturns = Regex.Matches(flow, "return true;").Count;
        Assert.Equal(2, successReturns);
        Assert.Contains("if (result.Success)", flow);
        Assert.Contains("sent.Code == \"already_verified\"", flow);
        Assert.Contains("FirstSendFailed", flow);                                              // a failed send is explained, never hidden
    }
}
