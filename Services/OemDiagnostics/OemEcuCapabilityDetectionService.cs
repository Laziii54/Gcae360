using System.Diagnostics;
using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Confirms definition-declared ECU capabilities using exact read-only probes.
/// A definition entry alone never becomes an Available capability.
/// </summary>
public sealed class OemEcuCapabilityDetectionService
{
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly ManufacturerDefinitionCatalogService _manufacturers;
    private readonly OemDefinitionCatalogService _definitions;
    private readonly OemAddressRegistry _addresses;
    private readonly UdsProtocol _uds;
    private readonly OBDService _obd;

    public OemCapabilityDetectionResult? Current { get; private set; }
    public event EventHandler<OemCapabilityDetectionResult>? CapabilitiesUpdated;

    public OemEcuCapabilityDetectionService(
        VehicleProfileResolverService vehicleProfiles,
        ManufacturerDefinitionCatalogService manufacturers,
        OemDefinitionCatalogService definitions,
        OemAddressRegistry addresses,
        UdsProtocol uds,
        OBDService obd)
    {
        _vehicleProfiles = vehicleProfiles;
        _manufacturers = manufacturers;
        _definitions = definitions;
        _addresses = addresses;
        _uds = uds;
        _obd = obd;
    }

    public async Task<OemCapabilityDetectionResult> DetectAsync(
        VehicleInfo vehicle,
        OemModuleDiscoveryResult discovery,
        CancellationToken token = default)
    {
        var confirmedDefinitionIds = discovery.Probes
            .Where(probe => probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited)
            .Select(probe => probe.DefinitionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (confirmedDefinitionIds.Count == 0)
            return Publish(OemCapabilityDetectionResult.Empty(
                "Capability check არ დაწყებულა — პასუხმგებელი OEM ECU არ დადასტურდა."));

        var resolvedVehicle = _vehicleProfiles.Resolve(vehicle);
        await _definitions.EnsureLoadedAsync(token).ConfigureAwait(false);
        var manufacturer = _manufacturers.Resolve(resolvedVehicle);
        var definitions = _definitions.GetForVehicle(resolvedVehicle, manufacturer)
            .Where(definition => confirmedDefinitionIds.Contains(definition.DefinitionId))
            .ToArray();
        var addresses = _addresses.GetForVehicle(resolvedVehicle)
            .ToDictionary(address => address.DefinitionId, StringComparer.OrdinalIgnoreCase);
        var discoveryByDefinition = discovery.Probes
            .GroupBy(probe => probe.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var profiles = new List<OemEcuCapabilityProfile>();
        var sentCommand = false;
        try
        {
            foreach (var definition in definitions)
            {
                token.ThrowIfCancellationRequested();
                if (!addresses.TryGetValue(definition.DefinitionId, out var address) ||
                    !discoveryByDefinition.TryGetValue(definition.DefinitionId, out var discoveryProbe))
                    continue;

                var capabilities = new List<OemCapabilityProbeResult>();
                foreach (var command in definition.ReadOnlyCommands)
                {
                    token.ThrowIfCancellationRequested();
                    if (!command.ProbeDuringCapabilityDetection)
                    {
                        capabilities.Add(OemCapabilityResponseEvaluator.NotChecked(command));
                        continue;
                    }

                    if (command.DiscoveryProbe)
                    {
                        capabilities.Add(OemCapabilityResponseEvaluator.FromDiscovery(command, discoveryProbe));
                        continue;
                    }

                    var request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
                    var stopwatch = Stopwatch.StartNew();
                    UdsResponse response;
                    try
                    {
                        sentCommand = true;
                        response = await _uds.SendIsoTpAsync(
                            request,
                            address.TxCanId,
                            address.RxCanId,
                            address.Extended29Bit,
                            TimeSpan.FromMilliseconds(command.TimeoutMs),
                            token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        response = new UdsResponse(
                            UdsResponseKind.Malformed,
                            request[0],
                            Array.Empty<byte>(),
                            Message: string.IsNullOrWhiteSpace(ex.Message) ? "Capability probe failed" : ex.Message);
                    }
                    stopwatch.Stop();
                    capabilities.Add(OemCapabilityResponseEvaluator.Evaluate(
                        command, response, stopwatch.ElapsedMilliseconds));
                }

                profiles.Add(new OemEcuCapabilityProfile(
                    definition.DefinitionId,
                    definition.EcuId,
                    definition.EcuName,
                    capabilities));
            }
        }
        finally
        {
            if (sentCommand)
            {
                try { await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
        }

        var available = profiles.Sum(profile => profile.Capabilities.Count(capability =>
            capability.Availability == OemCapabilityAvailability.Available));
        return Publish(new OemCapabilityDetectionResult(
            profiles,
            profiles.Count == 0 ? "NOT CHECKED" : "COMPLETE",
            profiles.Count == 0
                ? "შესაბამისი executable ECU definition ვერ მოიძებნა."
                : $"{profiles.Count} ECU-ზე {available} capability დადებითი პასუხით დადასტურდა."));
    }

    private OemCapabilityDetectionResult Publish(OemCapabilityDetectionResult result)
    {
        Current = result;
        CapabilitiesUpdated?.Invoke(this, result);
        return result;
    }
}
