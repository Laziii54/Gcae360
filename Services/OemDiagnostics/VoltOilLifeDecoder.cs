using CarScanner.Services.OBD;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Strict decoder for the existing research DID 0x119F read path.
/// Parsing tests do not establish the DID's meaning on a real vehicle.
/// </summary>
public static class VoltOilLifeDecoder
{
    /// <summary>
    /// Attempts to extract and decode oil-life percent from a raw ELM327/OBD
    /// response string. Returns true only after an exact positive 0x62 DID
    /// echo with at least one data byte is present.
    /// </summary>
    public static bool TryDecodeFromRaw(string? raw, out double percent, int did = 0x119F)
    {
        percent = 0;
        if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
            return false;

        // Parse complete lines, never search arbitrary payloads for a DID.
        // Production requests enable headers; a header identifies the sender.
        var values = new List<byte>();
        foreach (var line in raw.Replace(">", "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var compact = string.Concat(line.Where(c => !char.IsWhiteSpace(c)));
            if (compact.Equals($"22{did:X4}", StringComparison.OrdinalIgnoreCase)) continue;
            if (compact.Length % 2 == 1)
            {
                if (!compact.StartsWith("7E8", StringComparison.OrdinalIgnoreCase)) return false;
                compact = compact[3..];
            }
            if (compact.Length == 0 || compact.Length % 2 != 0 || compact.Any(c => !Uri.IsHexDigit(c))) return false;
            var bytes = Convert.FromHexString(compact);
            if (bytes.Length > 0 && bytes[0] == 0x04)
                bytes = bytes.Length is >= 5 and <= 8 ? bytes[1..5] : Array.Empty<byte>();
            if (bytes.Length != 4 || bytes[0] != 0x62 || bytes[1] != (byte)(did >> 8) || bytes[2] != (byte)did)
                return false;
            values.Add(bytes[3]);
        }
        if (values.Count != 1) return false;
        percent = values[0] * 100d / 255d;
        return true;
    }
}
