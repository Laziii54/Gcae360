namespace CarScanner.Tests;

/// <summary>
/// Monthly and yearly product id selection and mapping for OBD PRO / VIN PRO / Full PRO.
/// PurchaseService and ProViewModel both depend on MAUI Preferences/SecureStorage and are not
/// instantiable outside a real MAUI host (same reason as ProServiceLogoutTests/ProTierTests), so
/// this checks the actual source, the same way the rest of this codebase's service-wiring tests do.
///
/// Written after an App Store Connect audit found that PurchaseService.IsCovered and
/// SubscriptionProductIds only recognized the three *monthly* product ids - a yearly id fell
/// through to "not covered"/"not priced" even though the plan it belongs to might already be
/// active, or might be correctly configured in App Store Connect. That did not itself cause
/// PRODUCT_NOT_FOUND (the actual StoreKit purchase request already used the right id), but it was
/// a real, confirmed mapping bug in the same area, so it's fixed and pinned here.
/// </summary>
public sealed class SubscriptionProductMappingTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    private static string PurchaseServiceSource() => Read("Services", "PurchaseService.cs");
    private static string ProServiceSource() => Read("Services", "ProService.cs");
    private static string ViewModelSource() => Read("ViewModels", "AdvancedViewModels.cs");

    [Fact]
    public void EveryPlan_HasBothAMonthlyAndAYearlyProductId_InProService()
    {
        var source = ProServiceSource();
        Assert.Contains("public const string ObdProductId = \"com.georgiancarscanner.pro.monthly\";", source);
        Assert.Contains("public const string VinProductId = \"com.georgiancarscanner.vin.monthly\";", source);
        Assert.Contains("public const string FullProductId = \"com.georgiancarscanner.full.monthly\";", source);
        Assert.Contains("public const string ObdYearlyProductId = \"com.georgiancarscanner.pro.yearly\";", source);
        Assert.Contains("public const string VinYearlyProductId = \"com.georgiancarscanner.vin.yearly\";", source);
        Assert.Contains("public const string FullYearlyProductId = \"com.georgiancarscanner.full.yearly\";", source);
    }

    [Fact]
    public void SubscriptionProductIds_ListsBothMonthlyAndYearlyIdsForEveryPlan()
    {
        var source = PurchaseServiceSource();
        var array = source[source.IndexOf("public static readonly string[] SubscriptionProductIds", StringComparison.Ordinal)..];
        array = array[..array.IndexOf("];", StringComparison.Ordinal)];

        foreach (var id in new[]
                 {
                     "ProService.ObdProductId", "ProService.VinProductId", "ProService.FullProductId",
                     "ProService.ObdYearlyProductId", "ProService.VinYearlyProductId", "ProService.FullYearlyProductId"
                 })
        {
            Assert.Contains(id, array);
        }
    }

    [Fact]
    public void IsCovered_TreatsTheYearlyIdOfAPlanTheSameAsItsMonthlyId()
    {
        var source = PurchaseServiceSource();
        var method = source[source.IndexOf("private bool IsCovered(string productId)", StringComparison.Ordinal)..];
        method = method[..(method.IndexOf("\n    }", StringComparison.Ordinal) + "\n    }".Length)];

        Assert.Contains("ProService.ObdProductId or ProService.ObdYearlyProductId => _pro.HasObd", method);
        Assert.Contains("ProService.VinProductId or ProService.VinYearlyProductId => _pro.HasVin", method);
        Assert.Contains("ProService.FullProductId or ProService.FullYearlyProductId => _pro.HasObd && _pro.HasVin", method);
    }

    [Fact]
    public void EachPlanItem_IsConstructedWithItsOwnYearlyProductId_NotAnotherPlansOrNone()
    {
        var source = ViewModelSource();
        Assert.Contains("new ProPlanItem { ProductId = ProService.ObdProductId, YearlyProductId = ProService.ObdYearlyProductId }", source);
        Assert.Contains("new ProPlanItem { ProductId = ProService.VinProductId, YearlyProductId = ProService.VinYearlyProductId }", source);
        Assert.Contains("new ProPlanItem { ProductId = ProService.FullProductId, YearlyProductId = ProService.FullYearlyProductId, IsRecommended = true }", source);
    }

    [Fact]
    public void ActiveProductId_PicksTheYearlyId_OnlyWhenYearlyBillingIsOnAndThePlanHasOne()
    {
        var source = ViewModelSource();
        var method = source[source.IndexOf("private string ActiveProductId(ProPlanItem plan)", StringComparison.Ordinal)..];
        method = method[..method.IndexOf('\n', method.IndexOf(';', StringComparison.Ordinal))];

        Assert.Contains("IsYearlyBilling", method);
        Assert.Contains("plan.YearlyProductId", method);
        Assert.Contains("plan.ProductId", method);
        // Must fall back to the monthly id when the plan has no yearly id configured, not return empty/null.
        Assert.Contains("!string.IsNullOrEmpty(plan.YearlyProductId)", method);
    }
}
