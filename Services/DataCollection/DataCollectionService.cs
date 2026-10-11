using System.Net.Http.Json;
using CarScanner.Services.OBD;

namespace CarScanner.Services.DataCollection;

/// <summary>
/// Optional, opt-in collection of diagnostic compatibility facts. This class is only the platform adapter
/// (consent flag, vehicle, HTTP); all logic lives in <see cref="CollectionPipeline"/>.
///
///  • Off by default. Nothing is stored or sent until the user turns it on; turning it off deletes the local
///    queue and asks the server to delete what this device sent.
///  • Observes only (see <see cref="ObdExchangeTap"/>): never sends a request of its own.
///  • No VIN, no account token, no name. OEM module answers are sanitised raw; standard OBD2 requests are reduced
///    to summaries (which PIDs / DTC modes answered, the support bitmap, DTC codes), never measured values.
///  • Not fully anonymous: the upload carries a device header that the server turns into a salted hash so that
///    independent devices can be counted. That hash is stable per device, so the data is pseudonymous.
/// </summary>
public sealed class DataCollectionService : IDisposable
{
    /// <summary>The one consent switch (also the "granted" flag of <see cref="Consent"/>).</summary>
    public const string ConsentKey = ConsentPolicy.GrantedKey;

    private readonly VehicleInfoService _vehicle;
    private readonly AccountService _account;
    private readonly CollectionQueue _queue;
    private readonly CollectionPipeline _pipeline;
    private bool _subscribed;

    public DataCollectionService(VehicleInfoService vehicle, AccountService account)
    {
        _vehicle = vehicle;
        _account = account;
        Consent = new ConsentPolicy(new PreferencesConsentStorage());
        _queue = new CollectionQueue(Path.Combine(FileSystem.AppDataDirectory, "ecu_collection_queue.json"));
        _pipeline = new CollectionPipeline(
            _queue,
            () => IsEnabled,
            CurrentVehicle,
            new HttpUploader(account));

        if (IsEnabled)
            Subscribe();

        if (IsEnabled || _queue.DeletePending)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                await _pipeline.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            });
        }
    }

    /// <summary>The consent decision and its timestamps (granted, declined, withdrawn).</summary>
    public ConsentPolicy Consent { get; }

    public bool IsEnabled => Consent.IsGranted;

    public int PendingCount => _queue.Count;

    private VehicleSnapshot? CurrentVehicle()
    {
        var vehicle = _vehicle.CurrentVehicle;
        return vehicle is null ? null : new VehicleSnapshot(vehicle.Manufacturer, vehicle.Model, vehicle.Year, vehicle.Protocol);
    }

    /// <summary>Turns collection on or off. Turning it off always removes the queued data and the server copy.</summary>
    public async Task<bool> SetEnabledAsync(bool enabled, CancellationToken token = default)
    {
        if (enabled)
        {
            Consent.Grant();
            Subscribe();
            return true;
        }

        // Withdrawal stops observing first, then clears the queue and asks the server to delete this device's data.
        Consent.Revoke();
        Unsubscribe();
        return await _pipeline.DisableAsync(token).ConfigureAwait(false);
    }

    public Task<int> FlushAsync(CancellationToken token) => _pipeline.FlushAsync(token);

    private void Subscribe()
    {
        if (_subscribed)
            return;
        ObdExchangeTap.Observed += _pipeline.Observe;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        ObdExchangeTap.Observed -= _pipeline.Observe;
        _subscribed = false;
    }

    public void Dispose()
    {
        Unsubscribe();
        _pipeline.Dispose();
    }

    private sealed class HttpUploader(AccountService account) : ICollectionUploader
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<bool> UploadAsync(IReadOnlyList<QueuedExchange> batch, CancellationToken token)
        {
            using var request = await CreateRequestAsync(HttpMethod.Post, "/api/ecu-observations").ConfigureAwait(false);
            request.Content = JsonContent.Create(CollectionPayload.Build(batch, AppInfo.Current.VersionString));

            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteMineAsync(CancellationToken token)
        {
            using var request = await CreateRequestAsync(HttpMethod.Delete, "/api/ecu-observations/mine").ConfigureAwait(false);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>The device header only; the account token is never attached.</summary>
        private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path)
        {
            if (!Uri.TryCreate(AIConfig.BackendBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Secure backend is unavailable.");

            var request = new HttpRequestMessage(method, new Uri(baseUri, path));
            request.Headers.TryAddWithoutValidation("X-GCS-Device-Id", await account.GetDeviceIdAsync().ConfigureAwait(false));
            return request;
        }
    }
}
