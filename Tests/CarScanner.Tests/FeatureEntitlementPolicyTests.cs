namespace CarScanner.Tests;

/// <summary>
/// The product decisions that must survive the security upgrade:
///
///   • Original Mileage discovery is FREE — it must not be added to the route
///     guard, and no PRO check may creep into it.
///   • Service procedures are FREE.
///   • Oil Life Reset (100% and 50%) is FREE, under the existing vehicle
///     compatibility rules only.
///
/// These are source tests because the pages/ViewModels depend on MAUI, but they
/// pin exactly the lines someone would edit while "hardening" the app.
/// </summary>
public sealed class FeatureEntitlementPolicyTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing: {from}");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"missing: {to}");
        return source[start..end];
    }

    /// <summary>The route-guard block only: from its product-decision comment to the next method.</summary>
    private static string Guard()
    {
        var shell = Read("AppShell.xaml.cs");
        var start = shell.IndexOf("FREE BY PRODUCT DECISION", StringComparison.Ordinal);
        Assert.True(start >= 0, "the route guard must document the free pages");
        var end = shell.IndexOf("private static async Task FadeInCurrentPageAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return shell[start..end];
    }

    // ---- Original mileage stays free ----------------------------------------

    [Fact]
    public void OriginalMileage_IsNotRouteGuarded()
    {
        var guard = Guard();

        Assert.DoesNotContain("nameof(MileagePage)", guard);
        Assert.Contains("original mileage discovery is free", guard);
    }

    [Fact]
    public void TheMileageScreen_HasNoProGate()
    {
        var vm = Read("ViewModels", "MileageViewModel.cs");

        Assert.DoesNotContain("ProGate.", vm);
        Assert.DoesNotContain("EnsureProAsync", vm);
        Assert.DoesNotContain("HasObd", vm);
    }

    // ---- Service procedures stay free ---------------------------------------

    [Fact]
    public void ServiceProcedures_HaveNoProGate()
    {
        var vm = Read("ViewModels", "ServiceViewModel.Procedures.cs");

        Assert.DoesNotContain("ProGate", vm);
        Assert.DoesNotContain("HasObd", vm);
        Assert.DoesNotContain("EnsureProAsync", vm);
    }

    [Fact]
    public void TheServicePage_IsNotRouteGuarded()
    {
        var guard = Guard();

        Assert.DoesNotContain("nameof(ServicePage)", guard);
        Assert.Contains("service procedures are free", guard);
    }

    // ---- Oil life reset stays free ------------------------------------------

    [Fact]
    public void TheOilLifeReset_IsGatedOnlyByVehicleCompatibility()
    {
        var vm = Read("ViewModels", "AdvancedViewModels.cs");

        var apply = Between(vm, "private async Task ResetOilLifeAsync(int targetPercent)", "if (!confirmed)");
        Assert.DoesNotContain("ProGate", apply);
        Assert.DoesNotContain("HasObd", apply);
        Assert.DoesNotContain("EnsureProAsync", apply);

        // It keeps its own safety preconditions.
        Assert.Contains("!CanResetOilLife", apply);
        Assert.Contains("IsOilResetRunning", apply);

        var command = Between(vm, "private Task ResetOilLifeFull()", "private async Task ResetOilLifeAsync");
        Assert.Contains("ResetOilLifeAsync(100)", command);
        Assert.Contains("private Task ResetOilLifeHalf() => ResetOilLifeAsync(50);", vm);
    }

    // ---- genuinely premium routes stay guarded ------------------------------

    [Fact]
    public void ThePremiumRoutes_AreGuarded()
    {
        var guard = Guard();

        foreach (var page in new[]
                 {
                     "AiPage", "FuelPage", "DiagnosticReportPage", "ModuleScanPage", "CodingPage"
                 })
        {
            Assert.Contains($"nameof({page})", guard);
        }
    }

    [Fact]
    public void PremiumCommands_StillCallTheGate()
    {
        // Spot-check that the module/coding/hybrid/pre-purchase entry points kept
        // their gate — the route guard alone is not the whole policy.
        Assert.Contains("ProGate.EnsureAsync", Read("ViewModels", "AdvancedViewModels.cs"));
        Assert.Contains("ProGate.EnsureOrOpenAsync", Read("ViewModels", "CodingViewModel.cs"));
        Assert.Contains("ProGate.EnsureOrOpenAsync", Read("ViewModels", "HybridHealthViewModel.cs"));
        Assert.Contains("ProGate.EnsureAsync", Read("ViewModels", "PrePurchaseViewModel.cs"));
        Assert.Contains("ProGate.EnsureAsync", Read("ViewModels", "ModuleLiveDataViewModel.cs"));
    }

    // ---- the gate itself is token-based -------------------------------------

    [Fact]
    public void TheGateReadsTheTokenBackedProService()
    {
        var gate = Read("Services", "ProGate.cs");

        Assert.Contains("Pro is not { HasObd: true }", gate);
        Assert.Contains("await pro.EnsureProAsync()", gate);
    }

    [Fact]
    public void ProService_HasNoLegacyBooleanGrantPath()
    {
        var pro = Read("Services", "ProService.cs");

        // No code path may set a flag to true without a verified token.
        Assert.DoesNotContain("IsPro = true", pro);
        Assert.DoesNotContain("ServerVerified = true", pro);
        Assert.DoesNotContain("_accountObd", pro);
        Assert.DoesNotContain("_appleObd", pro);
    }
}
