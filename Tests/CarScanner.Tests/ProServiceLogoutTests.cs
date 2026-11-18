namespace CarScanner.Tests;

/// <summary>
/// PRO is derived from a signed entitlement token, never from a local flag.
///
/// History: the build before this one trusted <c>ProEntitlement.IsPro</c> and
/// <c>VerifiedByServer</c> straight out of an unencrypted SQLite file, so on a
/// rooted device (or with an edited preferences file) setting those values
/// unlocked every locally gated feature for good. The tests below pin the new
/// contract: only <c>EntitlementTokenVerifier</c> output can grant access, and a
/// sign-out must drop the account token without touching an App Store one.
///
/// ProService itself needs MAUI Preferences/SecureStorage, so — like the rest of
/// this codebase's wiring tests — these read the actual source. The cryptography
/// is covered for real in EntitlementTokenTests.
/// </summary>
public sealed class ProServiceLogoutTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string ProServiceSource() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Services", "ProService.cs")).Replace("\r\n", "\n");

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        return source[start..];
    }

    // ============================================================
    // TOKEN-ONLY TRUST
    // ============================================================

    [Fact]
    public void AccessComesOnlyFromVerifiedTokenClaims()
    {
        var source = ProServiceSource();

        Assert.Contains("EntitlementTokenVerifier.Verify", source);
        Assert.Contains("public bool IsPro => _accountClaims?.HasObd == true || _appleClaims?.HasObd == true;", source);
        Assert.Contains("public bool HasVin => _accountClaims?.HasVin == true || _appleClaims?.HasVin == true;", source);
        Assert.Contains("public bool HasObd => IsPro && ServerVerified;", source);
    }

    [Fact]
    public void TheLocalDatabaseRow_IsNoLongerReadForAuthorization()
    {
        var source = ProServiceSource();

        // The old implementation loaded local.IsPro / local.VerifiedByServer into
        // the access flags. Nothing may read an entitlement out of SQLite again.
        Assert.DoesNotContain("local.IsPro", source);
        Assert.DoesNotContain("local.VerifiedByServer", source);
        Assert.DoesNotContain("AppleVerifiedByServer", source);
        Assert.DoesNotContain("GetProAsync", source);

        // ... and the old preferences flag is gone from the authorization path.
        Assert.DoesNotContain("pro.hasVin", source[..source.IndexOf("MigrateLegacyTrustAsync", StringComparison.Ordinal)]);
    }

    [Fact]
    public void ACorruptOrExpiredToken_IsDroppedRatherThanTrusted()
    {
        var source = ProServiceSource();
        var read = Method(source, "private static async Task<(EntitlementTokenClaims? Claims, bool Lost)> ReadVerifiedAsync");
        read = read[..read.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("EntitlementTokenVerifier.Verify(stored)", read);
        Assert.Contains("if (claims is null)", read);
        Assert.Contains("SecureStorage.Default.Remove(key)", read);
        Assert.Contains("return (null, true)", read);
    }

    // ============================================================
    // SIGN-OUT / ACCOUNT SWITCHING
    // ============================================================

    [Fact]
    public void ProService_SubscribesToAccountChanged_InItsConstructor()
    {
        var source = ProServiceSource();
        var ctor = source[source.IndexOf("public ProService(", StringComparison.Ordinal)..];
        ctor = ctor[..ctor.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_account.AccountChanged += OnAccountChanged;", ctor);
    }

    [Fact]
    public void SigningOut_DropsOnlyTheAccountToken()
    {
        var source = ProServiceSource();
        var handler = Method(source, "private async void OnAccountChanged");
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("if (!_account.IsSignedIn)", handler);
        Assert.Contains("ClearAccountEntitlementAsync()", handler);

        var clear = Method(source, "private async Task ClearAccountEntitlementAsync()");
        clear = clear[..clear.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("_accountClaims = null", clear);
        Assert.Contains("RemoveTokenAsync(AccountTokenKey)", clear);

        // An App Store entitlement belongs to the Apple Account: it survives.
        Assert.DoesNotContain("AppleTokenKey", clear);
        Assert.DoesNotContain("_appleClaims = null", clear);
    }

    [Fact]
    public void SigningIn_ForcesATokenRefresh()
    {
        var source = ProServiceSource();
        var handler = Method(source, "private async void OnAccountChanged");
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];

        // A different account must never keep the previous account's access:
        // signing in invalidates the throttle and refetches immediately.
        Assert.Contains("_lastRefreshAttempt = DateTimeOffset.MinValue;", handler);
        Assert.Contains("LoadAsync(forceServerRefresh: true)", handler);
    }

    [Fact]
    public void DeactivateAsync_ClearsBothTokensAndTheAppleSession()
    {
        var source = ProServiceSource();
        var method = Method(source, "public async Task DeactivateAsync()");
        method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("_accountClaims = null", method);
        Assert.Contains("_appleClaims = null", method);
        Assert.Contains("RemoveTokenAsync(AccountTokenKey)", method);
        Assert.Contains("RemoveTokenAsync(AppleTokenKey)", method);
        Assert.Contains("_appleStore.ForgetAsync()", method);
        Assert.Contains("Publish();", method);
    }

    // ============================================================
    // MIGRATION
    // ============================================================

    [Fact]
    public void LegacyLocallyTrustedValues_AreClearedOnce()
    {
        var pro = ProServiceSource();
        var migration = Method(pro, "private async Task MigrateLegacyTrustAsync");
        migration = migration[..migration.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("Preferences.Default.Remove(\"pro.hasVin\")", migration);
        Assert.Contains("ClearLegacyEntitlementTrustAsync", migration);
        Assert.Contains("_migrationStarted", pro);

        var database = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Database", "AppDatabase.cs")).Replace("\r\n", "\n");

        Assert.Contains("public async Task ClearLegacyEntitlementTrustAsync()", database);
        Assert.Contains("row.IsPro = false;", database);
        Assert.Contains("row.VerifiedByServer = false;", database);
        Assert.Contains("row.AppleVerifiedByServer = false;", database);
        Assert.Contains("LegacyTrustClearedSetting", database);
    }

    // ============================================================
    // REFRESH
    // ============================================================

    [Fact]
    public void Refresh_IsSingleFlightAndThrottled()
    {
        var source = ProServiceSource();

        Assert.Contains("await _refreshGate.WaitAsync();", source);
        Assert.Contains("RefreshInterval", source);
        Assert.Contains("_lastRefreshAttempt", source);

        // Tokens are refreshed proactively before they expire.
        Assert.Contains("RefreshBeforeExpiry", source);
    }

    [Fact]
    public void AnonymousPurchaseToken_IsAppliedOnlyWhenItVerifies()
    {
        var source = ProServiceSource();
        var apply = Method(source, "public async Task ApplyAppleEntitlementAsync");
        apply = apply[..apply.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("EntitlementTokenVerifier.Verify(entitlement.EntitlementToken)", apply);
        Assert.Contains("if (claims is null)", apply);
        Assert.Contains("return;", apply);

        // The response's own hasObd/hasVin flags are never turned into access.
        Assert.DoesNotContain("entitlement.HasObd", apply);
        Assert.DoesNotContain("entitlement.HasVin", apply);
    }

    [Fact]
    public void AnAccountTokenForAnotherSubject_IsNeverStored()
    {
        var source = ProServiceSource();
        var refresh = Method(source, "private async Task RefreshAccountTokenAsync");
        refresh = refresh[..refresh.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("claims.Subject", refresh);
        Assert.Contains("userId", refresh);
        Assert.Contains("return;", refresh);
    }
}
