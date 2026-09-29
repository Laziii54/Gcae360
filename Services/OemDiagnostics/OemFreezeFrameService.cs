using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Reads DTC snapshots only for DTCs actually returned by the ECU.</summary>
public sealed class OemFreezeFrameService
{
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly ManufacturerDefinitionCatalogService _manufacturers;
    private readonly OemDefinitionCatalogService _definitions;
    private readonly OemAddressRegistry _addresses;
    private readonly UdsProtocol _uds;
    private readonly OBDService _obd;

    public OemFreezeFrameScanResult? Current { get; private set; }
    public event EventHandler<OemFreezeFrameScanResult>? FreezeFramesUpdated;

    public OemFreezeFrameService(
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

    public async Task<OemFreezeFrameScanResult> ReadAsync(
        VehicleInfo vehicle,
        OemDtcScanResult dtcScan,
        CancellationToken token = default)
    {
        if (dtcScan.Ecus.Count == 0)
            return Publish(OemFreezeFrameScanResult.Unavailable(
                "OEM Freeze Frame არ წაკითხულა — OEM DTC შედეგი არ არსებობს."));

        var resolvedVehicle = _vehicleProfiles.Resolve(vehicle);
        await _definitions.EnsureLoadedAsync(token).ConfigureAwait(false);
        var manufacturer = _manufacturers.Resolve(resolvedVehicle);
        var dtcByDefinition = dtcScan.Ecus.ToDictionary(
            result => result.DefinitionId, StringComparer.OrdinalIgnoreCase);
        var definitions = _definitions.GetForVehicle(resolvedVehicle, manufacturer)
            .Where(definition => dtcByDefinition.ContainsKey(definition.DefinitionId))
            .ToArray();
        var addresses = _addresses.GetForVehicle(resolvedVehicle)
            .ToDictionary(address => address.DefinitionId, StringComparer.OrdinalIgnoreCase);
        var results = new List<OemEcuFreezeFrameResult>();
        var sentCommand = false;
        try
        {
            foreach (var definition in definitions)
            {
                token.ThrowIfCancellationRequested();
                var command = definition.ReadOnlyCommands.SingleOrDefault(item =>
                    item.CapabilityCode.Equals("FREEZEFRAME.READ", StringComparison.OrdinalIgnoreCase));
                if (command is null || !addresses.TryGetValue(definition.DefinitionId, out var address))
                    continue;

                var dtcResult = dtcByDefinition[definition.DefinitionId];
                if (dtcResult.State != OemDtcReadState.Available || dtcResult.Records.Count == 0)
                {
                    results.Add(new OemEcuFreezeFrameResult(
                        definition.DefinitionId, definition.EcuId, definition.EcuName,
                        OemFreezeFrameReadState.NotApplicable,
                        Array.Empty<OemFreezeFrameRecord>(),
                        dtcResult.State == OemDtcReadState.Available
                            ? "OEM DTC არ აღმოჩნდა — DTC snapshot მოთხოვნა საჭირო არ არის."
                            : "OEM DTC სია ვერ დადასტურდა — snapshot მოთხოვნა უსაფრთხოდ არ გაიგზავნა."));
                    continue;
                }

                var decoded = new List<OemFreezeFrameDecodeResult>();
                foreach (var dtc in dtcResult.Records
                             .DistinctBy(record => record.RawCodeHex)
                             .Take(command.MaxSnapshotDtcs))
                {
                    token.ThrowIfCancellationRequested();
                    var request = BuildRequest(command, dtc.RawCodeHex);
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
                    catch (Exception exception)
                    {
                        response = new UdsResponse(
                            UdsResponseKind.Malformed,
                            0x19,
                            Array.Empty<byte>(),
                            Message: exception.Message);
                    }
                    decoded.Add(OemFreezeFrameResponseDecoder.Decode(command, dtc.RawCodeHex, response));
                }

                var frames = decoded
                    .Where(result => result.State == OemFreezeFrameReadState.Available)
                    .SelectMany(result => result.Records)
                    .ToArray();
                var successful = decoded.Count(result => result.State == OemFreezeFrameReadState.Available);
                var state = successful > 0
                    ? OemFreezeFrameReadState.Available
                    : decoded.Select(result => result.State).FirstOrDefault();
                results.Add(new OemEcuFreezeFrameResult(
                    definition.DefinitionId,
                    definition.EcuId,
                    definition.EcuName,
                    state,
                    frames,
                    state == OemFreezeFrameReadState.Available
                        ? frames.Length == 0
                            ? "ECU-მ snapshot მოთხოვნას უპასუხა, მაგრამ შენახული snapshot არ დააბრუნა."
                            : $"{frames.Length} OEM DTC snapshot record წაკითხულია."
                        : string.Join("; ", decoded.Select(result => result.Detail).Distinct())));
            }
        }
        finally
        {
            if (sentCommand)
                await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var available = results.Count(result => result.State == OemFreezeFrameReadState.Available);
        return Publish(new OemFreezeFrameScanResult(
            results,
            results.Count == 0 ? "NOT AVAILABLE" : available == results.Count ? "AVAILABLE" : "PARTIAL",
            results.Count == 0
                ? "Responding OEM ECU-სთვის reviewed freeze-frame definition არ არსებობს."
                : $"OEM Freeze Frame: {available}/{results.Count} ECU-მ snapshot მოთხოვნას უპასუხა."));
    }

    public static byte[] BuildRequest(OemReadOnlyCommandDefinition command, string rawDtcHex)
    {
        var request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
        var dtc = Convert.FromHexString(rawDtcHex);
        if (request.Length != 6 || request[0] != 0x19 || request[1] != 0x04 || dtc.Length != 3)
            throw new InvalidOperationException("Invalid freeze-frame template or DTC number.");
        request[2] = dtc[0];
        request[3] = dtc[1];
        request[4] = dtc[2];
        return request;
    }

    private OemFreezeFrameScanResult Publish(OemFreezeFrameScanResult result)
    {
        Current = result;
        FreezeFramesUpdated?.Invoke(this, result);
        return result;
    }
}
