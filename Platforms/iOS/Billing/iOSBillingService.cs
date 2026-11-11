#if IOS

using Foundation;
using StoreKit;
using CarScanner.Services;

namespace CarScanner.Platforms.iOS.Billing;

#pragma warning disable CA1422

/// <summary>One StoreKit transaction as it is sent to the server for verification.</summary>
public sealed record StoreTransactionInfo(
    string TransactionId,
    string? OriginalTransactionId,
    string ProductId);

/// <summary>
/// StoreKit transaction bridge. It only returns Apple transaction IDs;
/// the secure backend is solely responsible for granting PRO entitlement.
///
/// Purchased transactions are finished (<c>FinishTransaction</c>) only after
/// the server accepted them. Until then StoreKit keeps them in the payment
/// queue and delivers them again at the next launch, where the observer
/// (added at app start, see AppDelegate) hands them to the server verifier
/// that PurchaseService registers.
/// </summary>
public sealed class iOSBillingService : IDisposable
{
    private const string ProductId =
        ProService.ProductId;

    private readonly PaymentObserver _observer;
    private readonly SemaphoreSlim _operationGate =
        new(1, 1);
    private readonly SemaphoreSlim _processGate =
        new(1, 1);

    // Loaded SKProducts, keyed by their exact product identifier. The price the
    // PRO page shows and the product a purchase request uses both come from
    // this one entry, so a displayed price can never belong to a different
    // product than the one being bought.
    private readonly Dictionary<string, CachedProduct> _products =
        new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _productGate = new(1, 1);

    /// <summary>How long a loaded product (and therefore its price) is reused.</summary>
    private static readonly TimeSpan ProductLifetime = TimeSpan.FromMinutes(10);

    private sealed record CachedProduct(SKProduct Product, DateTimeOffset LoadedAt);

    private Func<StoreTransactionInfo, CancellationToken, Task<bool>>? _verifier;
    private bool _disposed;

    public bool IsReady =>
        !_disposed && SKPaymentQueue.CanMakePayments;

    public iOSBillingService()
    {
        _observer = new PaymentObserver(
            () => _ = ProcessUnfinishedAsync(CancellationToken.None));
        SKPaymentQueue.DefaultQueue
            .AddTransactionObserver(_observer);
    }

    /// <summary>
    /// Registers the server check used for purchased transactions that are not
    /// part of a purchase the user is waiting for (re-delivered at launch,
    /// approved "Ask to Buy", renewals). It returns true when the transaction may
    /// be finished.
    /// </summary>
    public void SetServerVerifier(
        Func<StoreTransactionInfo, CancellationToken, Task<bool>> verifier)
    {
        _verifier = verifier;
        _ = ProcessUnfinishedAsync(CancellationToken.None);
    }

    /// <summary>Sends every unfinished purchased transaction to the server once.</summary>
    public async Task ProcessUnfinishedAsync(
        CancellationToken cancellationToken = default)
    {
        var verifier = _verifier;
        if (verifier is null || _disposed)
            return;

        try
        {
            await _processGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            foreach (var info in _observer.UnclaimedTransactions())
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                    break;

                bool accepted;
                try
                {
                    accepted = await verifier(info, cancellationToken);
                }
                catch
                {
                    accepted = false;
                }

                // Not accepted: stays in the queue and is tried again on
                // the next resume / launch.
                if (accepted)
                    _observer.Finish(info.TransactionId);
            }
        }
        finally
        {
            _processGate.Release();
        }
    }

    public Task<string> GetProductStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            IsReady
                ? $"IOS_BILLING_READY|{ProductId}"
                : $"IOS_BILLING_UNAVAILABLE|{ProductId}");
    }

    public Task<string> GetProductIdAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProductId);
    }

    public Task<bool> IsStoreAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsReady);
    }

    public async Task<string?> GetLocalizedProPriceAsync(
        CancellationToken cancellationToken = default)
        => await GetLocalizedPriceAsync(ProductId, cancellationToken);

    public async Task<string?> GetLocalizedPriceAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
            return null;

        var prices = await GetLocalizedPricesAsync([productId], cancellationToken);
        return prices.TryGetValue(productId, out var price) ? price : null;
    }

    /// <summary>
    /// The App Store's own localized prices for the given products, keyed by
    /// product id. One SKProductsRequest is used for the whole set and the
    /// result is cached, so opening the PRO page does not fire a request per
    /// price. A product that StoreKit does not resolve is simply absent from
    /// the result — the caller must show that as "price unavailable" and must
    /// never substitute an invented price.
    /// </summary>
    public async Task<Dictionary<string, string>> GetLocalizedPricesAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken = default)
    {
        var prices = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!IsReady || productIds.Count == 0)
            return prices;

        var products = await LoadProductsAsync(productIds, cancellationToken);
        foreach (var (id, product) in products)
        {
            var price = FormatPrice(product);
            if (!string.IsNullOrWhiteSpace(price))
                prices[id] = price!;
        }

        return prices;
    }

    /// <summary>Apple's localized price string for one product (currency follows the App Store territory).</summary>
    private static string? FormatPrice(SKProduct product)
    {
        try
        {
            using var formatter = new NSNumberFormatter
            {
                NumberStyle = NSNumberFormatterStyle.Currency,
                Locale = product.PriceLocale
            };

            return formatter.StringFromNumber(product.Price);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> PurchaseProAsync(
        CancellationToken cancellationToken = default)
        => await PurchaseProductAsync(ProductId, cancellationToken);

    /// <summary>
    /// Starts an App Store purchase. Returns the transaction ID of a purchased
    /// (still unfinished) transaction, "PENDING" (Ask to Buy waits for approval),
    /// "USER_CANCELED" or "ERROR:...". After the server check the caller must call
    /// <see cref="CompleteTransaction"/> with the transaction ID.
    /// </summary>
    /// <param name="applicationUsername">
    /// Optional opaque account id (SHA-256 hex of the OBD360 account id) set on
    /// <c>SKMutablePayment.ApplicationUsername</c> so the server can bind the
    /// purchase to the account.
    /// </param>
    public async Task<string> PurchaseProductAsync(
        string productId,
        CancellationToken cancellationToken = default,
        string? applicationUsername = null)
    {
        if (!IsReady)
            return "ERROR:STORE_UNAVAILABLE";

        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            // The same cached product the price was read from, so the amount
            // shown on the PRO page always belongs to the product bought here.
            var loaded = await LoadProductsAsync([productId], cancellationToken);
            if (!loaded.TryGetValue(productId, out var product) || product is null)
                return "ERROR:PRODUCT_NOT_FOUND";

            var resultTask = _observer.BeginPurchase(productId);

            using var registration =
                cancellationToken.Register(
                    _observer.CancelPurchase);

            var payment = SKMutablePayment.PaymentWithProduct(product);
            if (!string.IsNullOrWhiteSpace(applicationUsername))
                payment.ApplicationUsername = applicationUsername;

            SKPaymentQueue.DefaultQueue.AddPayment(payment);

            return await resultTask;
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App Store purchase failed: {ex.GetType().Name}");
            return "ERROR:PURCHASE_FAILED";
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Details of a purchased transaction that is not finished yet.</summary>
    public StoreTransactionInfo? GetTransaction(string transactionId) =>
        _observer.Find(transactionId);

    /// <summary>
    /// Ends the app's handling of a purchase returned by
    /// <see cref="PurchaseProductAsync"/>. Accepted by the server: the
    /// transaction is finished. Otherwise it stays in the StoreKit queue and is
    /// verified again later (next resume or launch).
    /// </summary>
    public void CompleteTransaction(
        string transactionId,
        bool acceptedByServer)
    {
        if (acceptedByServer)
            _observer.Finish(transactionId);
        else
            _observer.Release(transactionId);
    }

    /// <summary>
    /// Restores completed App Store transactions. Returns the newest restored
    /// transaction per original transaction (each with its own transaction ID,
    /// original transaction ID and product ID).
    /// </summary>
    public async Task<IReadOnlyList<StoreTransactionInfo>> RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
            return [];

        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            var resultTask = _observer.BeginRestore();

            using var registration =
                cancellationToken.Register(
                    _observer.CancelRestore);

            SKPaymentQueue.DefaultQueue
                .RestoreCompletedTransactions();

            return await resultTask;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Resolves a set of product ids to <see cref="SKProduct"/>s, reusing the
    /// cache when it is still fresh. One request for the whole set. Products
    /// StoreKit cannot resolve are simply missing from the result (usually a
    /// product id that does not exist in App Store Connect yet, or is not in a
    /// state StoreKit will return).
    /// </summary>
    private async Task<Dictionary<string, SKProduct>> LoadProductsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SKProduct>(StringComparer.Ordinal);
        var missing = new List<string>();

        await _productGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var id in productIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (_products.TryGetValue(id, out var cached) &&
                    now - cached.LoadedAt < ProductLifetime)
                {
                    result[id] = cached.Product;
                }
                else
                {
                    missing.Add(id);
                }
            }

            if (missing.Count == 0)
                return result;

            var fetched = await RequestProductsAsync(missing, cancellationToken);
            foreach (var (id, product) in fetched)
            {
                _products[id] = new CachedProduct(product, now);
                result[id] = product;
            }

            return result;
        }
        finally
        {
            _productGate.Release();
        }
    }

    private static async Task<Dictionary<string, SKProduct>> RequestProductsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken)
    {
        var completion =
            new TaskCompletionSource<Dictionary<string, SKProduct>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        using var productIdentifiers =
            NSSet.MakeNSObjectSet(
                productIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => (NSObject)new NSString(id))
                    .ToArray());

        using var request = new SKProductsRequest(productIdentifiers);

        using var requestDelegate = new ProductRequestDelegate(completion);

        request.Delegate = requestDelegate;

        using var registration =
            cancellationToken.Register(
                () =>
                {
                    request.Cancel();
                    completion.TrySetCanceled(cancellationToken);
                });

        request.Start();
        return await completion.Task;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        SKPaymentQueue.DefaultQueue
            .RemoveTransactionObserver(_observer);

        _observer.Dispose();
        _operationGate.Dispose();
        _productGate.Dispose();
        _products.Clear();
    }

    private sealed class ProductRequestDelegate :
        NSObject,
        ISKProductsRequestDelegate,
        ISKRequestDelegate
    {
        private readonly TaskCompletionSource<Dictionary<string, SKProduct>>
            _completion;

        public ProductRequestDelegate(
            TaskCompletionSource<Dictionary<string, SKProduct>> completion)
        {
            _completion = completion;
        }

        [Export("productsRequest:didReceiveResponse:")]
        public void ReceivedResponse(
            SKProductsRequest request,
            SKProductsResponse response)
        {
            var products = new Dictionary<string, SKProduct>(StringComparer.Ordinal);
            foreach (var product in response.Products)
            {
                if (!string.IsNullOrWhiteSpace(product.ProductIdentifier))
                    products[product.ProductIdentifier] = product;
            }

            _completion.TrySetResult(products);
        }

        [Export("request:didFailWithError:")]
        public void RequestFailed(
            SKRequest request,
            NSError error)
        {
            // A failed request is reported as "no products" rather than an
            // exception: the UI then shows "price unavailable / retry" instead
            // of a crash, and a genuine product that simply was not loaded is
            // never mistaken for a wrong price.
            System.Diagnostics.Debug.WriteLine(
                $"SKProductsRequest failed: {error.LocalizedDescription}");
            _completion.TrySetResult(new Dictionary<string, SKProduct>(StringComparer.Ordinal));
        }
    }

    private sealed class PaymentObserver :
        SKPaymentTransactionObserver
    {
        private readonly object _gate = new();
        private readonly Action _unclaimedArrived;

        // Purchased transactions that are not finished yet, by transaction ID.
        private readonly Dictionary<string, (SKPaymentTransaction Transaction, StoreTransactionInfo Info)>
            _unfinished = new(StringComparer.Ordinal);

        // Transactions handed to a purchase the user is waiting for; the
        // background verifier leaves them alone until they are released.
        private readonly HashSet<string> _claimed =
            new(StringComparer.Ordinal);

        private readonly List<(StoreTransactionInfo Info, double Date)> _restored = new();

        private string? _activeProductId;
        private TaskCompletionSource<string>?
            _purchaseCompletion;
        private TaskCompletionSource<IReadOnlyList<StoreTransactionInfo>>?
            _restoreCompletion;

        public PaymentObserver(Action unclaimedArrived)
        {
            _unclaimedArrived = unclaimedArrived;
        }

        public Task<string> BeginPurchase(string productId)
        {
            var completion =
                new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _activeProductId = productId;
                _purchaseCompletion = completion;
            }
            return completion.Task;
        }

        public Task<IReadOnlyList<StoreTransactionInfo>> BeginRestore()
        {
            var completion =
                new TaskCompletionSource<IReadOnlyList<StoreTransactionInfo>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _restored.Clear();
                _restoreCompletion = completion;
            }
            return completion.Task;
        }

        public void CancelPurchase()
        {
            _purchaseCompletion?.TrySetResult(
                "ERROR:CANCELLED_BY_APP");
        }

        public void CancelRestore()
        {
            _restoreCompletion?.TrySetResult(
                Array.Empty<StoreTransactionInfo>());
        }

        public StoreTransactionInfo? Find(string transactionId)
        {
            lock (_gate)
            {
                return _unfinished.TryGetValue(transactionId, out var entry)
                    ? entry.Info
                    : null;
            }
        }

        public List<StoreTransactionInfo> UnclaimedTransactions()
        {
            lock (_gate)
            {
                return _unfinished.Values
                    .Where(entry => !_claimed.Contains(entry.Info.TransactionId))
                    .Select(entry => entry.Info)
                    .ToList();
            }
        }

        public void Release(string transactionId)
        {
            lock (_gate)
            {
                _claimed.Remove(transactionId);
            }
        }

        public void Finish(string transactionId)
        {
            SKPaymentTransaction? transaction = null;
            lock (_gate)
            {
                if (_unfinished.TryGetValue(transactionId, out var entry))
                {
                    transaction = entry.Transaction;
                    _unfinished.Remove(transactionId);
                }
                _claimed.Remove(transactionId);
            }

            if (transaction is null)
                return;

            try
            {
                SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FinishTransaction failed: {ex.GetType().Name}");
            }
        }

        private bool IsActivePurchase(string productId)
        {
            var completion = _purchaseCompletion;
            return completion is not null &&
                   !completion.Task.IsCompleted &&
                   string.Equals(
                       productId,
                       _activeProductId,
                       StringComparison.Ordinal);
        }

        public override void UpdatedTransactions(
            SKPaymentQueue queue,
            SKPaymentTransaction[] transactions)
        {
            var unclaimedArrived = false;

            foreach (var transaction in transactions)
            {
                var productId =
                    transaction.Payment?.ProductIdentifier ?? string.Empty;

                switch (transaction.TransactionState)
                {
                    case SKPaymentTransactionState.Purchasing:
                        break;

                    case SKPaymentTransactionState.Deferred:
                    {
                        // Ask to Buy: waits for a parent's approval. Not finished;
                        // StoreKit later delivers Purchased (verified in the
                        // background) or Failed.
                        if (IsActivePurchase(productId))
                            _purchaseCompletion?.TrySetResult("PENDING");
                        break;
                    }

                    case SKPaymentTransactionState.Purchased:
                    {
                        var id = transaction.TransactionIdentifier;
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            if (IsActivePurchase(productId))
                                _purchaseCompletion?.TrySetResult("ERROR:EMPTY_TRANSACTION_ID");
                            break;
                        }

                        var info = new StoreTransactionInfo(
                            id,
                            transaction.OriginalTransaction?.TransactionIdentifier,
                            productId);

                        var claimed = false;
                        lock (_gate)
                        {
                            _unfinished[id] = (transaction, info);

                            if (IsActivePurchase(productId))
                            {
                                // Claim first, so the background verifier never
                                // races the purchase the user is waiting for.
                                _claimed.Add(id);
                                claimed = _purchaseCompletion?.TrySetResult(id) == true;
                                if (!claimed)
                                    _claimed.Remove(id);
                            }
                        }

                        // NOT finished here: only after the server accepted it.
                        if (!claimed)
                            unclaimedArrived = true;
                        break;
                    }

                    case SKPaymentTransactionState.Restored:
                    {
                        var id = transaction.TransactionIdentifier;
                        if (!string.IsNullOrWhiteSpace(id) &&
                            !string.IsNullOrWhiteSpace(productId))
                        {
                            var date =
                                transaction.TransactionDate?.SecondsSinceReferenceDate ?? 0;
                            lock (_gate)
                            {
                                _restored.Add((
                                    new StoreTransactionInfo(
                                        id,
                                        transaction.OriginalTransaction?.TransactionIdentifier,
                                        productId),
                                    date));
                            }
                        }

                        // A restored transaction is a copy; the entitlement is
                        // kept on the server, so it is finished right away.
                        queue.FinishTransaction(transaction);
                        break;
                    }

                    case SKPaymentTransactionState.Failed:
                    {
                        // SKErrorPaymentCancelled = 2
                        var cancelled =
                            transaction.Error?.Code == 2;
                        if (IsActivePurchase(productId))
                        {
                            _purchaseCompletion?.TrySetResult(
                                cancelled
                                    ? "USER_CANCELED"
                                    : "ERROR:PURCHASE_FAILED");
                        }
                        queue.FinishTransaction(transaction);
                        break;
                    }
                }
            }

            if (unclaimedArrived)
                _unclaimedArrived();
        }

        public override void RestoreCompletedTransactionsFinished(
            SKPaymentQueue queue)
        {
            List<StoreTransactionInfo> result;
            lock (_gate)
            {
                // Every renewal is restored separately: keep the newest one per
                // subscription (original transaction).
                result = _restored
                    .GroupBy(
                        item => item.Info.OriginalTransactionId ?? item.Info.TransactionId,
                        StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(item => item.Date).First().Info)
                    .ToList();
                _restored.Clear();
            }

            _restoreCompletion?.TrySetResult(result);
        }

        public override void RestoreCompletedTransactionsFailedWithError(
            SKPaymentQueue queue,
            NSError error)
        {
            lock (_gate)
            {
                _restored.Clear();
            }

            _restoreCompletion?.TrySetResult(
                Array.Empty<StoreTransactionInfo>());
        }
    }
}

#pragma warning restore CA1422

#endif
