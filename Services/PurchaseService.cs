using System.Net.Http.Json;
using CarScanner.Services.OBD;

#if IOS
using CarScanner.Platforms.iOS.Billing;
#endif

namespace CarScanner.Services;

/// <summary>
/// Store purchases. The secure backend is the only authority for PRO and VIN
/// credits; the app never grants anything locally.
///
/// iOS: App Store (StoreKit). A transaction is finished only after the server
/// accepted it; unfinished transactions that StoreKit delivers again at launch
/// (or an approved "Ask to Buy") are sent to the server in the background.
///
/// Android: no in-app purchases (Google Play Billing is not used). PRO bought
/// on the website is unlocked by signing in to the account.
/// </summary>
public sealed class PurchaseService
{
    public const string ProProductId = ProService.ProductId;

    /// <summary>Result code on Android, where the app sells nothing.</summary>
    public const string PurchasesNotAvailableOnAndroid = "PURCHASES_NOT_AVAILABLE_ANDROID";

    /// <summary>User text for <see cref="PurchasesNotAvailableOnAndroid"/>.</summary>
    public static string PurchasesNotAvailableOnAndroidMessage =>
        ObdLang.L(
            "Android აპში შეძენა არ არის ხელმისაწვდომი. თუ შენს OBD360 ანგარიშზე PRO უკვე აქტიურია, შედი ანგარიშზე ან დააჭირე „PRO-ს აღდგენას“.",
            "Purchases are not available in the Android app. If PRO is already active on your OBD360 account, sign in or tap Restore PRO.");

    private readonly ProService _pro;
    private readonly AccountService _account;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);
    private DateTimeOffset _lastAutomaticSynchronization = DateTimeOffset.MinValue;

#if IOS
    private readonly iOSBillingService _iosBilling;
#endif

    public PurchaseService(
        ProService pro,
        AccountService account
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

#if IOS
        _iosBilling = iosBilling;

        // Purchased transactions that nobody is waiting for (re-delivered at
        // launch, approved Ask to Buy, renewals) are synced here and finished
        // only when the server accepted them.
        _iosBilling.SetServerVerifier(VerifyUnfinishedAppleTransactionAsync);
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
#if IOS
        return await _iosBilling
            .GetLocalizedProPriceAsync(token);
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    public static readonly string[] SubscriptionProductIds =
    [
        ProService.ObdProductId,
        ProService.VinProductId,
        ProService.FullProductId,
        ProService.ObdYearlyProductId,
        ProService.VinYearlyProductId,
        ProService.FullYearlyProductId
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
#if IOS
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

    /// <summary>Buys OBD PRO, VIN PRO or Full PRO.</summary>
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
        return PurchasesNotAvailableOnAndroid;
#elif IOS
        try
        {
            var result = await PurchaseAppleAsync(productId, token);
            if (result != "VERIFIED")
                return result;

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
        return "STORE_UNAVAILABLE";
#endif
    }

    /// <summary>
    /// Buys VIN report credits (+5, +10 or a single report). The server
    /// adds the credits once per transaction.
    /// </summary>
    public async Task<string> BuyVinCreditsAsync(
        string productId,
        CancellationToken token = default)
    {
        if (!VinCreditProductIds.Contains(productId))
            return "ERROR:UNKNOWN_PRODUCT";

#if ANDROID
        await Task.CompletedTask;
        return PurchasesNotAvailableOnAndroid;
#elif IOS
        try
        {
            var result = await PurchaseAppleAsync(productId, token);
            return result == "VERIFIED" ? "SUCCESS" : result;
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
    /// True when the account already has the entitlement a product id would grant - monthly and
    /// yearly ids of the same plan must answer the same way, or a yearly purchase attempt (or a
    /// yearly id's "already active" check) silently falls through to the "_ => false" case and
    /// starts a real purchase even though the account is already covered.
    /// </summary>
    private bool IsCovered(string productId) => productId switch
    {
        ProService.ObdProductId or ProService.ObdYearlyProductId => _pro.HasObd,
        ProService.VinProductId or ProService.VinYearlyProductId => _pro.HasVin,
        ProService.FullProductId or ProService.FullYearlyProductId => _pro.HasObd && _pro.HasVin,
        _ => false
    };

    public async Task<string> BeginProPurchaseAsync(
        CancellationToken token = default)
    {
        await _pro.LoadAsync();

        if (_pro.IsPro)
            return "PRO_ALREADY_ACTIVE";

#if ANDROID
        return PurchasesNotAvailableOnAndroid;
#elif IOS
        try
        {
            var result = await PurchaseAppleAsync(ProProductId, token);
            if (result != "VERIFIED")
                return result;

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
#if IOS
        try
        {
            // Each restored subscription is sent with its own transaction ID,
            // its own product ID and its original transaction ID.
            var restored =
                await _iosBilling.RestoreAsync(token);

            foreach (var transaction in restored)
            {
                await SyncWithServerAsync(
                    _account.EffectiveUserId,
                    "apple",
                    transaction.ProductId,
                    transaction.TransactionId,
                    transaction.OriginalTransactionId,
                    token);
            }

            // Purchases still waiting in the StoreKit queue are retried too.
            await _iosBilling.ProcessUnfinishedAsync(token);

            await _pro.LoadAsync(forceServerRefresh: true);
        }
        catch
        {
            /*
             * Do not manufacture a local PRO entitlement.
             */
        }
#else
        // Android: nothing to restore from a store. PRO bought on the website
        // comes from the account on the server.
        try
        {
            await _pro.LoadAsync(forceServerRefresh: true);
        }
        catch
        {
        }
#endif
    }

    /// <summary>
    /// Quietly reconciles store purchases with the server. This is safe to call
    /// during app startup/resume and never opens a purchase or restore UI.
    /// Repeated lifecycle events are throttled.
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

#if IOS
            // Unfinished App Store transactions (e.g. the server was not
            // reachable when they were bought) are retried on resume.
            await _iosBilling.ProcessUnfinishedAsync(token);
#endif

            // The server remains the authority on all platforms.
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

#if IOS
    /// <summary>
    /// Runs one App Store purchase and sends it to the server. Returns
    /// "VERIFIED" when the server accepted the transaction (it is then
    /// finished), otherwise the result code for the UI. "PENDING" means Ask to
    /// Buy: the purchase waits for approval and is synced in the background.
    /// </summary>
    private async Task<string> PurchaseAppleAsync(
        string productId,
        CancellationToken token)
    {
        var purchaseResult =
            await _iosBilling.PurchaseProductAsync(
                productId,
                token,
                _account.HashedAccountId);

        if (string.IsNullOrWhiteSpace(purchaseResult))
            return "ERROR:EMPTY_PURCHASE_RESULT";

        if (purchaseResult == "USER_CANCELED")
            return "CANCELLED";

        if (purchaseResult == "PENDING")
            return "PENDING";

        if (purchaseResult.StartsWith("ERROR:", StringComparison.Ordinal))
            return purchaseResult;

        var transactionId = purchaseResult;
        var info = _iosBilling.GetTransaction(transactionId);
        var accepted = false;

        try
        {
            accepted = await SyncWithServerAsync(
                _account.EffectiveUserId,
                "apple",
                info?.ProductId ?? productId,
                transactionId,
                info?.OriginalTransactionId,
                token);
        }
        finally
        {
            // Finished only when the server accepted it; otherwise StoreKit
            // keeps it and it is verified again later (resume / next launch).
            _iosBilling.CompleteTransaction(transactionId, accepted);
        }

        return accepted
            ? "VERIFIED"
            : "ERROR:SERVER_VERIFICATION_FAILED";
    }

    private async Task<bool> VerifyUnfinishedAppleTransactionAsync(
        StoreTransactionInfo transaction,
        CancellationToken token)
    {
        var accepted = await SyncWithServerAsync(
            _account.EffectiveUserId,
            "apple",
            transaction.ProductId,
            transaction.TransactionId,
            transaction.OriginalTransactionId,
            token);

        if (accepted)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(
                    () => _pro.LoadAsync(forceServerRefresh: true));
            }
            catch
            {
                // The entitlement is on the server; the UI refreshes later.
            }
        }

        return accepted;
    }
#endif

    private async Task<bool> SyncWithServerAsync(
        string userId,
        string store,
        string productId,
        string transactionId,
        string? originalTransactionId,
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
            request.Content = JsonContent.Create(new StoreVerifyRequest
            {
                UserId = userId,
                Store = store,
                ProductId = productId,
                TransactionId = transactionId,
                OriginalTransactionId = string.IsNullOrWhiteSpace(originalTransactionId)
                    ? null
                    : originalTransactionId
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

    /// <summary>Body of POST /api/store/verify (backend StoreVerifyRequest).</summary>
    private sealed class StoreVerifyRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("userId")]
        public string? UserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("store")]
        public string? Store { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("productId")]
        public string? ProductId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("transactionId")]
        public string? TransactionId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("originalTransactionId")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? OriginalTransactionId { get; set; }
    }

    private sealed class ServerEntitlement
    {
        public bool Success { get; set; }
    }
}
