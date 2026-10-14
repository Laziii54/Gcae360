using System.Text.RegularExpressions;
using CarScanner.Api.Services;

namespace CarScanner.Tests;

/// <summary>
/// Admin perks (Deep Scan, the admin-only panel, unlimited AI quota) and a normal user's AI access are two
/// different things and must stay that way:
///   • Admin perks (<see cref="AdminAccess.HasPerks"/>) require BOTH a listed e-mail (GCS_ADMIN_EMAILS) AND that
///     exact account having completed a 2-step TOTP sign-in on /admin. Listing an e-mail alone proves nothing.
///   • A normal user's AI access is decided by <c>access.HasObd</c>, which comes from their own entitlement
///     (<see cref="EntitlementStore.GetAccessAsync"/>) — a real purchase, trial, or an admin "grant" of a product
///     to their account. It is never unlocked by GCS_ADMIN_EMAILS or by signing in to /admin.
///   • <see cref="AdminAccess.Apply"/> only ever ADDS access for a verified admin; for everyone else it returns
///     the entitlement result unchanged, so a non-admin's AI access can never be reduced or granted by this path.
/// These tests call the real, internal <c>AdminAccess</c>/<c>AdminService</c> members directly (this test project
/// has InternalsVisibleTo into CarScanner.Api), then re-check the server routes at the source level — the same
/// technique AiFailureTests/EmailGatingTests/DeepScanTests already use for Program.cs's top-level route bodies.
/// </summary>
public sealed class AdminAiSeparationTests
{
    private const string EnvVar = "GCS_ADMIN_EMAILS";

    [Fact]
    public void BeingListedInAdminEmails_IsNotEnoughOnItsOwn_WithoutTheTotpSignIn()
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, "fake-admin-1@example.test, fake-admin-2@example.test");
            Assert.True(AdminAccess.IsAdmin("fake-admin-1@example.test"));

            // A brand-new account id that never went through AdminService.MarkTotpLoginAsync.
            var neverSignedIn = Guid.NewGuid().ToString();
            Assert.False(AdminService.IsTotpVerified(neverSignedIn));
            Assert.False(AdminAccess.HasPerks(neverSignedIn, "fake-admin-1@example.test"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }

    [Fact]
    public void AnUnverifiedAdminEmail_NeverGetsTheFullAccessUpgrade()
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, "fake-admin-3@example.test");
            var neverSignedIn = Guid.NewGuid().ToString();

            var noEntitlement = new AccessInfo(false, false, "none", null, null);
            var applied = AdminAccess.Apply(neverSignedIn, "fake-admin-3@example.test", noEntitlement);

            // Not a verified admin: the entitlement result passes through exactly as-is.
            Assert.Equal(noEntitlement, applied);
            Assert.False(applied.HasObd);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }

    [Fact]
    public void ApplyForANonAdmin_NeverTouchesWhatTheirOwnEntitlementAlreadyGrants()
    {
        // A regular paying user (real OBD PRO entitlement), not listed as admin at all.
        var paidUser = new AccessInfo(true, false, "obd", DateTimeOffset.UtcNow.AddDays(10), null);
        var applied = AdminAccess.Apply(Guid.NewGuid().ToString(), "paying-user@example.test", paidUser);
        Assert.Equal(paidUser, applied);
        Assert.True(applied.HasObd);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Program() =>
        File.ReadAllText(Path.Combine(Root(), "Backend", "CarScanner.Api", "Program.cs")).Replace("\r\n", "\n");

    /// <summary>The body of GenerateGeminiAsync, the shared handler behind /api/ai/diagnose and /api/ai/chat.</summary>
    private static string AiHandler(string program)
    {
        var start = program.IndexOf("static async Task<IResult> GenerateGeminiAsync", StringComparison.Ordinal);
        Assert.True(start > 0, "GenerateGeminiAsync not found");
        var body = program[start..];
        return body[..body.IndexOf("// VIN REPORT", StringComparison.Ordinal)];
    }

    [Fact]
    public void TheAiEndpoint_GatesOnTheUsersOwnEntitlement_NotOnAdminStatus()
    {
        var ai = AiHandler(Program());

        // The base access comes from the account's own entitlement (EntitlementStore), with AdminAccess.Apply
        // only ever layered on top of it — never the reverse.
        var access = ai.IndexOf("AdminAccess.Apply(account.AccountId, account.Email, await entitlements.GetAccessAsync(", StringComparison.Ordinal);
        Assert.True(access > 0, "expected the entitlement lookup to feed AdminAccess.Apply, not the other way round");

        // The 403 that blocks AI for someone with no access checks access.HasObd — not AdminAccess.HasPerks.
        var forbidden = ai.IndexOf("if (!access.HasObd)", StringComparison.Ordinal);
        Assert.True(forbidden > access, "expected the HasObd gate right after the entitlement/admin merge");

        // AdminAccess.HasPerks shows up exactly once after that point, and only to waive the daily AI rate
        // limit for a verified admin — never to decide whether AI is allowed at all.
        var perksUse = ai.IndexOf("AdminAccess.HasPerks", StringComparison.Ordinal);
        Assert.True(perksUse > forbidden, "AdminAccess.HasPerks must not be consulted before the HasObd gate");
        var dailyLimit = ai.IndexOf("TryConsumeDailyAsync", StringComparison.Ordinal);
        Assert.True(dailyLimit > perksUse, "expected HasPerks to only gate the daily-limit skip, after the real access check");
    }

    /// <summary>The source of one route's handler: from its path string up to the next route (mirrors EmailGatingTests.Route).</summary>
    private static string Route(string program, string path)
    {
        var at = program.IndexOf("\"" + path + "\"", StringComparison.Ordinal);
        Assert.True(at >= 0, path + " not found");
        var rest = program[at..];
        var next = Regex.Match(rest[10..], @"\napp\.Map(Get|Post|Delete|Put)\(");
        return next.Success ? rest[..(next.Index + 10)] : rest;
    }

    [Fact]
    public void TheMeEndpoint_IsAdminFlag_ComesOnlyFromHasPerks_NeverFromAiOrEntitlementState()
    {
        var get = Route(Program(), "/api/accounts/me");

        Assert.Contains("isAdmin = AdminAccess.HasPerks(account.AccountId, account.Email)", get);
        Assert.DoesNotContain("HasObd", get);
        Assert.DoesNotContain("GetAccessAsync", get);
    }

    [Fact]
    public void TheAdminGrantAction_GivesARealEntitlement_WithoutTouchingAdminStatusOrTotp()
    {
        // /admin → Users → "grant": the correct way to give a test/support account real AI (OBD PRO) access
        // without adding it to GCS_ADMIN_EMAILS or making it sign in to /admin.
        var program = Program();
        var at = program.IndexOf("case \"grant\":", StringComparison.Ordinal);
        Assert.True(at > 0, "expected the admin Users 'grant' action");
        // "grant" is the last case in this switch; bound the slice by length rather than a following "case \""
        // (there isn't one), comfortably covering the action without reaching into unrelated code below it.
        var block = program.Substring(at, Math.Min(1200, program.Length - at));

        Assert.Contains("UpsertSubscriptionAsync", block);
        Assert.Contains("EntitlementRecord(", block);
        Assert.DoesNotContain("AdminAccess", block);
        Assert.DoesNotContain("TotpLogin", block);
    }

    [Fact]
    public void DeepScanAndAdminPanel_StayGatedOnHasPerks_NotOnAnyAiOrEntitlementCheck()
    {
        var vm = File.ReadAllText(Path.Combine(Root(), "ViewModels", "CodingViewModel.cs"));
        var line = vm.Split('\n').First(l => l.Contains("IsAdmin = await"));
        Assert.Contains("_account.IsAdminAsync()", line);

        var service = File.ReadAllText(Path.Combine(Root(), "Services", "AccountService.cs"));
        var method = Regex.Match(service, @"public async Task<bool> IsAdminAsync\([^)]*\)\s*\{.*?\n    \}", RegexOptions.Singleline);
        Assert.True(method.Success, "IsAdminAsync not found");
        Assert.Contains("/api/accounts/me", method.Value);
        Assert.DoesNotContain("/api/ai", method.Value);
    }
}
