using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.UDS;
using SQLite;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Reads maintenance/service values from the bundled OBDb-derived PID catalog.
///
/// Production behavior:
/// - VIN/profile verified manufacturer + model is required;
/// - debug/research definitions are NOT sent by normal Service refresh;
/// - only UDS 0x22 ReadDataByIdentifier is sent;
/// - 11-bit, DAxx 29-bit normal-fixed, and BMW extended-address read forms are
///   resolved conservatively; unsupported address forms are skipped;
/// - signals sharing one DID are read once and decoded together;
/// - a value exists only after an exact positive 0x62 DID echo;
/// - unsupported/no-response values are omitted instead of fabricated.
/// </summary>
public sealed partial class ServiceDataReaderService
{
    private const int MaximumRequestsPerRefresh = 14;

    private readonly DtcCatalogService _databaseCatalog;
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly OBDService _obd;
    // Optional test seam: when non-null, this delegate is used instead of the
    // real OBDService.ReadPhysicalCanReadOnlyAsync to allow lightweight tests
    // to simulate adapter responses without constructing a full OBDService.
    private readonly Func<ServiceDiagnosticAddress, ReadOnlyMemory<byte>, TimeSpan, CancellationToken, Task<string>>? _readPhysicalOverride;
    private readonly IsoTpReassembler _isoTp = new();

    public ServiceDataReaderService(
        DtcCatalogService databaseCatalog,
        VehicleProfileResolverService vehicleProfiles,
        OBDService obd)
    {
        _databaseCatalog = databaseCatalog;
        _vehicleProfiles = vehicleProfiles;
        _obd = obd;
    }

    // Internal constructor used by tests to inject a lightweight response delegate.
    internal ServiceDataReaderService(
        DtcCatalogService databaseCatalog,
        VehicleProfileResolverService vehicleProfiles,
        Func<ServiceDiagnosticAddress, ReadOnlyMemory<byte>, TimeSpan, CancellationToken, Task<string>> readPhysicalOverride)
    {
        _databaseCatalog = databaseCatalog;
        _vehicleProfiles = vehicleProfiles;
        _readPhysicalOverride = readPhysicalOverride;
        // Tests never reach the adapter: every physical read goes through the override.
        _obd = null!;
    }

    public Task<ServiceDataReadResult> ReadAsync(
        VehicleInfo vehicle,
        CancellationToken token = default) =>
        ReadCoreAsync(vehicle, includeResearchDefinitions: false, token);

    /// <summary>
    /// Diagnostic/capture helper only. Debug definitions may be attempted, but
    /// returned values remain marked ResearchCatalog and should not be presented
    /// as production-verified service data.
    /// </summary>
    public Task<ServiceDataReadResult> ReadResearchAsync(
        VehicleInfo vehicle,
        CancellationToken token = default) =>
        ReadCoreAsync(vehicle, includeResearchDefinitions: true, token);

    private async Task<ServiceDataReadResult> ReadCoreAsync(
        VehicleInfo vehicle,
        bool includeResearchDefinitions,
        CancellationToken token)
    {
        var resolved = _vehicleProfiles.Resolve(vehicle);
        _currentVehicleKey = VehicleKey(vehicle);
        _currentVin = IsFullVin(vehicle.VIN) ? vehicle.VIN.Trim().ToUpperInvariant() : string.Empty;
        _verifiedGmResetProfile = IsFirstGenerationChevroletVolt(resolved);
        _reviewedGmReset = FindReviewedGmOilLifeReset(resolved);

        // GM oil life (read-only). Tried before the identity gate: the
        // learned source and the GM oil-life identifiers are harmless 0x22 /
        // 0x1A reads, and a GM car whose VIN decode is incomplete should
        // still show its oil life.
        LastOilLifeProbeNote = string.Empty;
        ServiceMetricReading? gmOilLife = null;
        if (ShouldProbeGmOilLife(vehicle, resolved))
            gmOilLife = await TryReadFirstGenerationVoltOilLifeAsync(token).ConfigureAwait(false);

        // Sources the user confirmed against the dashboard with "Search"
        // (this exact car). They are read first and also work when the VIN
        // could not be decoded.
        var preferred = await LoadPreferredCandidatesAsync(vehicle, token).ConfigureAwait(false);

        if (!resolved.CanUseManufacturerSpecificDefinitions && preferred.Count == 0)
        {
            _readingSources.Clear();
            return gmOilLife is null
                ? new ServiceDataReadResult(
                    Array.Empty<ServiceMetricReading>(),
                    "IDENTITY REQUIRED",
                    resolved.VerificationMessage)
                : new ServiceDataReadResult(
                    new[] { gmOilLife },
                    "AVAILABLE",
                    "GM oil life read; other catalog values need a verified vehicle identity.");
        }

        await _databaseCatalog.EnsureLoadedAsync(token).ConfigureAwait(false);

        var loaded = resolved.CanUseManufacturerSpecificDefinitions
            ? await LoadCandidatesAsync(resolved, token).ConfigureAwait(false)
            : new CandidateLoadResult(Array.Empty<ServicePidCandidate>(), 0);
        var production = loaded.Candidates.Where(item => !item.Debug).ToArray();
        var research = loaded.Candidates.Where(item => item.Debug).ToArray();
        IReadOnlyList<ServicePidCandidate> selected = preferred
            .Concat(includeResearchDefinitions ? loaded.Candidates : production)
            .ToArray();
        var preferredGroups = new HashSet<string>(
            preferred.Select(item => $"{item.ServiceId:X2}|{AddressKey(item.Address, item.DataIdentifier)}"),
            StringComparer.OrdinalIgnoreCase);

        var readings = new List<ServiceMetricReading>();
        _readingSources.Clear();

        // Chevrolet Volt Gen-1 has a documented GM enhanced read-only oil-life
        // identifier (Mode/Service 0x22, DID 0x119F). We still accept a value
        // only after an exact positive 0x62 11 9F response from the connected
        // ECU. A missing/negative response is treated as unsupported; no value
        // is synthesized. This is deliberately read-only and does not imply a
        // verified reset/write command.
        if (gmOilLife is not null)
            readings.Add(gmOilLife);

        // Oil life already read through the learned GM source (e.g. Volt):
        // do not also send the brand-wide oil-life requests.
        if (readings.Any(item => item.Kind == ServiceMetricKind.OilLifePercent))
        {
            selected = selected
                .Where(item => !(item.BrandFallback && item.Kind == ServiceMetricKind.OilLifePercent))
                .ToArray();
        }

        if (selected.Count == 0 && readings.Count == 0)
        {
            return new ServiceDataReadResult(
                Array.Empty<ServiceMetricReading>(),
                research.Length > 0 ? "RESEARCH ONLY" : "NO PROFILE",
                research.Length > 0
                    ? $"{research.Length} research/debug service definition(s) match this model, but normal Service mode does not send them."
                    : "No matching read-only service metric exists in the local catalog for this exact vehicle model.")
            {
                ProductionCandidateCount = production.Length,
                ResearchCandidateCount = research.Length,
                UnsupportedAddressCount = loaded.UnsupportedAddressCount
            };
        }

        // One DID can contain many signals (e.g. Honda maintenance counters).
        // Read it once and decode every field from the same positive response.
        var groups = selected
            .GroupBy(item => $"{item.ServiceId:X2}|{AddressKey(item.Address, item.DataIdentifier)}", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => preferredGroups.Contains(group.Key))
            .ThenBy(group => group.Min(item => Priority(item.Kind)))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRequestsPerRefresh)
            .ToArray();

        var attempted = readings.Count > 0 ? 1 : 0;

        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var first = group.First();
            attempted++;

            try
            {
                var request = first.ServiceId == 0x21
                    ? new byte[] { 0x21, (byte)first.DataIdentifier }
                    : new byte[]
                    {
                        0x22,
                        (byte)(first.DataIdentifier >> 8),
                        (byte)(first.DataIdentifier & 0xFF)
                    };

                var raw = _readPhysicalOverride is not null
                    ? await _readPhysicalOverride(first.Address, request, TimeSpan.FromMilliseconds(2600), token).ConfigureAwait(false)
                    : await _obd.ReadPhysicalCanReadOnlyAsync(
                        first.Address,
                        request,
                        TimeSpan.FromMilliseconds(2600),
                        token).ConfigureAwait(false);

                if (!TryExtractDidData(
                        raw,
                        first.DataIdentifier,
                        first.Address.ReceiveCanId,
                        out var data,
                        first.ServiceId))
                {
                    continue;
                }

                foreach (var candidate in group)
                {
                    if (!TryDecodeValue(candidate, data, out var value) ||
                        !IsPlausible(candidate.Kind, candidate.Unit, value))
                    {
                        continue;
                    }

                    var evidence = candidate.Debug || candidate.BrandFallback
                        ? ServiceMetricEvidenceLevel.ResearchCatalog
                        : ServiceMetricEvidenceLevel.ProductionCatalog;

                    _readingSources[candidate.ParameterId] = new ReadingSource(candidate, data, group.Count());

                    readings.Add(new ServiceMetricReading(
                        candidate.ParameterId,
                        candidate.Kind,
                        candidate.Name,
                        value,
                        candidate.Unit,
                        $"REAL ECU READ · 0x{candidate.ServiceId:X2} ID 0x{candidate.DataIdentifier:X4} · {candidate.Address.Detail}",
                        candidate.SourceCatalog,
                        candidate.Address.TransmitCanId,
                        candidate.DataIdentifier,
                        candidate.Address.ReceiveCanId,
                        candidate.Address.Extended29Bit,
                        candidate.Address.ExtendedAddress,
                        evidence));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A catalog candidate that does not answer is not supported for
                // this exact vehicle/session/adapter. Never create fallback data.
            }
        }

        var distinct = readings
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => Priority(item.Kind))
            .ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var detail = distinct.Length > 0
            ? $"Read {distinct.Length} real service value(s) using {attempted} physical read request(s). Production candidates: {production.Length}. Research/debug definitions excluded: {(includeResearchDefinitions ? 0 : research.Length)}."
            : $"Catalog candidates existed, but no exact positive ECU response was returned. No value was guessed. Production candidates: {production.Length}; research/debug definitions excluded: {(includeResearchDefinitions ? 0 : research.Length)}.";

        return new ServiceDataReadResult(
            distinct,
            distinct.Length > 0 ? "AVAILABLE" : "NO RESPONSE",
            detail)
        {
            ProductionCandidateCount = production.Length,
            ResearchCandidateCount = research.Length,
            AttemptedRequestCount = attempted,
            UnsupportedAddressCount = loaded.UnsupportedAddressCount
        };
    }

    private static readonly string[] GmMakeKeys =
    {
        "chevrolet", "chevy", "gmc", "buick", "cadillac", "opel", "vauxhall", "holden", "pontiac", "saturn", "hummer"
    };

    private static bool IsGmMake(string? value)
    {
        var key = new string((value ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return key.Length > 0 && GmMakeKeys.Any(item => key.Contains(item, StringComparison.Ordinal));
    }

    /// <summary>
    /// GM oil-life reads are sent to GM-family cars only (verified or not),
    /// and to the exact Gen-1 Volt profile. Other makes never get them.
    /// </summary>
    private static bool ShouldProbeGmOilLife(VehicleInfo vehicle, ResolvedVehicleProfile resolved) =>
        IsFirstGenerationChevroletVolt(resolved) ||
        IsGmMake(vehicle.Manufacturer) ||
        IsGmMake(resolved.Manufacturer.Value) ||
        IsGmMake(resolved.SelectedBrand.Id) ||
        IsGmMake(resolved.SelectedBrand.DisplayName);

    private static bool IsFirstGenerationChevroletVolt(ResolvedVehicleProfile vehicle)
    {
        if (!vehicle.Manufacturer.IsVerified ||
            !vehicle.Model.IsVerified ||
            !vehicle.Year.IsVerified)
        {
            return false;
        }

        if (!vehicle.Manufacturer.Value.Equals(
                "Chevrolet",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!vehicle.Model.Value.Equals(
                "Volt",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(vehicle.Year.Value, out var year))
            return false;

        // Chevrolet Volt first generation: model years 2011–2015.
        return year is >= 2011 and <= 2015;
    }

    /// <summary>
    /// Short record of what the ECU answered to the last GM oil-life probe.
    /// Shown on the Service page when no value could be read, so the exact
    /// ECU reply can be checked.
    /// </summary>
    public string LastOilLifeProbeNote { get; private set; } = string.Empty;

    // Read-only GM oil-life identifiers, tried in order on the ECM
    // (TX 0x7E0 / RX 0x7E8):
    //  - 0x119F: classic GM "Engine Oil Life Remaining" (A*100/255);
    //  - 0x4DC0: newer GM "Engine oil life remaining" (OBDb, A*100/255).
    // Only 0x22 ReadDataByIdentifier is sent; a negative or missing reply is
    // treated as "not supported" and nothing is ever written.
    private static readonly int[] GmOilLifeDids = { 0x119F, 0x4DC0 };

    private async Task<ServiceMetricReading?> TryReadFirstGenerationVoltOilLifeAsync(
        CancellationToken token)
    {
        var address = new ServiceDiagnosticAddress(
            0x7E0,
            0x7E8,
            false,
            null,
            "11-bit CAN",
            "Chevrolet Volt ECM · TX 0x7E0 / RX 0x7E8");

        var notes = new List<string>();

        // A source found earlier by the discovery tool is tried first.
        var learned = LoadLearnedOilLifeSource();
        if (learned is not null)
        {
            try
            {
                var learnedAddress = new ServiceDiagnosticAddress(
                    learned.Value.Tx, learned.Value.Rx, false, null, "11-bit CAN",
                    $"Learned GM oil life · TX 0x{learned.Value.Tx:X3} / RX 0x{learned.Value.Rx:X3}");
                var raw = learned.Value.Service == 0x1A
                    ? await _obd.ReadGmlanDidFunctionalAsync(learned.Value.Did, TimeSpan.FromMilliseconds(2000), token).ConfigureAwait(false)
                    : learned.Value.Tx == 0x7DF
                    ? await _obd.ReadDidFunctionalAsync(learned.Value.Did, TimeSpan.FromMilliseconds(2000), token).ConfigureAwait(false)
                    : await _obd.ReadPhysicalCanReadOnlyAsync(
                        learnedAddress,
                        new byte[] { 0x22, (byte)(learned.Value.Did >> 8), (byte)(learned.Value.Did & 0xFF) },
                        TimeSpan.FromMilliseconds(2800),
                        token)
                    .ConfigureAwait(false);

                notes.Add($"learned {learned.Value.Tx:X3} {learned.Value.Service:X2} {learned.Value.Did:X4} → {Shorten(raw)}");

                byte[] learnedData = Array.Empty<byte>();
                var learnedOk = learned.Value.Service == 0x1A
                    ? TryExtractGmlanData(raw, learned.Value.Did, out learnedData)
                    : TryExtractDidData(raw, learned.Value.Did, learned.Value.Tx == 0x7DF ? null : learned.Value.Rx, out learnedData);
                if (learnedOk &&
                    learnedData.Length > learned.Value.ByteIndex)
                {
                    var rawByte = learnedData[learned.Value.ByteIndex];
                    var learnedPercent = learned.Value.Scale255 ? rawByte * 100d / 255d : rawByte;
                    if (learnedPercent is >= 0 and <= 100)
                    {
                        LastOilLifeProbeNote = string.Join(" · ", notes);
                        return new ServiceMetricReading(
                            $"gm_oil_life_learned_{learned.Value.Tx:x3}_{learned.Value.Did:x4}",
                            ServiceMetricKind.OilLifePercent,
                            "Engine Oil Life Remaining",
                            learnedPercent,
                            "%",
                            $"REAL ECU READ · 0x{learned.Value.Service:X2} ID 0x{learned.Value.Did:X4} on 0x{learned.Value.Tx:X3} (found by discovery)",
                            "GM oil life · discovered on this vehicle",
                            learned.Value.Tx,
                            learned.Value.Did,
                            learned.Value.Rx,
                            false,
                            null,
                            ServiceMetricEvidenceLevel.ResearchCatalog);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                notes.Add($"learned source → {ex.GetType().Name}");
            }
        }

        foreach (var did in GmOilLifeDids)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var raw = await _obd.ReadPhysicalCanReadOnlyAsync(
                        address,
                        new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) },
                        TimeSpan.FromMilliseconds(2800),
                        token)
                    .ConfigureAwait(false);

                notes.Add($"22 {did:X4} (7E0) → {Shorten(raw)}");

                if (!TryExtractDidData(raw, did, address.ReceiveCanId, out var data) || data.Length < 1)
                {
                    // Fallback: functional (broadcast 7DF) request with the
                    // adapter's automatic header — the form older GM tools use.
                    string functionalRaw;
                    try
                    {
                        functionalRaw = await _obd.SendRawCommandAsync($"22{did:X4}", token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        functionalRaw = ex.Message;
                    }

                    notes.Add($"22 {did:X4} (7DF) → {Shorten(functionalRaw)}");

                    if (!TryExtractDidData(functionalRaw, did, null, out data) || data.Length < 1)
                        continue;
                }

                var percent = data[0] * 100d / 255d;
                if (percent is < 0 or > 100)
                    continue;

                LastOilLifeProbeNote = string.Join(" · ", notes);

                return new ServiceMetricReading(
                    $"chevrolet_volt_oil_life_{did:x4}",
                    ServiceMetricKind.OilLifePercent,
                    "Engine Oil Life Remaining",
                    percent,
                    "%",
                    $"REAL ECU READ · GM enhanced 0x22 DID 0x{did:X4} · exact positive 0x62 response",
                    "Chevrolet Volt Gen-1 read-only response",
                    address.TransmitCanId,
                    did,
                    address.ReceiveCanId,
                    false,
                    null,
                    ServiceMetricEvidenceLevel.ResearchCatalog);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                notes.Add($"22 {did:X4} → {ex.GetType().Name}: {ex.Message}");
            }
        }

        LastOilLifeProbeNote = string.Join(" · ", notes);
        return null;
    }

    // ============================================================
    // GM OIL-LIFE DISCOVERY (read-only)
    // ============================================================

    // The old app-wide key let an address found on one car be used on every
    // other car. It is no longer read; learned READ sources are kept per VIN.
    private const string LegacyLearnedOilLifeKey = "gm_oil_life_source_v1";
    private const string LearnedOilLifeKeyPrefix = "gm_oil_life_source_v2_";

    /// <summary>Full 17-character VIN of the car of the last read, or empty.</summary>
    private string _currentVin = string.Empty;

    private static bool IsFullVin(string? vin) =>
        !string.IsNullOrWhiteSpace(vin) && vin.Trim().Length == 17 &&
        !vin.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>Per-VIN key; empty when the VIN is unknown (nothing is stored then).</summary>
    private string LearnedOilLifeKey =>
        _currentVin.Length == 17 ? LearnedOilLifeKeyPrefix + _currentVin : string.Empty;

    private readonly record struct LearnedOilLifeSource(int Tx, int Rx, int Did, int ByteIndex, bool Scale255, int Service = 0x22);

    private LearnedOilLifeSource? LoadLearnedOilLifeSource()
    {
        try
        {
            Preferences.Default.Remove(LegacyLearnedOilLifeKey);
            var key = LearnedOilLifeKey;
            if (key.Length == 0)
                return null;

            var text = Preferences.Default.Get(key, string.Empty);
            var parts = text.Split('|');
            if (parts.Length is not (5 or 6))
                return null;

            return new LearnedOilLifeSource(
                Convert.ToInt32(parts[0], 16),
                Convert.ToInt32(parts[1], 16),
                Convert.ToInt32(parts[2], 16),
                int.Parse(parts[3]),
                parts[4] == "255",
                parts.Length == 6 ? Convert.ToInt32(parts[5], 16) : 0x22);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Stores a READ source found by discovery for this VIN only. It is never
    /// used as a write address.
    /// </summary>
    private bool SaveLearnedOilLifeSource(LearnedOilLifeSource source)
    {
        var key = LearnedOilLifeKey;
        if (key.Length == 0)
            return false;

        Preferences.Default.Set(
            key,
            $"{source.Tx:X3}|{source.Rx:X3}|{source.Did:X4}|{source.ByteIndex}|{(source.Scale255 ? "255" : "1")}|{source.Service:X2}");
        return true;
    }

    public void ForgetLearnedOilLifeSource()
    {
        var key = LearnedOilLifeKey;
        if (key.Length > 0)
            Preferences.Default.Remove(key);
    }

    // ============================================================
    // GM OIL-LIFE RESET (Service page "Reset service reminder")
    // ============================================================

    private const string OilResetHistoryKey = "gm_oil_life_reset_history_v1";

    /// <summary>
    /// The oil-life reset is offered only when a REVIEWED catalog entry exists
    /// for the connected (VIN-verified) model. Learned/discovered sources are
    /// read-only: matching a byte to the dashboard percentage does not prove
    /// that writing to it is safe.
    /// </summary>
    public bool CanResetLearnedOilLife =>
        _verifiedGmResetProfile && _reviewedGmReset is not null;

    /// <summary>
    /// A GM oil-life write address confirmed from service documentation and a
    /// real-car test for this exact model range. Only these are ever written.
    /// </summary>
    private sealed record ReviewedGmOilLifeReset(
        string Make,
        string Model,
        int YearFrom,
        int YearTo,
        int Tx,
        int Rx,
        int Did,
        int ByteIndex,
        bool Scale255);

    /// <summary>
    /// Reviewed write catalog. Intentionally empty until an entry has been
    /// reviewed (documented address + write/read-back/dashboard test on that
    /// model). Without an entry the reset is not offered.
    /// </summary>
    private static readonly ReviewedGmOilLifeReset[] ReviewedGmOilLifeResets =
        Array.Empty<ReviewedGmOilLifeReset>();

    private ReviewedGmOilLifeReset? _reviewedGmReset;

    private static ReviewedGmOilLifeReset? FindReviewedGmOilLifeReset(ResolvedVehicleProfile vehicle)
    {
        if (!vehicle.Manufacturer.IsVerified || !vehicle.Model.IsVerified || !vehicle.Year.IsVerified)
            return null;
        if (!int.TryParse(vehicle.Year.Value, out var year))
            return null;

        return ReviewedGmOilLifeResets.FirstOrDefault(entry =>
            entry.Make.Equals(vehicle.Manufacturer.Value, StringComparison.OrdinalIgnoreCase) &&
            entry.Model.Equals(vehicle.Model.Value, StringComparison.OrdinalIgnoreCase) &&
            year >= entry.YearFrom && year <= entry.YearTo);
    }

    /// <summary>Minimum module voltage for any write (engine off, battery healthy).</summary>
    private const double MinimumWriteVoltage = 12.0;

    /// <summary>
    /// Speed must be readable and 0, voltage readable and at least 12.0 V.
    /// Returns null when it is safe to write, otherwise the refusal result.
    /// </summary>
    private async Task<OilLifeResetResult?> CheckWriteSafetyAsync(List<string> steps, CancellationToken token)
    {
        var speed = await TryReadPidByteAsync(0x0D, token).ConfigureAwait(false);
        if (speed is null)
            return OilLifeResetResult.NotSent(OilLifeResetFailure.SafetyCheckUnavailable, "speed not readable");
        if (speed > 0)
            return OilLifeResetResult.NotSent(OilLifeResetFailure.VehicleMoving, $"speed {speed} km/h");

        byte[]? voltageData = null;
        try
        {
            voltageData = await _obd.ReadMode01PidDataAsync(0x42, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            voltageData = null;
        }

        if (voltageData is not { Length: >= 2 })
            return OilLifeResetResult.NotSent(OilLifeResetFailure.SafetyCheckUnavailable, "voltage not readable");

        var volts = ((voltageData[0] << 8) | voltageData[1]) / 1000d;
        steps.Add($"{volts:0.0} V");
        if (volts < MinimumWriteVoltage)
            return OilLifeResetResult.NotSent(OilLifeResetFailure.LowVoltage, $"{volts:0.0} V");

        return null;
    }

    /// <summary>
    /// The connected car is a VIN-verified first-generation Chevrolet Volt
    /// (2011–2015). Only this profile may receive the GMLAN oil-life reset —
    /// it was tested on a real Volt 2013 (write, read-back, dashboard).
    /// Other GM models are added only after the same real-car test.
    /// </summary>
    private bool _verifiedGmResetProfile;

    /// <summary>Short text of the learned source, e.g. "GMLAN 1A $4F · A×100/255".</summary>
    public string LearnedOilLifeDescription
    {
        get
        {
            var learned = LoadLearnedOilLifeSource();
            if (learned is null)
                return string.Empty;

            var id = learned.Value.Service == 0x1A
                ? $"GMLAN 1A ${learned.Value.Did:X2}"
                : $"22 {learned.Value.Did:X4}";
            return $"{id} · {(learned.Value.Scale255 ? "A×100/255" : "A = %")}";
        }
    }

    /// <summary>Saved reset history, newest first (date|before|after|target|ok).</summary>
    public static IReadOnlyList<OilLifeResetHistoryEntry> LoadOilResetHistory()
    {
        try
        {
            return Preferences.Default.Get(OilResetHistoryKey, string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(OilLifeResetHistoryEntry.TryParse)
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .ToArray();
        }
        catch
        {
            return Array.Empty<OilLifeResetHistoryEntry>();
        }
    }

    private static void AddOilResetHistory(OilLifeResetHistoryEntry entry)
    {
        try
        {
            var lines = new[] { entry.Serialize() }
                .Concat(LoadOilResetHistory().Select(item => item.Serialize()))
                .Take(10);
            Preferences.Default.Set(OilResetHistoryKey, string.Join("\n", lines));
        }
        catch
        {
            // History is informational only.
        }
    }

    /// <summary>
    /// Reads the GM oil-life value once (read-only): from the reviewed reset
    /// entry when one exists, otherwise from the source learned for this VIN.
    /// Returns null when there is no source or the ECU did not answer.
    /// </summary>
    public async Task<double?> ReadLearnedOilLifePercentAsync(CancellationToken token)
    {
        if (_reviewedGmReset is { } reviewed)
            return await ReadReviewedOilLifePercentAsync(reviewed, token).ConfigureAwait(false);

        var learned = LoadLearnedOilLifeSource();
        if (learned is null || learned.Value.Service != 0x1A)
            return null;

        var raw = await _obd.ReadGmlanDidFunctionalAsync(learned.Value.Did, TimeSpan.FromMilliseconds(2000), token).ConfigureAwait(false);
        if (!TryExtractGmlanData(raw, learned.Value.Did, out var data) || data.Length <= learned.Value.ByteIndex)
            return null;

        return ToOilPercent(learned.Value, data[learned.Value.ByteIndex]);
    }

    private static double ToOilPercent(LearnedOilLifeSource source, byte value) =>
        source.Scale255 ? value * 100d / 255d : value;

    private static double ToOilPercent(ReviewedGmOilLifeReset entry, byte value) =>
        entry.Scale255 ? value * 100d / 255d : value;

    /// <summary>Reads the reviewed identifier, accepting only the reviewed module's reply.</summary>
    private async Task<(byte Raw, string Reply)?> ReadReviewedOilLifeByteAsync(ReviewedGmOilLifeReset entry, CancellationToken token)
    {
        var raw = await _obd.ReadGmlanDidFilteredAsync(entry.Tx, entry.Rx, entry.Did, TimeSpan.FromMilliseconds(2000), token).ConfigureAwait(false);
        if (!TryExtractGmlanData(LinesFrom(raw, entry.Rx), entry.Did, out var data) || data.Length <= entry.ByteIndex)
            return null;
        return (data[entry.ByteIndex], raw);
    }

    private async Task<double?> ReadReviewedOilLifePercentAsync(ReviewedGmOilLifeReset entry, CancellationToken token)
    {
        var read = await ReadReviewedOilLifeByteAsync(entry, token).ConfigureAwait(false);
        return read is { } value ? ToOilPercent(entry, value.Raw) : null;
    }

    /// <summary>
    /// Resets the GM oil-life value to <paramref name="targetPercent"/> (100 or 50)
    /// with one GMLAN $3B write. Safeguards:
    ///  - only an address from a REVIEWED catalog entry for this VIN-verified
    ///    model is written — never a learned/inferred address; without an
    ///    entry the reset is not supported and nothing is sent;
    ///  - vehicle speed must be readable and 0, module voltage readable and
    ///    at least 12.0 V; otherwise nothing is sent;
    ///  - the write goes only to the reviewed physical module address — never
    ///    as a functional (broadcast 0x7DF) request, and it is not retried;
    ///  - a negative reply (7F 3B xx) is reported, and the value is read back —
    ///    success is reported only when the ECU really shows the new value.
    /// </summary>
    public async Task<OilLifeResetResult> ResetGmOilLifeAsync(
        int targetPercent,
        IProgress<string>? progress,
        CancellationToken token)
    {
        // Fail before adapter initialization or any vehicle I/O.
        if (!_verifiedGmResetProfile || _reviewedGmReset is not { } entry)
            return OilLifeResetResult.NotSent(
                OilLifeResetFailure.NotSupported,
                "GM oil-life reset is not supported for this car yet (no reviewed reset entry for this model)");

        if (targetPercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(targetPercent));

        var steps = new List<string>();
        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(token).ConfigureAwait(false);

        // 1) Vehicle must be stationary, voltage must be healthy — both readable.
        progress?.Report("speed / voltage");
        if (await CheckWriteSafetyAsync(steps, token).ConfigureAwait(false) is { } refused)
            return refused;

        // 2) Current value (backup), from the reviewed module only.
        progress?.Report("read");
        progress?.Report("module");
        var beforeRead = await ReadReviewedOilLifeByteAsync(entry, token).ConfigureAwait(false);
        if (beforeRead is not { } beforeValue)
            return OilLifeResetResult.NotSent(OilLifeResetFailure.ModuleNotFound,
                string.Join(" · ", steps.Append($"1A {entry.Did:X2} (0x{entry.Rx:X3}) → no reply")));

        var beforeByte = beforeValue.Raw;
        var before = ToOilPercent(entry, beforeByte);
        steps.Add($"before {before:0.#}% (0x{beforeByte:X2})");
        steps.Add($"module 0x{entry.Tx:X3}/0x{entry.Rx:X3}");

        // Already at the requested value → nothing to write.
        // 100% → 0xFF, 50% → 0x80, 90% → 0xE6 (A×100/255).
        var value = entry.Scale255
            ? (byte)Math.Clamp((int)Math.Round(targetPercent * 255d / 100d), 0, 255)
            : (byte)targetPercent;
        if (beforeByte == value)
            return new OilLifeResetResult(true, false, before, before, OilLifeResetFailure.None, string.Join(" · ", steps.Append("already set")));

        // 3) Exactly one physical write request (no broadcast, no retry).
        progress?.Report("write");
        var writeRaw = await SafeWriteAsync(entry.Tx, entry.Rx, entry.Did, value, token).ConfigureAwait(false);
        steps.Add($"3B {entry.Did:X2} {value:X2} → {Shorten(writeRaw)}");

        var outcome = ParseWriteReply(LinesFrom(writeRaw, entry.Rx), entry.Did, out var nrc);
        if (outcome == WriteReply.Negative)
        {
            var failed = new OilLifeResetResult(false, true, before, before, OilLifeResetFailure.Rejected, string.Join(" · ", steps)) { NegativeCode = nrc };
            AddOilResetHistory(new OilLifeResetHistoryEntry(DateTime.Now, before, before, targetPercent, false));
            return failed;
        }

        // 4) Read back.
        progress?.Report("verify");
        double? after = null;
        for (var attempt = 0; attempt < 3 && after is null; attempt++)
        {
            await Task.Delay(700, token).ConfigureAwait(false);
            try
            {
                after = await ReadReviewedOilLifePercentAsync(entry, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // retry
            }
        }

        steps.Add($"after {(after.HasValue ? $"{after:0.#}%" : "—")}");
        var success = after.HasValue && Math.Abs(after.Value - targetPercent) <= 1.5;
        AddOilResetHistory(new OilLifeResetHistoryEntry(DateTime.Now, before, after ?? before, targetPercent, success));

        return new OilLifeResetResult(
            success,
            true,
            before,
            after,
            success ? OilLifeResetFailure.None : OilLifeResetFailure.NotChanged,
            string.Join(" · ", steps));
    }

    private async Task<string> SafeWriteAsync(int tx, int rx, int did, byte value, CancellationToken token)
    {
        try
        {
            return await _obd.WriteGmlanOilLifeByteAsync(tx, rx, did, value, TimeSpan.FromMilliseconds(2500), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async Task<int?> TryReadPidByteAsync(int pid, CancellationToken token)
    {
        try
        {
            var data = await _obd.ReadMode01PidDataAsync(pid, token).ConfigureAwait(false);
            return data is { Length: >= 1 } ? data[0] : null;
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

    private enum WriteReply { NoReply, Positive, Negative }

    // ============================================================
    // LEGACY RESET BY WRITING BACK TO THE SAME IDENTIFIER (disabled)
    // ============================================================
    //
    // Read support alone is insufficient evidence for a write. The allow-list
    // is intentionally empty; exact operations must use OemOperationService.

    private sealed record ReadingSource(ServicePidCandidate Candidate, byte[] Data, int SignalsInIdentifier);

    private readonly Dictionary<string, ReadingSource> _readingSources = new(StringComparer.OrdinalIgnoreCase);
    private string _currentVehicleKey = string.Empty;
    private const string RefusedWritesKey = "svc_write_refused_v1";

    private static string VehicleKey(VehicleInfo vehicle) =>
        !string.IsNullOrWhiteSpace(vehicle.VIN) && vehicle.VIN.Length >= 11
            ? vehicle.VIN.Trim().ToUpperInvariant()
            : $"{vehicle.Manufacturer}|{vehicle.Model}|{vehicle.Year}".ToUpperInvariant();

    private static HashSet<string> LoadRefusedWrites()
    {
        try
        {
            return new HashSet<string>(
                Preferences.Default.Get(RefusedWritesKey, string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void RememberRefusedWrite(int did)
    {
        try
        {
            var refused = LoadRefusedWrites();
            refused.Add($"{_currentVehicleKey}#{did:X4}");
            Preferences.Default.Set(RefusedWritesKey, string.Join(";", refused.TakeLast(200)));
        }
        catch
        {
        }
    }

    /// <summary>True when this reading can be reset by writing back to its identifier.</summary>
    public bool CanResetByWriteBack(ServiceMetricReading reading)
    {
        if (reading.Kind is not (ServiceMetricKind.OilLifePercent or ServiceMetricKind.DistanceSinceOilChange))
            return false;
        if (!_readingSources.TryGetValue(reading.Id, out var source))
            return false;

        var candidate = source.Candidate;
        return candidate.ServiceId == 0x22 &&
               source.SignalsInIdentifier == 1 &&
               candidate.BitOffset == 0 &&
               candidate.BitLength is 8 or 16 or 24 or 32 &&
               candidate.BitLength == source.Data.Length * 8 &&
               candidate.Address.ExtendedAddress is null &&
               OBDService.WritableServiceDids.Contains(candidate.DataIdentifier) &&
               !LoadRefusedWrites().Contains($"{_currentVehicleKey}#{candidate.DataIdentifier:X4}");
    }

    /// <summary>
    /// Resets a catalog service value (oil life % → <paramref name="target"/>,
    /// distance since oil change → 0) by writing to the identifier it was read
    /// from. Checks speed/voltage first, backs up the current value, sends one
    /// write request and reads the value back.
    /// </summary>
    public async Task<OilLifeResetResult> ResetByWriteBackAsync(
        ServiceMetricReading reading,
        double target,
        IProgress<string>? progress,
        CancellationToken token)
    {
        if (!CanResetByWriteBack(reading) || !_readingSources.TryGetValue(reading.Id, out var source))
            return OilLifeResetResult.NotSent(OilLifeResetFailure.NoLearnedSource, "not resettable");

        var candidate = source.Candidate;
        var steps = new List<string>();

        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(token).ConfigureAwait(false);

        // 1) Stationary + voltage — both must be readable.
        progress?.Report("speed / voltage");
        if (await CheckWriteSafetyAsync(steps, token).ConfigureAwait(false) is { } refused)
            return refused;

        // 2) Current value (backup).
        progress?.Report("read");
        var before = await ReadCandidateAsync(candidate, token).ConfigureAwait(false);
        if (before is null)
            return OilLifeResetResult.NotSent(OilLifeResetFailure.ReadFailed, $"22 {candidate.DataIdentifier:X4}");

        var (beforeValue, beforeData) = before.Value;
        steps.Add($"before {beforeValue:0.#} (0x{Convert.ToHexString(beforeData)})");

        // 3) Value to write (inverse of the catalog scaling).
        var mul = candidate.Format.Mul ?? 1d;
        var div = candidate.Format.Div ?? 1d;
        var add = candidate.Format.Add ?? 0d;
        if (Math.Abs(mul) < double.Epsilon)
            return OilLifeResetResult.NotSent(OilLifeResetFailure.NoLearnedSource, "format");

        var maxRaw = candidate.BitLength >= 32 ? uint.MaxValue : (1UL << candidate.BitLength) - 1;
        var rawTarget = (ulong)Math.Clamp(Math.Round((target - add) * div / mul), 0, maxRaw);
        var bytes = new byte[candidate.BitLength / 8];
        for (var i = 0; i < bytes.Length; i++)
            bytes[bytes.Length - 1 - i] = (byte)(rawTarget >> (8 * i));

        if (bytes.SequenceEqual(beforeData))
            return new OilLifeResetResult(true, false, beforeValue, beforeValue, OilLifeResetFailure.None, string.Join(" · ", steps.Append("already set")));

        // 4) One write request.
        progress?.Report("write");
        string writeRaw;
        try
        {
            writeRaw = await _obd.WriteServiceDidAsync(candidate.Address, candidate.DataIdentifier, bytes, TimeSpan.FromMilliseconds(2500), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            writeRaw = ex.Message;
        }

        steps.Add($"2E {candidate.DataIdentifier:X4} {Convert.ToHexString(bytes)} → {Shorten(writeRaw)}");

        var reply = ParseUdsWriteReply(writeRaw, candidate.DataIdentifier, out var nrc);
        if (reply == WriteReply.Negative)
        {
            // The car does not allow this reset: hide the button from now on.
            if (nrc is 0x11 or 0x12 or 0x31 or 0x33 or 0x7F)
                RememberRefusedWrite(candidate.DataIdentifier);

            return new OilLifeResetResult(false, true, beforeValue, beforeValue, OilLifeResetFailure.Rejected, string.Join(" · ", steps)) { NegativeCode = nrc };
        }

        if (reply == WriteReply.NoReply)
        {
            RememberRefusedWrite(candidate.DataIdentifier);
            return OilLifeResetResult.NotSent(OilLifeResetFailure.ModuleNotFound, string.Join(" · ", steps), beforeValue);
        }

        // 5) Read back.
        progress?.Report("verify");
        double? after = null;
        for (var attempt = 0; attempt < 3 && after is null; attempt++)
        {
            await Task.Delay(700, token).ConfigureAwait(false);
            after = (await ReadCandidateAsync(candidate, token).ConfigureAwait(false))?.Value;
        }

        steps.Add($"after {(after.HasValue ? $"{after:0.#}" : "—")}");
        var tolerance = reading.Kind == ServiceMetricKind.OilLifePercent ? 1.5 : Math.Max(5, Math.Abs(mul / div) * 2);
        var success = after.HasValue && Math.Abs(after.Value - target) <= tolerance;

        return new OilLifeResetResult(
            success,
            true,
            beforeValue,
            after,
            success ? OilLifeResetFailure.None : OilLifeResetFailure.NotChanged,
            string.Join(" · ", steps));
    }

    private async Task<(double Value, byte[] Data)?> ReadCandidateAsync(ServicePidCandidate candidate, CancellationToken token)
    {
        try
        {
            var raw = await _obd.ReadPhysicalCanReadOnlyAsync(
                    candidate.Address,
                    new byte[] { 0x22, (byte)(candidate.DataIdentifier >> 8), (byte)(candidate.DataIdentifier & 0xFF) },
                    TimeSpan.FromMilliseconds(2600),
                    token)
                .ConfigureAwait(false);

            if (!TryExtractDidData(raw, candidate.DataIdentifier, candidate.Address.ReceiveCanId, out var data) ||
                !TryDecodeValue(candidate, data, out var value))
            {
                return null;
            }

            return (value, data);
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

    private static WriteReply ParseUdsWriteReply(string? raw, int did, out byte negativeCode)
    {
        negativeCode = 0;
        var bytes = ObdResponseParser.ParseHex(raw ?? string.Empty);

        for (var i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] == 0x6E && bytes[i + 1] == (byte)(did >> 8) && bytes[i + 2] == (byte)did)
                return WriteReply.Positive;
        }

        for (var i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] == 0x7F && bytes[i + 1] == 0x2E && bytes[i + 2] != 0x78)
            {
                negativeCode = bytes[i + 2];
                return WriteReply.Negative;
            }
        }

        return WriteReply.NoReply;
    }

    /// <summary>
    /// Returns only the reply lines sent by CAN ID <paramref name="rx"/>
    /// (header removed), e.g. "7E8 03 5A 6D E4" → "03 5A 6D E4".
    /// </summary>
    private static string LinesFrom(string? raw, int rx)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var header = rx.ToString("X3");
        var lines = raw.Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(header, StringComparison.OrdinalIgnoreCase))
            .Select(line => line[header.Length..].Trim());

        return string.Join("\n", lines);
    }

    private static WriteReply ParseWriteReply(string? raw, int did, out byte negativeCode)
    {
        negativeCode = 0;
        var bytes = ObdResponseParser.ParseHex(raw ?? string.Empty);

        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == 0x7B && bytes[i + 1] == (byte)did)
                return WriteReply.Positive;
        }

        for (var i = 0; i + 2 < bytes.Length; i++)
        {
            // 7F 3B 78 = "response pending"; keep looking for the final reply.
            if (bytes[i] == 0x7F && bytes[i + 1] == 0x3B && bytes[i + 2] != 0x78)
            {
                negativeCode = bytes[i + 2];
                return WriteReply.Negative;
            }
        }

        return WriteReply.NoReply;
    }

    // 11-bit request → response IDs of GM modules reachable from the OBD port.
    private static readonly (int Tx, int Rx, string Name)[] GmProbeModules =
    {
        (0x7E0, 0x7E8, "ECM"),
        (0x7E1, 0x7E9, "7E1"),
        (0x7E2, 0x7EA, "TCM"),
        (0x7E3, 0x7EB, "7E3"),
        (0x7E4, 0x7EC, "HPCM/7E4"),
        (0x7E5, 0x7ED, "7E5"),
        (0x7E6, 0x7EE, "7E6"),
        (0x7E7, 0x7EF, "7E7"),
        (0x241, 0x641, "BCM"),
        (0x243, 0x643, "243"),
        (0x244, 0x644, "244"),
        (0x24C, 0x64C, "24C"),
        (0x251, 0x651, "251"),
        (0x25D, 0x65D, "25D")
    };

    private static readonly int[] GmProbeDids = { 0xF190, 0x119F, 0x4DC0 };

    /// <summary>
    /// Read-only search for the identifier that holds the oil-life value on
    /// this car. Sends only 0x22 ReadDataByIdentifier. When the dashboard
    /// percentage is given, any response byte equal to it (raw or ×100/255)
    /// is reported, and the first match is saved for normal reads.
    /// </summary>
    public async Task<string> RunGmOilLifeDiscoveryAsync(
        int? dashboardPercent,
        bool deepScan,
        IProgress<string>? progress,
        CancellationToken token)
    {
        var report = new System.Text.StringBuilder();
        var responding = new List<(int Tx, int Rx, string Name)>();
        LearnedOilLifeSource? found = null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        report.AppendLine($"OBD360 oil-life test · {DateTime.Now:yyyy-MM-dd HH:mm} · {(deepScan ? "DEEP" : "QUICK")} · dash {(dashboardPercent?.ToString() ?? "—")}%");
        report.AppendLine($"Protocol: {_obd.CurrentProtocolDescription}");

        // Control requests: standard OBD (must work) + mode 01 support.
        foreach (var command in new[] { "0100", "0120", "0140", "0105" })
        {
            token.ThrowIfCancellationRequested();
            string controlRaw;
            try
            {
                controlRaw = await _obd.SendRawCommandAsync(command, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                controlRaw = ex.Message;
            }

            report.AppendLine($"7DF {command}: {Shorten(controlRaw)}");
        }

        // 1) Which modules answer at all?
        for (var m = 0; m < GmProbeModules.Length; m++)
        {
            token.ThrowIfCancellationRequested();
            var module = GmProbeModules[m];
            progress?.Report($"Module {m + 1}/{GmProbeModules.Length}: {module.Name} (0x{module.Tx:X3})");

            var answered = false;
            var line = new List<string>();
            foreach (var did in GmProbeDids)
            {
                var (kind, raw, data) = await ProbeDidAsync(module.Tx, module.Rx, did, 700, token).ConfigureAwait(false);
                line.Add($"{did:X4}:{kind}");
                if (kind == "OK" || kind.StartsWith("7F", StringComparison.Ordinal))
                    answered = true;

                if (data is not null && found is null && dashboardPercent.HasValue)
                    found = MatchOilPercent(module.Tx, module.Rx, did, data, dashboardPercent.Value);
            }

            if (answered)
            {
                responding.Add(module);
                report.AppendLine($"{module.Name} 0x{module.Tx:X3}: {string.Join("  ", line)}");
            }
        }

        if (responding.Count == 0)
            report.AppendLine("No module answered direct (physical) 0x22 requests.");

        // Raw adapter reply for the ECM VIN request — shows why direct
        // requests fail (NO DATA, CAN ERROR, ?, BUFFER FULL…).
        try
        {
            var (_, ecmRaw, _) = await ProbeDidAsync(0x7E0, 0x7E8, 0xF190, 1500, token).ConfigureAwait(false);
            report.AppendLine($"ECM 7E0 raw: {Shorten(ecmRaw)}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { }

        // 1b) Broadcast (functional 7DF) requests — the same path that live
        //     data uses, which works on every adapter.
        progress?.Report("Broadcast (7DF) check…");
        var functionalAnswered = false;
        foreach (var did in GmProbeDids)
        {
            token.ThrowIfCancellationRequested();
            var (kind, raw, data) = await ProbeFunctionalAsync(did, 900, token).ConfigureAwait(false);
            report.AppendLine($"7DF 22 {did:X4}: {kind} {Shorten(raw)}");
            if (kind == "OK" || kind.StartsWith("7F", StringComparison.Ordinal))
                functionalAnswered = true;
            if (data is not null && found is null && dashboardPercent.HasValue)
                found = MatchOilPercent(0x7DF, 0x7E8, did, data, dashboardPercent.Value);
        }

        if (!functionalAnswered)
            report.AppendLine("No answer to broadcast 0x22 requests either.");

        // GMLAN ReadDataByIdentifier ($1A) — the service older GM Global A
        // modules use instead of $22. Read-only: 1A 90 = VIN.
        foreach (var command in new[] { "1A90", "0902" })
        {
            token.ThrowIfCancellationRequested();
            string raw;
            try
            {
                raw = await _obd.SendRawCommandAsync(command, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                raw = ex.Message;
            }

            report.AppendLine($"7DF {command}: {Shorten(raw)}");
        }

        // 2) Optional deep scan of GM engine-data identifiers on responding modules.
        if (deepScan && found is null && dashboardPercent.HasValue)
        {
            var ranges = new[] { (0x1100, 0x11FF), (0x1900, 0x19FF), (0x4D00, 0x4DFF) };
            var positives = 0;

            foreach (var module in responding.Take(3))
            {
                foreach (var (from, to) in ranges)
                {
                    for (var did = from; did <= to && found is null; did++)
                    {
                        token.ThrowIfCancellationRequested();
                        if ((did & 0x0F) == 0)
                            progress?.Report($"Deep scan {module.Name}: 22 {did:X4}…");

                        var (kind, _, data) = await ProbeDidAsync(module.Tx, module.Rx, did, 400, token).ConfigureAwait(false);
                        if (kind == "OK")
                            positives++;
                        if (data is not null)
                            found = MatchOilPercent(module.Tx, module.Rx, did, data, dashboardPercent.Value);
                    }

                    if (found is not null)
                        break;
                }

                if (found is not null)
                    break;
            }

            // GMLAN $1A scan (1-byte identifiers) — this car answers $1A.
            var gmlanPositives = new List<string>();
            for (var did = 0x00; did <= 0xFF && found is null; did++)
            {
                token.ThrowIfCancellationRequested();
                if ((did & 0x0F) == 0)
                    progress?.Report($"Deep scan GMLAN: 1A {did:X2}…");

                string raw;
                try
                {
                    raw = await _obd.ReadGmlanDidFunctionalAsync(did, TimeSpan.FromMilliseconds(400), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    continue;
                }

                if (!TryExtractGmlanData(raw, did, out var data))
                    continue;

                positives++;
                gmlanPositives.Add($"{did:X2}={string.Join(" ", data.Take(4).Select(x => x.ToString("X2")))}");
                var match = MatchOilPercent(0x7DF, 0x7E8, did, data, dashboardPercent.Value);
                // First match only: a later byte that happens to equal the
                // dashboard percentage must not replace it.
                if (match is not null && found is null)
                    found = match.Value with { Service = 0x1A };
            }

            if (gmlanPositives.Count > 0)
                report.AppendLine("1A answers: " + string.Join("  ", gmlanPositives));

            // Broadcast $22 scan over the GM engine-data areas (works even
            // when direct requests do not). Read-only.
            var functionalRanges = new[]
            {
                (0x1100, 0x11FF), (0x1200, 0x12FF), (0x1900, 0x19FF),
                (0x1C00, 0x1CFF), (0x2300, 0x23FF), (0x4D00, 0x4DFF)
            };
            var totalFunctional = functionalRanges.Sum(r => r.Item2 - r.Item1 + 1);
            var doneFunctional = 0;
            var functionalPositives = new List<string>();
            foreach (var (from, to) in functionalRanges)
            {
                for (var did = from; did <= to && found is null; did++)
                {
                    token.ThrowIfCancellationRequested();
                    doneFunctional++;
                    if ((did & 0x0F) == 0)
                        progress?.Report($"Deep scan 22 {did:X4} · {doneFunctional * 100 / totalFunctional}%");

                    var (kind, _, data) = await ProbeFunctionalAsync(did, 350, token).ConfigureAwait(false);
                    if (kind == "OK")
                    {
                        positives++;
                        if (data is not null && functionalPositives.Count < 80)
                            functionalPositives.Add($"{did:X4}={string.Join(" ", data.Take(3).Select(x => x.ToString("X2")))}");
                    }
                    if (data is not null)
                        found = MatchOilPercent(0x7DF, 0x7E8, did, data, dashboardPercent.Value);
                }

                if (found is not null)
                    break;
            }

            report.AppendLine($"Deep scan: {positives} identifiers answered.");
            if (functionalPositives.Count > 0)
                report.AppendLine("7DF answers: " + string.Join("  ", functionalPositives));
        }

        if (found is not null)
        {
            // Saved for READING on this VIN only; never used as a write address.
            var saved = SaveLearnedOilLifeSource(found.Value);
            report.AppendLine(
                $"MATCH: 0x{found.Value.Tx:X3} · {found.Value.Service:X2} {found.Value.Did:X4} · byte {found.Value.ByteIndex} · " +
                (found.Value.Scale255 ? "A×100/255" : "A = %") +
                (saved ? " — saved for this VIN (read-only)." : " — not saved (VIN unknown)."));
        }
        else if (dashboardPercent.HasValue)
        {
            report.AppendLine("No identifier matched the dashboard value.");
        }

        report.AppendLine($"Time: {stopwatch.Elapsed:mm\\:ss}");
        return report.ToString().TrimEnd();
    }

    private async Task<(string Kind, string Raw, byte[]? Data)> ProbeDidAsync(
        int tx, int rx, int did, int timeoutMs, CancellationToken token)
    {
        string raw;
        try
        {
            raw = await _obd.ReadPhysicalCanReadOnlyAsync(
                    new ServiceDiagnosticAddress(tx, rx, false, null, "11-bit CAN", $"probe 0x{tx:X3}"),
                    new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) },
                    TimeSpan.FromMilliseconds(timeoutMs),
                    token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ("ERR", ex.Message, null);
        }

        if (TryExtractDidData(raw, did, rx, out var data) && data.Length > 0)
            return ("OK", raw, data);

        var compact = new string((raw ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        var negative = compact.IndexOf("7F22", StringComparison.Ordinal);
        if (negative >= 0 && compact.Length >= negative + 6)
            return ($"7F{compact.Substring(negative + 4, 2)}", raw ?? string.Empty, null);

        return ("—", raw ?? string.Empty, null);
    }

    private async Task<(string Kind, string Raw, byte[]? Data)> ProbeFunctionalAsync(
        int did, int timeoutMs, CancellationToken token)
    {
        string raw;
        try
        {
            raw = await _obd.ReadDidFunctionalAsync(did, TimeSpan.FromMilliseconds(timeoutMs), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ("ERR", ex.Message, null);
        }

        if (TryExtractDidData(raw, did, null, out var data) && data.Length > 0)
            return ("OK", raw, data);

        var compact = new string((raw ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        var negative = compact.IndexOf("7F22", StringComparison.Ordinal);
        if (negative >= 0 && compact.Length >= negative + 6)
            return ($"7F{compact.Substring(negative + 4, 2)}", raw ?? string.Empty, null);

        return ("—", raw ?? string.Empty, null);
    }

    /// <summary>Positive GMLAN $1A reply: 5A &lt;id&gt; data…</summary>
    private static bool TryExtractGmlanData(string? raw, int did, out byte[] data)
    {
        data = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
            return false;

        var bytes = ObdResponseParser.ParseHex(raw);
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == 0x5A && bytes[i + 1] == (byte)did)
            {
                data = bytes[(i + 2)..];
                return data.Length > 0;
            }
        }

        return false;
    }

    private static LearnedOilLifeSource? MatchOilPercent(int tx, int rx, int did, byte[] data, int dashboardPercent)
    {
        // Oil life is a single-byte value. VIN (F190) and long records are
        // ignored so that ASCII text or unrelated data cannot match.
        if (did is 0xF190 or 0x90 || data.Length is < 1 or > 2)
            return null;

        var value = data[0];
        if (Math.Abs(value * 100d / 255d - dashboardPercent) <= 1.0)
            return new LearnedOilLifeSource(tx, rx, did, 0, true);
        if (value == dashboardPercent)
            return new LearnedOilLifeSource(tx, rx, did, 0, false);

        return null;
    }

    private static string Shorten(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "(no reply)";

        var flat = string.Join(" ", raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= 60 ? flat : flat[..60] + "…";
    }

    private async Task<CandidateLoadResult> LoadCandidatesAsync(
        ResolvedVehicleProfile vehicle,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var connection = new SQLiteAsyncConnection(
            _databaseCatalog.DatabasePath,
            SQLiteOpenFlags.ReadOnly);

        try
        {
            var all = new List<ServicePidRow>();
            var ownMakes = MakeAliases(vehicle.Manufacturer.Value);
            var familyMakes = ownMakes.Concat(BrandFamily(vehicle.Manufacturer.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var make in familyMakes)
            {
                var rows = await connection.QueryAsync<ServicePidRow>(
                    """
                    SELECT
                        p.id AS Id,
                        p.make AS Make,
                        p.model AS Model,
                        p.header AS Header,
                        p.command_json AS CommandJson,
                        p.parameter_id AS ParameterId,
                        p.name AS Name,
                        p.unit AS Unit,
                        p.bit_length AS BitLength,
                        p.bit_offset AS BitOffset,
                        p.eax AS Eax,
                        p.format_json AS FormatJson,
                        p.debug AS Debug,
                        p.raw_json AS RawJson,
                        s.name AS SourceCatalog
                    FROM pid_definitions p
                    INNER JOIN source_catalogs s ON s.id = p.source_catalog_id
                    WHERE lower(p.make) = lower(?)
                      AND p.pid IN ('22', '21');
                    """,
                    make).ConfigureAwait(false);

                all.AddRange(rows);
            }

            var model = vehicle.Model.Value;
            var candidates = new List<ServicePidCandidate>();
            var unsupportedAddressCount = 0;

            // Exact model (only when the model is verified).
            if (vehicle.HasVerifiedModel)
            {
                foreach (var row in all.Where(row =>
                             ownMakes.Contains(row.Make ?? string.Empty, StringComparer.OrdinalIgnoreCase) &&
                             ModelMatches(row.Make, row.Model, model)))
                {
                    var candidate = TryBuildCandidate(row, out var addressSupported);
                    if (!addressSupported)
                        unsupportedAddressCount++;
                    if (candidate is not null)
                        candidates.Add(candidate);
                }
            }

            // Brand-wide identifiers: the same service identifier is used by
            // many models of one manufacturer (e.g. GM 22 4DC0 oil life,
            // Ford 22 054B oil life, Mazda 22 0519 distance since oil change).
            // Read-only; a value is shown only after an exact positive reply.
            var known = new HashSet<string>(
                candidates.Select(item => $"{item.ServiceId:X2}|{AddressKey(item.Address, item.DataIdentifier)}|{item.BitOffset}|{item.BitLength}"),
                StringComparer.OrdinalIgnoreCase);
            foreach (var row in all)
            {
                var candidate = TryBuildCandidate(row, out _);
                if (candidate is null || !IsBrandWideKind(candidate.Kind))
                    continue;

                var key = $"{candidate.ServiceId:X2}|{AddressKey(candidate.Address, candidate.DataIdentifier)}|{candidate.BitOffset}|{candidate.BitLength}";
                if (!known.Add(key))
                    continue;

                candidates.Add(candidate with { Debug = false, BrandFallback = true });
            }

            var distinct = candidates
                .GroupBy(item => $"{item.ParameterId}|{item.ServiceId:X2}|{AddressKey(item.Address, item.DataIdentifier)}|{item.BitOffset}|{item.BitLength}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            return new CandidateLoadResult(distinct, unsupportedAddressCount);
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private static ServicePidCandidate? TryBuildCandidate(
        ServicePidRow row,
        out bool addressSupported)
    {
        addressSupported = true;

        if (string.IsNullOrWhiteSpace(row.CommandJson) ||
            string.IsNullOrWhiteSpace(row.ParameterId) ||
            string.IsNullOrWhiteSpace(row.Name) ||
            row.BitLength is < 1 or > 64 ||
            row.BitOffset < 0)
        {
            return null;
        }

        string? didText = null;
        var serviceId = 0x22;
        try
        {
            using var doc = JsonDocument.Parse(row.CommandJson);
            if (doc.RootElement.TryGetProperty("22", out var didElement) &&
                didElement.ValueKind == JsonValueKind.String)
            {
                didText = didElement.GetString();
            }
            else if (doc.RootElement.TryGetProperty("21", out var localElement) &&
                     localElement.ValueKind == JsonValueKind.String)
            {
                // KWP2000 ReadDataByLocalIdentifier (Toyota/Lexus, read-only).
                didText = localElement.GetString();
                serviceId = 0x21;
            }
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(didText) || didText.Length != (serviceId == 0x21 ? 2 : 4) ||
            !int.TryParse(didText, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var did))
        {
            return null;
        }

        var kind = Classify(row.Name, row.ParameterId);
        if (kind == ServiceMetricKind.Other && !IsServiceRelevantName(row.Name))
            return null;

        if (!ObdbDiagnosticAddressResolver.TryResolve(
                row.Header,
                row.Eax,
                row.RawJson,
                out var address,
                out _))
        {
            addressSupported = false;
            return null;
        }

        var format = ServiceFormat.Empty;
        if (!string.IsNullOrWhiteSpace(row.FormatJson))
        {
            try
            {
                format = JsonSerializer.Deserialize<ServiceFormat>(
                    row.FormatJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? ServiceFormat.Empty;
            }
            catch
            {
                return null;
            }
        }

        return new ServicePidCandidate(
            row.ParameterId,
            row.Name,
            kind,
            address,
            did,
            row.BitOffset,
            row.BitLength,
            NormalizeUnit(row.Unit, format.Unit),
            format,
            row.SourceCatalog ?? "Local catalog",
            row.Debug != 0,
            false,
            serviceId);
    }

    private bool TryExtractDidData(
        string raw,
        int did,
        int? expectedReceiveCanId,
        out byte[] data,
        int serviceId = 0x22)
    {
        data = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
            return false;

        // Frames from another module (e.g. the engine ECU answering for a different address) are not this module's reply.
        if (ObdResponseParser.IsOnlyFromOtherModules(raw, expectedReceiveCanId, out _))
            return false;

        if (expectedReceiveCanId.HasValue)
        {
            var frames = ObdResponseParser.ParseCanFrames(raw, expectedReceiveCanId.Value);
            if (TryMessage(frames, did, out data, serviceId))
                return true;
        }

        foreach (var id in ObdResponseParser.ExtractCanIdentifiers(raw))
        {
            var frames = ObdResponseParser.ParseCanFrames(raw, id);
            if (TryMessage(frames, did, out data, serviceId))
                return true;
        }

        // Headerless adapters / already de-framed payloads.
        var anyFrames = ObdResponseParser.ParseCanFrames(raw);
        if (TryMessage(anyFrames, did, out data, serviceId))
            return true;

        var bytes = ObdResponseParser.ParseHex(raw);
        if (serviceId == 0x21)
        {
            for (var i = 0; i + 1 < bytes.Length; i++)
            {
                if (bytes[i] == 0x61 && bytes[i + 1] == (byte)did)
                {
                    data = bytes[(i + 2)..];
                    return data.Length > 0;
                }
            }

            return false;
        }

        for (var i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] == 0x62 &&
                bytes[i + 1] == (byte)(did >> 8) &&
                bytes[i + 2] == (byte)did)
            {
                data = bytes[(i + 3)..];
                return data.Length > 0;
            }
        }

        return false;
    }

    private bool TryMessage(
        IReadOnlyList<byte[]> frames,
        int did,
        out byte[] data,
        int serviceId = 0x22)
    {
        data = Array.Empty<byte>();
        if (frames.Count == 0)
            return false;

        byte[] message;
        try
        {
            var first = frames[0];
            if (first.Length == 0)
                return false;

            var frameType = first[0] >> 4;
            message = frameType is 0 or 1
                ? _isoTp.Reassemble(frames)
                : frames.SelectMany(frame => frame).ToArray();
        }
        catch
        {
            return false;
        }

        if (serviceId == 0x21)
        {
            if (message.Length < 3 || message[0] != 0x61 || message[1] != (byte)did)
                return false;

            data = message[2..];
            return data.Length > 0;
        }

        if (message.Length < 4 ||
            message[0] != 0x62 ||
            message[1] != (byte)(did >> 8) ||
            message[2] != (byte)did)
        {
            return false;
        }

        data = message[3..];
        return data.Length > 0;
    }

    private static bool TryDecodeValue(
        ServicePidCandidate candidate,
        byte[] data,
        out double value)
    {
        value = 0;
        var endBit = candidate.BitOffset + candidate.BitLength;
        if (endBit > data.Length * 8)
            return false;

        ulong raw = 0;
        for (var bit = 0; bit < candidate.BitLength; bit++)
        {
            var absolute = candidate.BitOffset + bit;
            var byteIndex = absolute / 8;
            var bitInByte = 7 - (absolute % 8);
            raw = (raw << 1) | (((ulong)data[byteIndex] >> bitInByte) & 0x01UL);
        }

        double number;
        if (candidate.Format.Sign && candidate.BitLength < 64)
        {
            var signBit = 1UL << (candidate.BitLength - 1);
            var signed = (raw & signBit) != 0
                ? (long)(raw | (ulong.MaxValue << candidate.BitLength))
                : (long)raw;
            number = signed;
        }
        else
        {
            number = raw;
        }

        if (candidate.Format.Mul.HasValue)
            number *= candidate.Format.Mul.Value;
        if (candidate.Format.Div.HasValue)
        {
            if (Math.Abs(candidate.Format.Div.Value) < double.Epsilon)
                return false;
            number /= candidate.Format.Div.Value;
        }
        if (candidate.Format.Add.HasValue)
            number += candidate.Format.Add.Value;

        if (candidate.Format.NullMin.HasValue && number <= candidate.Format.NullMin.Value)
            return false;
        if (candidate.Format.NullMax.HasValue && number >= candidate.Format.NullMax.Value)
            return false;

        // Min/max in OBDb are validity bounds, not values to fabricate by
        // clamping. Reject an out-of-range decode instead.
        if (candidate.Format.Min.HasValue && number < candidate.Format.Min.Value)
            return false;
        if (candidate.Format.Max.HasValue && number > candidate.Format.Max.Value)
            return false;

        if (!double.IsFinite(number))
            return false;

        value = number;
        return true;
    }

    private static bool IsPlausible(ServiceMetricKind kind, string unit, double value)
    {
        if (!double.IsFinite(value))
            return false;

        return kind switch
        {
            ServiceMetricKind.OilLifePercent => value is >= 0 and <= 100,
            ServiceMetricKind.DistanceToNextService => value is >= 0 and <= 300000,
            ServiceMetricKind.DaysToNextService => value is >= 0 and <= 5000,
            ServiceMetricKind.DistanceSinceOilChange => value is >= 0 and <= 1500000,
            ServiceMetricKind.OilChangeInterval => value is >= 0 and <= 1500000,
            ServiceMetricKind.MaintenanceCounter => value is >= 0 and <= 1000000,
            ServiceMetricKind.OilLevel when unit.Equals("percent", StringComparison.OrdinalIgnoreCase) => value is >= 0 and <= 100,
            _ => true
        };
    }

    private static ServiceMetricKind Classify(string? name, string? parameterId)
    {
        var text = $"{name} {parameterId}".ToLowerInvariant();
        if (text.Contains("oil life") || text.Contains("oil degradation"))
            return ServiceMetricKind.OilLifePercent;
        if (text.Contains("oil level") || text.Contains("oil fill level"))
            return ServiceMetricKind.OilLevel;
        if (text.Contains("distance to next service") || text.Contains("service distance") || text.Contains("distance until service"))
            return ServiceMetricKind.DistanceToNextService;
        if (text.Contains("days to next service") || text.Contains("service days") || text.Contains("days until service"))
            return ServiceMetricKind.DaysToNextService;
        if (text.Contains("distance since last oil change") || text.Contains("distance since oil changed") || text.Contains("distance since oil change"))
            return ServiceMetricKind.DistanceSinceOilChange;
        if (text.Contains("max distance to oil change") || text.Contains("oil change interval"))
            return ServiceMetricKind.OilChangeInterval;
        if (text.Contains("inspection") && text.Contains("distance"))
            return ServiceMetricKind.InspectionDistance;
        if (text.Contains("inspection") && text.Contains("day"))
            return ServiceMetricKind.InspectionDays;
        if (text.Contains("maintenance"))
            return ServiceMetricKind.MaintenanceCounter;
        if (text.Contains("service status") || text.Contains("maintenance status"))
            return ServiceMetricKind.ServiceStatus;
        return ServiceMetricKind.Other;
    }

    private static bool IsServiceRelevantName(string? name)
    {
        var text = (name ?? string.Empty).ToLowerInvariant();
        return text.Contains("oil life") ||
               text.Contains("oil degradation") ||
               text.Contains("oil level") ||
               text.Contains("oil change") ||
               text.Contains("next service") ||
               text.Contains("service interval") ||
               text.Contains("maintenance") ||
               (text.Contains("inspection") &&
                (text.Contains("distance") || text.Contains("day")));
    }

    private static bool ModelMatches(string? make, string? catalogModel, string vehicleModel)
    {
        var expected = Normalize(catalogModel);
        var actual = Normalize(vehicleModel);
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            return false;

        if (expected == actual || expected.Contains(actual) || actual.Contains(expected))
            return true;

        // OBDb currently contains a manufacturer-wide Mercedes research row
        // under model "Benz". It stays debug/research-only in production mode.
        return Normalize(make).Contains("mercedes") && expected == "benz";
    }

    private static IReadOnlyList<string> MakeAliases(string make)
    {
        var value = make.Trim();
        var normalized = Normalize(value);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { value };

        // The catalog's own spelling of the make (Mercedes, VauxhallOpel, Land …).
        if (CatalogMakes.Known(value))
            aliases.Add(CatalogMakes.For(value)[0]);

        if (normalized.Contains("mercedes"))
        {
            aliases.Add("Mercedes");
            aliases.Add("Mercedes-Benz");
        }
        else if (normalized is "vw" or "volkswagen")
        {
            aliases.Add("Volkswagen");
            aliases.Add("VW");
        }
        else if (normalized.Contains("chevrolet") || normalized == "chevy")
        {
            aliases.Add("Chevrolet");
        }

        return aliases.ToArray();
    }

    /// <summary>Catalog makes that share service identifiers with <paramref name="make"/>.</summary>
    private static IEnumerable<string> BrandFamily(string make) =>
        CatalogMakes.Known(make) ? CatalogMakes.For(make) : Array.Empty<string>();

    private static bool IsBrandWideKind(ServiceMetricKind kind) => kind is
        ServiceMetricKind.OilLifePercent or
        ServiceMetricKind.DistanceSinceOilChange or
        ServiceMetricKind.DistanceToNextService or
        ServiceMetricKind.DaysToNextService or
        ServiceMetricKind.InspectionDistance or
        ServiceMetricKind.InspectionDays;

    private static string AddressKey(ServiceDiagnosticAddress address, int did) =>
        $"{address.TransmitCanId:X8}|{address.ReceiveCanId?.ToString("X8") ?? "*"}|{address.Extended29Bit}|{address.ExtendedAddress?.ToString("X2") ?? "--"}|{did:X4}";

    private static string Normalize(string? value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalizeUnit(string? rowUnit, string? formatUnit)
    {
        var unit = !string.IsNullOrWhiteSpace(formatUnit) ? formatUnit : rowUnit;
        return (unit ?? string.Empty).Trim();
    }

    private static int Priority(ServiceMetricKind kind) => kind switch
    {
        ServiceMetricKind.OilLifePercent => 0,
        ServiceMetricKind.DistanceToNextService => 1,
        ServiceMetricKind.DaysToNextService => 2,
        ServiceMetricKind.DistanceSinceOilChange => 3,
        ServiceMetricKind.OilChangeInterval => 4,
        ServiceMetricKind.InspectionDistance => 5,
        ServiceMetricKind.InspectionDays => 6,
        ServiceMetricKind.MaintenanceCounter => 7,
        ServiceMetricKind.OilLevel => 8,
        ServiceMetricKind.ServiceStatus => 9,
        _ => 20
    };

    private sealed class ServicePidRow
    {
        public int Id { get; set; }
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
        public int Debug { get; set; }
        public string? RawJson { get; set; }
        public string? SourceCatalog { get; set; }
    }

    private sealed record ServicePidCandidate(
        string ParameterId,
        string Name,
        ServiceMetricKind Kind,
        ServiceDiagnosticAddress Address,
        int DataIdentifier,
        int BitOffset,
        int BitLength,
        string Unit,
        ServiceFormat Format,
        string SourceCatalog,
        bool Debug,
        bool BrandFallback = false,
        int ServiceId = 0x22);

    private sealed record CandidateLoadResult(
        IReadOnlyList<ServicePidCandidate> Candidates,
        int UnsupportedAddressCount);

    private sealed class ServiceFormat
    {
        public static ServiceFormat Empty { get; } = new();
        public double? Mul { get; set; }
        public double? Div { get; set; }
        public double? Add { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? NullMin { get; set; }
        public double? NullMax { get; set; }
        public bool Sign { get; set; }
        public string? Unit { get; set; }
    }
}

/// <summary>Why an oil-life reset was not completed.</summary>
public enum OilLifeResetFailure
{
    None,
    NoLearnedSource,
    VehicleMoving,
    LowVoltage,
    ReadFailed,
    ModuleNotFound,
    ModuleAmbiguous,
    Rejected,
    NotChanged,
    /// <summary>No reviewed reset entry exists for this model: the reset is not offered.</summary>
    NotSupported,
    /// <summary>Vehicle speed or module voltage could not be read: nothing was sent.</summary>
    SafetyCheckUnavailable
}

/// <summary>Result of one Service page oil-life reset.</summary>
public sealed record OilLifeResetResult(
    bool Success,
    bool WriteSent,
    double? Before,
    double? After,
    OilLifeResetFailure Failure,
    string Detail)
{
    public byte NegativeCode { get; init; }

    public static OilLifeResetResult NotSent(OilLifeResetFailure failure, string detail, double? before = null) =>
        new(false, false, before, before, failure, detail);
}

/// <summary>One saved oil-life reset (kept on the phone, newest first).</summary>
public sealed record OilLifeResetHistoryEntry(DateTime Date, double Before, double After, int Target, bool Success)
{
    public string Serialize() =>
        string.Join("|",
            Date.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            Before.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            After.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            Target.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Success ? "1" : "0");

    public static OilLifeResetHistoryEntry? TryParse(string line)
    {
        var parts = line.Trim().Split('|');
        if (parts.Length != 5)
            return null;

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm", culture, System.Globalization.DateTimeStyles.None, out var date) &&
               double.TryParse(parts[1], System.Globalization.NumberStyles.Float, culture, out var before) &&
               double.TryParse(parts[2], System.Globalization.NumberStyles.Float, culture, out var after) &&
               int.TryParse(parts[3], System.Globalization.NumberStyles.Integer, culture, out var target)
            ? new OilLifeResetHistoryEntry(date, before, after, target, parts[4] == "1")
            : null;
    }
}
