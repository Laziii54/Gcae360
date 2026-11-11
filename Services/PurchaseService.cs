using System.Net.Http.Json;
using CarScanner.Services.OBD;

#if IOS
using CarScanner.Platforms.iOS.Billing;
#endif

namespace CarScanner.Services;

/// <summary>How a Restore Purchases attempt ended. Every value has its own user-facing text.</summary>
public enum RestoreStatus
{
    /// <summary>At least one subscription was restored and verified.</summary>
    Restored,

    /// <summary>Apple has nothing to restore on this Apple Account.</summary>
    NothingToRestore,

    /// <summary>The backend could not be reached (network/server), nothing is known.</summary>
    Unreachable,

    /// <summary>Apple returned a transaction but the backend refused it.</summary>
    VerificationFailed,

    /// <summary>The restored subscription had already expired.</summary>
    Expired,

    /// <summary>The restored subscription was revoked (refunded).</summary>
    Revoked,

    /// <summary>The subscription is linked to an OBD360 account — signing in activates it.</summary>
    LinkedToAccount,

    /// <summary>No store on this platform (Android sells nothing in-app).</summary>
    Unsupported
}

public sealed record RestoreResult(RestoreStatus Status, int RestoredCount = 0, string? ErrorCode = null);

/// <summary>Outcome of sending one store transaction to the backend.</summary>
internal sealed record StoreSyncResult(bool Accepted, bool Reached, string? Code)
{
    public static readonly StoreSyncResult Unreachable = new(false, false, null);
    public static StoreSyncResult Failed(string code) => new(false, true, code);
    public static readonly StoreSyncResult Ok = new(true, true, null);
}

/// <summary>
/// Store purchases. The secure backend is the only authority for PRO and VIN
/// credits; the app never grants anything locally.
///
/// iOS: App Store (StoreKit). A transaction is finished only after the server
/// accepted it; unfinished transactions that StoreKit delivers again at launch
/// (or an approved "Ask to Buy") are sent to the server in the background.
///
/// An iOS purchase never needs an OBD360 account (App Review 5.1.1(v)). It is
/// verified against the Apple transaction through
/// <see cref="AppleEntitlementService"/> and bound to this installation. A
/// transaction that a previous app version already linked to an OBD360 account
/// is still honoured: while signed in, the account-scoped endpoint is used as a
/// fallback so those purchases keep working and never change owner.
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
    private readonly AppleEntitlementService _appleStore;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);
    private DateTimeOffset _lastAutomaticSynchronization = DateTimeOffset.MinValue;

#if IOS
    private readonly iOSBillingService _iosBilling;
#endif

    public PurchaseService(
        ProService pro,
        AccountService account,
        AppleEntitlementService appleStore
#if IOS
        , iOSBillingService iosBilling
#endif
    )
    {
        _pro = pro;
        _account = account;
        _appleStore = appleStore;

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

    /// <summary>The three monthly plans the app sells, and only those.</summary>
    public static readonly string[] SubscriptionProductIds =
    [
        ProService.ObdProductId,
        ProService.VinProductId,
        ProService.FullProductId
    ];

    /// <summary>One-time VIN credits (consumables).</summary>
    /// <summary>
    /// Android only: the prices the OBD360 website will actually charge for the
    /// same plans (the Android app has no billing of its own). Returned in the
    /// exact same shape as <see cref="GetPricesAsync"/>, so the PRO page renders
    /// identically on both platforms. Empty when the catalog cannot be read —
    /// the page then shows "price unavailable" and a retry, never a guess.
    /// </summary>
    public async Task<Dictionary<string, string>> GetWebsitePricesAsync(
        CancellationToken token = default)
    {
        var prices = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Uri.TryCreate(AIConfig.BackendBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return prices;
        }

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(
                HttpMethod.Get,
                "/api/web/prices?currency=" + WebsiteCurrency());

            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return prices;

            var payload = await response.Content.ReadFromJsonAsync<WebPricesResponse>(
                cancellationToken: token);

            foreach (var item in payload?.Products ?? [])
            {
                if (!string.IsNullOrWhiteSpace(item.ProductId) &&
                    !string.IsNullOrWhiteSpace(item.Formatted))
                {
                    prices[item.ProductId!] = item.Formatted!;
                }
            }
        }
        catch
        {
            // The caller renders "price unavailable" + retry.
        }

        return prices;
    }

    /// <summary>
    /// The currency the website itself would charge this visitor in: lari in
    /// Georgia, US dollars everywhere else (the same rule the checkout uses).
    /// </summary>
    private static string WebsiteCurrency()
    {
        try
        {
            if (TimeZoneInfo.Local.Id.Contains("Tbilisi", StringComparison.OrdinalIgnoreCase))
                return "GEL";

            return string.Equals(
                System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName,
                "GE",
                StringComparison.OrdinalIgnoreCase)
                ? "GEL"
                : "USD";
        }
        catch
        {
            return "GEL";
        }
    }

    public static readonly string[] VinCreditProductIds =
    [
        ProService.VinPack5ProductId,
        ProService.VinPack10ProductId,
        ProService.VinSingleProductId
    ];

    /// <summary>Every product the app may ask StoreKit about. Never a yearly id.</summary>
    public static readonly string[] AllProductIds =
    [
        ProService.ObdProductId,
        ProService.VinProductId,
        ProService.FullProductId,
        ProService.VinPack5ProductId,
        ProService.VinPack10ProductId,
        ProService.VinSingleProductId
    ];

    /// <summary>
    /// Store prices of every product, keyed by product id. Empty when the
    /// store cannot answer — callers must show a loading/error state, never a
    /// made-up price.
    /// </summary>
    public async Task<Dictionary<string, string>> GetPricesAsync(
        CancellationToken token = default)
    {
#if IOS
        return await _iosBilling.GetLocalizedPricesAsync(AllProductIds, token);
#else
        await Task.CompletedTask;
        return new Dictionary<string, string>();
#endif
    }

    // ============================================================
    // BUY
    // ============================================================

    /// <summary>Buys OBD PRO, VIN PRO or Full PRO. No account required.</summary>
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

            await _pro.RefreshAsync();
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
    /// Buys VIN report credits (+5, +10 or a single report). The server adds the
    /// credits once per transaction. Credits are account-scoped, so this one
    /// purchase really does need an OBD360 account.
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
        if (!_account.IsSignedIn)
            return "ERROR:ACCOUNT_REQUIRED";

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
    /// True when the account already has the entitlement a product id grants.
    /// Only the three monthly ids are sold in the app; a yearly purchase made on
    /// the website still satisfies the same plan through HasObd / HasVin.
    /// </summary>
    private bool IsCovered(string productId) => productId switch
    {
        ProService.ObdProductId => _pro.HasObd,
        ProService.VinProductId => _pro.HasVin,
        ProService.FullProductId => _pro.HasObd && _pro.HasVin,
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

            await _pro.RefreshAsync();

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

    // ============================================================
    // RESTORE
    // ============================================================

    /// <summary>
    /// Restore Purchases for the PRO page. Works with no OBD360 account at all:
    /// Apple returns the transactions, the backend verifies them, the device
    /// gets the entitlement back.
    /// </summary>
    public async Task<RestoreResult> RestoreDetailedAsync(
        CancellationToken token = default)
    {
#if IOS
        try
        {
            var restored = await _iosBilling.RestoreAsync(token);

            // Purchases still waiting in the StoreKit queue count as restored too.
            await _iosBilling.ProcessUnfinishedAsync(token);

            if (restored.Count == 0)
            {
                await _pro.RefreshAsync();
                return _pro.IsPro || _pro.HasVin
                    ? new RestoreResult(RestoreStatus.Restored, 1)
                    : new RestoreResult(RestoreStatus.NothingToRestore);
            }

            var accepted = 0;
            var firstCode = (string?)null;
            var unreachable = false;

            foreach (var transaction in restored)
            {
                var sync = await SyncWithServerAsync(
                    _account.EffectiveUserId,
                    "apple",
                    transaction.ProductId,
                    transaction.TransactionId,
                    transaction.OriginalTransactionId,
                    token);

                if (sync.Accepted)
                {
                    accepted++;
                    continue;
                }

                if (!sync.Reached)
                {
                    unreachable = true;
                    continue;
                }

                firstCode ??= sync.Code;
            }

            await _pro.RefreshAsync();

            if (accepted > 0 || _pro.IsPro || _pro.HasVin)
                return new RestoreResult(RestoreStatus.Restored, Math.Max(accepted, 1));

            if (unreachable)
                return new RestoreResult(RestoreStatus.Unreachable);

            return new RestoreResult(StatusFor(firstCode), 0, firstCode);
        }
        catch (OperationCanceledException)
        {
            return new RestoreResult(RestoreStatus.Unreachable);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App Store restore failed: {ex.GetType().Name}");
            return new RestoreResult(RestoreStatus.Unreachable);
        }
#else
        // Android: nothing to restore from a store. PRO bought on the website
        // comes from the account on the server.
        await Task.CompletedTask;
        try
        {
            await _pro.RefreshAsync();
            return _pro.IsPro || _pro.HasVin
                ? new RestoreResult(RestoreStatus.Restored, 1)
                : new RestoreResult(RestoreStatus.NothingToRestore);
        }
        catch
        {
            return new RestoreResult(RestoreStatus.Unreachable);
        }
#endif
    }

    /// <summary>Backwards-compatible wrapper used by older call sites.</summary>
    public async Task RestorePurchasesAsync(CancellationToken token = default)
        => _ = await RestoreDetailedAsync(token);

    private static RestoreStatus StatusFor(string? code) => code switch
    {
        "LINKED_TO_ACCOUNT" => RestoreStatus.LinkedToAccount,
        "SUBSCRIPTION_EXPIRED" => RestoreStatus.Expired,
        "SUBSCRIPTION_REVOKED" => RestoreStatus.Revoked,
        "ACCOUNT_REQUIRED" => RestoreStatus.LinkedToAccount,
        null => RestoreStatus.NothingToRestore,
        _ => RestoreStatus.VerificationFailed
    };

    /// <summary>
    /// Quietly reconciles store purchases with the server. Safe to call during
    /// app startup/resume; never opens a purchase or restore UI. Repeated
    /// lifecycle events are throttled.
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

            // The server remains the authority on all platforms, for both the
            // account entitlement and the accountless App Store one.
            await _pro.RefreshAsync();
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
        string? failureCode = null;

        try
        {
            var sync = await SyncWithServerAsync(
                _account.EffectiveUserId,
                "apple",
                info?.ProductId ?? productId,
                transactionId,
                info?.OriginalTransactionId,
                token);

            accepted = sync.Accepted;
            failureCode = sync.Code;
        }
        finally
        {
            // Finished only when the server accepted it; otherwise StoreKit
            // keeps it and it is verified again later (resume / next launch).
            _iosBilling.CompleteTransaction(transactionId, accepted);
        }

        if (accepted)
            return "VERIFIED";

        return failureCode switch
        {
            "LINKED_TO_ACCOUNT" => "ERROR:LINKED_TO_ACCOUNT",
            "ACCOUNT_REQUIRED" => "ERROR:ACCOUNT_REQUIRED",
            "SUBSCRIPTION_EXPIRED" => "ERROR:SUBSCRIPTION_EXPIRED",
            "SUBSCRIPTION_REVOKED" => "ERROR:SUBSCRIPTION_REVOKED",
            _ => "ERROR:SERVER_VERIFICATION_FAILED"
        };
    }

    private async Task<bool> VerifyUnfinishedAppleTransactionAsync(
        StoreTransactionInfo transaction,
        CancellationToken token)
    {
        var sync = await SyncWithServerAsync(
            _account.EffectiveUserId,
            "apple",
            transaction.ProductId,
            transaction.TransactionId,
            transaction.OriginalTransactionId,
            token);

        if (sync.Accepted)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(
                    () => _pro.RefreshAsync());
            }
            catch
            {
                // The entitlement is on the server; the UI refreshes later.
            }
        }

        return sync.Accepted;
    }
#endif

    /// <summary>
    /// Sends one store transaction to the backend. iOS purchases are verified
    /// accountlessly first (no OBD360 account required); a transaction that an
    /// older app version already linked to an account falls back to the
    /// account-scoped endpoint while signed in, so it keeps working and is
    /// never transferred to a different owner.
    /// </summary>
    private async Task<StoreSyncResult> SyncWithServerAsync(
        string userId,
        string store,
        string productId,
        string transactionId,
        string? originalTransactionId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            return StoreSyncResult.Failed("EMPTY_TRANSACTION");

        var accountless = store == "apple" &&
                          AppleEntitlementService.IsSupported &&
                          HasSecureBackend();

        if (accountless)
        {
            var verification = await _appleStore.VerifyAsync(
                productId,
                transactionId,
                originalTransactionId,
                token);

            if (!verification.Reached)
                return StoreSyncResult.Unreachable;

            if (verification.Entitlement is { } entitlement)
            {
                await _pro.ApplyAppleEntitlementAsync(entitlement);
                return StoreSyncResult.Ok;
            }

            var code = verification.ErrorCode;

            // A purchase from an older app version belongs to the OBD360
            // account that bought it. While that account is signed in, use the
            // account endpoint instead of taking the transaction away.
            var canFallBackToAccount =
                _account.IsSignedIn &&
                (code is "LINKED_TO_ACCOUNT" or "ACCOUNT_REQUIRED");

            if (!canFallBackToAccount)
                return StoreSyncResult.Failed(code ?? "NOT_VERIFIED");
        }

        return await SyncWithAccountAsync(
            userId,
            store,
            productId,
            transactionId,
            originalTransactionId,
            token);
    }

    private async Task<StoreSyncResult> SyncWithAccountAsync(
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
            return StoreSyncResult.Unreachable;
        }

        /*
         * Production security:
         * never send purchase verification traffic
         * over plain HTTP.
         */
        if (baseUri.Scheme != Uri.UriSchemeHttps)
            return StoreSyncResult.Unreachable;

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
                return StoreSyncResult.Failed("ACCOUNT_VERIFICATION_FAILED");

            var payload =
                await response.Content
                    .ReadFromJsonAsync<ServerEntitlement>(
                        cancellationToken: token);

            return payload?.Success == true
                ? StoreSyncResult.Ok
                : StoreSyncResult.Failed("ACCOUNT_VERIFICATION_FAILED");
        }
        catch
        {
            return StoreSyncResult.Unreachable;
        }
    }

    /// <summary>
    /// Purchase verification is never sent over plain HTTP: without a usable
    /// HTTPS backend the app reports a transport failure instead of downgrading.
    /// </summary>
    private static bool HasSecureBackend() =>
        Uri.TryCreate(AIConfig.BackendBaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

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

    /// <summary>Body of GET /api/web/prices.</summary>
    private sealed class WebPricesResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("products")]
        public List<WebPriceItem>? Products { get; set; }
    }

    private sealed class WebPriceItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("productId")]
        public string? ProductId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("formatted")]
        public string? Formatted { get; set; }
    }
}
