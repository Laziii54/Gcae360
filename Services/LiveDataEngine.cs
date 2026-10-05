using CarScanner.Models;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services;

public sealed class LiveDataEngine : IAsyncDisposable
{
    private static readonly HashSet<int> CorePids = new() { 0x0C, 0x0D, 0x05, 0x04, 0x11, 0x42 };
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan SlowPollInterval = TimeSpan.FromMilliseconds(1800);

    // How many non-priority PIDs get one read per loop tick, in addition to
    // the 6 core dashboard signals and any dashboard/focused/fuel PIDs. This
    // is what makes the All Sensors screen eventually fill in every
    // ECU-advertised value instead of leaving most of them at "—" forever.
    private const int RotationBatchSize = 6;

    private readonly OBDService _obd;
    private readonly ILogger<LiveDataEngine> _logger;
    private readonly IBluetoothService _bluetooth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _dashboardGate = new();
    private int[] _dashboardExtras = Array.Empty<int>();
    private int? _focusedPid;
    private bool _performanceMode;
    private bool _performanceSpeedOnly;
    private bool _fuelAnalyticsEnabled;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private bool _disposed;

    // Round-robin cursor over every PID the ECU has advertised as supported.
    // Rebuilt whenever the supported-PID set changes (e.g. after a fresh
    // DiscoverSupportedPidsAsync following a new connection).
    private int[] _rotationPids = Array.Empty<int>();
    private int _rotationCursor;
    private int _lastKnownSupportedCount = -1;

    public bool IsRunning { get; private set; }
    public LiveDataModel? CurrentData { get; private set; }
    public double DataRate { get; private set; }
    public int LastSuccessfulPidCount { get; private set; }
    public event EventHandler<LiveDataModel>? LiveDataUpdated;
    public event EventHandler<IReadOnlyList<SensorValue>>? LiveValuesUpdated;
    public LiveDataEngine(OBDService obd, IBluetoothService bluetooth, ILogger<LiveDataEngine> logger) { _obd = obd; _bluetooth = bluetooth; _logger = logger; _bluetooth.ConnectionChanged += OnConnectionChanged; }
    private void OnConnectionChanged(object? sender, bool connected) { if (!connected) _cts?.Cancel(); }

    /// <summary>
    /// Adds up to three non-core PIDs to the live polling loop. Core speed,
    /// RPM and health signals remain available for the main dashboard and
    /// acceleration measurement.
    /// </summary>
    public void SetDashboardPids(IEnumerable<int> pids)
    {
        lock (_dashboardGate)
        {
            _dashboardExtras = pids
                .Where(pid => !CorePids.Contains(pid))
                .Distinct()
                .Take(3)
                .ToArray();
        }
    }

    /// <summary>
    /// Keeps the PID currently opened by the user in the polling loop without
    /// replacing the dashboard configuration. The OBD service still filters
    /// it against the ECU's advertised support before sending any command.
    /// </summary>
    public void SetFocusedPid(int? pid)
    {
        lock (_dashboardGate)
            _focusedPid = pid;
    }

    /// <summary>
    /// Performance Lab temporarily uses a minimal, speed-first OBD polling
    /// profile. This prevents optional dashboard PIDs from delaying PID 01 0D.
    /// </summary>
    public void SetPerformanceMode(bool enabled, bool speedOnly = false)
    {
        lock (_dashboardGate)
        {
            _performanceMode = enabled;
            _performanceSpeedOnly = enabled && speedOnly;
        }
    }

    /// <summary>Enables the few standard PIDs needed by Fuel Analytics only while that page is active.</summary>
    public void SetFuelAnalyticsEnabled(bool enabled)
    {
        lock (_dashboardGate)
            _fuelAnalyticsEnabled = enabled;
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LiveDataEngine));
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(token).ConfigureAwait(false);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            IsRunning = true;
            _task = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning) return;
            _cts?.Cancel();
            if (_task != null) try { await _task.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _cts?.Dispose(); _cts = null; _task = null; IsRunning = false;
        }
        finally { _gate.Release(); }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                try
                {
                    bool performanceMode;
                    bool performanceSpeedOnly;
                    bool fuelAnalyticsEnabled;
                    int[] extras;
                    int? focusedPid;
                    lock (_dashboardGate)
                    {
                        performanceMode = _performanceMode;
                        performanceSpeedOnly = _performanceSpeedOnly;
                        fuelAnalyticsEnabled = _fuelAnalyticsEnabled;
                        extras = _dashboardExtras;
                        focusedPid = _focusedPid;
                    }

                    var data = performanceMode
                        ? performanceSpeedOnly
                            ? await _obd.GetPerformanceSpeedDataAsync(token).ConfigureAwait(false)
                            : await _obd.GetPerformanceDataAsync(token).ConfigureAwait(false)
                        : await _obd.GetLiveDataAsync(token).ConfigureAwait(false);
                    var values = ToSensorValues(data).ToList();

                    var fuelPids = fuelAnalyticsEnabled && !performanceMode
                        ? GetFuelAnalyticsPids()
                        : Array.Empty<int>();
                    var priorityPids = performanceMode
                        ? Array.Empty<int>()
                        : focusedPid.HasValue
                            ? fuelPids.Concat(extras).Append(focusedPid.Value).Distinct().Take(4).ToArray()
                            : fuelPids.Concat(extras).Distinct().Take(4).ToArray();

                    // Round-robin batch: covers every ECU-advertised PID over
                    // successive ticks instead of only ever reading the
                    // priority set above. Skipped in performance mode so the
                    // 0-60 measurement loop stays speed-first.
                    var rotationBatch = performanceMode
                        ? Array.Empty<int>()
                        : NextRotationBatch(priorityPids);

                    var requestedPids = priorityPids
                        .Concat(rotationBatch)
                        .Distinct()
                        .ToArray();

                    if (requestedPids.Length > 0)
                    {
                        var extraValues = await _obd.ReadLiveValuesAsync(requestedPids, token).ConfigureAwait(false);
                        values.AddRange(extraValues.Where(x => values.All(current => current.Pid != x.Pid)));
                    }

                    CurrentData = data;
                    LastSuccessfulPidCount = values.Count;
                    DataRate = values.Count / Math.Max((DateTime.UtcNow - started).TotalSeconds, 0.001);
                    LiveDataUpdated?.Invoke(this, data);
                    LiveValuesUpdated?.Invoke(this, values);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogDebug(ex, "Live data poll failed; retrying."); }
                var elapsed = DateTime.UtcNow - started;
                bool isPerformancePolling;
                bool isSpeedOnlyPolling;
                lock (_dashboardGate)
                {
                    isPerformancePolling = _performanceMode;
                    isSpeedOnlyPolling = _performanceSpeedOnly;
                }

                // Performance modes poll back to back: the adapter round trip
                // itself is the rate limit (speed is read on every tick).
                var performanceFloor = TimeSpan.FromMilliseconds(10);

                var profile = Preferences.Default.Get("polling", "Balanced");
                var fast = isPerformancePolling
                    ? performanceFloor
                    : profile == "Fast" ? TimeSpan.FromMilliseconds(650) : profile == "Low power" ? TimeSpan.FromMilliseconds(1600) : FastPollInterval;
                var slow = isPerformancePolling
                    ? performanceFloor
                    : profile == "Fast" ? TimeSpan.FromMilliseconds(1100) : profile == "Low power" ? TimeSpan.FromMilliseconds(2600) : SlowPollInterval;
                var target = elapsed > fast ? slow : fast;
                var delay = target - elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally { IsRunning = false; }
    }

    /// <summary>
    /// Returns the next slice of non-priority, ECU-supported PIDs to poll
    /// this tick, cycling through the full supported set over time. Rebuilds
    /// the rotation list whenever the supported-PID count changes (new
    /// connection / fresh discovery), and always excludes whatever is already
    /// being read as a priority PID this tick so no command is duplicated.
    /// </summary>
    private int[] NextRotationBatch(IReadOnlyCollection<int> priorityPids)
    {
        var supported = _obd.SupportedPids;
        if (supported.Count != _lastKnownSupportedCount)
        {
            _rotationPids = supported
                .Select(p => p.Pid)
                .Where(pid => !CorePids.Contains(pid))
                .OrderBy(pid => pid)
                .ToArray();
            _rotationCursor = 0;
            _lastKnownSupportedCount = supported.Count;
        }

        if (_rotationPids.Length == 0)
            return Array.Empty<int>();

        var batch = new List<int>(RotationBatchSize);
        var scanned = 0;
        // Bounded by 2x the full list length so a rotation list shorter than
        // the batch size, or one made entirely of priority PIDs, cannot spin
        // forever inside this loop.
        var guard = _rotationPids.Length * 2;

        while (batch.Count < RotationBatchSize && scanned < guard)
        {
            var pid = _rotationPids[_rotationCursor];
            _rotationCursor = (_rotationCursor + 1) % _rotationPids.Length;
            scanned++;

            if (!priorityPids.Contains(pid))
                batch.Add(pid);
        }

        return batch.ToArray();
    }

    private int[] GetFuelAnalyticsPids()
    {
        // Use the direct fuel-rate PID first. MAF remains a labelled fallback.
        if (_obd.IsPidSupported(0x5E))
            return [0x5E, 0x2F, 0x51];
        return [0x10, 0x2F, 0x51];
    }

    private static IEnumerable<SensorValue> ToSensorValues(LiveDataModel data)
    {
        if (data.HasRpm) yield return MakeValue(0x0C, "Engine RPM", "rpm", "ძრავა", data.Rpm);
        if (data.HasSpeed) yield return MakeValue(0x0D, "Vehicle speed", "km/h", "ძრავა", data.Speed);
        if (data.HasCoolantTemp) yield return MakeValue(0x05, "Engine coolant temperature", "°C", "ძრავა", data.CoolantTemp);
        if (data.HasEngineLoad) yield return MakeValue(0x04, "Calculated engine load", "%", "ძრავა", data.EngineLoad);
        if (data.HasThrottlePosition) yield return MakeValue(0x11, "Throttle position", "%", "ძრავა", data.ThrottlePosition);
        if (data.HasVoltage) yield return MakeValue(0x42, "Control module voltage", "V", "ელექტრო", data.Voltage);
    }

    private static SensorValue MakeValue(int pid, string name, string unit, string category, double value) => new()
    {
        Pid = pid,
        Name = name,
        Unit = unit,
        Category = category,
        Value = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
    };

    public async ValueTask DisposeAsync() { if (_disposed) return; _disposed = true; await StopAsync().ConfigureAwait(false); _gate.Dispose(); }
}
