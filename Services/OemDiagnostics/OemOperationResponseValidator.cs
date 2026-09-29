using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

public static class OemOperationResponseValidator
{
    public static (bool Success, string Detail) Validate(
        byte[] request,
        UdsResponse response,
        string expectedPayloadHex = "")
    {
        if (response.Kind != UdsResponseKind.Positive)
            return (false, response.Kind == UdsResponseKind.Negative && response.NegativeResponseCode.HasValue
                ? $"ECU rejected request with NRC 0x{response.NegativeResponseCode.Value:X2}: {response.Message}"
                : string.IsNullOrWhiteSpace(response.Message) ? response.Kind.ToString() : response.Message);
        var expectedService = (byte)(request[0] + 0x40);
        if (response.Service != expectedService)
            return (false, $"Unexpected positive service 0x{response.Service:X2}; expected 0x{expectedService:X2}.");

        var echoLength = request[0] switch
        {
            0x10 => 1,
            0x22 or 0x2E or 0x2F => 2,
            0x31 => 3,
            _ => 0
        };
        if (response.Payload.Length < echoLength)
            return (false, "Positive response echo is truncated.");
        if (request[0] == 0x10)
        {
            if ((response.Payload[0] & 0x7F) != (request[1] & 0x7F))
                return (false, "Diagnostic session response does not match the request.");
        }
        else if (echoLength > 0 && !response.Payload.AsSpan(0, echoLength).SequenceEqual(request.AsSpan(1, echoLength)))
        {
            return (false, "Positive response identifier/routine echo does not match the request.");
        }

        if (!string.IsNullOrWhiteSpace(expectedPayloadHex))
        {
            var expected = ParsePayload(expectedPayloadHex);
            if (response.Payload.Length < expected.Length ||
                !response.Payload.AsSpan(0, expected.Length).SequenceEqual(expected))
                return (false, "Verification response does not contain the reviewed expected value.");
        }
        return (true, "ECU returned the expected positive response.");
    }

    private static byte[] ParsePayload(string value)
    {
        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray());
        return Enumerable.Range(0, normalized.Length / 2)
            .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
            .ToArray();
    }
}
