using CarScanner.Models;
using CarScanner.Services.Localization;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Reads manufacturer-specific DTCs only through reviewed ECU definitions.
/// Human-readable text is resolved through the SQLite DTC master catalog
/// when a reviewed raw-to-catalog mapping is present.
/// </summary>
public sealed class OemEcuDtcService
{
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly ManufacturerDefinitionCatalogService _manufacturers;
    private readonly OemDefinitionCatalogService _definitions;
    private readonly OemAddressRegistry _addresses;
    private readonly OemModuleDiscoveryService _discovery;
    private readonly DtcCatalogService _catalog;
    private readonly UdsProtocol _uds;
    private readonly OBDService _obd;

    public OemDtcScanResult? Current { get; private set; }
    public event EventHandler<OemDtcScanResult>? DtcUpdated;

    public OemEcuDtcService(
        VehicleProfileResolverService vehicleProfiles,
        ManufacturerDefinitionCatalogService manufacturers,
        OemDefinitionCatalogService definitions,
        OemAddressRegistry addresses,
        OemModuleDiscoveryService discovery,
        DtcCatalogService catalog,
        UdsProtocol uds,
        OBDService obd)
    {
        _vehicleProfiles = vehicleProfiles;
        _manufacturers = manufacturers;
        _definitions = definitions;
        _addresses = addresses;
        _discovery = discovery;
        _catalog = catalog;
        _uds = uds;
        _obd = obd;
    }

    public async Task<OemDtcScanResult> ReadAsync(
        VehicleInfo vehicle,
        CancellationToken token = default)
    {
        var discovery = await _discovery.DiscoverAsync(vehicle, token).ConfigureAwait(false);
        return await ReadAsync(vehicle, discovery, token: token).ConfigureAwait(false);
    }

    public async Task<OemDtcScanResult> ReadAsync(
        VehicleInfo vehicle,
        OemModuleDiscoveryResult discovery,
        OemCapabilityDetectionResult? capabilityDetection = null,
        CancellationToken token = default)
    {
        var responding = discovery.Probes
            .Where(probe => probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited)
            .GroupBy(probe => probe.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (responding.Count == 0)
            return Publish(OemDtcScanResult.Unavailable(
                "OEM DTC წაკითხვა არ დაწყებულა — პასუხმგებელი verified ECU არ დადასტურდა."));

        var resolvedVehicle = _vehicleProfiles.Resolve(vehicle);
        await _definitions.EnsureLoadedAsync(token).ConfigureAwait(false);
        var manufacturer = _manufacturers.Resolve(resolvedVehicle);
        var definitions = _definitions.GetForVehicle(resolvedVehicle, manufacturer)
            .Where(definition => responding.ContainsKey(definition.DefinitionId))
            .ToArray();
        var addresses = _addresses.GetForVehicle(resolvedVehicle)
            .ToDictionary(address => address.DefinitionId, StringComparer.OrdinalIgnoreCase);

        var ecuResults = new List<OemEcuDtcResult>();
        var sentCommand = false;
        try
        {
            foreach (var definition in definitions)
            {
                token.ThrowIfCancellationRequested();
                if (!addresses.TryGetValue(definition.DefinitionId, out var address))
                    continue;

                var commands = definition.ReadOnlyCommands.Where(command =>
                    command.CapabilityCode.Equals("DTC.READ", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (commands.Length == 0)
                    continue;

                var decoded = new List<OemDtcDecodeResult>();
                foreach (var command in commands)
                {
                    token.ThrowIfCancellationRequested();
                    UdsResponse response;
                    if (command.DiscoveryProbe)
                    {
                        response = RebuildDiscoveryResponse(command, responding[definition.DefinitionId]);
                    }
                    else if (FindCapturedCapability(
                                 capabilityDetection,
                                 definition.DefinitionId,
                                 command.Id) is { } captured)
                    {
                        response = RebuildCapabilityResponse(command, captured);
                    }
                    else
                    {
                        sentCommand = true;
                        var request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
                        response = await _uds.SendIsoTpAsync(
                            request,
                            address.TxCanId,
                            address.RxCanId,
                            address.Extended29Bit,
                            TimeSpan.FromMilliseconds(command.TimeoutMs),
                            token).ConfigureAwait(false);
                    }
                    decoded.Add(OemUdsDtcResponseDecoder.Decode(command, response));
                }

                var rawRecords = decoded
                    .Where(result => result.State == OemDtcReadState.Available)
                    .SelectMany(result => result.Records)
                    .GroupBy(record => (record.RawCodeHex, record.StatusByte))
                    .Select(group => group.First())
                    .ToArray();
                var records = new List<OemDtcRecord>();
                foreach (var raw in rawRecords)
                    records.Add(await EnrichAsync(definition, raw, token).ConfigureAwait(false));

                var state = decoded.Any(result => result.State == OemDtcReadState.Available)
                    ? OemDtcReadState.Available
                    : decoded.Select(result => result.State).FirstOrDefault();
                var detail = state == OemDtcReadState.Available
                    ? records.Count == 0
                        ? "ECU-მ OEM DTC მოთხოვნას უპასუხა — კოდი არ აღმოჩნდა."
                        : $"{records.Count} OEM DTC ჩანაწერი წაკითხულია."
                    : string.Join("; ", decoded.Select(result => result.Detail).Distinct());
                ecuResults.Add(new OemEcuDtcResult(
                    definition.DefinitionId,
                    definition.EcuId,
                    definition.EcuName,
                    state,
                    records,
                    detail));
            }
        }
        finally
        {
            if (sentCommand)
                await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var successful = ecuResults.Count(result => result.State == OemDtcReadState.Available);
        return Publish(new OemDtcScanResult(
            ecuResults,
            ecuResults.Count == 0 ? "NOT AVAILABLE" : successful == ecuResults.Count ? "AVAILABLE" : "PARTIAL",
            ecuResults.Count == 0
                ? "Responding OEM ECU-სთვის reviewed DTC command არ არსებობს."
                : $"OEM DTC reader: {successful}/{ecuResults.Count} ECU წარმატებით წაიკითხა."));
    }

    private async Task<OemDtcRecord> EnrichAsync(
        OemEcuDefinition definition,
        OemRawDtcRecord raw,
        CancellationToken token)
    {
        var mapping = definition.DtcMappings.FirstOrDefault(item =>
            item.RawCodeHex.Equals(raw.RawCodeHex, StringComparison.OrdinalIgnoreCase));
        DtcCatalogEntry? catalogEntry = null;
        if (mapping is not null)
        {
            try
            {
                catalogEntry = await _catalog.GetAsync(
                    mapping.CatalogCode,
                    new DtcLookupContext(
                        Manufacturer: definition.Brand,
                        Model: definition.VehicleProfile,
                        EcuModel: definition.EcuId,
                        EcuType: definition.EcuName,
                        AllowManufacturerSpecific: true,
                        ManufacturerContextVerified: true),
                    token).ConfigureAwait(false);
            }
            catch when (!token.IsCancellationRequested)
            {
                // The real ECU code remains visible even if a bundled text
                // catalog asset cannot be loaded. No fallback text is invented.
            }
        }

        var english = Preferences.Default.Get(LocalizationService.LanguageKey, "ქართული")
            .Equals("English", StringComparison.OrdinalIgnoreCase);
        var title = catalogEntry is null
            ? string.Empty
            : english
                ? FirstNonEmpty(catalogEntry.TitleEn, catalogEntry.DescriptionEn)
                : FirstNonEmpty(catalogEntry.TitleKa, catalogEntry.DescriptionKa, catalogEntry.TitleEn);
        var description = string.IsNullOrWhiteSpace(title)
            ? english
                ? "Manufacturer-specific description is not available in the reviewed catalog."
                : "Manufacturer-specific აღწერა reviewed კატალოგში მიუწვდომელია."
            : title;

        return new OemDtcRecord(
            definition.DefinitionId,
            definition.EcuId,
            definition.EcuName,
            raw.RawCodeHex,
            mapping?.CatalogCode ?? $"0x{raw.RawCodeHex}",
            description,
            raw.StatusByte,
            raw.StatusText,
            catalogEntry is not null,
            definition.Evidence.SourceReference);
    }

    private static UdsResponse RebuildDiscoveryResponse(
        OemReadOnlyCommandDefinition command,
        OemModuleProbeResult probe)
    {
        var request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
        var kind = probe.State switch
        {
            OemModuleProbeState.Available => UdsResponseKind.Positive,
            OemModuleProbeState.RespondedLimited => UdsResponseKind.Negative,
            OemModuleProbeState.NoResponse => UdsResponseKind.Timeout,
            OemModuleProbeState.TransportUnsupported => UdsResponseKind.Unsupported,
            OemModuleProbeState.MalformedResponse => UdsResponseKind.Malformed,
            _ => UdsResponseKind.Malformed
        };
        return new UdsResponse(
            kind,
            probe.ResponseService == 0 ? request[0] : probe.ResponseService,
            probe.ResponsePayload ?? Array.Empty<byte>(),
            probe.NegativeResponseCode,
            probe.Detail);
    }

    private static OemCapabilityProbeResult? FindCapturedCapability(
        OemCapabilityDetectionResult? detection,
        string definitionId,
        string commandId) => detection?.Ecus
        .FirstOrDefault(ecu => ecu.DefinitionId.Equals(definitionId, StringComparison.OrdinalIgnoreCase))?
        .Capabilities.FirstOrDefault(capability =>
            capability.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase) &&
            capability.Availability != OemCapabilityAvailability.NotChecked);

    private static UdsResponse RebuildCapabilityResponse(
        OemReadOnlyCommandDefinition command,
        OemCapabilityProbeResult capability)
    {
        var request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
        var kind = capability.Availability switch
        {
            OemCapabilityAvailability.Available => UdsResponseKind.Positive,
            OemCapabilityAvailability.Conditional or OemCapabilityAvailability.NotSupported => UdsResponseKind.Negative,
            OemCapabilityAvailability.NoResponse => UdsResponseKind.Timeout,
            OemCapabilityAvailability.TransportUnsupported => UdsResponseKind.Unsupported,
            OemCapabilityAvailability.InvalidResponse => UdsResponseKind.Malformed,
            _ => UdsResponseKind.Malformed
        };
        return new UdsResponse(
            kind,
            capability.ResponseService == 0 ? request[0] : capability.ResponseService,
            capability.ResponsePayload ?? Array.Empty<byte>(),
            capability.NegativeResponseCode,
            capability.Evidence);
    }

    private OemDtcScanResult Publish(OemDtcScanResult result)
    {
        Current = result;
        DtcUpdated?.Invoke(this, result);
        return result;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
