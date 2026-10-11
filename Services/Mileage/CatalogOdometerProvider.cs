using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;
using SQLite;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Reads the odometer stored in several modules of the car (instrument
/// cluster, engine ECU, transmission, …) using the odometer identifiers of the
/// bundled OBDb catalog (~240 signals, VW/Audi, BMW, Toyota, Hyundai/Kia,
/// Ford, Nissan, Mazda, Honda, Subaru, Opel, GM …).
///
/// Each module may keep its own copy of the distance; the Mileage page shows what each one reports and
/// compares them without deciding which is right. Only identifiers listed for THIS model are requested.
/// Read-only (0x22 / 0x21); a value is used only after an exact positive reply from the car.
/// </summary>
public sealed class CatalogOdometerProvider : IActualMileageProvider
{
    private const int MaximumRequests = 16;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1800);

    private static readonly Regex OdometerName = new(
        @"odometer|total distance|mileage",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Distance since …", adaptation readings etc. are not the car's total distance.
    private static readonly Regex NotTotal = new(
        @"since|after|adaptation|kiss|injector|fault|trip|range|service|reset|last|learn|regen|oil|filter|remaining|until|to empty",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DtcCatalogService _catalog;
    private readonly OBDService _obd;

    public string Name => "Multi-module odometer";

    public CatalogOdometerProvider(DtcCatalogService catalog, OBDService obd)
    {
        _catalog = catalog;
        _obd = obd;
    }

    public Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(vehicle, false, cancellationToken);

    /// <summary>
    /// First module that returns a valid odometer (Service page: the current
    /// distance is enough, the comparison of modules is the Mileage page's job).
    /// </summary>
    public async Task<MileageEvidence?> ReadFirstAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default) =>
        (await ReadCoreAsync(vehicle, true, cancellationToken).ConfigureAwait(false)).FirstOrDefault(e => e.Status == MileageReadStatus.Ok);

    private async Task<IReadOnlyList<MileageEvidence>> ReadCoreAsync(
        VehicleInfo vehicle,
        bool stopAfterFirst,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(vehicle.Manufacturer))
        {
            MileageTrace.Add("მწარმოებელი უცნობია — მოდულების ოდომეტრი ვერ მოიძებნება");
            return Array.Empty<MileageEvidence>();
        }

        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var allSignals = await LoadSignalsAsync(vehicle, cancellationToken).ConfigureAwait(false);

        // Only identifiers the catalog lists for THIS model are requested. An identifier that another model of the same
        // brand answers is not evidence that this car supports it, so it is never tried.
        var signals = allSignals.Where(signal => signal.ExactModel).ToList();
        var modelKnown = !string.IsNullOrWhiteSpace(vehicle.Model) &&
                         !vehicle.Model.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        var assessment = MileageSourceCompatibility.Evaluate(
            MileageSourceFamily.CatalogUdsRead,
            new MileageSourceFacts(true, modelKnown, _obd.IsInitialized, _obd.IsCanProtocol, null,
                signals.Select(signal => signal.Key).Distinct().Count(), false, 0));

        if (!assessment.ShouldTry)
        {
            MileageTrace.Add($"{vehicle.Manufacturer} {vehicle.Model}: module odometer not requested ({assessment.Reason}; {allSignals.Count} brand-level signals ignored)");
            MileageTrace.Attempt(new MileageAttempt(Name, "UDS 22/21", MileageAttemptOutcome.NotTried, string.Empty, assessment.Status, assessment.Reason));
            return Array.Empty<MileageEvidence>();
        }

        var evidence = new List<MileageEvidence>();
        MileageTrace.Add($"{vehicle.Manufacturer} {vehicle.Model}: {signals.Select(s => s.Key).Distinct().Count()} module address(es) listed for this model");

        // One request per module/identifier; the first signal that decodes is used.
        foreach (var group in signals
                     .GroupBy(signal => signal.Key)
                     .Take(MaximumRequests))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = group.First();
            var method = $"0x{first.ServiceId:X2} 0x{first.Identifier:X4} @ {first.Header}";

            (byte[]? Data, OdometerDecodeFailure Failure, int? Responder) reply;
            try
            {
                reply = await ReadAsync(first, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MileageTrace.Add($"{method}: error ({ex.GetType().Name})");
                MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.ModuleNotResponding, ex.GetType().Name, assessment.Status, assessment.Reason));
                continue;
            }

            if (reply.Data is null)
            {
                MileageTrace.Add($"{method}: {reply.Failure}");
                MileageTrace.Attempt(new MileageAttempt(Name, method, reply.Failure switch
                {
                    OdometerDecodeFailure.Negative => MileageAttemptOutcome.ReadingUnavailable,
                    OdometerDecodeFailure.NotPositiveForRequest => MileageAttemptOutcome.InvalidReading,
                    _ => MileageAttemptOutcome.ModuleNotResponding
                }, reply.Failure == OdometerDecodeFailure.ForeignReply
                    // Frames came back, but from another module: not an answer from the one that was asked.
                    ? $"Reply came from 0x{reply.Responder:X3}, not from this module; not used."
                    : reply.Failure.ToString(), assessment.Status, assessment.Reason));
                continue;
            }

            var decodedAny = false;
            OdometerDecodeResult? lastFailure = null;

            foreach (var signal in group.OrderByDescending(item => item.IsMetric))
            {
                var decoded = OdometerResponseDecoder.Decode(
                    new OdometerSignalSpec(signal.BitOffset, signal.BitLength, signal.Mul, signal.Div, signal.Add, signal.Miles, signal.Meters),
                    reply.Data);

                if (decoded.Failure == OdometerDecodeFailure.Implausible && decoded.Km.HasValue)
                {
                    // The module answered with a value nobody should believe: keep it visible for review, never use it.
                    evidence.Add(Build(signal, decoded, method, MileageReadStatus.Implausible, reply.Responder));
                    MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.InvalidReading, "Implausible value.", assessment.Status, assessment.Reason));
                    decodedAny = true;
                    break;
                }

                if (!decoded.IsValue)
                {
                    lastFailure = decoded;
                    continue;
                }

                evidence.Add(Build(signal, decoded, method, MileageReadStatus.Ok, reply.Responder));
                MileageTrace.Add($"{method}: {decoded.Km:0} km");
                MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.Read, string.Empty, assessment.Status, assessment.Reason));
                decodedAny = true;
                break;
            }

            if (decodedAny && stopAfterFirst)
                break;

            if (!decodedAny)
            {
                MileageTrace.Add($"{method}: reply could not be decoded ({lastFailure?.Failure}) {Convert.ToHexString(reply.Data.Take(8).ToArray())}");
                MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.InvalidReading,
                    lastFailure?.Failure.ToString() ?? "Undecodable", assessment.Status, assessment.Reason));
            }
        }

        return evidence;
    }

    private MileageEvidence Build(OdometerSignal signal, OdometerDecodeResult decoded, string method, MileageReadStatus status, int? responder)
    {
        // With a known reply id only that module is accepted (see OdometerResponseDecoder). Without one, the responder
        // is whoever echoed the identifier: it is named, and the value is not credited to the module that was merely
        // addressed when the standard ISO 15765-4 pair (request 0x7E0–0x7E7, reply +8) says it was somebody else.
        var otherResponder = OdometerResponseDecoder.IsResponderForeign(
            signal.Address.TransmitCanId, signal.Address.ReceiveCanId, responder);
        var ecu = otherResponder ? $"ECU 0x{responder:X3} (request was addressed to {signal.Header})" : ModuleName(signal);
        var detail = $"{signal.Address.Detail} · {signal.SignalName}" +
                     (responder is { } from ? $" · reply from 0x{from:X3}" : string.Empty);
        return new MileageEvidence(Name, ecu, decoded.Km ?? double.NaN, signal.ExactModel ? 0.8 : 0.7, detail)
        {
            Measurement = MileageMeasurement.TotalOdometer,
            Status = status,
            Unit = decoded.Unit,
            RawValue = decoded.RawValue,
            SourceKind = MileageSourceKind.PublicDefinition,
            Method = method
        };
    }

    // ------------------------------------------------------------------
    // Catalog
    // ------------------------------------------------------------------

    private sealed record OdometerSignal(
        string Key,
        string SignalName,
        ServiceDiagnosticAddress Address,
        int ServiceId,
        int Identifier,
        int BitOffset,
        int BitLength,
        double Mul,
        double Div,
        double Add,
        bool Miles,
        bool ExactModel,
        string Header,
        bool Meters = false)
    {
        public bool IsMetric => !Miles;
    }

    private sealed class Row
    {
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Header { get; set; }
        public string? CommandJson { get; set; }
        public string? Name { get; set; }
        public string? Unit { get; set; }
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
        public string? Unit { get; set; }
    }

    private async Task<IReadOnlyList<OdometerSignal>> LoadSignalsAsync(VehicleInfo vehicle, CancellationToken token)
    {
        await _catalog.EnsureLoadedAsync(token).ConfigureAwait(false);

        var makes = MakeFamily(vehicle.Manufacturer);
        var connection = new SQLiteAsyncConnection(_catalog.DatabasePath, SQLiteOpenFlags.ReadOnly);
        var rows = new List<Row>();
        try
        {
            foreach (var make in makes)
            {
                rows.AddRange(await connection.QueryAsync<Row>(
                    """
                    SELECT make AS Make, model AS Model, header AS Header, command_json AS CommandJson,
                           name AS Name, unit AS Unit, bit_length AS BitLength, bit_offset AS BitOffset,
                           eax AS Eax, format_json AS FormatJson, raw_json AS RawJson
                    FROM pid_definitions
                    WHERE lower(make) = lower(?)
                      AND pid IN ('22', '21')
                      AND (lower(name) LIKE '%odometer%' OR lower(name) LIKE '%total distance%' OR lower(name) LIKE '%mileage%');
                    """,
                    make).ConfigureAwait(false));
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        var model = Normalize(vehicle.Model);
        var signals = new List<OdometerSignal>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || !OdometerName.IsMatch(row.Name) || NotTotal.IsMatch(row.Name))
                continue;
            if (row.BitLength is < 16 or > 32 || row.BitOffset < 0)
                continue;
            if (!TryParseCommand(row.CommandJson, out var serviceId, out var identifier))
                continue;
            if (!ObdbDiagnosticAddressResolver.TryResolve(row.Header, row.Eax, row.RawJson, out var address, out _))
                continue;

            var format = ParseFormat(row.FormatJson);
            var unit = (format?.Unit ?? row.Unit ?? string.Empty).ToLowerInvariant();
            if (unit is not ("kilometers" or "kilometer" or "km" or "miles" or "mile" or "meters" or "meter"))
                continue;

            var rowModel = Normalize(row.Model);
            var exact = model.Length > 0 && rowModel.Length > 0 &&
                        (rowModel == model || rowModel.Contains(model) || model.Contains(rowModel));

            signals.Add(new OdometerSignal(
                $"{serviceId:X2}|{address.TransmitCanId:X8}|{address.ExtendedAddress?.ToString("X2") ?? "--"}|{identifier:X4}",
                row.Name,
                address,
                serviceId,
                identifier,
                row.BitOffset,
                row.BitLength,
                format?.Mul ?? 1d,
                format?.Div ?? 1d,
                format?.Add ?? 0d,
                unit.StartsWith("mile", StringComparison.Ordinal),
                exact,
                (row.Header ?? string.Empty).ToUpperInvariant(),
                unit.StartsWith("meter", StringComparison.Ordinal)));
        }

        // Exact model first, then the identifiers most models of the brand
        // share (asked first because they answer on most cars).
        var shared = signals.GroupBy(signal => signal.Key).ToDictionary(group => group.Key, group => group.Count());
        return signals
            .OrderByDescending(signal => signal.ExactModel)
            .ThenByDescending(signal => shared[signal.Key])
            .ToArray();
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
        catch
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
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Reading + decoding
    // ------------------------------------------------------------------

    private async Task<(byte[]? Data, OdometerDecodeFailure Failure, int? Responder)> ReadAsync(OdometerSignal signal, CancellationToken token)
    {
        var request = signal.ServiceId == 0x21
            ? new byte[] { 0x21, (byte)signal.Identifier }
            : new byte[] { 0x22, (byte)(signal.Identifier >> 8), (byte)signal.Identifier };

        var raw = await _obd.ReadPhysicalCanReadOnlyAsync(signal.Address, request, RequestTimeout, token).ConfigureAwait(false);
        var data = OdometerResponseDecoder.ExtractPayload(raw, signal.ServiceId, signal.Identifier, signal.Address.ReceiveCanId, out var failure, out var responder);
        return (data, failure, responder);
    }

    // ------------------------------------------------------------------
    // Names
    // ------------------------------------------------------------------

    private static string ModuleName(OdometerSignal signal)
    {
        var name = signal.SignalName.ToLowerInvariant();
        if (name.Contains("dash") || name.Contains("instrument"))
            return "Instrument cluster";
        if (name.Contains("transmission") || name.Contains("egs") || name.Contains("tcm"))
            return "Transmission";
        if (name.Contains("engine"))
            return "Engine ECU";

        return signal.Header switch
        {
            "7E0" => "Engine ECU",
            "7E1" => "Transmission",
            "714" or "720" or "7C6" or "743" or "7C0" or "6B4" => "Instrument cluster",
            _ => $"ECU {signal.Header}"
        };
    }

    private static string Normalize(string? value) =>
        new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static IReadOnlyList<string> MakeFamily(string manufacturer) =>
        CatalogMakes.For(manufacturer);
}
