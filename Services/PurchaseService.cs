using System.Net.Http.Json;

#if ANDROID
using CarScanner.Platforms.Android.Billing;
#endif
#if IOS
using CarScanner.Platforms.iOS.Billing;
#endif

namespace CarScanner.Services;

public sealed class PurchaseService
{
    public const string ProProductId = ProService.ProductId;

    private readonly ProService _pro;
    private readonly AccountService _account;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);
    private DateTimeOffset _lastAutomaticSynchronization = DateTimeOffset.MinValue;

#if ANDROID
    private readonly AndroidBillingService _androidBilling;
#endif
#if IOS
    private readonly iOSBillingService _iosBilling;
#endif

    public PurchaseService(
        ProService pro,
        AccountService account
#if ANDROID
        , AndroidBillingService androidBilling
#endif
#if IOS
        , iOSBillingService iosBilling
#endif
    )
    {
        _pro = pro;
        _account = account;

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

#if ANDROID
        _androidBilling = androidBilling;
#endif
#if IOS
        _iosBilling = iosBilling;
#endif
    }

    public async Task<string> GetPurchaseStatusAsync(
        CancellationToken token = default)
    {
        await _pro.LoadAsync();

        return _pro.IsPro
            ? $"PRO active until {_pro.ExpiresAt:dd.MM.yyyy}"
            : "FREE";
    }

    public async Task<string?> GetProPriceAsync(
        CancellationToken token = default)
    {
#if ANDROID
        return await _androidBilling
            .GetLocalizedProPriceAsync();
#elif IOS
        return await _iosBilling
            .GetLocalizedProPriceAsync(token);
#elif IOS
        try
        {
            var purchaseResult = await _iosBilling.PurchaseProductAsync(productId, token);
            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";
            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";
            if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
                return purchaseResult;

            if (!await SyncWithServerAsync(
                    _account.EffectiveUserId,
                    "apple",
                    productId,
                    purchaseResult,
                    token))
                return "ERROR:SERVER_VERIFICATION_FAILED";

            await _pro.LoadAsync(forceServerRefresh: true);
            return IsCovered(productId) ? "SUCCESS" : "ERROR:ENTITLEMENT_NOT_ACTIVE";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App Store purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    public static readonly string[] SubscriptionProductIds =
    [
        ProService.ObdProductId,
        ProService.VinProductId,
        ProService.FullProductId
    ];

    public static readonly string[] VinCreditProductIds =
    [
        ProService.VinPack5ProductId,
        ProService.VinPack10ProductId,
        ProService.VinSingleProductId
    ];

    /// <summary>Store prices of every product (missing ones use the fallback price).</summary>
    public async Task<Dictionary<string, string>> GetPricesAsync(
        CancellationToken token = default)
    {
#if ANDROID
        try
        {
            return await _androidBilling.GetLocalizedPricesAsync(
                SubscriptionProductIds.Concat(VinCreditProductIds));
        }
        catch
        {
            return new Dictionary<string, string>();
        }
#elif IOS
        var prices = new Dictionary<string, string>();
        foreach (var productId in SubscriptionProductIds.Concat(VinCreditProductIds))
        {
            try
            {
                var price = await _iosBilling.GetLocalizedPriceAsync(productId, token);
                if (!string.IsNullOrWhiteSpace(price))
                    prices[productId] = price;
            }
            catch
            {
                // Missing App Store Connect products keep their UI fallback price.
            }
        }
        return prices;
#else
        await Task.CompletedTask;
        return new Dictionary<string, string>();
#endif
    }

    /// <summary>
    /// Buys OBD PRO, VIN PRO or Full PRO. Moving to or from Full PRO
    /// replaces the current subscription (Google credits the unused time).
    /// </summary>
    public async Task<string> BeginSubscriptionPurchaseAsync(
        string productId,
        CancellationToken token = default)
    {
        if (productId == ProService.ObdProductId)
            return await BeginProPurchaseAsync(token);

        await _pro.LoadAsync(forceServerRefresh: true);
        if (IsCovered(productId))
            return "PRO_ALREADY_ACTIVE";

#if ANDROID
        try
        {
            var owned = await _androidBilling.GetOwnedPurchasesAsync(true, token);
            var replace = ReplacementToken(productId, owned);

            var purchaseResult = await _androidBilling.PurchaseProductAsync(productId, replace, token);
            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";
            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";
            if (purchaseResult == "PENDING")
                return "PENDING";

            if (purchaseResult == "ERROR:ITEM_ALREADY_OWNED")
            {
                purchaseResult = (await _androidBilling.GetOwnedPurchasesAsync(true, token))
                    .FirstOrDefault(item => item.ProductId == productId).Token;
                if (string.IsNullOrWhiteSpace(purchaseResult))
                    return "ERROR:OWNED_SUBSCRIPTION_NOT_FOUND";
            }

            if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
                return purchaseResult;

            if (!await SyncWithServerAsync(_account.EffectiveUserId, "google", productId, purchaseResult, token))
                return "ERROR:SERVER_VERIFICATION_FAILED";

            await _pro.LoadAsync(forceServerRefresh: true);
            return IsCovered(productId) ? "SUCCESS" : "ERROR:ENTITLEMENT_NOT_ACTIVE";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Google Play purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }
#elif IOS
        try
        {
            var purchaseResult = await _iosBilling.PurchaseProductAsync(productId, token);
            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";
            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";
            if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
                return purchaseResult;

            if (!await SyncWithServerAsync(
                    _account.EffectiveUserId,
                    "apple",
                    productId,
                    purchaseResult,
                    token))
                return "ERROR:SERVER_VERIFICATION_FAILED";

            await _pro.LoadAsync(forceServerRefresh: true);
            return IsCovered(productId) ? "SUCCESS" : "ERROR:ENTITLEMENT_NOT_ACTIVE";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App Store purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }
#elif IOS
        try
        {
            var purchaseResult = await _iosBilling.PurchaseProductAsync(productId, token);
            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";
            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";
            if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
                return purchaseResult;

            if (!await SyncWithServerAsync(_account.EffectiveUserId, "apple", productId, purchaseResult, token))
                return "ERROR:SERVER_VERIFICATION_FAILED";

            await _pro.LoadAsync(forceServerRefresh: true);
            return IsCovered(productId) ? "SUCCESS" : "ERROR:ENTITLEMENT_NOT_ACTIVE";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App Store purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }
#else
        await Task.CompletedTask;
        return "STORE_UNAVAILABLE";
#endif
    }

    /// <summary>
    /// Buys VIN report credits (+5, +10 or a single report). The server
    /// adds the credits once; the purchase is then consumed so it can be
    /// bought again.
    /// </summary>
    public async Task<string> BuyVinCreditsAsync(
        string productId,
        CancellationToken token = default)
    {
        if (!VinCreditProductIds.Contains(productId))
            return "ERROR:UNKNOWN_PRODUCT";

#if ANDROID
        try
        {
            var purchaseResult = await _androidBilling.PurchaseProductAsync(productId, null, token);
            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";
            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";
            if (purchaseResult == "PENDING")
                return "PENDING";

            if (purchaseResult == "ERROR:ITEM_ALREADY_OWNED")
            {
                // An earlier purchase was not finished: credit and consume it.
                purchaseResult = (await _androidBilling.GetOwnedPurchasesAsync(false, token))
                    .FirstOrDefault(item => item.ProductId == productId).Token;
                if (string.IsNullOrWhiteSpace(purchaseResult))
                    return "ERROR:OWNED_PURCHASE_NOT_FOUND";
            }

            if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
                return purchaseResult;

            if (!await SyncWithServerAsync(_account.EffectiveUserId, "google", productId, purchaseResult, token))
                return "ERROR:SERVER_VERIFICATION_FAILED";

            await _androidBilling.ConsumeAsync(purchaseResult);
            return "SUCCESS";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Google Play purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }
#elif IOS
        try
        {
            var purchaseResult = await _iosBilling.PurchaseProductAsync(productId, token);
            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";
            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";
            if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
                return purchaseResult;

            return await SyncWithServerAsync(
                _account.EffectiveUserId,
                "apple",
                productId,
                purchaseResult,
                token)
                ? "SUCCESS"
                : "ERROR:SERVER_VERIFICATION_FAILED";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App Store purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }
#elif IOS
        try
        {
            // The App Store finishes the consumable transaction itself; the
            // server adds the credits once per transaction ID.
            var purchaseResult = await _iosBilling.PurchaseProductAsync(productId, token);
            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";
            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";
            if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
                return purchaseResult;

            return await SyncWithServerAsync(_account.EffectiveUserId, "apple", productId, purchaseResult, token)
                ? "SUCCESS"
                : "ERROR:SERVER_VERIFICATION_FAILED";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App Store purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }
#else
        await Task.CompletedTask;
        return "STORE_UNAVAILABLE";
#endif
    }

    private bool IsCovered(string productId) => productId switch
    {
        ProService.ObdProductId => _pro.HasObd,
        ProService.VinProductId => _pro.HasVin,
        ProService.FullProductId => _pro.HasObd && _pro.HasVin,
        _ => false
    };

    private static string? ReplacementToken(
        string productId,
        IReadOnlyList<(string ProductId, string Token)> owned)
    {
        // Upgrade to Full replaces OBD / VIN; leaving Full replaces Full.
        // Buying VIN next to OBD (or the reverse) keeps both.
        if (productId == ProService.FullProductId)
            return owned.FirstOrDefault(item =>
                item.ProductId is ProService.ObdProductId or ProService.VinProductId).Token;

        return owned.FirstOrDefault(item => item.ProductId == ProService.FullProductId).Token;
    }

    /// <summary>Sends every purchase Google Play still holds to the server.</summary>
    private async Task SyncAllOwnedAsync(CancellationToken token)
    {
#if ANDROID
        foreach (var (productId, purchaseToken) in await _androidBilling.GetOwnedPurchasesAsync(true, token))
        {
            if (SubscriptionProductIds.Contains(productId))
                await SyncWithServerAsync(_account.EffectiveUserId, "google", productId, purchaseToken, token);
        }

        foreach (var (productId, purchaseToken) in await _androidBilling.GetOwnedPurchasesAsync(false, token))
        {
            if (VinCreditProductIds.Contains(productId) &&
                await SyncWithServerAsync(_account.EffectiveUserId, "google", productId, purchaseToken, token))
                await _androidBilling.ConsumeAsync(purchaseToken);
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task<string> BeginProPurchaseAsync(
        CancellationToken token = default)
    {
        await _pro.LoadAsync();

        if (_pro.IsPro)
            return "PRO_ALREADY_ACTIVE";

#if ANDROID
        try
        {
            /*
             * Start the real Google Play Billing flow.
             *
             * The Android billing service will:
             *
             * 1. Connect to Google Play
             * 2. Load the subscription
             * 3. Open Google's purchase UI
             * 4. Wait for the purchase callback
             * 5. Return the purchase token only when the
             *    purchase is actually completed
             */

            var purchaseResult =
                await _androidBilling
                    .PurchaseProAsync(token);

            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";

            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";

            if (purchaseResult == "PENDING")
                return "PENDING";

            if (purchaseResult == "ERROR:ITEM_ALREADY_OWNED")
            {
                purchaseResult = await _androidBilling
                    .GetActiveProPurchaseTokenAsync(token);
                if (string.IsNullOrWhiteSpace(purchaseResult))
                    return "ERROR:OWNED_SUBSCRIPTION_NOT_FOUND";
            }

            if (purchaseResult.StartsWith("ERROR:",
                    StringComparison.Ordinal))
            {
                return purchaseResult;
            }

            /*
             * For a successful Google Play purchase,
             * the returned value is the purchase token.
             */
            var purchaseToken = purchaseResult;

            var userId = _account.EffectiveUserId;

            /*
             * IMPORTANT:
             * The purchase token is sent to our secure backend.
             *
             * The backend must verify the token with Google Play
             * before granting the PRO entitlement.
             */
            var verified =
                await SyncWithServerAsync(
                    userId,
                    "google",
                    ProProductId,
                    purchaseToken,
                    token);

            if (!verified)
                return "ERROR:SERVER_VERIFICATION_FAILED";

            /*
             * Reload the entitlement from the server.
             *
             * We do NOT locally manufacture a PRO subscription.
             */
            await _pro.LoadAsync(forceServerRefresh: true);

            return _pro.IsPro
                ? "SUCCESS"
                : "ERROR:ENTITLEMENT_NOT_ACTIVE";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Google Play purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }

#elif IOS
        try
        {
            var purchaseResult =
                await _iosBilling.PurchaseProAsync(token);

            if (string.IsNullOrWhiteSpace(purchaseResult))
                return "ERROR:EMPTY_PURCHASE_RESULT";

            if (purchaseResult == "USER_CANCELED")
                return "CANCELLED";

            if (purchaseResult.StartsWith(
                    "ERROR:",
                    StringComparison.Ordinal))
            {
                return purchaseResult;
            }

            var verified =
                await SyncWithServerAsync(
                    _account.EffectiveUserId,
                    "apple",
                    ProProductId,
                    purchaseResult,
                    token);

            if (!verified)
                return "ERROR:SERVER_VERIFICATION_FAILED";

            await _pro.LoadAsync(forceServerRefresh: true);

            return _pro.IsPro
                ? "SUCCESS"
                : "ERROR:ENTITLEMENT_NOT_ACTIVE";
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"App Store purchase failed: {ex.GetType().Name}");
            return "ERROR:UNEXPECTED";
        }

#else

        return "STORE_UNAVAILABLE";

#endif
    }

    public async Task RestorePurchasesAsync(
        CancellationToken token = default)
    {
#if ANDROID

        try
        {
            await SyncAllOwnedAsync(token);
            await _pro.LoadAsync(forceServerRefresh: true);
        }
        catch
        {
            /*
             * Do not manufacture a local PRO entitlement.
             */
        }

#elif IOS
        try
        {
            var transactionId =
                await _iosBilling.RestoreProAsync(token);

            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                var verified =
                    await SyncWithServerAsync(
                        _account.EffectiveUserId,
                        "apple",
                        ProProductId,
                        transactionId,
                        token);

                if (!verified)
                    return;
            }

            await _pro.LoadAsync(forceServerRefresh: true);
        }
        catch
        {
            /*
             * Do not manufacture a local PRO entitlement.
             */
        }

#else

        await _pro.LoadAsync();

#endif
    }

    /// <summary>
    /// Quietly reconciles the locally owned store subscription with the
    /// server. This is safe to call during app startup/resume and never opens
    /// a purchase or restore UI. Repeated lifecycle events are throttled.
    /// </summary>
    public async Task SynchronizeOwnedPurchaseAsync(
        CancellationToken token = default)
    {
        if (DateTimeOffset.UtcNow - _lastAutomaticSynchronization <
            TimeSpan.FromMinutes(5))
        {
            return;
        }

        if (!await _synchronizationGate.WaitAsync(0, token))
            return;

        try
        {
            if (DateTimeOffset.UtcNow - _lastAutomaticSynchronization <
                TimeSpan.FromMinutes(5))
            {
                return;
            }

#if ANDROID
            await SyncAllOwnedAsync(token);
#endif

            // The server remains the authority on all platforms. On iOS the
            // StoreKit transaction observer handles purchases, while an
            // explicit restore stays user initiated to avoid system UI during
            // app startup.
            await _pro.LoadAsync(forceServerRefresh: true);
            _lastAutomaticSynchronization = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // App is leaving the foreground; no user-facing error is needed.
        }
        catch
        {
            // Startup synchronization is best-effort. It must never prevent
            // the main UI from loading when the store or network is offline.
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    private async Task<bool> SyncWithServerAsync(
        string userId,
        string store,
        string productId,
        string transactionId,
        CancellationToken token)
    {
        if (!Uri.TryCreate(
                AIConfig.BackendBaseUrl,
                UriKind.Absolute,
                out var baseUri))
        {
            return false;
        }

        /*
         * Production security:
         * never send purchase verification traffic
         * over plain HTTP.
         */
        if (baseUri.Scheme != Uri.UriSchemeHttps)
            return false;

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(
                HttpMethod.Post, "/api/store/verify");
            request.Content = JsonContent.Create(new
            {
                userId,
                store,
                productId,
                transactionId
            });
            using var response = await _http.SendAsync(request, token);

            if (!response.IsSuccessStatusCode)
                return false;

            var payload =
                await response.Content
                    .ReadFromJsonAsync<ServerEntitlement>(
                        cancellationToken: token);

            return payload?.Success == true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class ServerEntitlement
    {
        public bool Success { get; set; }
    }
}
