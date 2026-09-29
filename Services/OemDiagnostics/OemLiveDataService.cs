using System.Diagnostics;
using CarScanner.Models;
using CarScanner.Services.UDS;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Polls reviewed OEM UDS DIDs while generic polling is paused by the UI.
/// Commands are rotated in bounded batches so low-cost adapters are not
/// flooded. The generic ELM session is restored when polling stops.
/// </summary>
public sealed class OemLiveDataService : IAsyncDisposable
{
    private const int MaximumCommandsPerCycle = 6;
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly ManufacturerDefinitionCatalogService _manufacturers;
    private readonly OemDefinitionCatalogService _definitions;
    private readonly OemAddressRegistry _addresses;
    private readonly OemModuleDiscoveryService _discovery;
    private readonly UdsProtocol _uds;
    private readonly OBDService _obd;
    private readonly IBluetoothService _bluetooth;
    private readonly ILogger<OemLiveDataService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private OemLiveTarget[] _targets = Array.Empty<OemLiveTarget>();
    private int _rotationIndex;
    private bool _disposed;

    public bool IsRunning { get; private set; }
    public OemLiveDataReadResult? Current { get; private set; }
    public event EventHandler<OemLiveDataReadResult>? ValuesUpdated;

    public OemLiveDataService(
        VehicleProfileResolverService vehicleProfiles,
        ManufacturerDefinitionCatalogService manufacturers,
        OemDefinitionCatalogService definitions,
        OemAddressRegistry addresses,
        OemModuleDiscoveryService discovery,
        UdsProtocol uds,
        OBDService obd,
        IBluetoothService bluetooth,
        ILogger<OemLiveDataService> logger)
    {
        _vehicleProfiles = vehicleProfiles;
        _manufacturers = manufacturers;
        _definitions = definitions;
        _addresses = addresses;
        _discovery = discovery;
        _uds = uds;
        _obd = obd;
        _bluetooth = bluetooth;
        _logger = logger;
        _bluetooth.ConnectionChanged += OnConnectionChanged;
    }

    public async Task<OemLiveDataReadResult> StartAsync(
        VehicleInfo vehicle,
        CancellationToken token = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OemLiveDataService));
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                return Current ?? OemLiveDataReadResult.Unavailable("OEM live data is starting.");
            if (!_bluetooth.IsConnected)
                return Publish(OemLiveDataReadResult.Unavailable("OBD adapter არ არის დაკავშირებული."));

            var setup = await BuildTargetsAsync(vehicle, token).ConfigureAwait(false);
            if (setup.Targets.Length == 0)
                return Publish(OemLiveDataReadResult.Unavailable(setup.Detail));

            _targets = setup.Targets;
            _rotationIndex = 0;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            IsRunning = true;
            var started = new OemLiveDataReadResult(
                Array.Empty<OemLiveCommandResult>(),
                "LOADING",
                $"{_targets.Length} reviewed OEM DID მზადაა რეალურ დროში წასაკითხად.",
                DateTimeOffset.UtcNow,
                0);
            Publish(started);
            _loop = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);
            return started;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning) return;
            _cts?.Cancel();
            if (_loop is not null)
            {
                try { await _loop.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _cts?.Dispose();
            _cts = null;
            _loop = null;
            _targets = Array.Empty<OemLiveTarget>();
            IsRunning = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var stopwatch = Stopwatch.StartNew();
                var batch = NextBatch();
                var results = new List<OemLiveCommandResult>(batch.Length);
                foreach (var target in batch)
                {
                    token.ThrowIfCancellationRequested();
                    var request = OemDefinitionPackageParser.ParseReadOnlyRequest(target.Command.RequestHex);
                    UdsResponse response;
                    try
                    {
                        response = await _uds.SendIsoTpAsync(
                            request,
                            target.Address.TxCanId,
                            target.Address.RxCanId,
                            target.Address.Extended29Bit,
                            TimeSpan.FromMilliseconds(target.Command.TimeoutMs),
                            token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        response = new UdsResponse(
                            UdsResponseKind.Malformed,
                            request[0],
                            Array.Empty<byte>(),
                            Message: exception.Message);
                    }
                    results.Add(OemLiveDataResponseDecoder.Decode(target.Definition, target.Command, response));
                }

                stopwatch.Stop();
                var valueCount = results.Sum(result => result.Values.Count);
                var available = results.Count(result => result.State == OemLiveValueState.Available);
                var dataRate = valueCount / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                Publish(new OemLiveDataReadResult(
                    results,
                    available == results.Count ? "AVAILABLE" : available > 0 ? "PARTIAL" : "NOT AVAILABLE",
                    $"OEM Live Data: {available}/{results.Count} DID პასუხობს · {valueCount} მნიშვნელობა.",
                    DateTimeOffset.UtcNow,
                    dataRate));

                var profile = Preferences.Default.Get("polling", "Balanced");
                var interval = profile == "Fast"
                    ? TimeSpan.FromMilliseconds(700)
                    : profile == "Low power"
                        ? TimeSpan.FromMilliseconds(1800)
                        : TimeSpan.FromMilliseconds(1100);
                var delay = interval - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "OEM live-data polling stopped unexpectedly");
            Publish(OemLiveDataReadResult.Unavailable(
                string.IsNullOrWhiteSpace(exception.Message) ? "OEM live data stopped." : exception.Message));
        }
        finally
        {
            try
            {
                if (_bluetooth.IsConnected)
                    await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Could not restore generic OBD session after OEM live data");
            }
            IsRunning = false;
        }
    }

    private OemLiveTarget[] NextBatch()
    {
        if (_targets.Length <= MaximumCommandsPerCycle)
            return _targets;
        var batch = Enumerable.Range(0, MaximumCommandsPerCycle)
            .Select(offset => _targets[(_rotationIndex + offset) % _targets.Length])
            .ToArray();
        _rotationIndex = (_rotationIndex + MaximumCommandsPerCycle) % _targets.Length;
        return batch;
    }

    private async Task<(OemLiveTarget[] Targets, string Detail)> BuildTargetsAsync(
        VehicleInfo vehicle,
        CancellationToken token)
    {
        var resolved = _vehicleProfiles.Resolve(vehicle);
        if (!resolved.CanUseManufacturerSpecificDefinitions)
            return (Array.Empty<OemLiveTarget>(), resolved.VerificationMessage);

        await _definitions.EnsureLoadedAsync(token).ConfigureAwait(false);
        var manufacturer = _manufacturers.Resolve(resolved);
        var definitions = _definitions.GetForVehicle(resolved, manufacturer);
        if (definitions.Count == 0)
            return (Array.Empty<OemLiveTarget>(), "ამ ზუსტი ავტომობილისთვის reviewed OEM Live Data definition არ არსებობს.");

        var discovery = await _discovery.DiscoverAsync(vehicle, token).ConfigureAwait(false);
        var responding = discovery.Probes
            .Where(probe => probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited)
            .Select(probe => probe.DefinitionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addresses = _addresses.GetForVehicle(resolved)
            .ToDictionary(address => address.DefinitionId, StringComparer.OrdinalIgnoreCase);
        var targets = definitions
            .Where(definition => responding.Contains(definition.DefinitionId) && addresses.ContainsKey(definition.DefinitionId))
            .SelectMany(definition => definition.ReadOnlyCommands
                .Where(command => command.CapabilityCode.Equals("DATA.READ", StringComparison.OrdinalIgnoreCase))
                .Select(command => new OemLiveTarget(definition, command, addresses[definition.DefinitionId])))
            .OrderBy(target => target.Definition.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Command.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (targets, targets.Length == 0
            ? "Responding OEM ECU-სთვის reviewed DATA.READ command არ არსებობს."
            : string.Empty);
    }

    private OemLiveDataReadResult Publish(OemLiveDataReadResult result)
    {
        Current = result;
        ValuesUpdated?.Invoke(this, result);
        return result;
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        if (!connected) _cts?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _bluetooth.ConnectionChanged -= OnConnectionChanged;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private sealed record OemLiveTarget(
        OemEcuDefinition Definition,
        OemReadOnlyCommandDefinition Command,
        VerifiedEcuAddress Address);
}
