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

        var product = await LoadProductAsync(productId, cancellationToken);
        if (product == null)
            return null;

        using var formatter = new NSNumberFormatter
        {
            NumberStyle = NSNumberFormatterStyle.Currency,
            Locale = product.PriceLocale
        };

        return formatter.StringFromNumber(product.Price);
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
            var product =
                await LoadProductAsync(productId, cancellationToken);

            if (product == null)
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

    private static async Task<SKProduct?> LoadProductAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        var completion =
            new TaskCompletionSource<SKProduct?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        using var productIdentifiers =
            NSSet.MakeNSObjectSet(
                new[] { new NSString(productId) });

        using var request =
            new SKProductsRequest(productIdentifiers);

        using var requestDelegate =
            new ProductRequestDelegate(
                productId,
                completion);

        request.Delegate = requestDelegate;

        using var registration =
            cancellationToken.Register(
                () =>
                {
                    request.Cancel();
                    completion.TrySetCanceled(
                        cancellationToken);
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
    }

    private sealed class ProductRequestDelegate :
        NSObject,
        ISKProductsRequestDelegate,
        ISKRequestDelegate
    {
        private readonly string _productId;
        private readonly TaskCompletionSource<SKProduct?>
            _completion;

        public ProductRequestDelegate(
            string productId,
            TaskCompletionSource<SKProduct?> completion)
        {
            _productId = productId;
            _completion = completion;
        }

        [Export("productsRequest:didReceiveResponse:")]
        public void ReceivedResponse(
            SKProductsRequest request,
            SKProductsResponse response)
        {
            var product =
                response.Products.FirstOrDefault(
                    item => string.Equals(
                        item.ProductIdentifier,
                        _productId,
                        StringComparison.Ordinal));

            _completion.TrySetResult(product);
        }

        [Export("request:didFailWithError:")]
        public void RequestFailed(
            SKRequest request,
            NSError error)
        {
            _completion.TrySetException(
                new InvalidOperationException(
                    error.LocalizedDescription));
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
