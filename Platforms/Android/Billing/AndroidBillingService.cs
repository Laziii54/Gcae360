#if ANDROID

using Android.BillingClient.Api;
using CarScanner.Services;
using Microsoft.Maui.ApplicationModel;
using System.Collections.Generic;

namespace CarScanner.Platforms.Android.Billing;

/// <summary>
/// Google Play Billing for every product of the app:
///  • subscriptions (OBD PRO, VIN PRO, Full PRO) — monthly,
///  • one-time VIN credits (+5, +10, single report).
/// Purchases are only reported back as tokens; the server verifies them.
/// </summary>
public sealed class AndroidBillingService :
    Java.Lang.Object,
    IBillingClientStateListener,
    IPurchasesUpdatedListener
{
    // Google Play ReplacementMode.WITH_TIME_PRORATION
    private const int ReplacementWithTimeProration = 1;

    private BillingClient? _billingClient;

    private TaskCompletionSource<bool>? _connectionTcs;

    private TaskCompletionSource<string?>? _purchaseTcs;

    private readonly Dictionary<string, ProductDetails> _details = new(StringComparer.Ordinal);

    public bool IsConnected =>
        _billingClient?.IsReady == true;

    public async Task<bool> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return true;

        var activity = Platform.CurrentActivity;

        if (activity is null)
            return false;

        try
        {
            _connectionTcs =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var pendingPurchases =
                PendingPurchasesParams
                    .NewBuilder()
                    .EnableOneTimeProducts()
                    .Build();

            _billingClient =
                BillingClient
                    .NewBuilder(activity)
                    .SetListener(this)
                    .EnablePendingPurchases(
                        pendingPurchases)
                    .EnableAutoServiceReconnection()
                    .Build();

            _billingClient.StartConnection(this);

            using var registration =
                cancellationToken.Register(
                    () => _connectionTcs.TrySetCanceled(
                        cancellationToken));

            return await _connectionTcs.Task;
        }
        catch (System.Exception)
        {
            try
            {
                _billingClient?.EndConnection();
                _billingClient?.Dispose();
            }
            catch
            {
            }

            _billingClient = null;

            return false;
        }
    }

    public static bool IsSubscription(string productId) =>
        productId is ProService.ObdProductId or ProService.VinProductId or ProService.FullProductId;

    public Task<ProductDetails?> GetProductDetailsAsync() =>
        GetProductDetailsAsync(ProService.ProductId);

    public async Task<ProductDetails?> GetProductDetailsAsync(string productId)
    {
        if (_details.TryGetValue(productId, out var cached))
            return cached;

        await LoadProductDetailsAsync(new[] { productId });
        return _details.TryGetValue(productId, out cached) ? cached : null;
    }

    /// <summary>Loads several products of the same kind with one store call.</summary>
    private async Task LoadProductDetailsAsync(IEnumerable<string> productIds)
    {
        if (!IsConnected && !await InitializeAsync())
            return;

        foreach (var group in productIds.GroupBy(IsSubscription))
        {
            try
            {
                var type = group.Key ? BillingClient.ProductType.Subs : BillingClient.ProductType.Inapp;
                var productList = group
                    .Select(id => QueryProductDetailsParams.Product
                        .NewBuilder()
                        .SetProductId(id)
                        .SetProductType(type)
                        .Build())
                    .ToList();

                var queryParams =
                    QueryProductDetailsParams
                        .NewBuilder()
                        .SetProductList(productList)
                        .Build();

                var result =
                    await _billingClient!
                        .QueryProductDetailsAsync(
                            queryParams);

                if (result?.ProductDetailsList is null)
                    continue;

                foreach (var details in result.ProductDetailsList)
                {
                    if (details?.ProductId is { } id)
                        _details[id] = details;
                }
            }
            catch (System.Exception)
            {
                // A missing product simply has no store price.
            }
        }
    }

    public async Task<string> GetProductStatusAsync()
    {
        try
        {
            var product =
                await GetProductDetailsAsync();

            if (product is null)
                return "PRODUCT_NOT_FOUND";

            return
                $"FOUND|{product.ProductId}|{product.Title ?? string.Empty}|{product.Description ?? string.Empty}";
        }
        catch (System.Exception ex)
        {
            return $"ERROR|{ex.Message}";
        }
    }

    // ============================================================
    // PURCHASE
    // ============================================================

    public Task<string?> PurchaseProAsync(
        CancellationToken cancellationToken = default) =>
        PurchaseProductAsync(ProService.ProductId, null, cancellationToken);

    /// <summary>
    /// Opens Google's purchase sheet. Returns the purchase token, or
    /// USER_CANCELED / PENDING / ERROR:… .
    /// <paramref name="replacePurchaseToken"/> switches an active
    /// subscription to this one (Google credits the unused time).
    /// </summary>
    public async Task<string?> PurchaseProductAsync(
        string productId,
        string? replacePurchaseToken,
        CancellationToken cancellationToken = default)
    {
        if (_purchaseTcs is not null)
            return "ERROR:PURCHASE_ALREADY_IN_PROGRESS";

        if (!IsConnected)
        {
            var connected =
                await InitializeAsync(
                    cancellationToken);

            if (!connected)
                return "ERROR:BILLING_UNAVAILABLE";
        }

        var product =
            await GetProductDetailsAsync(productId);

        if (product is null)
            return "ERROR:PRODUCT_NOT_FOUND";

        var subscription = IsSubscription(productId);
        var offerToken = subscription
            ? GetSubscriptionOfferToken(product)
            : GetOneTimeOfferToken(product);

        if (subscription && string.IsNullOrWhiteSpace(offerToken))
            return "ERROR:SUBSCRIPTION_OFFER_NOT_FOUND";

        var activity =
            Platform.CurrentActivity;

        if (activity is null)
            return "ERROR:ACTIVITY_UNAVAILABLE";

        _purchaseTcs =
            new TaskCompletionSource<string?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        try
        {
            var detailsBuilder =
                BillingFlowParams.ProductDetailsParams
                    .NewBuilder()
                    .SetProductDetails(product);

            if (!string.IsNullOrWhiteSpace(offerToken))
                detailsBuilder.SetOfferToken(offerToken);

            var flowBuilder =
                BillingFlowParams
                    .NewBuilder()
                    .SetProductDetailsParamsList(
                        new List<BillingFlowParams.ProductDetailsParams>
                        {
                            detailsBuilder.Build()
                        });

            if (subscription && !string.IsNullOrWhiteSpace(replacePurchaseToken))
                ApplySubscriptionReplacement(flowBuilder, replacePurchaseToken);

            var result =
                _billingClient!
                    .LaunchBillingFlow(
                        activity,
                        flowBuilder.Build());

            if (result.ResponseCode !=
                BillingResponseCode.Ok)
            {
                return MapBillingResponse(
                    result.ResponseCode);
            }

            using var registration =
                cancellationToken.Register(
                    () => _purchaseTcs.TrySetResult(
                        "ERROR:PURCHASE_CANCELLED"));

            return await _purchaseTcs.Task;
        }
        catch (OperationCanceledException)
        {
            return "ERROR:PURCHASE_CANCELLED";
        }
        catch (System.Exception ex)
        {
            return $"ERROR:{ex.Message}";
        }
        finally
        {
            _purchaseTcs = null;
        }
    }

    /// <summary>
    /// Adds SubscriptionUpdateParams through reflection so the build does
    /// not depend on the exact binding names of the billing library. When
    /// it cannot be applied the purchase still works as a new subscription.
    /// </summary>
    private static void ApplySubscriptionReplacement(
        BillingFlowParams.Builder flowBuilder,
        string oldPurchaseToken)
    {
        try
        {
            var updateType = typeof(BillingFlowParams).GetNestedType("SubscriptionUpdateParams");
            var builder = updateType?.GetMethod("NewBuilder", System.Type.EmptyTypes)?.Invoke(null, null);
            if (builder is null)
                return;

            var builderType = builder.GetType();
            builderType.GetMethod("SetOldPurchaseToken", new[] { typeof(string) })?
                .Invoke(builder, new object[] { oldPurchaseToken });

            var modeMethod = builderType.GetMethods().FirstOrDefault(method =>
                method.Name is "SetSubscriptionReplacementMode" or "SetReplaceProrationMode" &&
                method.GetParameters().Length == 1);
            if (modeMethod is not null)
            {
                var parameterType = modeMethod.GetParameters()[0].ParameterType;
                var mode = parameterType.IsEnum
                    ? System.Enum.ToObject(parameterType, ReplacementWithTimeProration)
                    : (object)ReplacementWithTimeProration;
                modeMethod.Invoke(builder, new[] { mode });
            }

            var updateParams = builderType.GetMethod("Build", System.Type.EmptyTypes)?.Invoke(builder, null);
            if (updateParams is null)
                return;

            flowBuilder.GetType().GetMethod("SetSubscriptionUpdateParams", new[] { updateParams.GetType() })?
                .Invoke(flowBuilder, new[] { updateParams });
        }
        catch
        {
            // Fallback: a separate subscription; the server combines them.
        }
    }

    // ============================================================
    // PRICES
    // ============================================================

    public Task<string?> GetLocalizedProPriceAsync() =>
        GetLocalizedPriceAsync(ProService.ProductId);

    public async Task<string?> GetLocalizedPriceAsync(string productId)
    {
        var product = await GetProductDetailsAsync(productId);
        if (product is null)
            return null;

        return IsSubscription(productId)
            ? GetRecurringFormattedPrice(product)
            : GetOneTimeFormattedPrice(product);
    }

    /// <summary>Store prices for several products (missing ones are left out).</summary>
    public async Task<Dictionary<string, string>> GetLocalizedPricesAsync(IEnumerable<string> productIds)
    {
        var ids = productIds.Distinct().ToList();
        await LoadProductDetailsAsync(ids.Where(id => !_details.ContainsKey(id)));

        var prices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!_details.TryGetValue(id, out var product))
                continue;
            var price = IsSubscription(id)
                ? GetRecurringFormattedPrice(product)
                : GetOneTimeFormattedPrice(product);
            if (!string.IsNullOrWhiteSpace(price))
                prices[id] = price;
        }

        return prices;
    }

    // ============================================================
    // OWNED PURCHASES
    // ============================================================

    public async Task<string?> GetActiveProPurchaseTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var owned = await GetOwnedPurchasesAsync(true, cancellationToken);
        return owned.FirstOrDefault(item => item.ProductId == ProService.ProductId).Token;
    }

    /// <summary>
    /// Purchases Google Play still holds: active subscriptions, or
    /// one-time purchases that were not consumed yet.
    /// </summary>
    public async Task<List<(string ProductId, string Token)>> GetOwnedPurchasesAsync(
        bool subscriptions,
        CancellationToken cancellationToken = default)
    {
        var list = new List<(string ProductId, string Token)>();
        if (!IsConnected && !await InitializeAsync(cancellationToken))
            return list;

        try
        {
            var query = QueryPurchasesParams
                .NewBuilder()
                .SetProductType(subscriptions ? BillingClient.ProductType.Subs : BillingClient.ProductType.Inapp)
                .Build();
            var result = await _billingClient!.QueryPurchasesAsync(query);
            if (result.Result.ResponseCode != BillingResponseCode.Ok)
                return list;

            foreach (var purchase in result.Purchases)
            {
                if (purchase is null ||
                    purchase.PurchaseState != PurchaseState.Purchased ||
                    string.IsNullOrWhiteSpace(purchase.PurchaseToken))
                    continue;

                foreach (var product in purchase.Products)
                {
                    if (!string.IsNullOrWhiteSpace(product))
                        list.Add((product, purchase.PurchaseToken));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        return list;
    }

    /// <summary>Consumes a one-time purchase so it can be bought again.</summary>
    public async Task<bool> ConsumeAsync(string purchaseToken)
    {
        if (!IsConnected && !await InitializeAsync())
            return false;

        try
        {
            var consumeParams = ConsumeParams
                .NewBuilder()
                .SetPurchaseToken(purchaseToken)
                .Build();
            // The server already consumes the purchase; this is a fallback.
            await _billingClient!.ConsumeAsync(consumeParams);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // REFLECTION HELPERS (offer details)
    // ============================================================

    private static Java.Lang.Object? CallJava(Java.Lang.Object? target, string method)
    {
        if (target is null) return null;
        return target.Class?
            .GetMethod(method)?
            .Invoke(target, System.Array.Empty<Java.Lang.Object>());
    }

    private static Java.Lang.Object? ListItem(Java.Lang.Object? list, int index)
    {
        if (list is null) return null;
        var size = CallJava(list, "size") as Java.Lang.Integer;
        if (size is null || size.IntValue() <= index) return null;
        return list.Class?
            .GetMethod("get", new[] { Java.Lang.Integer.Type! })?
            .Invoke(list, Java.Lang.Integer.ValueOf(index));
    }

    private static string? GetSubscriptionOfferToken(
        ProductDetails product)
    {
        try
        {
            var offer = ListItem(CallJava(product, "getSubscriptionOfferDetails"), 0);
            return CallJava(offer, "getOfferToken")?.ToString();
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static string? GetOneTimeOfferToken(ProductDetails product)
    {
        try
        {
            // Billing 8 lists one-time offers; older products have none.
            var offer = ListItem(CallJava(product, "getOneTimePurchaseOfferDetailsList"), 0);
            return CallJava(offer, "getOfferToken")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetOneTimeFormattedPrice(ProductDetails product)
    {
        try
        {
            var offer = CallJava(product, "getOneTimePurchaseOfferDetails")
                        ?? ListItem(CallJava(product, "getOneTimePurchaseOfferDetailsList"), 0);
            return CallJava(offer, "getFormattedPrice")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetRecurringFormattedPrice(
        ProductDetails product)
    {
        try
        {
            var offer = ListItem(CallJava(product, "getSubscriptionOfferDetails"), 0);
            var phases = CallJava(CallJava(offer, "getPricingPhases"), "getPricingPhaseList");
            if (phases is null)
                return null;

            var count = (CallJava(phases, "size") as Java.Lang.Integer)?.IntValue() ?? 0;
            if (count <= 0)
                return null;

            // The last phase is the recurring/base-plan price when an
            // introductory or free-trial phase exists before it.
            var phase = ListItem(phases, count - 1);
            return CallJava(phase, "getFormattedPrice")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string MapBillingResponse(
        BillingResponseCode code)
    {
        return code switch
        {
            (BillingResponseCode)1 =>
                "USER_CANCELED",

            BillingResponseCode.ServiceUnavailable =>
                "ERROR:SERVICE_UNAVAILABLE",

            BillingResponseCode.BillingUnavailable =>
                "ERROR:BILLING_UNAVAILABLE",

            BillingResponseCode.ItemUnavailable =>
                "ERROR:ITEM_UNAVAILABLE",

            BillingResponseCode.DeveloperError =>
                "ERROR:DEVELOPER_ERROR",

            BillingResponseCode.Error =>
                "ERROR:BILLING_ERROR",

            BillingResponseCode.ItemAlreadyOwned =>
                "ERROR:ITEM_ALREADY_OWNED",

            BillingResponseCode.NetworkError =>
                "ERROR:NETWORK_ERROR",

            BillingResponseCode.FeatureNotSupported =>
                "ERROR:FEATURE_NOT_SUPPORTED",

            _ =>
                $"ERROR:BILLING_RESPONSE_{(int)code}"
        };
    }

    public void OnBillingSetupFinished(
        BillingResult billingResult)
    {
        var success =
            billingResult.ResponseCode ==
            BillingResponseCode.Ok;

        _connectionTcs?.TrySetResult(
            success);
    }

    public void OnBillingServiceDisconnected()
    {
        _connectionTcs?.TrySetResult(false);
    }

    public void OnPurchasesUpdated(
        BillingResult billingResult,
        IList<Purchase>? purchases)
    {
        if (_purchaseTcs is null)
            return;

        /*
         * Google Play Billing:
         * response code 1 = USER_CANCELED
         */

        if (billingResult.ResponseCode ==
            (BillingResponseCode)1)
        {
            _purchaseTcs.TrySetResult(
                "USER_CANCELED");

            return;
        }

        if (billingResult.ResponseCode !=
            BillingResponseCode.Ok)
        {
            _purchaseTcs.TrySetResult(
                MapBillingResponse(
                    billingResult.ResponseCode));

            return;
        }

        if (purchases is null ||
            purchases.Count == 0)
        {
            _purchaseTcs.TrySetResult(
                "ERROR:NO_PURCHASE_RETURNED");

            return;
        }

        foreach (var purchase in purchases)
        {
            if (purchase is null)
                continue;

            if (purchase.PurchaseState ==
                PurchaseState.Pending)
            {
                _purchaseTcs.TrySetResult(
                    "PENDING");

                return;
            }

            if (purchase.PurchaseState !=
                PurchaseState.Purchased)
            {
                continue;
            }

            var purchaseToken =
                purchase.PurchaseToken;

            if (string.IsNullOrWhiteSpace(
                purchaseToken))
            {
                _purchaseTcs.TrySetResult(
                    "ERROR:EMPTY_PURCHASE_TOKEN");

                return;
            }

            _purchaseTcs.TrySetResult(
                purchaseToken);

            return;
        }

        _purchaseTcs.TrySetResult(
            "ERROR:PURCHASE_NOT_COMPLETED");
    }

    public void Disconnect()
    {
        try
        {
            if (_billingClient is not null)
            {
                _billingClient.EndConnection();
                _billingClient.Dispose();
                _billingClient = null;
            }
        }
        catch
        {
        }
    }
}

#endif
