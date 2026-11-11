namespace CarScanner.Tests;

/// <summary>
/// The App Review 5.1.1(v) contract, end to end, at the level a unit test can
/// reach without an iPhone: buying and restoring an App Store subscription must
/// not depend on an OBD360 account, while the verification itself stays exactly
/// as strict as before.
///
/// Source-reading tests, following this codebase's convention for anything that
/// needs MAUI Essentials or a live ASP.NET host.
/// </summary>
public sealed class AccountlessIosFlowTests
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

    // ============================================================
    // CLIENT: buying and restoring without an account
    // ============================================================

    [Fact]
    public void BuyingOnIos_DoesNotRequireSigningIn()
    {
        var vm = Read("ViewModels", "AdvancedViewModels.cs");
        var openPlan = Between(vm, "public async Task OpenPlan(ProPlanItem? plan)", "[RelayCommand]");

        var iosBranch = Between(openPlan, "if (CanBuyInApp)", "if (!_account.IsSignedIn)");

        Assert.Contains("await BuyPlan(plan);", iosBranch);
        Assert.DoesNotContain("AccountPage", iosBranch);
        Assert.DoesNotContain("Sign in first", iosBranch);
        Assert.DoesNotContain("IsSignedIn", iosBranch);
    }

    [Fact]
    public void RestorePurchases_DoesNotRedirectToLogin()
    {
        var vm = Read("ViewModels", "AdvancedViewModels.cs");
        var restore = Between(vm, "public async Task Restore()", "[RelayCommand]");

        Assert.Contains("RestoreDetailedAsync", restore);
        Assert.DoesNotContain("AccountPage", restore);
        Assert.DoesNotContain("Sign in first", restore);
    }

    [Fact]
    public void RestorePurchases_ReportsEveryOutcomeInsteadOfSwallowingIt()
    {
        var vm = Read("ViewModels", "AdvancedViewModels.cs");
        var presentation = Between(vm, "private (string Message, Color Color) RestorePresentation", "// ============================================================");

        foreach (var status in new[]
                 {
                     "RestoreStatus.Restored",
                     "RestoreStatus.NothingToRestore",
                     "RestoreStatus.Expired",
                     "RestoreStatus.Revoked",
                     "RestoreStatus.LinkedToAccount",
                     "RestoreStatus.VerificationFailed",
                     "RestoreStatus.Unsupported"
                 })
        {
            Assert.Contains(status, presentation);
        }

        // The unreachable/default case is a real message too, not an empty string.
        Assert.Contains("Could not reach the server", presentation);
    }

    [Fact]
    public void StoreVerification_PrefersTheAccountlessPath_AndNeverMarksATransactionUnverified()
    {
        var service = Read("Services", "PurchaseService.cs");

        Assert.Contains("_appleStore.VerifyAsync(", service);
        Assert.Contains("await _pro.ApplyAppleEntitlementAsync(entitlement);", service);

        // A purchase that an older version linked to an OBD360 account falls back
        // to the account endpoint while that account is signed in — it is never
        // taken away and never reported as verified without the server.
        Assert.Contains("canFallBackToAccount", service);
        Assert.Contains("code is \"LINKED_TO_ACCOUNT\" or \"ACCOUNT_REQUIRED\"", service);
    }

    [Fact]
    public void AVinCreditPack_StillNeedsAnAccount()
    {
        // Credits are counted per OBD360 account, so this one purchase stays
        // account-scoped: it is the only thing on the PRO page that does.
        var vm = Read("ViewModels", "AdvancedViewModels.cs");
        var buyPack = Between(vm, "public async Task BuyPack(ProPackItem? pack)", "[RelayCommand]");

        Assert.Contains("if (!_account.IsSignedIn)", buyPack);
        Assert.Contains("Extra VIN checks are stored on your account", buyPack);
    }

    [Fact]
    public void AnUnreachableApple_DoesNotSwitchOffAPaidSubscription()
    {
        var pro = Read("Services", "ProService.cs");
        var start = pro.IndexOf("private void ApplyApple(AppleEntitlement entitlement)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var apply = pro[start..];
        apply = apply[..apply.IndexOf("\n    }", StringComparison.Ordinal)];

        // The refresh answer carries verified:false when Apple could not be
        // reached; the last known server-verified state must survive that.
        Assert.Contains("if (!entitlement.Verified)", apply);
        var guard = apply.IndexOf("if (!entitlement.Verified)", StringComparison.Ordinal);
        var ret = apply.IndexOf("return;", guard, StringComparison.Ordinal);
        var assign = apply.IndexOf("_appleObd =", StringComparison.Ordinal);
        Assert.True(ret > guard && assign > ret,
            "an unverified refresh must return before touching the cached entitlement");
    }

    // ============================================================
    // STOREKIT: one cached product per id, never a price without a product
    // ============================================================

    [Fact]
    public void ProductPrices_AreCachedAndLoadedTogether()
    {
        var billing = Read("Platforms", "iOS", "Billing", "iOSBillingService.cs");

        Assert.Contains("private readonly Dictionary<string, CachedProduct> _products", billing);
        Assert.Contains("ProductLifetime", billing);
        Assert.Contains("public async Task<Dictionary<string, string>> GetLocalizedPricesAsync(", billing);

        // The price and the purchase must come from the same cached product.
        Assert.Contains("var loaded = await LoadProductsAsync([productId], cancellationToken);", billing);
    }

    [Fact]
    public void AFailedProductRequest_IsAnEmptyResult_NotAnInventedPrice()
    {
        var billing = Read("Platforms", "iOS", "Billing", "iOSBillingService.cs");

        Assert.Contains("A failed request is reported as \"no products\"", billing);
        Assert.Contains("_completion.TrySetResult(new Dictionary<string, SKProduct>(StringComparer.Ordinal));", billing);
    }

    // ============================================================
    // BACKEND: an anonymous endpoint that is still strict
    // ============================================================

    [Fact]
    public void TheAccountlessVerifyEndpoint_NeedsNoBearerToken()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(
            program,
            "\"/api/store/apple/anonymous/verify\"",
            "\"/api/store/apple/anonymous/refresh\"");

        Assert.DoesNotContain("Results.Unauthorized()", endpoint);
        Assert.DoesNotContain("accounts.ResolveAsync", endpoint);
        Assert.DoesNotContain("Authorization", endpoint);
    }

    [Fact]
    public void TheAccountlessVerifyEndpoint_StillVerifiesWithApple()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(
            program,
            "\"/api/store/apple/anonymous/verify\"",
            "\"/api/store/apple/anonymous/refresh\"");

        // The App Store Server API is the only thing believed.
        Assert.Contains("await verifier.VerifyAsync(", endpoint);
        Assert.Contains("\"apple\"", endpoint);
        Assert.Contains("RateWindow.TryConsume", endpoint);
        Assert.Contains("NOT_VERIFIED", endpoint);
    }

    [Fact]
    public void TheAccountlessEndpoint_NeverTakesAPurchaseAwayFromAnAccount()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(
            program,
            "\"/api/store/apple/anonymous/verify\"",
            "\"/api/store/apple/anonymous/refresh\"");

        Assert.Contains("EntitlementStore.IsAnonymousOwner(existingOwner)", endpoint);
        Assert.Contains("LINKED_TO_ACCOUNT", endpoint);
        Assert.Contains("Status409Conflict", endpoint);
    }

    [Fact]
    public void SubscriptionOwnership_IsKeyedByOriginalTransactionId_NotByTheTransactionId()
    {
        // Apple's two identifiers are not interchangeable: a purchase, a restore
        // and every renewal each have their own transactionId, while the
        // subscription keeps one originalTransactionId. Keying device links by a
        // transaction id would give each device its own "subscription".
        var verifier = Read("Backend", "CarScanner.Api", "Services", "StoreVerificationService.cs");

        var method = verifier[verifier.IndexOf("private async Task<VerifiedPurchase?> VerifyAppleAsync", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("var subscriptionKey =", method);
        Assert.Contains("tx?.OriginalTransactionId", method);
        Assert.Contains("GetAppleSubscriptionStateAsync(subscriptionKey", method);

        // The old form keyed the lookup off whatever the client sent — including
        // a bare per-transaction id.
        Assert.DoesNotContain("GetAppleSubscriptionStateAsync(originalTransactionId ?? transactionId", method);
        Assert.DoesNotContain("GetAppleSubscriptionStateAsync(transactionId", method);

        // And the record exposes the distinction explicitly.
        Assert.Contains("public string SubscriptionKey => OriginalTransactionId ?? TransactionId;", verifier);
    }

    [Fact]
    public void TheAccountlessEndpoint_LinksBySubscriptionKey()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(
            program,
            "\"/api/store/apple/anonymous/verify\"",
            "\"/api/store/apple/anonymous/refresh\"");

        Assert.Contains("var subscriptionKey = verified.SubscriptionKey;", endpoint);
        Assert.Contains("LinkSubscriptionOwnerAsync(\"apple\", subscriptionKey", endpoint);

        // The link key and the key stored on the subscription row must be the
        // same value, or a revocation by key would miss the row.
        Assert.Contains("verified.ExpiresAt, DateTimeOffset.UtcNow, subscriptionKey)", endpoint);
        Assert.DoesNotContain("LinkSubscriptionOwnerAsync(\"apple\", verified.TransactionId", endpoint);
    }

    [Fact]
    public void AppleNotifications_ResolveTheSubscriptionByOriginalTransactionId()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var handler = Between(
            program,
            "\"/api/store/apple/notifications\"",
            "\"/api/store/google/rtdn\"");

        // Apple's own originalTransactionId wins over the notification's own
        // transaction id, so a renewal/refund lands on the subscription rather
        // than on one transaction.
        Assert.Contains("appleTx?.OriginalTransactionId ?? hinted.OriginalTransactionId", handler);
        Assert.Contains("GetAppleSubscriptionStateAsync(original, ct)", handler);
    }

    [Fact]
    public void TheAccountlessEndpoint_LinksExtraDevices_WithoutRevokingAny()
    {
        // One Apple subscription, several devices: verify must ADD a link, never
        // transfer ownership and never revoke the device that already had it.
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(
            program,
            "\"/api/store/apple/anonymous/verify\"",
            "\"/api/store/apple/anonymous/refresh\"");

        Assert.Contains("LinkSubscriptionOwnerAsync", endpoint);
        Assert.DoesNotContain("TryClaimTransactionAsync", endpoint);
        Assert.DoesNotContain("RevokeSubscriptionAsync", endpoint);
        Assert.DoesNotContain("allowAuthenticatedTransfer", endpoint);
    }

    [Fact]
    public void ARenewalOrRefundNotification_ReachesEveryLinkedInstallation()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var handler = Between(
            program,
            "\"/api/store/apple/notifications\"",
            "\"/api/store/google/rtdn\"");

        // Apple's state is applied to the account owner (unchanged) AND to every
        // installation linked to the same subscription, so a refund removes
        // access everywhere instead of waiting for each device to refresh.
        Assert.Contains("GetSubscriptionOwnersAsync", handler);
        Assert.Contains("foreach (var linkedOwner in linked)", handler);
        Assert.Contains("store.RevokeSubscriptionAsync(linkedOwner", handler);
    }

    [Fact]
    public void TheAccountlessEndpoint_DoesNotGrantReportCredits()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(
            program,
            "\"/api/store/apple/anonymous/verify\"",
            "\"/api/store/apple/anonymous/refresh\"");

        Assert.Contains("ProductCatalog.IsConsumable(verified.ProductId)", endpoint);
        Assert.Contains("ACCOUNT_REQUIRED", endpoint);
        Assert.DoesNotContain("GrantCreditsAsync", endpoint);
    }

    [Fact]
    public void TheAccountAuthenticatedEndpoint_IsStillThereAndStillRequiresAToken()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(program, "\"/api/store/verify\"", "\"/api/store/apple/anonymous/verify\"");

        Assert.Contains("Results.Unauthorized()", endpoint);
        Assert.Contains("accounts.ResolveAsync", endpoint);
        Assert.Contains("VerifyAsync(", endpoint);
    }

    [Fact]
    public void TheRefreshEndpoint_ReReadsTheStateFromApple()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        var endpoint = Between(
            program,
            "\"/api/store/apple/anonymous/refresh\"",
            "\"/api/store/apple/anonymous\",");

        Assert.Contains("GetAppleSubscriptionStateAsync", endpoint);
        Assert.Contains("X-GCS-Anon-Token", endpoint);
        Assert.Contains("verifiedByApple = false", endpoint);
    }

    // ============================================================
    // WEBSITE: Android-originated checkout is monthly-only
    // ============================================================

    [Fact]
    public void TheAndroidCheckoutLink_CarriesTheSourceMarker()
    {
        var links = Read("Services", "StoreLinks.cs");
        Assert.Contains("fromAndroidApp", links);
        Assert.Contains("\"source=android\"", links);

        var vm = Read("ViewModels", "AdvancedViewModels.cs");
        Assert.Contains("StoreLinks.ProUrlFor(_account.Email, fromAndroidApp: true)", vm);
    }

    [Fact]
    public void TheWebsiteOffersMonthlyOnly_ForAndroid_AndBothPeriodsOtherwise()
    {
        var program = Read("Backend", "CarScanner.Api", "Program.cs");
        Assert.Contains("monthlyOnly", program);
        Assert.Contains("WebPages.ProPage(", program);

        var pages = Read("Backend", "CarScanner.Api", "Services", "WebPages.cs");
        Assert.Contains("bool monthlyOnly = false", pages);

        // The yearly tabs/details are only emitted when the page is not monthly-only.
        Assert.Contains("var planTabs = monthlyOnly", pages);
        Assert.Contains("var planDetails = monthlyOnly", pages);

        // ... and the independent website keeps both periods.
        Assert.Contains("ProductCatalog.ObdYearly", pages);
        Assert.Contains("ProductCatalog.VinYearly", pages);
        Assert.Contains("ProductCatalog.FullYearly", pages);
    }
}
