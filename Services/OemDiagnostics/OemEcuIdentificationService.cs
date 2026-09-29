using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Reads OEM identification only from exact, reviewed definitions. Unknown
/// payloads are never heuristically interpreted as part/software numbers.
/// </summary>
public sealed class OemEcuIdentificationService
{
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly ManufacturerDefinitionCatalogService _manufacturers;
    private readonly OemDefinitionCatalogService _definitions;
    private readonly OemAddressRegistry _addresses;
    private readonly OemModuleDiscoveryService _discovery;
    private readonly UdsProtocol _uds;
    private readonly OBDService _obd;

    public OemIdentificationScanResult? Current { get; private set; }
    public event EventHandler<OemIdentificationScanResult>? IdentificationUpdated;

    public OemEcuIdentificationService(
        VehicleProfileResolverService vehicleProfiles,
        ManufacturerDefinitionCatalogService manufacturers,
        OemDefinitionCatalogService definitions,
        OemAddressRegistry addresses,
        OemModuleDiscoveryService discovery,
        UdsProtocol uds,
        OBDService obd)
    {
        _vehicleProfiles = vehicleProfiles;
        _manufacturers = manufacturers;
        _definitions = definitions;
        _addresses = addresses;
        _discovery = discovery;
        _uds = uds;
        _obd = obd;
    }

    public async Task<OemIdentificationScanResult> ReadAsync(
        VehicleInfo vehicle,
        CancellationToken token = default)
    {
        var resolvedVehicle = _vehicleProfiles.Resolve(vehicle);
        if (!resolvedVehicle.CanUseManufacturerSpecificDefinitions)
            return Publish(OemIdentificationScanResult.Unavailable(resolvedVehicle.VerificationMessage));

        await _definitions.EnsureLoadedAsync(token).ConfigureAwait(false);
        var manufacturer = _manufacturers.Resolve(resolvedVehicle);
        var definitions = _definitions.GetForVehicle(resolvedVehicle, manufacturer);
        if (definitions.Count == 0)
            return Publish(OemIdentificationScanResult.Unavailable(
                "ამ ზუსტი მოდელის/წლის reviewed OEM identification definition არ არსებობს."));

        var discovery = await _discovery.DiscoverAsync(vehicle, token).ConfigureAwait(false);
        var responding = discovery.Probes
            .Where(probe => probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited)
            .ToDictionary(probe => probe.DefinitionId, StringComparer.OrdinalIgnoreCase);
        if (responding.Count == 0)
            return Publish(OemIdentificationScanResult.Unavailable(discovery.Detail));

        var addresses = _addresses.GetForVehicle(resolvedVehicle)
            .ToDictionary(address => address.DefinitionId, StringComparer.OrdinalIgnoreCase);
        var values = new List<OemIdentificationValue>();
        var sentCommand = false;
        try
        {
            foreach (var definition in definitions.Where(item => responding.ContainsKey(item.DefinitionId)))
            {
                if (!addresses.TryGetValue(definition.DefinitionId, out var address))
                    continue;

                var discoveryProbe = responding[definition.DefinitionId];
                foreach (var command in definition.ReadOnlyCommands.Where(command =>
                             command.CapabilityCode.Equals("IDENTIFICATION.READ", StringComparison.OrdinalIgnoreCase)))
                {
                    token.ThrowIfCancellationRequested();
                    UdsResponse response;
                    if (command.DiscoveryProbe)
                    {
                        response = RebuildDiscoveryResponse(command, discoveryProbe);
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

                    values.AddRange(OemIdentificationResponseDecoder.Decode(definition, command, response));
                }
            }
        }
        finally
        {
            if (sentCommand)
                await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var available = values.Count(value => value.State == OemIdentificationValueState.Available);
        return Publish(new OemIdentificationScanResult(
            values,
            values.Count == 0 ? "NOT AVAILABLE" : available == values.Count ? "AVAILABLE" : "PARTIAL",
            values.Count == 0
                ? "OEM ECU პასუხობს, მაგრამ reviewed identification field ამ definition-ში არ არის."
                : $"{values.Count} OEM identification ველი შემოწმდა; მიღებულია: {available}."));
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

    private OemIdentificationScanResult Publish(OemIdentificationScanResult result)
    {
        Current = result;
        IdentificationUpdated?.Invoke(this, result);
        return result;
    }
}
