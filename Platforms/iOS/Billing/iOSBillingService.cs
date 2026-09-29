#if IOS

using Foundation;
using StoreKit;
using CarScanner.Services;

namespace CarScanner.Platforms.iOS.Billing;

#pragma warning disable CA1422

/// <summary>
/// StoreKit transaction bridge. It only returns an Apple transaction ID;
/// the secure backend is solely responsible for granting PRO entitlement.
/// </summary>
public sealed class iOSBillingService : IDisposable
{
    private const string ProductId =
        ProService.ProductId;

    private readonly PaymentObserver _observer;
    private readonly SemaphoreSlim _operationGate =
        new(1, 1);

    private bool _disposed;

    public bool IsReady =>
        !_disposed && SKPaymentQueue.CanMakePayments;

    public iOSBillingService()
    {
        _observer = new PaymentObserver();
        SKPaymentQueue.DefaultQueue
            .AddTransactionObserver(_observer);
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

    public async Task<string> PurchaseProductAsync(
        string productId,
        CancellationToken cancellationToken = default)
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

            SKPaymentQueue.DefaultQueue.AddPayment(
                SKPayment.CreateFrom(product));

            return await resultTask;
        }
        catch (OperationCanceledException)
        {
            return "ERROR:CANCELLED_BY_APP";
        }
        catch (Exception ex)
        {
            return $"ERROR:{ex.Message}";
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<string?> RestoreProAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
            return null;

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
            return null;
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
        private string? _activeProductId;
        private TaskCompletionSource<string>?
            _purchaseCompletion;
        private TaskCompletionSource<string?>?
            _restoreCompletion;
        private string? _restoredTransactionId;

        public Task<string> BeginPurchase(string productId)
        {
            _activeProductId = productId;
            _purchaseCompletion =
                new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            return _purchaseCompletion.Task;
        }

        public Task<string?> BeginRestore()
        {
            _restoredTransactionId = null;
            _restoreCompletion =
                new TaskCompletionSource<string?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            return _restoreCompletion.Task;
        }

        public void CancelPurchase()
        {
            _purchaseCompletion?.TrySetResult(
                "ERROR:CANCELLED_BY_APP");
        }

        public void CancelRestore()
        {
            _restoreCompletion?.TrySetResult(null);
        }

        public override void UpdatedTransactions(
            SKPaymentQueue queue,
            SKPaymentTransaction[] transactions)
        {
            foreach (var transaction in transactions)
            {
                var productId = transaction.Payment.ProductIdentifier;
                var isActivePurchase = string.Equals(
                    productId,
                    _activeProductId,
                    StringComparison.Ordinal);
                var isRestoringKnownSubscription =
                    _restoreCompletion is not null &&
                    PurchaseService.SubscriptionProductIds.Contains(productId);

                if (!isActivePurchase && !isRestoringKnownSubscription)
                {
                    continue;
                }

                switch (transaction.TransactionState)
                {
                    case SKPaymentTransactionState.Purchased:
                    {
                        var id = transaction.TransactionIdentifier;
                        _purchaseCompletion?.TrySetResult(
                            string.IsNullOrWhiteSpace(id)
                                ? "ERROR:EMPTY_TRANSACTION_ID"
                                : id);
                        queue.FinishTransaction(transaction);
                        break;
                    }

                    case SKPaymentTransactionState.Restored:
                    {
                        var id =
                            transaction.OriginalTransaction?
                                .TransactionIdentifier
                            ?? transaction.TransactionIdentifier;
                        if (!string.IsNullOrWhiteSpace(id))
                            _restoredTransactionId = id;
                        queue.FinishTransaction(transaction);
                        break;
                    }

                    case SKPaymentTransactionState.Failed:
                    {
                        var cancelled =
                            transaction.Error?.Code == 2;
                        _purchaseCompletion?.TrySetResult(
                            cancelled
                                ? "USER_CANCELED"
                                : "ERROR:" +
                                  (transaction.Error?
                                       .LocalizedDescription
                                   ?? "PURCHASE_FAILED"));
                        queue.FinishTransaction(transaction);
                        break;
                    }
                }
            }
        }

        public override void RestoreCompletedTransactionsFinished(
            SKPaymentQueue queue)
        {
            _restoreCompletion?.TrySetResult(
                _restoredTransactionId);
        }

        public override void RestoreCompletedTransactionsFailedWithError(
            SKPaymentQueue queue,
            NSError error)
        {
            _restoreCompletion?.TrySetResult(null);
        }
    }
}

#pragma warning restore CA1422

#endif
