using CarScanner.Models;
using CarScanner.Services.OBD;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services;

public sealed class OBDService
{
    // These standard PIDs provide a useful, fast snapshot on almost every
    // OBD-II ECU.  Walking every advertised PID can take minutes on low-cost
    // ELM327 adapters because a single unsupported response may time out.
    private static readonly int[] SnapshotPids =
    {
        0x04, 0x05, 0x06, 0x07, 0x0B, 0x0C, 0x0D, 0x0F,
        0x10, 0x11, 0x1F, 0x2F, 0x42, 0x46, 0x51
    };
    private readonly Elm327Protocol _protocol;
    private readonly PIDDecoder _decoder;
    private readonly DTCDecoder _dtc;
    private readonly ILogger<OBDService> _logger;
    private readonly Dictionary<int, SupportedPid> _supported = new();
    private readonly SemaphoreSlim _pidGate = new(1, 1);
    // Initialization is a multi-command ELM sequence.  It must be atomic: a
    // background VIN lookup and a page opened immediately after connection
    // must never reset/configure the adapter at the same time.
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly IBluetoothService _bluetooth;
    private readonly List<ObdCommandLog> _commandLog = new();
    private readonly object _logGate = new();
    private AdapterCapabilitySnapshot? _lastAdapterCapabilitySnapshot;

    public bool IsInitialized => _protocol.IsInitialized;
    public ObdProtocol CurrentProtocol => _protocol.CurrentProtocol;
    public string CurrentProtocolDescription => _protocol.CurrentProtocolDescription ?? _protocol.CurrentProtocol.ToString();
    public IReadOnlyCollection<SupportedPid> SupportedPids => _supported.Values.OrderBy(x => x.Pid).ToArray();
    public IReadOnlyList<ObdCommandLog> CommandLog { get { lock (_logGate) return _commandLog.ToArray(); } }
    public AdapterCapabilitySnapshot? LastAdapterCapabilitySnapshot => _lastAdapterCapabilitySnapshot;

    public OBDService(Elm327Protocol protocol, PIDDecoder decoder, DTCDecoder dtc, IBluetoothService bluetooth, ILogger<OBDService> logger)
    { _protocol = protocol; _decoder = decoder; _dtc = dtc; _bluetooth = bluetooth; _logger = logger; _bluetooth.ConnectionChanged += OnConnectionChanged; }

    private void OnConnectionChanged(object? sender, bool connected) { if (!connected) { _supported.Clear(); _lastAdapterCapabilitySnapshot = null; lock (_logGate) _commandLog.Clear(); } }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        await _initializationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!_bluetooth.IsConnected) throw new InvalidOperationException("OBD2 ადაპტერი არ არის დაკავშირებული. ჯერ დააკავშირე Bluetooth adapter.");
            await _protocol.InitializeAsync(token).ConfigureAwait(false);
            await DiscoverSupportedPidsAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<IReadOnlyCollection<SupportedPid>> DiscoverSupportedPidsAsync(CancellationToken token = default)
    {
        await _pidGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!_bluetooth.IsConnected) throw new InvalidOperationException("OBD2 ადაპტერი არ არის დაკავშირებული.");
            if (_supported.Count > 0) return SupportedPids;
            // Standard Mode 01 support bitmaps. A response bit 0 indicates that the next 0x20 block is supported.
            // The app's generic dashboard only decodes standard PIDs through
            // 0x5F.  Probing every theoretical OBD-II bitmap can add many
            // seconds on low-cost ELM327 clones, so stop after the useful
            // generic range.
            foreach (var basePid in new[] { 0x00, 0x20, 0x40, 0x60, 0x80, 0xA0 })
            {
                var raw = await SendTrackedAsync($"01{basePid:X2}", token, TimeSpan.FromMilliseconds(900), 1, false).ConfigureAwait(false);
                var bytes = PIDDecoder.ParseHexBytes(raw);

                // Several modules can answer (e.g. engine + hybrid control on a Volt):
                // every advertised PID of every answering module is used.
                var bitmap = new byte[4];
                var found = false;
                for (var i = 0; i + 5 < bytes.Length; i++)
                {
                    if (bytes[i] != 0x41 || bytes[i + 1] != (byte)basePid)
                        continue;
                    for (var k = 0; k < 4; k++)
                        bitmap[k] |= bytes[i + 2 + k];
                    found = true;
                    i += 5;
                }
                if (!found) break;
                for (var bit = 0; bit < 32; bit++)
                {
                    if ((bitmap[bit / 8] & (1 << (7 - (bit % 8)))) == 0) continue;
                    var pid = basePid + bit + 1; var def = PidRegistry.Get(pid); if (def != null && PIDDecoder.CanDecode(pid)) _supported[pid] = def;
                }
                if ((bitmap[3] & 0x01) == 0) break;
            }
            _logger.LogInformation("Supported PID discovery complete: {Count}", _supported.Count);
            return SupportedPids;
        }
        finally { _pidGate.Release(); }
    }

    public bool IsPidSupported(int pid) => _supported.ContainsKey(pid);

    /// <summary>Returns the ELM327 from a temporary raw-CAN OEM probe to the
    /// normal automatic OBD-II session before the rest of the app resumes.</summary>
    public async Task RestoreGenericSessionAsync(CancellationToken token = default)
    {
        await _initializationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _supported.Clear();
            await _protocol.RestoreAutomaticObdSessionAsync(token).ConfigureAwait(false);
            await DiscoverSupportedPidsAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<SensorValue?> ReadSensorAsync(int pid, CancellationToken token = default)
    {
        EnsureInitialized(); if (_supported.Count == 0) await DiscoverSupportedPidsAsync(token);
        var def = PidRegistry.Get(pid); if (def == null || !_supported.ContainsKey(pid)) return null;
        var raw = await SendTrackedAsync(def.Command, token, TimeSpan.FromSeconds(4), 2, true);
        return _decoder.TryDecode(pid, raw, out var value) ? new SensorValue { Pid = pid, Name = def.Name, Unit = def.Unit, Category = def.Category, Value = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) } : null;
    }

    public async Task<IReadOnlyList<SensorValue>> ReadAllSupportedValuesAsync(CancellationToken token = default)
    {
        EnsureInitialized(); if (_supported.Count == 0) await DiscoverSupportedPidsAsync(token);
        var list = new List<SensorValue>();
        foreach (var def in GetSnapshotDefinitions())
        {
            token.ThrowIfCancellationRequested();
            try { var raw = await SendTrackedAsync(def.Command, token, TimeSpan.FromMilliseconds(900), 1, true); if (_decoder.TryDecode(def.Pid, raw, out var v)) list.Add(new SensorValue { Pid = def.Pid, Name = def.Name, Unit = def.Unit, Category = def.Category, Value = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) }); }
            catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException) { _logger.LogDebug(ex, "PID {Pid:X2} failed", def.Pid); }
        }
        return list;
    }

    /// <summary>
    /// Reads the complete set of standard, decodable sensors advertised by
    /// this ECU. This is intentionally separate from the short dashboard
    /// snapshot so opening All Sensors does not make other pages slower.
    /// </summary>
    public async Task<(IReadOnlyList<SensorValue> Values, PidScanStats Stats)> ReadExpandedSupportedSensorValuesAsync(
        IProgress<SensorScanProgress>? progress = null,
        CancellationToken token = default)
    {
        EnsureInitialized();
        if (_supported.Count == 0)
            await DiscoverSupportedPidsAsync(token).ConfigureAwait(false);

        var definitions = _supported.Values
            .Where(definition => PIDDecoder.CanDecode(definition.Pid))
            .OrderBy(definition => ExpandedSensorCategoryOrder(definition.Pid))
            .ThenBy(definition => definition.Pid)
            .ToArray();

        var values = new List<SensorValue>();
        var failed = 0;
        progress?.Report(new SensorScanProgress(0, definitions.Length, "სენსორების მომზადება…"));

        for (var index = 0; index < definitions.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var definition = definitions[index];
            try
            {
                // The support bitmap was already checked. One short request
                // avoids a falsely advertised PID holding the screen up.
                var raw = await SendTrackedAsync(
                    definition.Command,
                    token,
                    TimeSpan.FromMilliseconds(700),
                    1,
                    true).ConfigureAwait(false);

                if (_decoder.TryDecode(definition.Pid, raw, out var value))
                {
                    values.Add(new SensorValue
                    {
                        Pid = definition.Pid,
                        Name = definition.Name,
                        Unit = definition.Unit,
                        Category = ExpandedSensorCategory(definition.Pid),
                        Value = FormatExpandedSensorValue(definition.Pid, value)
                    });
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
            {
                failed++;
                _logger.LogDebug(ex, "Expanded sensor PID {Pid:X2} unavailable", definition.Pid);
            }
            finally
            {
                progress?.Report(new SensorScanProgress(index + 1, definitions.Length, definition.Name));
            }
        }

        return (
            values,
            new PidScanStats
            {
                Advertised = definitions.Length,
                Decoded = values.Count,
                Failed = failed,
                UnsupportedDecoder = 0
            });
    }

    /// <summary>
    /// Reads a bounded set of ECU-advertised PIDs for the interactive live
    /// dashboard.  Every command has one short attempt so a clone/ECU that
    /// falsely advertises a PID cannot freeze the dashboard.
    /// </summary>
    public async Task<IReadOnlyList<SensorValue>> ReadLiveValuesAsync(
        IEnumerable<int> pids,
        CancellationToken token = default)
    {
        EnsureInitialized();
        if (_supported.Count == 0)
            await DiscoverSupportedPidsAsync(token).ConfigureAwait(false);

        var values = new List<SensorValue>();
        var requested = pids
            .Distinct()
            .Take(9)
            .Where(_supported.ContainsKey)
            .Select(pid => _supported[pid])
            .ToArray();

        foreach (var definition in requested)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var raw = await SendTrackedAsync(
                    definition.Command,
                    token,
                    TimeSpan.FromMilliseconds(750),
                    1,
                    true).ConfigureAwait(false);

                if (_decoder.TryDecode(definition.Pid, raw, out var decoded))
                {
                    values.Add(new SensorValue
                    {
                        Pid = definition.Pid,
                        Name = definition.Name,
                        Unit = definition.Unit,
                        Category = definition.Category,
                        Value = decoded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    });
                }
            }
            catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
            {
                _logger.LogDebug(ex, "Live PID {Pid:X2} unavailable", definition.Pid);
            }
        }

        return values;
    }


    public async Task<(IReadOnlyList<SensorValue> Values, PidScanStats Stats)> ReadAllSupportedValuesWithStatsAsync(CancellationToken token = default)
    {
        EnsureInitialized();
        if (_supported.Count == 0) await DiscoverSupportedPidsAsync(token);
        var list = new List<SensorValue>();
        var failed = 0;
        var unsupported = 0;
        foreach (var def in GetSnapshotDefinitions())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var raw = await SendTrackedAsync(def.Command, token, TimeSpan.FromMilliseconds(900), 1, true);
                if (_decoder.TryDecode(def.Pid, raw, out var v)) list.Add(new SensorValue { Pid = def.Pid, Name = def.Name, Unit = def.Unit, Category = def.Category, Value = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) });
                else unsupported++;
            }
            catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException) { failed++; _logger.LogDebug(ex, "PID {Pid:X2} failed", def.Pid); }
        }
        return (list, new PidScanStats { Advertised = _supported.Count, Decoded = list.Count, Failed = failed, UnsupportedDecoder = unsupported });
    }

    public async Task<LiveDataModel> GetLiveDataAsync(CancellationToken token = default)
    {
        EnsureInitialized(); if (_supported.Count == 0) await DiscoverSupportedPidsAsync(token);
        var d = new LiveDataModel { Timestamp = DateTime.Now };
        await TrySetAsync(0x0C, token, v => { d.Rpm = (int)Math.Round(v); d.HasRpm = true; }); await TrySetAsync(0x0D, token, v => { d.Speed = (int)Math.Round(v); d.HasSpeed = true; });
        await TrySetAsync(0x05, token, v => { d.CoolantTemp = (int)Math.Round(v); d.HasCoolantTemp = true; }); await TrySetAsync(0x04, token, v => { d.EngineLoad = (int)Math.Round(v); d.HasEngineLoad = true; });
        await TrySetAsync(0x11, token, v => { d.ThrottlePosition = (int)Math.Round(v); d.HasThrottlePosition = true; }); await TrySetAsync(0x42, token, v => { d.Voltage = v; d.HasVoltage = true; });
        return d;
    }

    /// <summary>
    /// Reads the small, speed-first set used by Performance Lab. Vehicle
    /// speed (standard Mode 01 PID 0D) is always requested before secondary
    /// values so a slow adapter cannot make the speedometer lag behind.
    /// Values are populated only after a positive ECU response; no fallback
    /// or simulated speed is used here.
    /// </summary>
    public async Task<LiveDataModel> GetPerformanceDataAsync(CancellationToken token = default)
    {
        EnsureInitialized();
        if (_supported.Count == 0)
            await DiscoverSupportedPidsAsync(token).ConfigureAwait(false);

        var data = new LiveDataModel { Timestamp = DateTime.UtcNow };
        var timeout = TimeSpan.FromMilliseconds(600);

        await TrySetAsync(0x0D, token, value =>
        {
            data.Speed = (int)Math.Round(value);
            data.HasSpeed = true;
        }, timeout).ConfigureAwait(false);

        await TrySetAsync(0x0C, token, value =>
        {
            data.Rpm = (int)Math.Round(value);
            data.HasRpm = true;
        }, timeout).ConfigureAwait(false);

        await TrySetAsync(0x11, token, value =>
        {
            data.ThrottlePosition = (int)Math.Round(value);
            data.HasThrottlePosition = true;
        }, timeout).ConfigureAwait(false);

        return data;
    }

    /// <summary>
    /// Reads only standard vehicle speed (Mode 01 PID 0D). The camera overlay
    /// uses this focused path so the speed sample is published immediately
    /// instead of waiting for the secondary RPM and throttle requests.
    /// </summary>
    public async Task<LiveDataModel> GetPerformanceSpeedDataAsync(CancellationToken token = default)
    {
        EnsureInitialized();
        if (_supported.Count == 0)
            await DiscoverSupportedPidsAsync(token).ConfigureAwait(false);

        var data = new LiveDataModel { Timestamp = DateTime.UtcNow };
        if (!_supported.ContainsKey(0x0D))
            return data;

        // "010D1" tells the ELM327 to return after the first ECU answer
        // instead of waiting out its response timer for more ECUs. That cuts
        // each speed sample from ~100-200 ms to the bare bus round trip.
        // Clones that reject the suffix fall back to the plain request.
        string? raw = null;
        if (!_speedSingleResponseUnsupported)
        {
            try
            {
                raw = await SendTrackedAsync("010D1", token, TimeSpan.FromMilliseconds(400), 1, true).ConfigureAwait(false);
                if (!_decoder.TryDecode(0x0D, raw, out _))
                    raw = null;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "010D1 failed; trying plain 010D");
                raw = null;
            }

            if (raw is null && ++_speedSingleResponseFailures >= 3)
                _speedSingleResponseUnsupported = true;
            else if (raw is not null)
                _speedSingleResponseFailures = 0;
        }

        if (raw is null)
        {
            try
            {
                raw = await SendTrackedAsync("010D", token, TimeSpan.FromMilliseconds(600), 1, true).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
            {
                _logger.LogDebug(ex, "PID 0D failed");
                return data;
            }
        }

        if (_decoder.TryDecode(0x0D, raw, out var speed))
        {
            data.Speed = (int)Math.Round(speed);
            data.HasSpeed = true;
            // Timestamp the actual speed response, not the start of the poll.
            data.Timestamp = DateTime.UtcNow;
        }

        return data;
    }

    private bool _speedSingleResponseUnsupported;
    private int _speedSingleResponseFailures;

    private async Task TrySetAsync(int pid, CancellationToken token, Action<double> set, TimeSpan? timeout = null)
    {
        if (!_supported.ContainsKey(pid)) return;
        try
        {
            var raw = await SendTrackedAsync(
                $"01{pid:X2}",
                token,
                timeout ?? TimeSpan.FromMilliseconds(750),
                1,
                true).ConfigureAwait(false);
            if (_decoder.TryDecode(pid, raw, out var value)) set(value);
        }
        catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
        {
            _logger.LogDebug(ex, "PID {Pid:X2} failed", pid);
        }
    }

    public Task<List<DiagnosticCode>> ReadStoredDtcAsync(CancellationToken token = default) => ReadDtcAsync("03", _dtc.DecodeStored, token);
    public Task<List<DiagnosticCode>> ReadPendingDtcAsync(CancellationToken token = default) => ReadDtcAsync("07", _dtc.DecodePending, token);
    public Task<List<DiagnosticCode>> ReadPermanentDtcAsync(CancellationToken token = default) => ReadDtcAsync("0A", _dtc.DecodePermanent, token);
    private async Task<List<DiagnosticCode>> ReadDtcAsync(string cmd, Func<string, List<DiagnosticCode>> decode, CancellationToken token)
    {
        EnsureInitialized();
        try
        {
            // DTC commands are one-shot standard reads. A short bounded wait
            // prevents an ECU with no response from making a clean scan feel
            // stuck, while still allowing normal ELM327 response time.
            var raw = await SendTrackedAsync(cmd, token, TimeSpan.FromMilliseconds(1500), 1, true).ConfigureAwait(false);
            return raw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) ? new() : decode(raw);
        }
        catch (Elm327CommandException ex)
        {
            if (ex.RawResponse.Contains("NO DATA", StringComparison.OrdinalIgnoreCase)) return new();
            throw;
        }
    }

    public async Task ClearDtcAsync(CancellationToken token = default)
    {
        var result = await ClearDtcWithResultAsync(token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
    }

    public async Task<DiagnosticOperationResult<bool>> ClearDtcWithResultAsync(CancellationToken token = default)
    {
        EnsureInitialized();

        var first = await TryClearDtcOnceAsync(token).ConfigureAwait(false);
        if (first.IsSuccess ||
            first.Status is DiagnosticOperationStatus.PreconditionsFailed
                or DiagnosticOperationStatus.SecurityDenied
                or DiagnosticOperationStatus.VehicleNotConnected)
        {
            return first;
        }

        // No clear answer: the adapter may still be set up for a module
        // (physical header / reply filter) after the full module scan.
        // Return it to the normal OBD-II session and send 04 once more.
        try
        {
            await RestoreGenericSessionAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generic session restore before DTC clear retry failed");
            return first;
        }

        return await TryClearDtcOnceAsync(token).ConfigureAwait(false);
    }

    private async Task<DiagnosticOperationResult<bool>> TryClearDtcOnceAsync(CancellationToken token)
    {
        try
        {
            var raw = await SendTrackedAsync("04", token, TimeSpan.FromSeconds(7), 1, false).ConfigureAwait(false);
            var bytes = ObdResponseParser.ParseHex(raw);

            // "Response pending" (7F 04 78) only: give the ECU time and ask once more.
            if (!bytes.Contains((byte)0x44) && FindClearNrc(bytes) == 0x78)
            {
                await Task.Delay(2000, token).ConfigureAwait(false);
                raw = await SendTrackedAsync("04", token, TimeSpan.FromSeconds(7), 1, false).ConfigureAwait(false);
                bytes = ObdResponseParser.ParseHex(raw);
            }

            if (bytes.Contains((byte)0x44))
                return new(DiagnosticOperationStatus.ClearedSuccessfully, true, "DTC clear command accepted by ECU.", raw);

            var nrc = FindClearNrc(bytes);
            return nrc switch
            {
                0x22 => new(DiagnosticOperationStatus.PreconditionsFailed, false,
                    "ECU-მ წაშლაზე უარი თქვა, რადგან ძრავა მუშაობს. გამორთე ძრავა (ჰიბრიდზე: გამორთე READY), ანთება დატოვე ჩართული და სცადე თავიდან. (7F 04 22)", raw),
                0x33 => new(DiagnosticOperationStatus.SecurityDenied, false,
                    "ECU-მ წაშლაზე უარი თქვა: დაცულია (7F 04 33).", raw),
                0x11 or 0x12 => new(DiagnosticOperationStatus.Unsupported, false,
                    $"ECU ამ ბრძანებას არ უჭერს მხარს (7F 04 {nrc:X2}).", raw),
                { } other when other != 0 => new(DiagnosticOperationStatus.ProtocolError, false,
                    $"ECU-მ წაშლაზე უარი თქვა (7F 04 {other:X2}). ანთება ჩართული, ძრავა გამორთული — სცადე თავიდან.", raw),
                _ => new(DiagnosticOperationStatus.ProtocolError, false,
                    ObdResponseParser.IsNegative(raw) || string.IsNullOrWhiteSpace(raw)
                        ? "ECU-მ წაშლის ბრძანებაზე პასუხი არ დააბრუნა. ანთება ჩართული, ძრავა გამორთული — სცადე თავიდან."
                        : "ECU-მ DTC clear-ის სტანდარტული positive response (44) არ დააბრუნა.", raw)
            };
        }
        catch (Elm327TimeoutException ex) { return new(DiagnosticOperationStatus.Timeout, false, ex.Message); }
        catch (Elm327NotConnectedException ex) { return new(DiagnosticOperationStatus.VehicleNotConnected, false, ex.Message); }
        catch (Elm327CommandException ex)
        {
            var nrc = FindClearNrc(ObdResponseParser.ParseHex(ex.RawResponse ?? string.Empty));
            return nrc == 0x22
                ? new(DiagnosticOperationStatus.PreconditionsFailed, false,
                    "ECU-მ წაშლაზე უარი თქვა, რადგან ძრავა მუშაობს. გამორთე ძრავა (ჰიბრიდზე: გამორთე READY), ანთება დატოვე ჩართული და სცადე თავიდან. (7F 04 22)", ex.RawResponse ?? string.Empty)
                : new(DiagnosticOperationStatus.TransportError, false, ex.Message, ex.RawResponse ?? string.Empty);
        }
    }

    /// <summary>Negative response code of "7F 04 xx", or 0 when there is none.</summary>
    private static byte FindClearNrc(byte[] bytes)
    {
        byte found = 0;
        for (var i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] != 0x7F || bytes[i + 1] != 0x04)
                continue;
            found = bytes[i + 2];
            if (found != 0x78)
                return found;
        }
        return found;
    }

    public async Task<DiagnosticOperationResult<bool>> VerifyDtcClearAsync(CancellationToken token = default)
    {
        var delays = new[] { 1000, 2000, 4000 };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            await Task.Delay(delays[attempt], token).ConfigureAwait(false);
            try
            {
                var stored = await ReadStoredDtcAsync(token);
                var pending = await ReadPendingDtcAsync(token);
                var permanent = await ReadPermanentDtcAsync(token);
                var readiness = await ReadReadinessAsync(token);
                var ok = stored.Count == 0 && pending.Count == 0 && permanent.Count == 0 && !readiness.MilOn;
                if (ok) return new(DiagnosticOperationStatus.ClearedSuccessfully, true, "Stored/Pending/Permanent DTC and MIL are clear.");
                if (attempt == delays.Length - 1) return new(DiagnosticOperationStatus.ClearedButFaultReturned, false, $"Verification found Stored={stored.Count}, Pending={pending.Count}, Permanent={permanent.Count}, MIL={(readiness.MilOn ? "ON" : "OFF")}.");
            }
            catch (Elm327TimeoutException ex) when (attempt == delays.Length - 1) { return new(DiagnosticOperationStatus.Timeout, false, ex.Message); }
            catch (Elm327CommandException ex) when (attempt == delays.Length - 1) { return new(DiagnosticOperationStatus.ProtocolError, false, ex.Message, ex.RawResponse); }
        }
        return new(DiagnosticOperationStatus.ProtocolError, false, "DTC clear verification did not complete.");
    }

    public async Task<ReadinessInfo> ReadReadinessAsync(CancellationToken token = default)
    {
        var data = await ReadMode01PidDataAsync(0x01, token).ConfigureAwait(false);
        if (data is null || data.Length < 4)
            throw new FormatException("Readiness response could not be decoded.");

        return new ReadinessInfo
        {
            MilOn = (data[0] & 0x80) != 0,
            StoredDtcCount = data[0] & 0x7F,
            RawBytes = data.Take(4).ToArray()
        };
    }

    public Task<string> ReadReadinessRawAsync(CancellationToken token = default) => SendTrackedAsync("0101", token, TimeSpan.FromSeconds(5), 2, true);

    /// <summary>
    /// Reads one standard Mode 01 PID and returns only its response payload.
    /// A negative/no-data response is represented as null, so optional PIDs do
    /// not get mistaken for a zero value.
    /// </summary>
    public async Task<byte[]?> ReadMode01PidDataAsync(int pid, CancellationToken token = default)
    {
        EnsureInitialized();
        var raw = await SendTrackedAsync($"01{pid:X2}", token, TimeSpan.FromMilliseconds(900), 1, false).ConfigureAwait(false);
        if (ObdResponseParser.IsNegative(raw))
            return null;

        var data = ObdResponseParser.ExtractMode01Pid(raw, (byte)pid);
        return data.Length == 0 ? null : data;
    }

    /// <summary>Read-only Mode 01 PID request with a short bounded timeout.</summary>
    public Task<string> ReadMode01PidQuickAsync(int pid, CancellationToken token = default) =>
        SendTrackedAsync($"01{pid:X2}", token, TimeSpan.FromMilliseconds(900), 1, false);
    public Task<string> ReadMode05RawAsync(CancellationToken token = default) => SendTrackedAsync("05", token, TimeSpan.FromSeconds(7), 2, true);
    public Task<string> ReadMode06RawAsync(CancellationToken token = default) => SendTrackedAsync("06", token, TimeSpan.FromSeconds(7), 2, true);
    public Task<string> ReadMode05QuickAsync(CancellationToken token = default) => SendTrackedAsync("05", token, TimeSpan.FromSeconds(2), 1, false);
    public Task<string> ReadMode06QuickAsync(CancellationToken token = default) => SendTrackedAsync("06", token, TimeSpan.FromSeconds(2), 1, false);

    /// <summary>CAN Mode $06 request for one monitor ID (e.g. 0600 = supported MIDs 01–20, 0621 = catalyst bank 1).</summary>
    public Task<string> ReadMode06MidAsync(byte mid, CancellationToken token = default) =>
        SendTrackedAsync($"06{mid:X2}", token, TimeSpan.FromSeconds(3), 1, false);

    public bool IsCanProtocol => CurrentProtocol is
        ObdProtocol.ISO_15765_4_CAN_11BIT_500K or ObdProtocol.ISO_15765_4_CAN_11BIT_250K or
        ObdProtocol.ISO_15765_4_CAN_29BIT_500K or ObdProtocol.ISO_15765_4_CAN_29BIT_250K;
    public Task<string> ReadMode08RawAsync(CancellationToken token = default) => throw new InvalidOperationException("Mode 08 bidirectional control is vehicle-specific and intentionally disabled in generic read-only mode.");
    public Task<string> ReadMode09RawAsync(int pid, CancellationToken token = default) => SendTrackedAsync($"09{pid:X2}", token, TimeSpan.FromSeconds(7), 2, true);
    public Task<string> ReadMode09QuickAsync(int pid, CancellationToken token = default) => SendTrackedAsync($"09{pid:X2}", token, TimeSpan.FromSeconds(3), 2, true);
    /// <summary>
    /// Read-only 0x22 ReadDataByIdentifier sent with the normal broadcast
    /// (functional) header, one attempt, "NO DATA" returned as text.
    /// Used by the Service page oil-life discovery.
    /// </summary>
    /// <summary>
    /// Read-only GMLAN $1A ReadDataByIdentifier (1-byte identifier), sent
    /// with the normal broadcast header, one attempt.
    /// </summary>
    public Task<string> ReadGmlanDidFunctionalAsync(int did, TimeSpan timeout, CancellationToken token = default)
    {
        EnsureInitialized();
        if (did is < 0 or > 0xFF)
            throw new ArgumentOutOfRangeException(nameof(did));
        return SendTrackedAsync($"1A{did:X2}", token, timeout, 1, false);
    }

    public Task<string> ReadDidFunctionalAsync(int did, TimeSpan timeout, CancellationToken token = default)
    {
        EnsureInitialized();
        if (did is < 0 or > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(did));
        return SendTrackedAsync($"22{did:X4}", token, timeout, 1, false);
    }

    /// <summary>
    /// Read-only GMLAN $1A request sent with header <paramref name="transmitCanId"/>
    /// (0x7DF = functional) and filtered to one reply ID, so the app can see
    /// exactly which module answered. Service page oil-life reset only.
    /// </summary>
    public Task<string> ReadGmlanDidFilteredAsync(int transmitCanId, int receiveCanId, int did, TimeSpan timeout, CancellationToken token = default)
    {
        EnsureInitialized();
        if (did is < 0 or > 0xFF)
            throw new ArgumentOutOfRangeException(nameof(did));
        return _protocol.SendCanIsoTpPayloadAsync(new byte[] { 0x1A, (byte)did }, transmitCanId, receiveCanId, false, timeout, token);
    }

    /// <summary>
    /// Legacy experimental GMLAN write path. A readable $1A identifier does
    /// not prove that writing the same identifier with $3B is a valid oil-life
    /// reset. Keep this fail-closed until an exact vehicle/ECU operation is
    /// promoted through the reviewed OEM operation catalog.
    /// </summary>
    public Task<string> WriteGmlanOilLifeByteAsync(int transmitCanId, int receiveCanId, int did, byte value, TimeSpan timeout, CancellationToken token = default)
    {
        throw new InvalidOperationException(
            "Inferred GMLAN oil-life writes are disabled. Use an exact reviewed OEM service operation.");
    }

    /// <summary>
    /// Service identifiers that may be written by the legacy read-DID/write-DID
    /// path. Intentionally empty: a successful 0x22 read does not establish
    /// that 0x2E is supported, nor what payload/session/security preconditions
    /// are required. Executable resets belong in the exact, reviewed OEM
    /// operation catalog instead.
    /// </summary>
    public static readonly IReadOnlySet<int> WritableServiceDids = new HashSet<int>();

    /// <summary>
    /// Service page only: one UDS 0x2E WriteDataByIdentifier to an identifier
    /// from <see cref="WritableServiceDids"/>, with 1–4 data bytes, sent to the
    /// physical ECU address the value was read from.
    /// </summary>
    public Task<string> WriteServiceDidAsync(ServiceDiagnosticAddress address, int did, byte[] data, TimeSpan timeout, CancellationToken token = default)
    {
        EnsureInitialized();
        if (!WritableServiceDids.Contains(did))
            throw new InvalidOperationException("This identifier cannot be written.");
        if (data is null || data.Length is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(data));
        if (address.ExtendedAddress.HasValue)
            throw new NotSupportedException("Extended CAN addressing is not supported for writes.");

        var receive = address.ReceiveCanId ?? (address.Extended29Bit
            ? (int)((address.TransmitCanId & 0xFFFF0000) | ((address.TransmitCanId & 0xFF) << 8) | ((address.TransmitCanId >> 8) & 0xFF))
            : address.TransmitCanId + 8);

        var payload = new byte[3 + data.Length];
        payload[0] = 0x2E;
        payload[1] = (byte)(did >> 8);
        payload[2] = (byte)did;
        Array.Copy(data, 0, payload, 3, data.Length);

        return _protocol.SendCanIsoTpPayloadAsync(payload, address.TransmitCanId, receive, address.Extended29Bit, timeout, token);
    }

    /// <summary>
    /// Sends one read-only service 02 (Freeze Frame) request, e.g. "020C00".
    /// Used by FreezeFrameReaderService; only service 02 is accepted.
    /// </summary>
    public Task<string> ReadFreezeFrameCommandAsync(string command, TimeSpan timeout, CancellationToken token = default)
    {
        EnsureInitialized();
        var normalized = new string((command ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (!normalized.StartsWith("02", StringComparison.Ordinal) || normalized.Length is not (4 or 6))
            throw new ArgumentException("Only service 02 freeze-frame requests are allowed.", nameof(command));
        return SendTrackedAsync(normalized, token, timeout, 1, false);
    }

    public Task<string> ReadFreezeFrameRawAsync(int pid, CancellationToken token = default) => SendTrackedAsync($"02{pid:X2}", token, TimeSpan.FromSeconds(5), 2, true);

    public async Task<FreezeFrame> ReadFreezeFrameAsync(string? triggerCode = null, CancellationToken token = default)
    {
        EnsureInitialized(); var ff = new FreezeFrame { TriggerCode = triggerCode ?? string.Empty };
        foreach (var pid in new[] { 0x02, 0x04, 0x05, 0x06, 0x07, 0x0B, 0x0C, 0x0D, 0x0F, 0x10, 0x11, 0x42 })
        {
            try { var raw = await ReadFreezeFrameRawAsync(pid, token); ff.Values[$"PID {pid:X2}"] = raw; } catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException) { _logger.LogDebug(ex, "Freeze-frame PID {Pid:X2} unavailable", pid); }
        }
        return ff;
    }

    /// <summary>
    /// Reads the useful Mode 02 snapshot values with one short request each.
    /// Freeze-frame support is optional; an ECU that has no snapshot must not
    /// keep the full module scan spinning for a minute.
    /// </summary>
    public async Task<FreezeFrame> ReadFreezeFrameQuickAsync(string? triggerCode = null, CancellationToken token = default)
    {
        EnsureInitialized();
        var frame = new FreezeFrame { TriggerCode = triggerCode ?? string.Empty };

        // PID 02 identifies the DTC that caused the Mode 02 snapshot. Probe
        // it first: without a positive response there is no real Freeze Frame
        // to display, so avoid issuing a long sequence of empty PID requests.
        try
        {
            var triggerRaw = await SendTrackedAsync("0202", token, TimeSpan.FromMilliseconds(900), 1, false).ConfigureAwait(false);
            if (triggerRaw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase))
                return frame;

            var triggerBytes = PIDDecoder.ParseHexBytes(triggerRaw);
            var triggerIndex = Find(triggerBytes, 0x42, 0x02);
            if (triggerIndex < 0 || triggerIndex + 3 >= triggerBytes.Length)
                return frame;

            var capturedCode = DecodeStandardDtc(triggerBytes[triggerIndex + 2], triggerBytes[triggerIndex + 3]);
            if (capturedCode is null)
                return frame;

            frame.TriggerCode = capturedCode;
            frame.Values["Freeze-frame DTC"] = capturedCode;
        }
        catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
        {
            _logger.LogDebug(ex, "Freeze-frame trigger PID is unavailable");
            return frame;
        }

        // These six values cover the useful diagnostic context while keeping
        // a valid snapshot responsive on lower-speed ELM327 adapters.
        foreach (var pid in new[] { 0x04, 0x05, 0x0B, 0x0C, 0x0D, 0x11 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var raw = await SendTrackedAsync($"02{pid:X2}", token, TimeSpan.FromMilliseconds(600), 1, false).ConfigureAwait(false);
                if (raw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase)) continue;
                if (!_decoder.TryDecodeFreezeFrame(pid, raw, out var value)) continue;
                var definition = PidRegistry.Get(pid);
                var label = definition?.Name ?? $"PID 02{pid:X2}";
                var unit = definition?.Unit ?? string.Empty;
                frame.Values[label] = $"{value:0.##} {unit}".Trim();
            }
            catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
            {
                _logger.LogDebug(ex, "Freeze-frame PID {Pid:X2} unavailable", pid);
            }
        }

        return frame;
    }

    /// <summary>
    /// Reads a fuller on-demand Mode 02 snapshot for the Freeze Frame screen.
    /// It is deliberately separate from regular DTC scans because optional
    /// snapshot PIDs can be slow on inexpensive ELM327 adapters.
    /// </summary>
    public async Task<FreezeFrame> ReadFreezeFrameDetailedAsync(
        string? requestedTriggerCode = null,
        IProgress<(int Percent, string Stage)>? progress = null,
        CancellationToken token = default)
    {
        EnsureInitialized();
        var frame = new FreezeFrame { TriggerCode = requestedTriggerCode ?? string.Empty };
        progress?.Report((5, "Freeze Frame DTC-ის არსებობის შემოწმება…"));

        try
        {
            var triggerRaw = await SendTrackedAsync("0202", token, TimeSpan.FromMilliseconds(900), 1, false).ConfigureAwait(false);
            if (triggerRaw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) || ObdResponseParser.IsNegative(triggerRaw))
                return frame;

            var triggerBytes = PIDDecoder.ParseHexBytes(triggerRaw);
            var triggerIndex = Find(triggerBytes, 0x42, 0x02);
            if (triggerIndex < 0 || triggerIndex + 3 >= triggerBytes.Length)
                return frame;

            var capturedCode = DecodeStandardDtc(triggerBytes[triggerIndex + 2], triggerBytes[triggerIndex + 3]);
            if (capturedCode is null)
                return frame;

            frame.TriggerCode = capturedCode;
            frame.Values["Freeze-frame DTC"] = capturedCode;
        }
        catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
        {
            _logger.LogDebug(ex, "Detailed freeze-frame trigger is unavailable");
            return frame;
        }

        var snapshotPids = new[] { 0x04, 0x05, 0x06, 0x07, 0x0A, 0x0B, 0x0C, 0x0D, 0x0F, 0x10, 0x11, 0x42 };
        for (var index = 0; index < snapshotPids.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var pid = snapshotPids[index];
            var percent = 10 + (int)Math.Round((index + 1) * 85d / snapshotPids.Length);
            var definition = PidRegistry.Get(pid);
            progress?.Report((percent, $"{definition?.Name ?? $"PID 02{pid:X2}"} წაიკითხება…"));

            try
            {
                var raw = await SendTrackedAsync($"02{pid:X2}", token, TimeSpan.FromMilliseconds(650), 1, false).ConfigureAwait(false);
                if (raw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) ||
                    !_decoder.TryDecodeFreezeFrame(pid, raw, out var value))
                    continue;

                var label = definition?.Name ?? $"PID 02{pid:X2}";
                var unit = definition?.Unit ?? string.Empty;
                frame.Values[label] = $"{value:0.##} {unit}".Trim();
            }
            catch (Exception ex) when (ex is Elm327CommandException or Elm327TimeoutException or FormatException)
            {
                _logger.LogDebug(ex, "Detailed freeze-frame PID {Pid:X2} unavailable", pid);
            }
        }

        progress?.Report((100, "Freeze Frame დასრულდა."));
        return frame;
    }

    private static string? DecodeStandardDtc(byte high, byte low)
    {
        if (high == 0 && low == 0)
            return null;

        var family = (high >> 6) switch
        {
            0 => 'P',
            1 => 'C',
            2 => 'B',
            _ => 'U'
        };

        return $"{family}{(high >> 4) & 0x03}{high & 0x0F:X1}{low:X2}";
    }

    public async Task<double?> ReadStandardOdometerAsync(CancellationToken token = default)
    {
        EnsureInitialized();
        // Odometer PID support is optional. A short, single request avoids
        // holding the Mileage Verification screen for several seconds when a
        // generic ECU does not implement it.
        var raw = await SendTrackedAsync("01A6", token, TimeSpan.FromMilliseconds(1200), 1, false);
        var data = ObdResponseParser.ExtractMode01Pid(raw, 0xA6);
        if (data.Length < 4) return null;
        return ((long)data[0] << 24 | (long)data[1] << 16 | (long)data[2] << 8 | data[3]) / 10.0;
    }

    /// <summary>
    /// Executes a physically addressed read-only CAN request. This method is
    /// intentionally separate from SendRawCommandAsync so a database-driven
    /// service-value reader can never use write/routine/security services.
    /// </summary>
    public Task<string> ReadPhysicalCanReadOnlyAsync(
        int transmitCanId,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken token = default) =>
        ReadPhysicalCanReadOnlyAsync(
            new ServiceDiagnosticAddress(
                transmitCanId,
                null,
                false,
                null,
                "11-bit CAN",
                $"TX 0x{transmitCanId:X3}"),
            payload,
            timeout,
            token);

    public async Task<string> ReadPhysicalCanReadOnlyAsync(
        ServiceDiagnosticAddress address,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken token = default)
    {
        EnsureInitialized();
        // 0x21 = KWP2000 ReadDataByLocalIdentifier (read-only).
        if (payload.Length == 0 || payload.Span[0] is not (0x19 or 0x21 or 0x22))
            throw new InvalidOperationException(
                "Only read-only diagnostic services 0x19, 0x21 and 0x22 are allowed.");

        return await _protocol.SendPhysicalCanReadOnlyPayloadAsync(
                payload,
                address.TransmitCanId,
                address.ReceiveCanId,
                address.Extended29Bit,
                address.ExtendedAddress,
                timeout,
                token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Physically addressed DTC read/clear requests to several modules in one
    /// adapter session (ABS, airbag, body …). Only DTC services are allowed.
    /// </summary>
    public async Task RunDtcModuleSessionAsync(
        Func<Elm327Protocol.IDtcModuleSession, Task> work,
        CancellationToken token = default)
    {
        EnsureInitialized();
        await _protocol.RunDtcModuleSessionAsync(work, token).ConfigureAwait(false);
    }

    public async Task<string> SendRawCommandAsync(string command, CancellationToken token = default)
    {
        EnsureInitialized();
        var normalized = new string((command ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Command must contain hexadecimal data.", nameof(command));
        if (normalized.StartsWith("270", StringComparison.Ordinal) || normalized.StartsWith("2E", StringComparison.Ordinal) || normalized.StartsWith("2F", StringComparison.Ordinal) || normalized.StartsWith("31", StringComparison.Ordinal) || normalized.StartsWith("34", StringComparison.Ordinal) || normalized.StartsWith("36", StringComparison.Ordinal) || normalized.StartsWith("37", StringComparison.Ordinal) || normalized.StartsWith("23", StringComparison.Ordinal))
            throw new InvalidOperationException("Write/security/programming/raw-memory commands are disabled in consumer mode. Use a supported OEM Service Function with safety gating.");
        return await SendTrackedAsync(normalized, token, TimeSpan.FromSeconds(5), 2, true);
    }

    public async Task<AdapterDiagnostics> GetAdapterDiagnosticsAsync(
        CancellationToken token = default,
        IProgress<AdapterTestProgress>? progress = null,
        bool includeExtendedCapabilities = false)
    {
        string firmware = "Unavailable", voltage = "Unavailable";
        bool elm327Responding = false, basicCommandResponding = false;
        bool vehicleResponding = false, ecuProbeAttempted = false;
        long probeLatencyMs = 0;

        // Adapter metadata is supplementary.  A clone that does not answer
        // ATI/ATRV must not make a full scan appear frozen.
        progress?.Report(new AdapterTestProgress(20, "ELM327 პასუხის შემოწმება (ATI)…"));
        try
        {
            if (_protocol.IsInitialized)
            {
                firmware = await _protocol.SendCommandAsync("ATI", TimeSpan.FromMilliseconds(800), 1, false, token);
                elm327Responding = IsMeaningfulAdapterResponse(firmware);
            }
        }
        catch { }

        progress?.Report(new AdapterTestProgress(45, "Adapter-ის საბაზისო ბრძანების შემოწმება (ATRV)…"));
        try
        {
            if (_protocol.IsInitialized)
            {
                voltage = await _protocol.SendCommandAsync("ATRV", TimeSpan.FromMilliseconds(800), 1, false, token);
                basicCommandResponding = IsMeaningfulAdapterResponse(voltage);
            }
        }
        catch { }

        progress?.Report(new AdapterTestProgress(70, "მანქანის ECU პასუხის შემოწმება (0100)…"));
        try
        {
            if (_protocol.IsInitialized)
            {
                var probe = System.Diagnostics.Stopwatch.StartNew();
                var raw = await SendTrackedAsync("0100", token, TimeSpan.FromMilliseconds(800), 1, false);
                probe.Stop();
                probeLatencyMs = probe.ElapsedMilliseconds;
                ecuProbeAttempted = true;
                vehicleResponding = IsMeaningfulAdapterResponse(raw) && !raw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }

        if (includeExtendedCapabilities && _protocol.IsInitialized && vehicleResponding)
        {
            progress?.Report(new AdapterTestProgress(76, "Adapter შესაძლებლობების უსაფრთხო შემოწმება…"));
            _lastAdapterCapabilitySnapshot =
                await _protocol.DetectAdapterCapabilitiesAsync(progress, token).ConfigureAwait(false);
        }

        progress?.Report(new AdapterTestProgress(96, "პროტოკოლისა და პასუხის დროის დამუშავება…"));
        return new AdapterDiagnostics
        {
            AdapterName = string.IsNullOrWhiteSpace(_bluetooth.ConnectedDeviceName) ? "OBD-II adapter" : _bluetooth.ConnectedDeviceName,
            Firmware = NormalizeAdapterText(firmware),
            Voltage = NormalizeAdapterText(voltage),
            Protocol = CurrentProtocolDescription,
            BluetoothConnected = _bluetooth.IsConnected,
            AdapterInitialized = _protocol.IsInitialized,
            Elm327Responding = elm327Responding,
            BasicCommandResponding = basicCommandResponding,
            VehicleResponding = vehicleResponding,
            EcuProbeAttempted = ecuProbeAttempted,
            ProbeLatencyMs = probeLatencyMs,
            CapabilitySnapshot = _lastAdapterCapabilitySnapshot
        };
    }

    private static string NormalizeAdapterText(string value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static bool IsMeaningfulAdapterResponse(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalized) &&
               !normalized.Equals("?", StringComparison.Ordinal) &&
               !normalized.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("ERROR", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("STOPPED", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<SupportedPid> GetSnapshotDefinitions() =>
        SnapshotPids
            .Where(_supported.ContainsKey)
            .Select(pid => _supported[pid]);

    private static int ExpandedSensorCategoryOrder(int pid) => ExpandedSensorCategory(pid) switch
    {
        "Engine" => 0,
        "Fuel" => 1,
        "Air / Intake" => 2,
        "Temperature" => 3,
        "Oxygen Sensors" => 4,
        "Emissions" => 5,
        "Throttle" => 6,
        "Vehicle Speed" => 7,
        "Electrical" => 8,
        _ => 9
    };

    private static string ExpandedSensorCategory(int pid) => pid switch
    {
        0x04 or 0x0C or 0x0E or 0x1F or 0x43 or 0x61 or 0x62 or 0x63 or 0x7F or 0x8E => "Engine",
        0x06 or 0x07 or 0x08 or 0x09 or 0x0A or 0x22 or 0x23 or 0x2F or 0x44 or 0x51 or 0x52 or 0x59 or 0x5D or 0x5E or 0x9D or 0xA2 => "Fuel",
        0x0B or 0x10 or 0x33 or 0x50 or 0x66 => "Air / Intake",
        0x05 or 0x0F or 0x3C or 0x3D or 0x3E or 0x3F or 0x46 or 0x5C or 0x67 or 0x68 or 0x77 or 0x84 => "Temperature",
        (>= 0x14 and <= 0x1B) or (>= 0x24 and <= 0x2B) or (>= 0x34 and <= 0x3B) or (>= 0x55 and <= 0x58) => "Oxygen Sensors",
        0x21 or 0x2C or 0x2D or 0x2E or 0x31 or 0x32 or 0x4D or 0x4E or 0x53 or 0x54 or 0x73 or 0x78 or 0x79 or 0x7C or 0x9E => "Emissions",
        0x11 or 0x45 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x5A or 0x8D => "Throttle",
        0x0D or 0xA6 or 0xA4 => "Vehicle Speed",
        0x1E or 0x42 or 0x5B => "Electrical",
        _ => "Engine"
    };

    private static string FormatExpandedSensorValue(int pid, double value) => pid == 0x51
        ? FuelTypeName((int)Math.Round(value))
        : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string FuelTypeName(int code) => code switch
    {
        1 => "ბენზინი",
        2 => "მეთანოლი",
        3 => "ეთანოლი",
        4 => "დიზელი",
        5 => "LPG",
        6 => "CNG",
        7 => "პროპანი",
        8 => "ელექტრო",
        9 => "ბენზინი / ელექტრო",
        16 => "ჰიბრიდი",
        17 => "ბენზინი ჰიბრიდი",
        18 => "ეთანოლი ჰიბრიდი",
        19 => "დიზელი ჰიბრიდი",
        20 => "ელექტრო ჰიბრიდი",
        24 => "Flex Fuel",
        _ => $"საწვავის კოდი {code}"
    };

    public void EnsureInitialized() { if (!_protocol.IsInitialized) throw new InvalidOperationException("ჯერ დააკავშირე OBD2 adapter და დაიწყე ინიციალიზაცია."); }

    private async Task<string> SendTrackedAsync(string command, CancellationToken token, TimeSpan timeout, int retries, bool negativeIsError)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); try { var result = await SendCoreAsync(command, token, timeout, retries, negativeIsError); AddLog(command, result, sw.ElapsedMilliseconds, true, null); return result; } catch (Exception ex) { AddLog(command, "", sw.ElapsedMilliseconds, false, ex.Message); throw; }
    }
    private Task<string> SendCoreAsync(string command, CancellationToken token, TimeSpan timeout, int retries, bool negativeIsError)
        => _protocol.SendCommandAsync(command, timeout, Math.Max(1, retries), negativeIsError, token);
    private void AddLog(string command, string response, long ms, bool success, string? error) { lock (_logGate) { _commandLog.Add(new ObdCommandLog { Timestamp = DateTime.Now, Command = command, Response = response, DurationMs = ms, Success = success, Error = error }); if (_commandLog.Count > 500) _commandLog.RemoveAt(0); } }
    private static int Find(byte[] bytes, byte a, byte b) { for (var i = 0; i + 1 < bytes.Length; i++) if (bytes[i] == a && bytes[i + 1] == b) return i; return -1; }
}
