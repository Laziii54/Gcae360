using System.Threading.Channels;
using CarScanner.Services.OBD;

namespace CarScanner.Services.DataCollection;

/// <summary>The known vehicle an observation belongs to (no VIN).</summary>
public sealed record VehicleSnapshot(string? Make, string? Model, string? Year, string? Protocol);

public interface ICollectionUploader
{
    /// <summary>True when the server accepted the batch. May throw; the pipeline treats that as a failed attempt.</summary>
    Task<bool> UploadAsync(IReadOnlyList<QueuedExchange> batch, CancellationToken token);

    /// <summary>Asks the server to delete everything this device contributed.</summary>
    Task<bool> DeleteMineAsync(CancellationToken token);
}

/// <summary>
/// Observation → queue → upload, independent of the UI platform. Observing is O(1) and never blocks the caller:
/// exchanges are handed to a bounded channel and processed on a background task. Upload failures back off and
/// are retried later; they never reach the diagnostic code. Consent is checked on every step.
/// </summary>
public sealed class CollectionPipeline : IDisposable
{
    public const int BatchSize = 100;
    public const int FlushThreshold = 25;

    private readonly CollectionQueue _queue;
    private readonly Func<bool> _isEnabled;
    private readonly Func<VehicleSnapshot?> _vehicle;
    private readonly ICollectionUploader _uploader;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Channel<(ObdRawExchange Exchange, VehicleSnapshot Vehicle, int Epoch)> _channel =
        Channel.CreateBounded<(ObdRawExchange, VehicleSnapshot, int)>(
            new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _consumer;
    private int _epoch;
    private int _outstanding;
    private int _failures;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;

    public CollectionPipeline(CollectionQueue queue, Func<bool> isEnabled, Func<VehicleSnapshot?> vehicle,
        ICollectionUploader uploader, Func<DateTimeOffset>? clock = null)
    {
        _queue = queue;
        _isEnabled = isEnabled;
        _vehicle = vehicle;
        _uploader = uploader;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _consumer = Task.Run(ConsumeAsync);
    }

    public int PendingCount => _queue.Count;
    public int ConsecutiveFailures => _failures;
    public DateTimeOffset NextAttempt => _nextAttempt;

    /// <summary>Hand-over from the tap. Cheap, non-blocking, never throws.</summary>
    public void Observe(ObdRawExchange exchange)
    {
        try
        {
            if (exchange is null || !_isEnabled())
                return;

            // Standard OBD2 only counts once the vehicle is identified: the start-up probe must not create records.
            var vehicle = _vehicle();
            if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.Make) || string.IsNullOrWhiteSpace(vehicle.Model))
                return;

            if (exchange.Origin == ObdExchangeOrigin.StandardObd && !ObdExchangeTap.IsObservableStandardRequest(exchange.RequestHex))
                return;

            Interlocked.Increment(ref _outstanding);
            if (!_channel.Writer.TryWrite((exchange, vehicle, Volatile.Read(ref _epoch))))
                Interlocked.Decrement(ref _outstanding);
        }
        catch
        {
            // never affects diagnostics
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var (exchange, vehicle, epoch) in _channel.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    if (epoch == Volatile.Read(ref _epoch) && _isEnabled())
                    {
                        CollectionIntake.TryAdd(_queue, exchange, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.Protocol, _clock());
                        if (_queue.Count >= FlushThreshold && _clock() >= _nextAttempt)
                            _ = Task.Run(() => FlushAsync(CancellationToken.None));
                    }
                }
                catch
                {
                    // one bad item must not stop the consumer
                }
                finally
                {
                    Interlocked.Decrement(ref _outstanding);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Waits until everything observed so far has been processed (used by tests and on shutdown).</summary>
    public async Task DrainAsync(TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (Volatile.Read(ref _outstanding) > 0 && DateTime.UtcNow < until)
            await Task.Delay(5).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads queued observations. Returns the number sent. A failure (offline, server error, exception) keeps
    /// the queue, increases the back-off (30 s, 60 s, … up to 1 h) and returns; <paramref name="force"/> ignores
    /// the back-off (for a manual retry).
    /// </summary>
    public async Task<int> FlushAsync(CancellationToken token, bool force = false)
    {
        if (!await _flushGate.WaitAsync(0, token).ConfigureAwait(false))
            return 0;

        var sent = 0;
        try
        {
            if (!force && _clock() < _nextAttempt)
                return 0;

            // A deletion the server has not confirmed yet comes first.
            if (_queue.DeletePending)
            {
                if (!await TryAsync(() => _uploader.DeleteMineAsync(token)).ConfigureAwait(false))
                    return 0;
                _queue.DeletePending = false;
            }

            while (_isEnabled())
            {
                var batch = _queue.Peek(BatchSize);
                if (batch.Count == 0)
                    break;

                if (!await TryAsync(() => _uploader.UploadAsync(batch, token)).ConfigureAwait(false))
                    return sent;

                // Consent may have been withdrawn while the request was in flight: then nothing is remembered.
                if (_isEnabled())
                    _queue.Remove(batch);
                sent += batch.Count;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            RegisterFailure();
        }
        finally
        {
            _flushGate.Release();
        }

        return sent;
    }

    private async Task<bool> TryAsync(Func<Task<bool>> action)
    {
        try
        {
            if (await action().ConfigureAwait(false))
            {
                _failures = 0;
                _nextAttempt = DateTimeOffset.MinValue;
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        RegisterFailure();
        return false;
    }

    private void RegisterFailure()
    {
        _failures++;
        var seconds = Math.Min(3600, 30 * Math.Pow(2, Math.Min(_failures - 1, 7)));
        _nextAttempt = _clock().AddSeconds(seconds);
    }

    /// <summary>
    /// Consent withdrawn: stop accepting, drop everything queued (and the already-sent memory), then ask the server
    /// to delete this device's contributions. If the server cannot be reached the deletion stays pending and is
    /// retried by <see cref="FlushAsync"/>. Returns true when the server confirmed.
    /// </summary>
    public async Task<bool> DisableAsync(CancellationToken token)
    {
        Interlocked.Increment(ref _epoch);
        _queue.DeletePending = true;
        _queue.Clear();

        var confirmed = false;
        try
        {
            confirmed = await _uploader.DeleteMineAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        // Anything the consumer was still holding is discarded too.
        await DrainAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        _queue.Clear();

        if (confirmed)
            _queue.DeletePending = false;
        else
            RegisterFailure();
        return confirmed;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _channel.Writer.TryComplete();
        _flushGate.Dispose();
    }
}
