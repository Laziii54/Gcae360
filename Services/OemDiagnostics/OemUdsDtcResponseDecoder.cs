using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Strict decoder for UDS ReadDTCInformation reportDTCByStatusMask responses.
/// Each accepted record is exactly three DTC bytes followed by one status byte.
/// </summary>
public static class OemUdsDtcResponseDecoder
{
    public static OemDtcDecodeResult Decode(
        OemReadOnlyCommandDefinition command,
        UdsResponse response)
    {
        if (response.Kind != UdsResponseKind.Positive)
            return OemDtcDecodeResult.Failure(MapFailure(response), ResponseDetail(response));
        if (response.Service != 0x59)
            return OemDtcDecodeResult.Failure(
                OemDtcReadState.InvalidResponse,
                $"Expected UDS positive service 0x59, received 0x{response.Service:X2}.");

        byte[] request;
        try
        {
            request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
        }
        catch (Exception exception)
        {
            return OemDtcDecodeResult.Failure(OemDtcReadState.Error, exception.Message);
        }

        if (request[0] != 0x19 ||
            request[1] == 0x02 && request.Length != 3 ||
            request[1] == 0x0A && request.Length != 2 ||
            request[1] is not (0x02 or 0x0A))
            return OemDtcDecodeResult.Failure(
                OemDtcReadState.Error,
                "Command is not a supported UDS DTC-by-status-mask request.");
        if (response.Payload.Length < 2 || response.Payload[0] != request[1])
            return OemDtcDecodeResult.Failure(
                OemDtcReadState.InvalidResponse,
                "Positive response sub-function does not match the reviewed request.");

        var recordBytes = response.Payload.AsSpan(2);
        if (recordBytes.Length % 4 != 0)
            return OemDtcDecodeResult.Failure(
                OemDtcReadState.InvalidResponse,
                $"DTC payload contains {recordBytes.Length} record bytes; groups of four are required.");

        var records = new List<OemRawDtcRecord>();
        for (var offset = 0; offset < recordBytes.Length; offset += 4)
        {
            var raw = recordBytes.Slice(offset, 3);
            if (raw[0] == 0 && raw[1] == 0 && raw[2] == 0)
                return OemDtcDecodeResult.Failure(
                    OemDtcReadState.InvalidResponse,
                    "ECU returned an invalid all-zero three-byte DTC record.");

            records.Add(new OemRawDtcRecord(Convert.ToHexString(raw), recordBytes[offset + 3]));
        }

        return new OemDtcDecodeResult(
            OemDtcReadState.Available,
            response.Payload[1],
            records,
            records.Count == 0
                ? "ECU DTC მოთხოვნას უპასუხა; შესაბამისი კოდი არ დაბრუნებულა."
                : $"ECU-დან {records.Count} UDS DTC ჩანაწერი რეალურად წაიკითხა.");
    }

    private static OemDtcReadState MapFailure(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode is 0x11 or 0x12 or 0x31 =>
            OemDtcReadState.NotSupported,
        UdsResponseKind.Timeout => OemDtcReadState.NoResponse,
        UdsResponseKind.Malformed => OemDtcReadState.InvalidResponse,
        UdsResponseKind.Unsupported => OemDtcReadState.NotSupported,
        _ => OemDtcReadState.Error
    };

    private static string ResponseDetail(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode.HasValue =>
            $"ECU NRC 0x{response.NegativeResponseCode.Value:X2}: {response.Message}",
        UdsResponseKind.Timeout => "ECU response timeout.",
        UdsResponseKind.Unsupported => string.IsNullOrWhiteSpace(response.Message)
            ? "Adapter/transport does not support the request."
            : response.Message,
        _ => string.IsNullOrWhiteSpace(response.Message) ? "DTC response unavailable." : response.Message
    };
}
