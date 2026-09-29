using System.Diagnostics;
using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Probes only ECU addresses that were registered with a verified source and
/// a read-only UDS request. It never guesses CAN IDs and never treats a
/// missing answer as proof that an ABS/SRS/etc. module does not exist.
/// </summary>
public sealed class OemModuleDiscoveryService
{
    private readonly OemProfileResolver _profiles;
    private readonly OemAddressRegistry _addresses;
    private readonly OemDefinitionCatalogService _definitions;
    private readonly ManufacturerDefinitionCatalogService _manufacturerDefinitions;
    private readonly OemTransportReadinessService _transport;
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly OBDService _obd;
    private readonly UdsProtocol _uds;

    public OemModuleDiscoveryService(
        OemProfileResolver profiles,
        OemAddressRegistry addresses,
        OemDefinitionCatalogService definitions,
        ManufacturerDefinitionCatalogService manufacturerDefinitions,
        OemTransportReadinessService transport,
        VehicleProfileResolverService vehicleProfiles,
        OBDService obd,
        UdsProtocol uds)
    {
        _profiles = profiles;
        _addresses = addresses;
        _definitions = definitions;
        _manufacturerDefinitions = manufacturerDefinitions;
        _transport = transport;
        _vehicleProfiles = vehicleProfiles;
        _obd = obd;
        _uds = uds;
    }

    public async Task<OemModuleDiscoveryResult> DiscoverAsync(
        VehicleInfo vehicle,
        CancellationToken token = default)
    {
        var resolvedVehicle = _vehicleProfiles.Resolve(vehicle);
        if (!resolvedVehicle.CanUseManufacturerSpecificDefinitions)
        {
            return OemModuleDiscoveryResult.NoDefinition(
                $"{resolvedVehicle.VerificationMessage} OEM ECU მოთხოვნა არ გაიგზავნა.");
        }

        var verifiedVehicle = resolvedVehicle.DiagnosticVehicle;
        await _definitions.EnsureLoadedAsync(token).ConfigureAwait(false);
        var manufacturerDefinition = _manufacturerDefinitions.Resolve(resolvedVehicle);
        if (!manufacturerDefinition.HasVerifiedReadOnlyDefinition)
            return OemModuleDiscoveryResult.NoDefinition(manufacturerDefinition.Evidence);

        var executableDefinitions = _definitions.GetForVehicle(resolvedVehicle, manufacturerDefinition);
        if (executableDefinitions.Count == 0)
        {
            var detail = _definitions.LoadErrors.Count > 0
                ? $"OEM definition package უარყოფილია: {string.Join("; ", _definitions.LoadErrors)}"
                : "ამ მოდელის/წლის executable ECU definition პაკეტში არ მოიძებნა.";
            return OemModuleDiscoveryResult.NoDefinition(detail);
        }

        var adapter = await _obd.GetAdapterDiagnosticsAsync(token).ConfigureAwait(false);
        var readiness = _transport.Evaluate(adapter);
        if (readiness.State != CapabilityState.Conditional)
            return OemModuleDiscoveryResult.NotReady(readiness.Evidence);

        var profile = _profiles.Resolve(verifiedVehicle);
        if (profile is UniversalOemProfile)
            return OemModuleDiscoveryResult.NoDefinition("VIN-ით დადასტურებული OEM მარკა ვერ განისაზღვრა.");

        var allowedDefinitionIds = executableDefinitions
            .Select(definition => definition.DefinitionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = _addresses.GetForVehicle(resolvedVehicle)
            .Where(address => allowedDefinitionIds.Contains(address.DefinitionId))
            .ToArray();
        if (candidates.Length == 0)
            return OemModuleDiscoveryResult.NoDefinition(
                $"{profile.DisplayName}-ისთვის ამ მოდელის/წლის დადასტურებული ECU მისამართები ჯერ არ არის.");

        var discovered = new List<OemDiscoveredModule>();
        var probes = new List<OemModuleProbeResult>();
        var definitionsById = executableDefinitions.ToDictionary(
            definition => definition.DefinitionId,
            StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var address in candidates)
            {
                token.ThrowIfCancellationRequested();
                if (!definitionsById.TryGetValue(address.DefinitionId, out var definition))
                    continue;

                var module = profile.GetModules()
                    .FirstOrDefault(item => item.Id.Equals(address.EcuId, StringComparison.OrdinalIgnoreCase));
                var name = module?.Name ?? definition.EcuName;
                var ecuName = module?.EcuName ?? definition.EcuName;
                var timeoutMs = definition.ReadOnlyCommands
                    .Single(command => command.DiscoveryProbe)
                    .TimeoutMs;
                var stopwatch = Stopwatch.StartNew();
                UdsResponse response;
                try
                {
                    response = await _uds.SendIsoTpAsync(
                        address.ReadOnlyProbe,
                        address.TxCanId,
                        address.RxCanId,
                        address.Extended29Bit,
                        TimeSpan.FromMilliseconds(timeoutMs),
                        token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    probes.Add(new OemModuleProbeResult(
                        address.DefinitionId, address.EcuId, name, ecuName,
                        definition.Transport.CanBus, OemModuleProbeState.Error,
                        string.IsNullOrWhiteSpace(ex.Message) ? "Unexpected transport error" : ex.Message,
                        stopwatch.ElapsedMilliseconds, address.Source, 0, Array.Empty<byte>()));
                    continue;
                }
                stopwatch.Stop();

                var state = MapState(response.Kind);
                var detail = ResponseDetail(response);
                probes.Add(new OemModuleProbeResult(
                    address.DefinitionId, address.EcuId, name, ecuName,
                    definition.Transport.CanBus, state, detail,
                    stopwatch.ElapsedMilliseconds, address.Source,
                    response.Service, response.Payload, response.NegativeResponseCode));

                if (response.Kind != UdsResponseKind.Positive)
                    continue;

                discovered.Add(new OemDiscoveredModule(
                    address.DefinitionId,
                    address.EcuId,
                    name,
                    ecuName,
                    definition.Transport.CanBus,
                    address.Source,
                    address.Notes));
            }
        }
        finally
        {
            // Raw CAN header/filter settings must never leak into the normal
            // Live Data/DTC flow after an OEM probe.
            try { await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }

        if (discovered.Count == probes.Count && discovered.Count > 0)
            return OemModuleDiscoveryResult.Found(discovered, probes);
        if (discovered.Count > 0 || probes.Any(probe => probe.State == OemModuleProbeState.RespondedLimited))
            return OemModuleDiscoveryResult.Partial(discovered, probes);
        return OemModuleDiscoveryResult.NoResponse(
            "დადასტურებულ მისამართებზე დადებითი read-only ECU პასუხი ვერ მივიღეთ.",
            probes);
    }

    private static OemModuleProbeState MapState(UdsResponseKind kind) => kind switch
    {
        UdsResponseKind.Positive => OemModuleProbeState.Available,
        UdsResponseKind.Negative => OemModuleProbeState.RespondedLimited,
        UdsResponseKind.Timeout => OemModuleProbeState.NoResponse,
        UdsResponseKind.Unsupported => OemModuleProbeState.TransportUnsupported,
        UdsResponseKind.Malformed => OemModuleProbeState.MalformedResponse,
        _ => OemModuleProbeState.Error
    };

    private static string ResponseDetail(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Positive => "Read-only discovery response received",
        UdsResponseKind.Negative when response.NegativeResponseCode.HasValue =>
            $"ECU responded: NRC 0x{response.NegativeResponseCode.Value:X2} — {response.Message}",
        UdsResponseKind.Timeout => "No response before the verified definition timeout",
        UdsResponseKind.Unsupported => $"Transport not supported: {response.Message}",
        UdsResponseKind.Malformed => $"Malformed response: {response.Message}",
        _ => string.IsNullOrWhiteSpace(response.Message) ? "Unknown response state" : response.Message
    };
}
