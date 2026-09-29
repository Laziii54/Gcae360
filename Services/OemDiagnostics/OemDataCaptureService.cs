using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

public sealed class OemDataCaptureService
{
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly OemAddressRegistry _addresses;
    private readonly OemModuleDiscoveryService _discovery;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    public OemDataCaptureService(
        VehicleProfileResolverService vehicleProfiles,
        OemAddressRegistry addresses,
        OemModuleDiscoveryService discovery)
    {
        _vehicleProfiles = vehicleProfiles;
        _addresses = addresses;
        _discovery = discovery;
    }

    public async Task<OemDataCapture> CaptureAsync(
        VehicleInfo vehicle,
        IReadOnlyList<ServiceMetricReading>? serviceReadings = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        var resolvedVehicle =
            _vehicleProfiles.Resolve(vehicle);

        var verifiedAddresses =
            _addresses.GetForVehicle(resolvedVehicle);

        var discovery =
            await _discovery.DiscoverAsync(
                vehicle,
                token);

        var result =
            new OemDataCapture
            {
                SchemaVersion = 3,

                CapturedAtUtc =
                    DateTimeOffset.UtcNow,

                Manufacturer =
                    vehicle.Manufacturer ?? "",

                Model =
                    vehicle.Model ?? "",

                Year =
                    vehicle.Year ?? "",

                Engine =
                    vehicle.Engine ?? "",

                Protocol =
                    vehicle.Protocol ?? ""
            };

        foreach (var reading in serviceReadings ?? Array.Empty<ServiceMetricReading>())
        {
            result.ServiceValues.Add(new ServiceResetCaptureValue
            {
                Id = reading.Id,
                Kind = reading.Kind.ToString(),
                SourceName = reading.SourceName,
                NumericValue = reading.NumericValue,
                Unit = reading.Unit,
                Detail = reading.Detail,
                SourceCatalog = reading.SourceCatalog,
                EvidenceLevel = reading.EvidenceLevel.ToString(),
                TxCanId = reading.TransmitCanId,
                RxCanId = reading.ReceiveCanId,
                DataIdentifier = reading.DataIdentifier
            });
        }

        foreach (var address in verifiedAddresses)
        {
            token.ThrowIfCancellationRequested();

            var probe =
                discovery.Probes
                    .FirstOrDefault(item =>
                        item.DefinitionId.Equals(
                            address.DefinitionId,
                            StringComparison.OrdinalIgnoreCase) &&

                        item.EcuId.Equals(
                            address.EcuId,
                            StringComparison.OrdinalIgnoreCase));

            var requestHex =
                Convert.ToHexString(
                    address.ReadOnlyProbe);

            var identityProbe =
                IsVehicleIdentifierProbe(address.ReadOnlyProbe);

            var responsePayload =
                identityProbe
                    ? Array.Empty<byte>()
                    : probe?.ResponsePayload
                      ?? Array.Empty<byte>();

            var responsePayloadHex =
                responsePayload.Length > 0
                    ? Convert.ToHexString(
                        responsePayload)
                    : "";

            var responseServiceHex =
                probe is not null &&
                probe.ResponseService != 0
                    ? $"0x{probe.ResponseService:X2}"
                    : "";

            var negativeResponseCodeHex =
                probe?.NegativeResponseCode is byte nrc
                    ? $"0x{nrc:X2}"
                    : "";

            var responseHex =
                identityProbe
                    ? "[redacted vehicle identifier]"
                    : BuildUdsResponseHex(
                        probe?.ResponseService ?? 0,
                        responsePayload,
                        probe?.NegativeResponseCode);

            var responseCaptured =
                probe is not null &&
                (
                    probe.ResponseService != 0 ||
                    responsePayload.Length > 0 ||
                    probe.NegativeResponseCode.HasValue
                );

            result.Modules.Add(
                new OemDataCaptureModule
                {
                    DefinitionId =
                        address.DefinitionId,

                    EcuId =
                        probe?.EcuId
                        ?? address.EcuId,

                    Name =
                        probe?.Name
                        ?? address.EcuId,

                    TxCanId =
                        address.TxCanId,

                    RxCanId =
                        address.RxCanId,

                    Extended29Bit =
                        address.Extended29Bit,

                    CanBus =
                        probe?.CanBus
                        ?? "",

                    State =
                        probe?.State.ToString()
                        ?? "NotProbed",

                    ElapsedMilliseconds =
                        probe?.ElapsedMilliseconds
                        ?? 0,

                    RequestHex =
                        requestHex,

                    ResponseHex =
                        responseHex,

                    ResponseServiceHex =
                        responseServiceHex,

                    ResponsePayloadHex =
                        responsePayloadHex,

                    NegativeResponseCodeHex =
                        negativeResponseCodeHex,

                    ResponseCaptured =
                        responseCaptured,

                    Detail =
                        probe?.Detail
                        ?? "",

                    Source =
                        probe?.Source
                        ?? address.Source
                });
        }

        foreach (var entry in ServiceDiagnosticTrace.SnapshotForExport())
        {
            result.Trace.Add(new ServiceResetCaptureTraceEntry
            {
                TimestampUtc = entry.TimestampUtc,
                Context = entry.Context,
                Request = entry.Request,
                Response = entry.Response,
                Result = entry.Result,
                TxCanId = entry.TxCanId,
                RxCanId = entry.RxCanId
            });
        }

        return result;
    }

    private static bool IsVehicleIdentifierProbe(byte[] request) =>
        request.Length >= 3 && request[0] == 0x22 && request[1] == 0xF1 && request[2] == 0x90;

    public async Task<string> CaptureAndSaveAsync(
        VehicleInfo vehicle,
        IReadOnlyList<ServiceMetricReading>? serviceReadings = null,
        CancellationToken token = default)
    {
        var capture =
            await CaptureAsync(
                vehicle,
                serviceReadings,
                token);

        var json =
            JsonSerializer.Serialize(
                capture,
                JsonOptions);

        var safeBrand =
            SafeFilePart(
                capture.Manufacturer);

        var safeModel =
            SafeFilePart(
                capture.Model);

        var timestamp =
            DateTimeOffset.UtcNow
                .ToString(
                    "yyyyMMdd-HHmmss");

        var fileName =
            $"service-reset-validation-{safeBrand}-{safeModel}-{timestamp}.json";

        var path =
            Path.Combine(
                FileSystem.CacheDirectory,
                fileName);

        await File.WriteAllTextAsync(
            path,
            json,
            token);

        return path;
    }

    private static string BuildUdsResponseHex(
        byte responseService,
        byte[] responsePayload,
        byte? negativeResponseCode)
    {
        if (negativeResponseCode is byte nrc)
        {
            if (responseService == 0)
                return "";

            return
                $"7F{responseService:X2}{nrc:X2}";
        }

        if (responseService != 0)
        {
            return
                $"{responseService:X2}" +
                Convert.ToHexString(
                    responsePayload);
        }

        if (responsePayload.Length > 0)
        {
            return
                Convert.ToHexString(
                    responsePayload);
        }

        return "";
    }

    private static string SafeFilePart(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var invalid =
            Path.GetInvalidFileNameChars();

        return new string(
            value.Trim()
                .Select(character =>
                    invalid.Contains(character)
                        ? '-'
                        : character)
                .ToArray())
            .Replace(
                ' ',
                '-');
    }
}
