namespace CarScanner.Tests;

/// <summary>
/// PRO status is per-account, never implied by "no session". Before this fix, signing out (or deleting the
/// account) left the device's cached PRO entitlement active: AccountService.LogoutAsync clears the stored
/// session but never touched ProService's cache, and the "refresh from server" call the UI made right after
/// logout had no access token any more, failed silently, and LoadAsync's failure path deliberately preserves the
/// existing cache (so a real network outage never manufactures/loses PRO) — which also preserved it across a
/// sign-out. Fixed by having ProService subscribe to AccountChanged and deactivate itself the moment IsSignedIn
/// becomes false, which AccountService guarantees is reliable (the stored session is cleared before the event
/// fires). Not unit-testable end-to-end here (ProService depends on MAUI Essentials Preferences/SecureStorage,
/// unavailable outside a real MAUI host) — so, like the rest of this codebase's Program.cs/mobile wiring tests,
/// this checks the actual source instead.
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
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Services", "ProService.cs"));

    [Fact]
    public void ProService_SubscribesToAccountChanged_InItsConstructor()
    {
        var source = ProServiceSource();
        var ctor = source[source.IndexOf("public ProService(", StringComparison.Ordinal)..];
        ctor = ctor[..ctor.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_account.AccountChanged += OnAccountChanged;", ctor);
    }

    [Fact]
    public void WhenTheAccountChangesAndNobodyIsSignedIn_ProIsDeactivated()
    {
        var source = ProServiceSource();
        var handler = source[source.IndexOf("private async void OnAccountChanged", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("if (_account.IsSignedIn)", handler);
        var guard = handler.IndexOf("if (_account.IsSignedIn)", StringComparison.Ordinal);
        var ret = handler.IndexOf("return;", guard, StringComparison.Ordinal);
        var deactivate = handler.IndexOf("DeactivateAsync()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && ret > guard && deactivate > ret,
            "signed-in must return before DeactivateAsync runs — PRO must never be cleared while still signed in");
    }

    [Fact]
    public void DeactivateAsync_ClearsBothObdAndVin_NotJustOne()
    {
        var source = ProServiceSource();
        var method = source[source.IndexOf("public async Task DeactivateAsync()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("IsPro = false", method);
        Assert.Contains("SaveLocalVin(false, null)", method);
        Assert.Contains("ProChanged?.Invoke(this, false)", method);
    }
}
