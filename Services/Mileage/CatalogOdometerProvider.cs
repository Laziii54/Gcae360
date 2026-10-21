using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.ServiceProcedures;
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
///
/// Besides the current copies, the same catalog lists records the modules keep of the past (the odometer at the last
/// adaptation, at a warning, at a TPMS event; an installation record of the module). Those are read after the current
/// values, kept apart (see <see cref="MileageSignalClassifier"/>), and compared only with the current readings.
/// </summary>
public sealed class CatalogOdometerProvider : IActualMileageProvider
{
    private const int MaximumRequests = 16;

    // Records are a bounded second pass: the current odometer values come first and are never crowded out.
    private const int MaximumRecordRequests = 10;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1800);

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

        // The replies of this scan, by request: a record signal that shares its identifier with a current-odometer signal
        // is decoded from the reply that was already received instead of asking the module again.
        var replies = new Dictionary<string, (byte[]? Data, OdometerDecodeFailure Failure, int? Responder, MileageReadObservation? Observed)>();

        // One request per module/identifier; the first signal that decodes is used.
        foreach (var group in signals
                     .Where(signal => signal.Class == MileageSignalClass.TotalOdometer)
                     .GroupBy(signal => signal.Key)
                     .Take(MaximumRequests))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = group.First();
            var method = $"0x{first.ServiceId:X2} 0x{first.Identifier:X4} @ {first.Header}";

            (byte[]? Data, OdometerDecodeFailure Failure, int? Responder, MileageReadObservation? Observed) reply;
            try
            {
                reply = await ReadAsync(first, cancellationToken).ConfigureAwait(false);
                replies[group.Key] = reply;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The outcome stays "not responding"; the log says what kind of failure it was (see MileageReadDiagnosis).
                MileageTrace.Add(MileageReadDiagnosis.ErrorLine(method, ex, records: false, linkDown: !_obd.IsAdapterConnected));
                MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.ModuleNotResponding, MileageReadDiagnosis.ErrorDetail(ex, linkDown: !_obd.IsAdapterConnected), assessment.Status, assessment.Reason));
                continue;
            }

            if (reply.Data is null)
            {
                MileageTrace.Add(MileageReadDiagnosis.FailureLine(method, reply.Failure, reply.Observed!, records: false));
                MileageTrace.Attempt(new MileageAttempt(Name, method, reply.Failure switch
                {
                    OdometerDecodeFailure.Negative => MileageAttemptOutcome.ReadingUnavailable,
                    OdometerDecodeFailure.NotPositiveForRequest => MileageAttemptOutcome.InvalidReading,
                    _ => MileageAttemptOutcome.ModuleNotResponding
                }, MileageReadDiagnosis.FailureDetail(reply.Failure == OdometerDecodeFailure.ForeignReply
                    // Frames came back, but from another module: not an answer from the one that was asked.
                    ? $"Reply came from 0x{reply.Responder:X3}, not from this module; not used."
                    : reply.Failure.ToString(), reply.Observed!), assessment.Status, assessment.Reason));
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
                    evidence.Add(Build(signal, decoded, method, MileageReadStatus.Implausible, reply.Responder, reply.Data));
                    MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.InvalidReading,
                        MileageReadDiagnosis.FailureDetail("Implausible value.", MileageReadDiagnosis.OfDecodeFailure(OdometerDecodeFailure.Implausible)), assessment.Status, assessment.Reason));
                    decodedAny = true;
                    break;
                }

                if (!decoded.IsValue)
                {
                    lastFailure = decoded;
                    continue;
                }

                evidence.Add(Build(signal, decoded, method, MileageReadStatus.Ok, reply.Responder, reply.Data));
                // The log also says when the value came from another ECU than the addressed module, or from a definition under test.
                var valueNote = MileageReadDiagnosis.OfDecodedValue(signal.UnderTest, signal.Address.TransmitCanId, signal.Address.ReceiveCanId, reply.Responder);
                MileageTrace.Add(MileageReadDiagnosis.ValueLine(method, decoded.Km!.Value, valueNote));
                MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.Read, MileageReadDiagnosis.ValueDetail(valueNote), assessment.Status, assessment.Reason));
                decodedAny = true;
                break;
            }

            if (decodedAny && stopAfterFirst)
                break;

            if (!decodedAny)
            {
                var undecoded = MileageReadDiagnosis.OfDecodeFailure(lastFailure?.Failure ?? OdometerDecodeFailure.None);
                MileageTrace.Add(MileageReadDiagnosis.UndecodedLine(method, lastFailure?.Failure, Convert.ToHexString(reply.Data.Take(8).ToArray()), undecoded));
                MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.InvalidReading,
                    MileageReadDiagnosis.FailureDetail(lastFailure?.Failure.ToString() ?? "Undecodable", undecoded), assessment.Status, assessment.Reason));
            }
        }

        if (!stopAfterFirst)
            await ReadRecordsAsync(signals, replies, evidence, assessment, cancellationToken).ConfigureAwait(false);

        return evidence;
    }

    /// <summary>
    /// Second pass: the records the modules keep of the past. Read-only like the first pass, bounded, and independent of it:
    /// a module that does not answer here never erases a current reading, and a record that cannot be decoded is only logged.
    /// </summary>
    private async Task ReadRecordsAsync(
        IReadOnlyList<OdometerSignal> signals,
        Dictionary<string, (byte[]? Data, OdometerDecodeFailure Failure, int? Responder, MileageReadObservation? Observed)> replies,
        List<MileageEvidence> evidence,
        MileageSourceAssessment assessment,
        CancellationToken cancellationToken)
    {
        var groups = signals
            .Where(signal => signal.Class != MileageSignalClass.TotalOdometer)
            .GroupBy(signal => signal.Key)
            .Take(MaximumRecordRequests)
            .ToList();
        if (groups.Count == 0)
            return;

        MileageTrace.Add($"module records: {groups.Count} identifier(s) listed for this model");
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = group.First();
            var method = $"0x{first.ServiceId:X2} 0x{first.Identifier:X4} @ {first.Header}";

            (byte[]? Data, OdometerDecodeFailure Failure, int? Responder, MileageReadObservation? Observed) reply;
            if (!replies.TryGetValue(group.Key, out reply))
            {
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
                    MileageTrace.Add(MileageReadDiagnosis.ErrorLine(method, ex, records: true, linkDown: !_obd.IsAdapterConnected));
                    MileageTrace.Attempt(new MileageAttempt(Name + " · records", method, MileageAttemptOutcome.ModuleNotResponding, MileageReadDiagnosis.ErrorDetail(ex, linkDown: !_obd.IsAdapterConnected), assessment.Status, assessment.Reason));
                    continue;
                }
            }

            if (reply.Data is null)
            {
                MileageTrace.Add(MileageReadDiagnosis.FailureLine(method, reply.Failure, reply.Observed!, records: true));
                MileageTrace.Attempt(new MileageAttempt(Name + " · records", method, reply.Failure switch
                {
                    OdometerDecodeFailure.Negative => MileageAttemptOutcome.ReadingUnavailable,
                    OdometerDecodeFailure.NotPositiveForRequest => MileageAttemptOutcome.InvalidReading,
                    _ => MileageAttemptOutcome.ModuleNotResponding
                }, MileageReadDiagnosis.FailureDetail(reply.Failure == OdometerDecodeFailure.ForeignReply
                    ? $"Reply came from 0x{reply.Responder:X3}, not from this module; not used."
                    : reply.Failure.ToString(), reply.Observed!), assessment.Status, assessment.Reason));
                continue;
            }

            var read = 0;
            foreach (var signal in group.Where(item => item.Class != MileageSignalClass.TotalOdometer))
            {
                var decoded = OdometerResponseDecoder.Decode(
                    new OdometerSignalSpec(signal.BitOffset, signal.BitLength, signal.Mul, signal.Div, signal.Add, signal.Miles, signal.Meters),
                    reply.Data);

                // Nothing recorded (all bits set, or zero) is silence, not a value.
                if (decoded.Failure == OdometerDecodeFailure.NotAvailable ||
                    (decoded.Failure == OdometerDecodeFailure.Implausible && decoded.Km is 0))
                {
                    MileageTrace.Add($"{method}: {signal.SignalName}: no record");
                    continue;
                }

                if (decoded.Failure == OdometerDecodeFailure.Implausible && decoded.Km.HasValue)
                {
                    evidence.Add(Build(signal, decoded, method, MileageReadStatus.Implausible, reply.Responder, reply.Data));
                    continue;
                }

                if (!decoded.IsValue)
                    continue;

                evidence.Add(Build(signal, decoded, method, MileageReadStatus.Ok, reply.Responder, reply.Data));
                MileageTrace.Add($"{method}: {signal.SignalName}: {decoded.Km:0} km");
                read++;
            }

            MileageTrace.Attempt(new MileageAttempt(Name + " · records", method,
                read > 0 ? MileageAttemptOutcome.Read : MileageAttemptOutcome.ReadingUnavailable,
                read > 0 ? string.Empty : "No record stored.", assessment.Status, assessment.Reason));
        }
    }

    private MileageEvidence Build(OdometerSignal signal, OdometerDecodeResult decoded, string method, MileageReadStatus status, int? responder, byte[]? data = null)
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
            Measurement = signal.Class switch
            {
                MileageSignalClass.OdometerAtEvent => MileageMeasurement.OdometerAtEvent,
                MileageSignalClass.ModuleInstallRecord => MileageMeasurement.ModuleInstallRecord,
                MileageSignalClass.ComponentDistance => MileageMeasurement.OtherDistance,
                _ => MileageMeasurement.TotalOdometer
            },
            EventKind = signal.EventKind,
            DefinitionUnderTest = signal.UnderTest,
            RecordedReplyForModelYear = signal.RecordedForYear,
            SignalName = signal.SignalName,
            RawHex = data is null ? string.Empty : Convert.ToHexString(data.Take(64).ToArray()),
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
        bool Meters = false,
        MileageSignalClass Class = MileageSignalClass.TotalOdometer,
        MileageEventKind EventKind = MileageEventKind.None,
        bool UnderTest = false,
        bool RecordedForYear = false)
    {
        public bool IsMetric => !Miles;
    }

    private sealed class Row
    {
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Header { get; set; }
        public string? CommandJson { get; set; }
        public string? ParameterId { get; set; }
        public string? Name { get; set; }
        public string? Unit { get; set; }
        public int BitLength { get; set; }
        public int BitOffset { get; set; }
        public string? Eax { get; set; }
        public string? FormatJson { get; set; }
        public string? RawJson { get; set; }
        public int Debug { get; set; }
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
                           parameter_id AS ParameterId, name AS Name, unit AS Unit, bit_length AS BitLength, bit_offset AS BitOffset,
                           eax AS Eax, format_json AS FormatJson, raw_json AS RawJson, debug AS Debug
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

        // What OBDb says per model year (see ObdbYearEvidence). Without a known model year, or for a request the evidence does not list,
        // nothing is concluded and the catalog's own flag stands, as before.
        var yearEvidence = await ObdbYearEvidenceStore.GetAsync(token).ConfigureAwait(false);
        var modelYear = ModelYearOf(vehicle);

        foreach (var row in rows)
        {
            // What kind of distance is it? Differences, remaining distances and averages are dropped; history and
            // installation records and component counters are kept apart from the current odometer copies.
            var signalClass = MileageSignalClassifier.Classify(row.Name, out var eventKind);
            if (signalClass is MileageSignalClass.NotMileage or MileageSignalClass.Excluded)
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

            var evidenceForYear = yearEvidence.For(row.Make, row.Model, row.ParameterId, modelYear);
            if (evidenceForYear.Known && !evidenceForYear.Applicable)
                continue; // OBDb: this request does not apply to the vehicle's model year, so it is not sent

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
                unit.StartsWith("meter", StringComparison.Ordinal),
                signalClass,
                eventKind,
                evidenceForYear.Known ? evidenceForYear.UnderTest : row.Debug != 0,
                evidenceForYear.Known && evidenceForYear.RecordedReply));
        }

        // Exact model first, then the identifiers most models of the brand
        // share (asked first because they answer on most cars).
        var shared = signals.GroupBy(signal => signal.Key).ToDictionary(group => group.Key, group => group.Count());
        return signals
            .OrderByDescending(signal => signal.ExactModel)
            .ThenByDescending(signal => shared[signal.Key])
            .ToArray();
    }

    /// <summary>The model year when the app knows it (a year the vehicle information has no origin for is not used).</summary>
    private static int? ModelYearOf(VehicleInfo vehicle) =>
        vehicle.YearOrigin == VehicleDataOrigin.Unknown ? null : ServiceMatching.ParseYear(vehicle.Year);

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

    private async Task<(byte[]? Data, OdometerDecodeFailure Failure, int? Responder, MileageReadObservation? Observed)> ReadAsync(OdometerSignal signal, CancellationToken token)
    {
        var request = signal.ServiceId == 0x21
            ? new byte[] { 0x21, (byte)signal.Identifier }
            : new byte[] { 0x22, (byte)(signal.Identifier >> 8), (byte)signal.Identifier };

        var raw = await _obd.ReadPhysicalCanReadOnlyAsync(signal.Address, request, RequestTimeout, token).ConfigureAwait(false);
        var data = OdometerResponseDecoder.ExtractPayload(raw, signal.ServiceId, signal.Identifier, signal.Address.ReceiveCanId, out var failure, out var responder);
        // What the reply showed, in words for the log: computed from the same text, it changes nothing about the read.
        var observed = data is null
            ? MileageReadDiagnosis.OfReply(raw, signal.Address.ReceiveCanId, signal.ServiceId, signal.Identifier)
            : null;
        return (data, failure, responder, observed);
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
