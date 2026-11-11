namespace CarScanner.Tests;

/// <summary>
/// What a sign-out must and must not clear.
///
/// History: signing out used to leave the device's cached account PRO active,
/// because the refresh attempted right after logout has no access token any
/// more, fails silently, and <c>LoadAsync</c>'s failure path deliberately keeps
/// the cache (to survive a real outage). ProService therefore subscribes to
/// AccountChanged and clears the *account* source the moment IsSignedIn becomes
/// false.
///
/// Since App Review 5.1.1(v), the same event must **not** clear an accountless
/// App Store subscription: that entitlement belongs to the Apple Account, was
/// verified against an Apple transaction, and has to keep working after the
/// user signs out of (or never signs into) an OBD360 account.
///
/// Not unit-testable end-to-end here (ProService depends on MAUI Essentials
/// Preferences/SecureStorage, unavailable outside a real MAUI host), so — like
/// the rest of this codebase's wiring tests — this checks the actual source.
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

    [Fact]
    public void ProService_SubscribesToAccountChanged_InItsConstructor()
    {
        var source = ProServiceSource();
        var ctor = source[source.IndexOf("public ProService(", StringComparison.Ordinal)..];
        ctor = ctor[..ctor.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_account.AccountChanged += OnAccountChanged;", ctor);
    }

    [Fact]
    public void WhenTheAccountChangesAndNobodyIsSignedIn_TheAccountSourceIsCleared()
    {
        var source = ProServiceSource();
        var handler = source[source.IndexOf("private async void OnAccountChanged", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("if (_account.IsSignedIn)", handler);
        var guard = handler.IndexOf("if (_account.IsSignedIn)", StringComparison.Ordinal);
        var ret = handler.IndexOf("return;", guard, StringComparison.Ordinal);
        var clear = handler.IndexOf("ClearAccountEntitlementAsync()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && ret > guard && clear > ret,
            "signed-in must return before anything is cleared — PRO must never be cleared while still signed in");
    }

    [Fact]
    public void SigningOut_KeepsAVerifiedAppleSubscription()
    {
        var source = ProServiceSource();
        var clear = Method(source, "private async Task ClearAccountEntitlementAsync()");
        clear = clear[..clear.IndexOf("\n    }", StringComparison.Ordinal)];

        // The account fields go...
        Assert.Contains("_accountObd = false", clear);
        Assert.Contains("SaveAccountVin(false, null)", clear);

        // ...the Apple fields and the Apple session must not.
        Assert.DoesNotContain("_appleObd = false", clear);
        Assert.DoesNotContain("_appleVin = false", clear);
        Assert.DoesNotContain("_appleStore.ForgetAsync()", clear);
    }

    [Fact]
    public void DeactivateAsync_ClearsBothSources()
    {
        var source = ProServiceSource();
        var method = Method(source, "public async Task DeactivateAsync()");
        method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("_accountObd = false", method);
        Assert.Contains("SaveAccountVin(false, null)", method);
        Assert.Contains("_appleObd = false", method);
        Assert.Contains("_appleVin = false", method);
        Assert.Contains("_appleStore.ForgetAsync()", method);
        Assert.Contains("Publish();", method);
    }

    [Fact]
    public void NothingIsGrantedFromALocalFlagAlone()
    {
        var source = ProServiceSource();

        // Both sources require "verified by the server" AND an unexpired date.
        Assert.Contains("local.VerifiedByServer", source);
        Assert.Contains("AppleVerifiedByServer", source);
        Assert.Contains("public bool HasObd => IsPro && ServerVerified;", source);
        Assert.Contains("public bool ServerVerified => _accountVerified || _appleVerified;", source);
    }
}
