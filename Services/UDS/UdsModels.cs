namespace CarScanner.Services.UDS;

public enum UdsResponseKind { Positive, Negative, Timeout, Malformed, Unsupported }

public sealed record UdsResponse(
    UdsResponseKind Kind,
    byte Service,
    byte[] Payload,
    byte? NegativeResponseCode = null,
    string Message = "");

public static class UdsNegativeResponseDecoder
{
    public static string Describe(byte nrc) => nrc switch
    {
        0x10 => "General reject",
        0x11 => "Service not supported",
        0x12 => "Sub-function not supported",
        0x13 => "Incorrect message length or invalid format",
        0x21 => "Busy repeat request",
        0x22 => "Conditions not correct",
        0x24 => "Request sequence error",
        0x31 => "Request out of range",
        0x33 => "Security access denied",
        0x35 => "Invalid key",
        0x36 => "Exceeded number of attempts",
        0x37 => "Required time delay not expired",
        0x70 => "Upload/download not accepted",
        0x72 => "General programming failure",
        0x73 => "Wrong block sequence counter",
        0x78 => "Response pending",
        0x7E => "Sub-function not supported in current session",
        0x7F => "Service not supported in current session",
        _ => $"NRC 0x{nrc:X2}"
    };
}

public static class UdsResponseParser
{
    public static UdsResponse Parse(byte[] payload)
    {
        if (payload.Length == 0) return new(UdsResponseKind.Malformed, 0, Array.Empty<byte>(), Message: "Empty UDS response");
        if (payload.Length >= 3 && payload[0] == 0x7F)
            return new(UdsResponseKind.Negative, payload[1], payload[2..], payload[2], UdsNegativeResponseDecoder.Describe(payload[2]));
        return new(UdsResponseKind.Positive, payload[0], payload[1..]);
    }
}
