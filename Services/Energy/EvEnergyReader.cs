using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;
using SQLite;

namespace CarScanner.Services.Energy;

/// <summary>Where one value is requested from (one ECU + one identifier).</summary>
public sealed record EvRequest(
    string Key,
    ServiceDiagnosticAddress Address,
    int ServiceId,
    int Identifier,
    string Header);

/// <summary>How the battery voltage/current of the connected car is read.</summary>
public sealed record EvBatterySource(
    string Label,
    bool IsStandard,
    EvRequest? VoltageRequest,
    EvSignal? Voltage,
    EvRequest? CurrentRequest,
    EvSignal? Current,
    EvRequest? SocRequest,
    EvSignal? Soc,
    bool StandardSoc);

public readonly record struct EvBatteryReading(double Volts, double Amps, double? SocPercent);

/// <summary>
/// Reads high-voltage battery voltage and current: first the standard
/// PID 01 9A (any brand), then brand identifiers from the bundled OBDb
/// catalog. Read-only (0x01 / 0x21 / 0x22); a value is used only after an
/// exact positive reply that decodes to a realistic pack voltage/current.
/// </summary>
public sealed class EvEnergyReader
{
    private const int MaximumResolveReads = 8;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1500);

    private static readonly Regex VoltageName = new(
        @"(hv|high.?voltage|traction|pack|hybrid|main).*volt|battery (dc )?voltage|dc voltage",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VoltageExclude = new(
        @"cell|module|block|min|max|12 ?v|aux|starter|low.?volt|leakage|isolation|dc.?dc|charger|charge voltage|input|target|commanded|booster|bus|motor|inverter|capacitor|link|at rest|start/stop|after ignition|line b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CurrentName = new(
        @"(hv|high.?voltage|traction|pack|hybrid|main).*(current|amperage)|battery (total )?current|battery amperage",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CurrentExclude = new(
        @"cell|module|12 ?v|aux|starter|low.?volt|start/stop|dc.?dc|charger|charg|input|limit|max|min|target|commanded|booster|motor|inverter|lv |highside|lowside|requested",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SocName = new(
        @"state of charge|hv battery charge|battery charge|\bsoc\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SocExclude = new(
        @"min|max|target|raw|request|limit|health|current|voltage",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DtcCatalogService _catalog;
    private readonly OBDService _obd;
    private readonly IsoTpReassembler _isoTp = new();
    private readonly List<string> _log = new();
    private Candidates? _cachedCandidates;
    private string? _cachedCandidatesKey;
    private readonly object _logGate = new();

    public EvEnergyReader(DtcCatalogService catalog, OBDService obd)
    {
        _catalog = catalog;
        _obd = obd;
    }

    /// <summary>What was asked and answered while looking for the battery data.</summary>
    public string Log
    {
        get { lock (_logGate) return string.Join(Environment.NewLine, _log); }
    }

    private void Trace(string line)
    {
        lock (_logGate)
        {
            if (_log.Count < 120)
                _log.Add(line);
        }
    }

    // ------------------------------------------------------------------
    // Resolve
    // ------------------------------------------------------------------

    public async Task<EvBatterySource?> ResolveAsync(VehicleInfo? vehicle, CancellationToken token)
    {
        lock (_logGate)
            _log.Clear();

        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(token).ConfigureAwait(false);

        Trace($"Vehicle: {vehicle?.Manufacturer} {vehicle?.Model} {vehicle?.Year} · fuel {vehicle?.FuelType}");

        // 1) Standard PID 01 9A.
        var standard = await ReadStandardAsync(token).ConfigureAwait(false);
        if (standard is { } s)
        {
            Trace($"01 9A OK: {s.Volts:0.0} V · {s.Amps:0.0} A");
            var socData = await TryReadMode01Async(0x5B, token).ConfigureAwait(false);
            return new EvBatterySource("OBD PID 01 9A", true, null, null, null, null, null, null, socData is not null);
        }

        Trace("01 9A: no answer");

        // 2) Known identifiers for cars whose catalog rows are incomplete
        //    (Chevrolet Volt / Bolt BECM data on 7E4 / 7E7).
        var known = await TryKnownSourcesAsync(vehicle, token).ConfigureAwait(false);
        if (known is not null)
            return known;

        // 3) Brand identifiers from the catalog.
        if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.Manufacturer) ||
            vehicle.Manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            Trace("Brand unknown: catalog skipped");
            return null;
        }

        var candidates = await LoadCandidatesAsync(vehicle, token).ConfigureAwait(false);
        Trace($"Catalog: {candidates.Voltages.Count} voltage, {candidates.Currents.Count} current, {candidates.Socs.Count} SoC signals");

        var cache = new Dictionary<string, byte[]?>();
        var reads = 0;

        async Task<byte[]?> GetAsync(EvRequest request)
        {
            if (cache.TryGetValue(request.Key, out var cached))
                return cached;
            if (reads >= MaximumResolveReads)
                return null;

            reads++;
            byte[]? data = null;
            try
            {
                data = await ReadAsync(request, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace($"{Describe(request)}: {ex.Message}");
            }

            Trace($"{Describe(request)}: {(data is null ? "no answer" : Convert.ToHexString(data))}");
            cache[request.Key] = data;
            return data;
        }

        foreach (var pair in BuildPairs(candidates))
        {
            token.ThrowIfCancellationRequested();
            if (reads >= MaximumResolveReads && !cache.ContainsKey(pair.Voltage.Request.Key))
                break;

            var voltageData = await GetAsync(pair.Voltage.Request).ConfigureAwait(false);
            if (voltageData is null || !EvSignalDecoder.TryDecodeVoltage(pair.Voltage.Signal, voltageData, out var volts))
                continue;

            var currentData = await GetAsync(pair.Current.Request).ConfigureAwait(false);
            if (currentData is null || !EvSignalDecoder.TryDecodeCurrent(pair.Current.Signal, currentData, out var amps))
                continue;

            Trace($"OK: {pair.Voltage.Signal.Name} {volts:0.0} V · {pair.Current.Signal.Name} {amps:0.0} A");

            // State of charge: prefer one inside a reply we already read.
            Candidate? soc = candidates.Socs.FirstOrDefault(item =>
                item.Request.Key == pair.Voltage.Request.Key || item.Request.Key == pair.Current.Request.Key);
            if (soc is null)
            {
                foreach (var item in candidates.Socs.Take(2))
                {
                    var socData = await GetAsync(item.Request).ConfigureAwait(false);
                    if (socData is not null && EvSignalDecoder.TryDecode(item.Signal, socData, out var percent) && percent is >= 0 and <= 100)
                    {
                        soc = item;
                        break;
                    }
                }
            }

            var label = pair.Voltage.Request.Key == pair.Current.Request.Key
                ? $"{vehicle.Manufacturer} · {Describe(pair.Voltage.Request)}"
                : $"{vehicle.Manufacturer} · {Describe(pair.Voltage.Request)} + {CommandText(pair.Current.Request)}";

            return new EvBatterySource(
                label,
                false,
                pair.Voltage.Request,
                pair.Voltage.Signal,
                pair.Current.Request,
                pair.Current.Signal,
                soc?.Request,
                soc?.Signal,
                false);
        }

        Trace("No brand identifier answered with a realistic pack voltage/current");
        return null;
    }

    // ------------------------------------------------------------------
    // Known cars (identifiers checked against GM Volt / Bolt BECM data)
    // ------------------------------------------------------------------

    private sealed record KnownProfile(
        string Label,
        IReadOnlyList<(EvRequest Request, EvSignal Signal)> Voltages,
        IReadOnlyList<(EvRequest Request, EvSignal Signal)> Currents,
        IReadOnlyList<(EvRequest Request, EvSignal Signal)> Socs,
        double MinVolts,
        double MaxVolts);

    private static EvRequest Did(int tx, int rx, int did) => new(
        $"22|{tx:X8}|--|{did:X4}",
        new ServiceDiagnosticAddress(tx, rx, false, null, "11-bit CAN", $"TX 0x{tx:X3}"),
        0x22,
        did,
        $"{tx:X3}");

    private static IEnumerable<KnownProfile> KnownProfiles(VehicleInfo? vehicle)
    {
        if (vehicle is null)
            yield break;

        var bolt = GmVoltBoltBattery.IsBolt(vehicle.Manufacturer, vehicle.Model);
        if (bolt is null)
            yield break;

        // Pack voltage / current / SoC as published by the GM community PID
        // lists (Torque) and OBDb. Values are accepted only when they decode
        // to a realistic pack voltage and current.
        var voltVoltage = (Did(0x7E4, 0x7EC, 0x2429), new EvSignal("HV battery voltage", 0, 16, false, 1, 64, 0));
        var voltCurrent = (Did(0x7E4, 0x7EC, 0x2414), new EvSignal("HV battery current", 0, 16, true, 1, 20, 0));
        var becmCurrent = (Did(0x7E7, 0x7EF, 0x40D4), new EvSignal("HV battery current (BECM)", 0, 16, true, 1, 20, 0));
        var voltSocRaw = (Did(0x7E4, 0x7EC, 0x43AF), new EvSignal("HV state of charge", 0, 16, false, 100, 65535, 0));
        var voltSocShown = (Did(0x7E4, 0x7EC, 0x8334), new EvSignal("Displayed state of charge", 0, 8, false, 100, 255, 0));
        var boltVoltage = (Did(0x7E4, 0x7EC, 0x432D), new EvSignal("HV battery voltage", 0, 16, false, 0.52, 1, 0));
        var boltSoc = (Did(0x7E4, 0x7EC, 0x432F), new EvSignal("HV state of charge", 0, 8, false, 20, 51, 0));

        if (bolt == true)
        {
            yield return new KnownProfile(
                "Chevrolet Bolt · BECM",
                new[] { boltVoltage, voltVoltage },
                new[] { becmCurrent, voltCurrent },
                new[] { boltSoc, voltSocShown },
                250, 450);
        }
        else
        {
            yield return new KnownProfile(
                "Chevrolet Volt · HV battery",
                new[] { voltVoltage, boltVoltage },
                new[] { voltCurrent, becmCurrent },
                new[] { voltSocShown, voltSocRaw },
                250, 450);
        }
    }

    private async Task<EvBatterySource?> TryKnownSourcesAsync(VehicleInfo? vehicle, CancellationToken token)
    {
        foreach (var profile in KnownProfiles(vehicle))
        {
            Trace($"Known profile: {profile.Label}");
            (EvRequest Request, EvSignal Signal)? voltage = null;
            foreach (var candidate in profile.Voltages)
            {
                var data = await SafeReadAsync(candidate.Request, token).ConfigureAwait(false);
                Trace($"{Describe(candidate.Request)}: {(data is null ? "no answer" : Convert.ToHexString(data))}");
                if (data is not null && EvSignalDecoder.TryDecode(candidate.Signal, data, out var volts) &&
                    volts >= profile.MinVolts && volts <= profile.MaxVolts)
                {
                    Trace($"voltage {volts:0.0} V");
                    voltage = candidate;
                    break;
                }
            }

            if (voltage is null)
                continue;

            (EvRequest Request, EvSignal Signal)? current = null;
            foreach (var candidate in profile.Currents)
            {
                var data = await SafeReadAsync(candidate.Request, token).ConfigureAwait(false);
                Trace($"{Describe(candidate.Request)}: {(data is null ? "no answer" : Convert.ToHexString(data))}");
                if (data is not null && EvSignalDecoder.TryDecodeCurrent(candidate.Signal, data, out var amps) && Math.Abs(amps) < 500)
                {
                    Trace($"current {amps:0.0} A");
                    current = candidate;
                    break;
                }
            }

            if (current is null)
                continue;

            (EvRequest Request, EvSignal Signal)? soc = null;
            foreach (var candidate in profile.Socs)
            {
                var data = await SafeReadAsync(candidate.Request, token).ConfigureAwait(false);
                if (data is not null && EvSignalDecoder.TryDecode(candidate.Signal, data, out var percent) && percent is > 0 and <= 100)
                {
                    soc = candidate;
                    break;
                }
            }

            return new EvBatterySource(
                $"{profile.Label} · {Describe(voltage.Value.Request)} + {CommandText(current.Value.Request)}",
                false,
                voltage.Value.Request,
                voltage.Value.Signal,
                current.Value.Request,
                current.Value.Signal,
                soc?.Request,
                soc?.Signal,
                false);
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Reading while driving
    // ------------------------------------------------------------------

    public async Task<EvBatteryReading?> ReadAsync(EvBatterySource source, bool includeSoc, CancellationToken token)
    {
        if (source.IsStandard)
        {
            var standard = await ReadStandardAsync(token).ConfigureAwait(false);
            if (standard is not { } s)
                return null;

            double? soc = null;
            if (includeSoc && source.StandardSoc)
                soc = EvSignalDecoder.DecodeStandardSoc(await TryReadMode01Async(0x5B, token).ConfigureAwait(false));
            return new EvBatteryReading(s.Volts, s.Amps, soc);
        }

        if (source.VoltageRequest is null || source.Voltage is null ||
            source.CurrentRequest is null || source.Current is null)
            return null;

        var voltageData = await SafeReadAsync(source.VoltageRequest, token).ConfigureAwait(false);
        if (voltageData is null || !EvSignalDecoder.TryDecodeVoltage(source.Voltage, voltageData, out var volts))
            return null;

        var currentData = source.CurrentRequest.Key == source.VoltageRequest.Key
            ? voltageData
            : await SafeReadAsync(source.CurrentRequest, token).ConfigureAwait(false);
        if (currentData is null || !EvSignalDecoder.TryDecodeCurrent(source.Current, currentData, out var amps))
            return null;

        double? percent = null;
        if (source.SocRequest is not null && source.Soc is not null)
        {
            byte[]? socData = null;
            if (source.SocRequest.Key == source.VoltageRequest.Key)
                socData = voltageData;
            else if (source.SocRequest.Key == source.CurrentRequest.Key)
                socData = currentData;
            else if (includeSoc)
                socData = await SafeReadAsync(source.SocRequest, token).ConfigureAwait(false);

            if (socData is not null && EvSignalDecoder.TryDecode(source.Soc, socData, out var value) && value is >= 0 and <= 100)
                percent = value;
        }

        return new EvBatteryReading(volts, amps, percent);
    }

    /// <summary>State of charge only (used when current cannot be read).</summary>
    public async Task<double?> ReadSocOnlyAsync(VehicleInfo? vehicle, CancellationToken token)
    {
        var standard = EvSignalDecoder.DecodeStandardSoc(await TryReadMode01Async(0x5B, token).ConfigureAwait(false));
        if (standard is { } value)
            return value;

        foreach (var known in KnownProfiles(vehicle))
        {
            foreach (var (request, signal) in known.Socs)
            {
                var data = await SafeReadAsync(request, token).ConfigureAwait(false);
                if (data is not null && EvSignalDecoder.TryDecode(signal, data, out var percent) && percent is > 0 and <= 100)
                    return percent;
            }
        }

        if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.Manufacturer))
            return null;

        var candidates = await LoadCandidatesAsync(vehicle, token).ConfigureAwait(false);
        foreach (var item in candidates.Socs.Take(3))
        {
            var data = await SafeReadAsync(item.Request, token).ConfigureAwait(false);
            if (data is not null && EvSignalDecoder.TryDecode(item.Signal, data, out var percent) && percent is >= 0 and <= 100)
                return percent;
        }

        return null;
    }

    private async Task<(double Volts, double Amps)?> ReadStandardAsync(CancellationToken token)
    {
        var data = await TryReadMode01Async(0x9A, token).ConfigureAwait(false);
        return data is not null && EvSignalDecoder.TryDecodeStandard9A(data, out var volts, out var amps)
            ? (volts, amps)
            : null;
    }

    private async Task<byte[]?> TryReadMode01Async(int pid, CancellationToken token)
    {
        try
        {
            var raw = await _obd.ReadMode01PidQuickAsync(pid, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
                return null;
            return ExtractResponse(raw, 0x41, new[] { (byte)pid }, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]?> SafeReadAsync(EvRequest request, CancellationToken token)
    {
        try
        {
            return await ReadAsync(request, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]?> ReadAsync(EvRequest request, CancellationToken token)
    {
        var payload = request.ServiceId == 0x21
            ? new byte[] { 0x21, (byte)request.Identifier }
            : new byte[] { 0x22, (byte)(request.Identifier >> 8), (byte)request.Identifier };

        var raw = await _obd.ReadPhysicalCanReadOnlyAsync(request.Address, payload, RequestTimeout, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
            return null;

        var idBytes = request.ServiceId == 0x21
            ? new[] { (byte)request.Identifier }
            : new[] { (byte)(request.Identifier >> 8), (byte)request.Identifier };

        return ExtractResponse(raw, (byte)(request.ServiceId + 0x40), idBytes, request.Address.ReceiveCanId);
    }

    private byte[]? ExtractResponse(string raw, byte positive, byte[] idBytes, int? receiveCanId)
    {
        var candidates = new List<IReadOnlyList<byte[]>>();
        if (receiveCanId.HasValue)
            candidates.Add(ObdResponseParser.ParseCanFrames(raw, receiveCanId.Value));
        foreach (var id in ObdResponseParser.ExtractCanIdentifiers(raw))
            candidates.Add(ObdResponseParser.ParseCanFrames(raw, id));
        candidates.Add(ObdResponseParser.ParseCanFrames(raw));

        foreach (var frames in candidates)
        {
            if (frames.Count == 0 || frames[0].Length == 0)
                continue;

            byte[] message;
            try
            {
                message = (frames[0][0] >> 4) is 0 or 1
                    ? _isoTp.Reassemble(frames)
                    : frames.SelectMany(frame => frame).ToArray();
            }
            catch
            {
                continue;
            }

            if (Matches(message, positive, idBytes, out var data))
                return data;
        }

        // Adapter already removed the framing (headers off, "0: 41 9A …" lines).
        return Matches(ObdResponseParser.ParseHex(raw), positive, idBytes, out var plain) ? plain : null;
    }

    private static bool Matches(byte[] bytes, byte positive, byte[] idBytes, out byte[] data)
    {
        data = Array.Empty<byte>();
        for (var i = 0; i + idBytes.Length < bytes.Length; i++)
        {
            if (bytes[i] != positive)
                continue;

            var ok = true;
            for (var j = 0; j < idBytes.Length && ok; j++)
                ok = bytes[i + 1 + j] == idBytes[j];

            if (!ok)
                continue;

            data = bytes[(i + 1 + idBytes.Length)..];
            return data.Length > 0;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Catalog
    // ------------------------------------------------------------------

    private sealed record Candidate(EvRequest Request, EvSignal Signal, int Score);

    private sealed record Pair(Candidate Voltage, Candidate Current, int Score);

    private sealed record Candidates(
        IReadOnlyList<Candidate> Voltages,
        IReadOnlyList<Candidate> Currents,
        IReadOnlyList<Candidate> Socs);

    private sealed class Row
    {
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Header { get; set; }
        public string? CommandJson { get; set; }
        public string? Name { get; set; }
        public string? Unit { get; set; }
        public string? Metric { get; set; }
        public int BitLength { get; set; }
        public int BitOffset { get; set; }
        public string? Eax { get; set; }
        public string? FormatJson { get; set; }
        public string? RawJson { get; set; }
    }

    private sealed class Format
    {
        public double? Mul { get; set; }
        public double? Div { get; set; }
        public double? Add { get; set; }
        public bool? Sign { get; set; }
    }

    private static IEnumerable<Pair> BuildPairs(Candidates candidates)
    {
        var pairs = new List<Pair>();
        foreach (var voltage in candidates.Voltages)
        {
            foreach (var current in candidates.Currents)
            {
                var sameRequest = voltage.Request.Key == current.Request.Key;
                var sameEcu = voltage.Request.Header == current.Request.Header;
                pairs.Add(new Pair(voltage, current, voltage.Score + current.Score + (sameRequest ? 6 : sameEcu ? 3 : 0)));
            }
        }

        return pairs.OrderByDescending(pair => pair.Score).Take(24);
    }

    private async Task<Candidates> LoadCandidatesAsync(VehicleInfo vehicle, CancellationToken token)
    {
        var cacheKey = $"{vehicle.Manufacturer}|{vehicle.Model}";
        if (_cachedCandidates is not null && _cachedCandidatesKey == cacheKey)
            return _cachedCandidates;

        await _catalog.EnsureLoadedAsync(token).ConfigureAwait(false);

        var connection = new SQLiteAsyncConnection(_catalog.DatabasePath, SQLiteOpenFlags.ReadOnly);
        var rows = new List<Row>();
        try
        {
            foreach (var make in MakeFamily(vehicle.Manufacturer))
            {
                rows.AddRange(await connection.QueryAsync<Row>(
                    """
                    SELECT make AS Make, model AS Model, header AS Header, command_json AS CommandJson,
                           name AS Name, unit AS Unit, suggested_metric AS Metric,
                           bit_length AS BitLength, bit_offset AS BitOffset,
                           eax AS Eax, format_json AS FormatJson, raw_json AS RawJson
                    FROM pid_definitions
                    WHERE lower(make) = lower(?)
                      AND pid IN ('22', '21')
                      AND unit IN ('volts', 'amps', 'percent');
                    """,
                    make).ConfigureAwait(false));
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        var model = Normalize(vehicle.Model);
        var voltages = new List<Candidate>();
        var currents = new List<Candidate>();
        var socs = new List<Candidate>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || row.BitLength is < 8 or > 32 || row.BitOffset < 0)
                continue;

            var name = row.Name;
            var metric = row.Metric ?? string.Empty;
            var unit = (row.Unit ?? string.Empty).ToLowerInvariant();
            var kind = unit switch
            {
                "volts" when metric == "tractionBatteryVoltage" ||
                             (VoltageName.IsMatch(name) && !VoltageExclude.IsMatch(name)) => 'V',
                "amps" when metric == "tractionBatteryCurrent" ||
                            (CurrentName.IsMatch(name) && !CurrentExclude.IsMatch(name)) => 'I',
                "percent" when metric == "stateOfCharge" ||
                               (SocName.IsMatch(name) && !SocExclude.IsMatch(name)) => 'S',
                _ => ' '
            };
            if (kind == ' ')
                continue;

            if (!TryParseCommand(row.CommandJson, out var serviceId, out var identifier))
                continue;
            if (!ObdbDiagnosticAddressResolver.TryResolve(row.Header, row.Eax, row.RawJson, out var address, out _))
                continue;

            var format = ParseFormat(row.FormatJson);
            var signal = new EvSignal(
                name,
                row.BitOffset,
                row.BitLength,
                format?.Sign ?? false,
                format?.Mul ?? 1d,
                format?.Div ?? 1d,
                format?.Add ?? 0d);

            var header = (row.Header ?? string.Empty).ToUpperInvariant();
            var request = new EvRequest(
                $"{serviceId:X2}|{address.TransmitCanId:X8}|{address.ExtendedAddress?.ToString("X2") ?? "--"}|{identifier:X4}",
                address,
                serviceId,
                identifier,
                header);

            var rowModel = Normalize(row.Model);
            var exact = model.Length > 0 && rowModel.Length > 0 &&
                        (rowModel == model || rowModel.Contains(model) || model.Contains(rowModel));

            var lower = name.ToLowerInvariant();
            var score = (exact ? 10 : 0) +
                        (metric is "tractionBatteryVoltage" or "tractionBatteryCurrent" or "stateOfCharge" ? 4 : 0) +
                        (lower.Contains("hv") || lower.Contains("high voltage") || lower.Contains("traction") ? 2 : 0) +
                        // A battery current must be able to go negative (regeneration).
                        (kind == 'I' && !signal.Signed && Math.Abs(signal.Add) < double.Epsilon ? -1 : 0) -
                        (lower.Contains("display") ? 1 : 0);

            var candidate = new Candidate(request, signal, score);
            switch (kind)
            {
                case 'V': voltages.Add(candidate); break;
                case 'I': currents.Add(candidate); break;
                default: socs.Add(candidate); break;
            }
        }

        static IReadOnlyList<Candidate> Distinct(List<Candidate> list) => list
            .GroupBy(item => $"{item.Request.Key}|{item.Signal.BitOffset}|{item.Signal.BitLength}|{item.Signal.Mul}|{item.Signal.Div}|{item.Signal.Add}")
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ToArray();

        var result = new Candidates(Distinct(voltages), Distinct(currents), Distinct(socs));
        _cachedCandidates = result;
        _cachedCandidatesKey = cacheKey;
        return result;
    }

    private static bool TryParseCommand(string? json, out int serviceId, out int identifier)
    {
        serviceId = 0;
        identifier = 0;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var (service, length) in new[] { ("22", 4), ("21", 2) })
            {
                if (doc.RootElement.TryGetProperty(service, out var element) &&
                    element.ValueKind == JsonValueKind.String &&
                    element.GetString() is { } text &&
                    text.Length == length &&
                    int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out identifier))
                {
                    serviceId = service == "22" ? 0x22 : 0x21;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static Format? ParseFormat(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Format>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CommandText(EvRequest request) => request.ServiceId == 0x21
        ? $"21 {request.Identifier:X2}"
        : $"22 {request.Identifier:X4}";

    private static string Describe(EvRequest request)
    {
        var builder = new StringBuilder();
        builder.Append(string.IsNullOrWhiteSpace(request.Header) ? $"{request.Address.TransmitCanId:X3}" : request.Header);
        builder.Append(" · ");
        builder.Append(CommandText(request));
        return builder.ToString();
    }

    private static string Normalize(string? value) =>
        new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static IReadOnlyList<string> MakeFamily(string manufacturer)
    {
        var value = Normalize(manufacturer);
        string[][] families =
        {
            new[] { "Volkswagen", "Audi", "Seat", "Skoda", "Porsche", "Cupra" },
            new[] { "BMW", "MINI", "Rolls" },
            new[] { "Toyota", "Lexus", "Daihatsu", "Mazda", "Subaru" },
            new[] { "Hyundai", "Kia", "Genesis" },
            new[] { "Ford", "Lincoln" },
            new[] { "Nissan", "NissanInfiniti", "Infiniti", "Renault", "Dacia" },
            new[] { "Chevrolet", "GMC", "Buick", "Cadillac" },
            new[] { "VauxhallOpel", "Opel", "Vauxhall" },
            new[] { "Honda", "Acura" },
            new[] { "Mercedes", "Smart" },
            new[] { "Jaguar", "Land" },
            new[] { "MG" },
            new[] { "Haval" },
            new[] { "Aiways" },
            new[] { "Jeep" },
            new[] { "Suzuki" }
        };

        foreach (var family in families)
        {
            if (family.Any(item => value.Contains(Normalize(item)) || (Normalize(item).Contains(value) && value.Length >= 3)))
                return family;
        }

        return new[] { manufacturer.Trim() };
    }
}
