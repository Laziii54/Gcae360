namespace CarScanner.Tests;

/// <summary>
/// The mobile product catalog, pinned against a regression that would be very
/// expensive at review time.
///
/// The apps sell exactly three monthly plans (OBD PRO, VIN PRO, Full PRO). The
/// yearly ids still exist in <c>ProService</c> because a yearly purchase made on
/// obd360.ge is a real entitlement the app has to understand — but the mobile
/// apps must never ask StoreKit for one, never show a Monthly/Yearly selector and
/// never show a yearly card. These are source-reading tests (like the rest of
/// this codebase's service-wiring tests) because PurchaseService and ProViewModel
/// depend on MAUI Preferences/SecureStorage and are not instantiable here.
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
    private static string ProPageSource() => Read("Views", "ProPage.xaml");

    [Fact]
    public void TheThreeMonthlyProductIds_AreUnchanged()
    {
        var source = ProServiceSource();
        Assert.Contains("public const string ObdProductId = \"com.georgiancarscanner.pro.monthly\";", source);
        Assert.Contains("public const string VinProductId = \"com.georgiancarscanner.vin.monthly\";", source);
        Assert.Contains("public const string FullProductId = \"com.georgiancarscanner.full.monthly\";", source);
    }

    [Fact]
    public void SubscriptionProductIds_OffersTheThreeMonthlyPlansAndNoYearlyId()
    {
        var source = PurchaseServiceSource();
        var array = source[source.IndexOf("public static readonly string[] SubscriptionProductIds", StringComparison.Ordinal)..];
        array = array[..array.IndexOf("];", StringComparison.Ordinal)];

        Assert.Contains("ProService.ObdProductId", array);
        Assert.Contains("ProService.VinProductId", array);
        Assert.Contains("ProService.FullProductId", array);

        // Nothing annual reaches StoreKit from the app.
        Assert.DoesNotContain("Yearly", array);
    }

    [Fact]
    public void EveryProductIdTheAppAsksStoreKitAbout_IsMonthlyOrAConsumablePack()
    {
        var source = PurchaseServiceSource();
        var all = source[source.IndexOf("public static readonly string[] AllProductIds", StringComparison.Ordinal)..];
        all = all[..all.IndexOf("];", StringComparison.Ordinal)];

        Assert.DoesNotContain("Yearly", all);
        foreach (var id in new[]
                 {
                     "ProService.ObdProductId", "ProService.VinProductId", "ProService.FullProductId",
                     "ProService.VinPack5ProductId", "ProService.VinPack10ProductId", "ProService.VinSingleProductId"
                 })
        {
            Assert.Contains(id, all);
        }
    }

    [Fact]
    public void IsCovered_MapsEachMonthlyPlanToItsOwnEntitlement()
    {
        var source = PurchaseServiceSource();
        var method = source[source.IndexOf("private bool IsCovered(string productId)", StringComparison.Ordinal)..];
        method = method[..(method.IndexOf("\n    }", StringComparison.Ordinal) + "\n    }".Length)];

        Assert.Contains("ProService.ObdProductId => _pro.HasObd", method);
        Assert.Contains("ProService.VinProductId => _pro.HasVin", method);
        Assert.Contains("ProService.FullProductId => _pro.HasObd && _pro.HasVin", method);
        Assert.DoesNotContain("Yearly", method);
    }

    [Fact]
    public void EachPlanCard_IsBuiltFromItsMonthlyProductIdOnly()
    {
        var source = ViewModelSource();

        Assert.Contains("new ProPlanItem { ProductId = ProService.ObdProductId", source);
        Assert.Contains("new ProPlanItem { ProductId = ProService.VinProductId", source);
        Assert.Contains("new ProPlanItem { ProductId = ProService.FullProductId, IsRecommended = true", source);
        Assert.DoesNotContain("YearlyProductId", source);
    }

    [Fact]
    public void ThePurchase_UsesTheSelectedPlansOwnProductId()
    {
        var source = ViewModelSource();
        Assert.Contains("await _purchase.BeginSubscriptionPurchaseAsync(plan.ProductId)", source);
    }

    [Fact]
    public void TheProPage_HasNoMonthlyYearlySelector()
    {
        var page = ProPageSource();

        Assert.DoesNotContain("SetYearlyBilling", page);
        Assert.DoesNotContain("SetMonthlyBilling", page);
        Assert.DoesNotContain("YearlyLabelText", page);
        Assert.DoesNotContain("MonthlyLabelText", page);
        Assert.DoesNotContain("YearlyDiscountBadgeText", page);
    }

    [Fact]
    public void TheProPage_ShowsNoInventedAnchorPriceOrDiscountBadge()
    {
        var page = ProPageSource();

        Assert.DoesNotContain("DisplayStrikeText", page);
        Assert.DoesNotContain("ShowDisplayStrike", page);
        Assert.DoesNotContain("Strikethrough", page);
        Assert.DoesNotContain("AnchorPriceText", page);
    }

    [Fact]
    public void ProService_KeepsNoHardcodedPriceConstantAtAll()
    {
        var source = ProServiceSource();

        // A compiled-in amount is exactly what App Review 3.1.2 flagged; there
        // must be nothing left for a fallback to reach for.
        Assert.DoesNotContain("PriceGEL", source);
        Assert.DoesNotContain("PriceAnchor", source);
        Assert.DoesNotContain("YearlyPrice", source);
    }

    [Fact]
    public void TheProViewModel_NeverUsesAFallbackPrice()
    {
        var source = ViewModelSource();
        var start = source.IndexOf("public partial class ProViewModel", StringComparison.Ordinal);
        var end = source.IndexOf("public sealed partial class ProPlanItem", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        // Prices come from one place: the StoreKit answer.
        Assert.Contains("private string? StorePrice(string productId)", source);
        Assert.Contains("_prices.TryGetValue(productId, out var price)", source);

        // ... and the Lari sign / a literal amount can no longer appear in the
        // PRO view model as a display price.
        Assert.DoesNotContain("₾", source[start..end]);
    }
}
