using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CarScanner.Services.OBD;

/// <summary>
/// Shared response parser. ELM327 adapters can return spaces, line breaks, echo,
/// CAN headers, ISO-TP frames and ASCII noise depending on their configuration.
/// This parser deliberately keeps raw bytes available and only strips framing that
/// is unambiguous.
/// </summary>
public static class ObdResponseParser
{
    private static readonly Regex HexPair = new(@"(?<![0-9A-F])([0-9A-F]{2})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CanToken = new(@"(?<![0-9A-F])([0-9A-F]{8}|[0-9A-F]{3}|[0-9A-F]{2})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static byte[] ParseHex(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<byte>();
        var matches = HexPair.Matches(raw.ToUpperInvariant());
        if (matches.Count > 0)
        {
            var list = new List<byte>(matches.Count);
            foreach (Match m in matches) list.Add(byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return list.ToArray();
        }

        var compact = Regex.Replace(raw.ToUpperInvariant(), @"[^0-9A-F]", "");
        if ((compact.Length & 1) != 0) return Array.Empty<byte>();
        var result = new byte[compact.Length / 2];
        for (var i = 0; i < result.Length; i++) result[i] = byte.Parse(compact.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return result;
    }

    public static byte[] ExtractPayload(string raw, byte positiveService, byte? pid = null)
    {
        var bytes = ParseHex(raw);
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] != positiveService || (pid.HasValue && bytes[i + 1] != pid.Value)) continue;
            var start = i + (pid.HasValue ? 2 : 1);
            return bytes[start..];
        }
        return Array.Empty<byte>();
    }

    public static byte[] ExtractMode01Pid(string raw, byte pid) => ExtractPayload(raw, 0x41, pid);

    public static string DecodeAscii(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        var chars = bytes.Where(b => b >= 0x20 && b <= 0x7E).Select(b => (char)b).ToArray();
        return new string(chars).Trim('\0', ' ', '\r', '\n', '\t');
    }

    public static string DecodeAsciiFromMode09(string raw, byte pid)
    {
        return DecodeAscii(ExtractPayload(raw, 0x49, pid));
    }

    public static bool IsNegative(string raw)
    {
        var t = (raw ?? string.Empty).ToUpperInvariant();
        return t.Contains("NO DATA") || t.Contains("ERROR") || t.Contains("UNABLE TO CONNECT") || t.Contains("CAN ERROR") || t.Trim() == "?";
    }

    /// <summary>
    /// Splits an ELM327 response with ATH1/ATS1/ATD0 into CAN data frames and
    /// removes the 11-bit or 29-bit identifier. When no header is present the
    /// response is returned as one byte stream, which also supports adapters
    /// that have already stripped ISO-TP framing.
    /// </summary>
    public static IReadOnlyList<byte[]> ParseCanFrames(string raw, int? expectedCanId = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<byte[]>();

        var matches = CanToken.Matches(raw.ToUpperInvariant());
        if (matches.Count == 0)
            return Array.Empty<byte[]>();

        var frames = new List<byte[]>();
        var current = new List<byte>(8);
        var sawHeader = false;
        var acceptCurrent = expectedCanId is null;

        void Flush()
        {
            if (acceptCurrent && current.Count > 0)
                frames.Add(current.ToArray());
            current.Clear();
        }

        foreach (Match match in matches)
        {
            var token = match.Groups[1].Value;
            if (token.Length is 3 or 8)
            {
                if (sawHeader)
                    Flush();

                sawHeader = true;
                var canId = int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                acceptCurrent = expectedCanId is null || canId == expectedCanId.Value;
                continue;
            }

            if (!sawHeader)
                acceptCurrent = true;

            if (acceptCurrent)
                current.Add(byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        Flush();

        // If a response had headers but none matched the requested ECU, do not
        // fall back to unrelated traffic from another module.
        if (sawHeader || frames.Count > 0)
            return frames;

        var headerless = ParseHex(raw);
        return headerless.Length == 0 ? Array.Empty<byte[]>() : new[] { headerless };
    }

    /// <summary>Returns only the CAN identifiers printed by ATH1. Data bytes
    /// are two digits and therefore cannot be confused with 3/8-digit IDs.</summary>
    public static IReadOnlyList<int> ExtractCanIdentifiers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<int>();

        return CanToken.Matches(raw.ToUpperInvariant())
            .Select(match => match.Groups[1].Value)
            .Where(token => token.Length is 3 or 8)
            .Select(token => int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Reassembles ISO-TP payload when the raw bytes contain FF/CF frames.
    /// If the adapter has already removed transport framing, the input is returned.
    /// </summary>
    public static byte[] ReassembleIsoTp(IEnumerable<byte> input)
    {
        var bytes = input.ToArray();
        if (bytes.Length == 0) return bytes;

        var first = bytes[0];
        var frameType = (byte)(first >> 4);
        if (frameType == 0) return bytes.Skip(1).Take(Math.Min(first & 0x0F, bytes.Length - 1)).ToArray();
        if (frameType != 1) return bytes;
        if (bytes.Length < 2) return Array.Empty<byte>();

        var total = ((first & 0x0F) << 8) | bytes[1];
        var output = new List<byte>(total);
        output.AddRange(bytes.Skip(2));
        var expectedSequence = 1;
        var index = 2 + Math.Max(0, bytes.Length - 2);
        // For a flat concatenated stream, try to interpret each following FF/CF block.
        // If the input isn't framed, callers still get the original payload via fallback.
        for (var i = 2; i < bytes.Length && output.Count < total; )
        {
            var b = bytes[i];
            if ((b >> 4) != 2) { i++; continue; }
            if ((b & 0x0F) != expectedSequence) { i++; continue; }
            output.AddRange(bytes.Skip(i + 1).Take(7));
            expectedSequence = (expectedSequence + 1) & 0x0F;
            i += 8;
        }
        return output.Count >= total ? output.Take(total).ToArray() : bytes;
    }
}
