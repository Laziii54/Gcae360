using System.Globalization;
using System.Text.RegularExpressions;

namespace CarScanner.Services.OBD;

/// <summary>
/// Parses VIN responses without treating ELM/CAN/ISO-TP framing bytes as VIN data.
/// Supports standard Mode 09 PID 02 and UDS DID F190 responses, including
/// indexed Mode 09 records, ELM multiline output, CAN headers and ISO-TP frames.
/// </summary>
public static class VinResponseParser
{
    private static readonly Regex TokenRegex = new(@"[0-9A-Fa-f]+", RegexOptions.Compiled);

    public static string? ParseMode09Vin(string? raw) =>
        Parse(raw, new byte[] { 0x49, 0x02 }, allowMode09RecordIndex: true);

    public static string? ParseUdsVin(string? raw) =>
        Parse(raw, new byte[] { 0x62, 0xF1, 0x90 }, allowMode09RecordIndex: false);

    private static string? Parse(string? raw, byte[] marker, bool allowMode09RecordIndex)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var upper = raw.ToUpperInvariant();
        if (upper.Contains("NO DATA", StringComparison.Ordinal) ||
            upper.Contains("UNABLE TO CONNECT", StringComparison.Ordinal) ||
            upper.Contains("CAN ERROR", StringComparison.Ordinal) ||
            upper.Trim() == "?")
        {
            return null;
        }

        var lines = raw
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.Contains("SEARCHING", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Equals("0902", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Equals("22F190", StringComparison.OrdinalIgnoreCase))
            .Where(line => line != ">")
            .ToArray();

        // 1) Mode 09 record format: each record repeats 49 02 + record number.
        if (allowMode09RecordIndex)
        {
            var indexed = new SortedDictionary<int, List<byte>>();
            foreach (var line in lines)
            {
                foreach (var payload in CandidatePayloads(line))
                {
                    var index = IndexOf(payload, marker);
                    if (index < 0) continue;

                    var start = index + marker.Length;
                    if (start >= payload.Length) continue;

                    var record = payload[start];
                    if (record is < 1 or > 9) continue;

                    if (!indexed.TryGetValue(record, out var bucket))
                    {
                        bucket = new List<byte>();
                        indexed[record] = bucket;
                    }

                    bucket.AddRange(payload.Skip(start + 1));
                }
            }

            if (indexed.Count > 0)
            {
                var vin = DecodeVin(indexed.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value));
                if (IsValidVin(vin)) return vin;
            }
        }

        // 2) ISO-TP frames (with or without CAN headers).
        foreach (var stream in BuildIsoTpStreams(lines))
        {
            var index = IndexOf(stream, marker);
            if (index < 0) continue;

            var data = stream.Skip(index + marker.Length).ToArray();
            if (allowMode09RecordIndex && data.Length > 0 && data[0] is >= 1 and <= 9)
                data = data[1..];

            var vin = DecodeVin(data);
            if (IsValidVin(vin)) return vin;
        }

        // 3) Adapter already removed ISO-TP framing. Search each payload independently.
        foreach (var line in lines)
        {
            foreach (var payload in CandidatePayloads(line))
            {
                var index = IndexOf(payload, marker);
                if (index < 0) continue;

                var data = payload.Skip(index + marker.Length).ToArray();
                if (allowMode09RecordIndex && data.Length > 0 && data[0] is >= 1 and <= 9)
                    data = data[1..];

                var vin = DecodeVin(data);
                if (IsValidVin(vin)) return vin;
            }
        }

        // 4) Final conservative fallback: flatten only two-digit byte tokens and
        // search after the positive response marker. Non-printable/framing bytes
        // are discarded, but a VIN is accepted only if exactly 17 valid VIN chars.
        var flat = lines.SelectMany(ParseSpacedByteTokens).ToArray();
        for (var offset = 0; offset <= flat.Length - marker.Length; offset++)
        {
            if (!Matches(flat, offset, marker)) continue;
            var data = flat.Skip(offset + marker.Length).ToArray();
            if (allowMode09RecordIndex && data.Length > 0 && data[0] is >= 1 and <= 9)
                data = data[1..];

            var vin = DecodeVin(data);
            if (IsValidVin(vin)) return vin;
        }

        return null;
    }

    private static IEnumerable<byte[]> BuildIsoTpStreams(IEnumerable<string> lines)
    {
        var frames = lines
            .SelectMany(line => CandidatePayloads(line))
            .Where(bytes => bytes.Length > 0)
            .ToArray();

        for (var i = 0; i < frames.Length; i++)
        {
            var first = frames[i];
            if (first.Length < 2 || (first[0] >> 4) != 1)
                continue;

            var totalLength = ((first[0] & 0x0F) << 8) | first[1];
            if (totalLength <= 0) continue;

            var data = new List<byte>(totalLength);
            data.AddRange(first.Skip(2));
            var expectedSequence = 1;

            for (var j = i + 1; j < frames.Length && data.Count < totalLength; j++)
            {
                var frame = frames[j];
                if (frame.Length == 0) continue;
                if ((frame[0] >> 4) != 2) continue;
                if ((frame[0] & 0x0F) != (expectedSequence & 0x0F)) continue;

                data.AddRange(frame.Skip(1));
                expectedSequence++;
            }

            if (data.Count >= totalLength)
                yield return data.Take(totalLength).ToArray();
        }
    }

    private static IEnumerable<byte[]> CandidatePayloads(string line)
    {
        var cleaned = line.Trim().Trim('>');
        if (string.IsNullOrWhiteSpace(cleaned)) yield break;

        // ELM formatted lines may start with "0:", "1:", ... .
        var colon = cleaned.IndexOf(':');
        if (colon >= 0 && colon <= 2)
            cleaned = cleaned[(colon + 1)..].Trim();

        var tokens = TokenRegex.Matches(cleaned)
            .Cast<Match>()
            .Select(match => match.Value)
            .ToArray();

        if (tokens.Length > 0)
        {
            var bytes = new List<byte>();
            var startedData = false;
            foreach (var token in tokens)
            {
                // 3/8-hex tokens are CAN identifiers when output is spaced.
                if (!startedData && token.Length is 3 or 8)
                    continue;

                if (token.Length == 2 && byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                {
                    startedData = true;
                    bytes.Add(value);
                }
            }

            if (bytes.Count > 0)
                yield return bytes.ToArray();
        }

        // Also support compact ELM output. Try raw compact bytes and variants
        // with common 11-bit/29-bit CAN identifier prefixes removed.
        var compact = new string(cleaned.Where(Uri.IsHexDigit).ToArray());
        foreach (var candidate in CompactCandidates(compact))
        {
            if (candidate.Length < 2 || (candidate.Length & 1) != 0) continue;
            var bytes = new byte[candidate.Length / 2];
            var ok = true;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(candidate.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                {
                    ok = false;
                    break;
                }

                bytes[i] = value;
            }
            if (ok) yield return bytes;
        }
    }

    private static IEnumerable<string> CompactCandidates(string compact)
    {
        if (string.IsNullOrWhiteSpace(compact)) yield break;
        yield return compact;

        // 11-bit CAN ID is printed as 3 hex digits, making total string length odd.
        if (compact.Length > 3 && (compact.Length - 3) % 2 == 0)
            yield return compact[3..];

        // 29-bit CAN ID is printed as 8 hex digits.
        if (compact.Length > 8 && (compact.Length - 8) % 2 == 0)
            yield return compact[8..];
    }

    private static IEnumerable<byte> ParseSpacedByteTokens(string line)
    {
        foreach (Match match in TokenRegex.Matches(line))
        {
            var token = match.Value;
            if (token.Length != 2) continue;
            if (byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                yield return value;
        }
    }

    private static int IndexOf(byte[] bytes, byte[] marker)
    {
        for (var i = 0; i <= bytes.Length - marker.Length; i++)
            if (Matches(bytes, i, marker)) return i;
        return -1;
    }

    private static bool Matches(byte[] bytes, int offset, byte[] marker)
    {
        for (var i = 0; i < marker.Length; i++)
            if (bytes[offset + i] != marker[i]) return false;
        return true;
    }

    private static string DecodeVin(IEnumerable<byte> bytes)
    {
        var chars = new List<char>(17);
        foreach (var value in bytes)
        {
            if (value is < 0x20 or > 0x7E) continue;
            var c = char.ToUpperInvariant((char)value);
            if (!IsVinCharacter(c)) continue;
            chars.Add(c);
            if (chars.Count == 17) break;
        }
        return new string(chars.ToArray());
    }

    private static bool IsVinCharacter(char c) =>
        (c >= '0' && c <= '9') ||
        (c >= 'A' && c <= 'Z' && c != 'I' && c != 'O' && c != 'Q');

    public static bool IsValidVin(string? vin) =>
        !string.IsNullOrWhiteSpace(vin) &&
        vin.Length == 17 &&
        vin.All(IsVinCharacter);
}
