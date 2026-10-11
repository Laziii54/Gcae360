using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Decoding of positive trouble-code replies (UDS 59 02, KWP 58, GMLAN A9) into code lists. Pure functions: no I/O.
/// A reply shorter than its documented record size yields no record for the cut-off part, never a partial one.
/// The letter code is the module's DTC bytes written in SAE P/C/B/U style; the exact bytes are kept in
/// <see cref="ModuleDtc.RawBytesHex"/> because a manufacturer format does not map onto SAE letters.
/// </summary>
public static class ModuleDtcParsers
{
    public static List<ModuleDtc> ParseUds(byte[] message)
    {
        // 59 02 <availability mask> [DTC high, DTC middle, failure type, status] …
        var codes = new List<ModuleDtc>();
        for (var i = 3; i + 3 < message.Length; i += 4)
        {
            var high = message[i];
            var middle = message[i + 1];
            var failureType = message[i + 2];
            var status = message[i + 3];
            if (high == 0 && middle == 0 && failureType == 0)
                continue;

            // testFailed / pending / confirmed. Codes that only "have not run yet" are skipped.
            if ((status & 0x0D) == 0)
                continue;

            codes.Add(new ModuleDtc
            {
                Code = FormatCode(high, middle),
                RawBytesHex = $"{high:X2}{middle:X2}{failureType:X2}",
                FailureType = failureType,
                StatusByte = status,
                Active = (status & 0x01) != 0,
                Pending = (status & 0x04) != 0,
                Confirmed = (status & 0x08) != 0
            });
        }

        return Distinct(codes);
    }

    public static List<ModuleDtc> ParseKwp(byte[] message)
    {
        // 58 <count> [DTC high, DTC low, status] …
        var codes = new List<ModuleDtc>();
        for (var i = 2; i + 2 < message.Length; i += 3)
        {
            var high = message[i];
            var low = message[i + 1];
            var status = message[i + 2];
            if (high == 0 && low == 0)
                continue;

            codes.Add(new ModuleDtc
            {
                Code = FormatCode(high, low),
                RawBytesHex = $"{high:X2}{low:X2}",
                StatusByte = status,
                // KWP: bit 6 = currently present, bit 5 = stored.
                Active = (status & 0x40) != 0,
                Confirmed = (status & 0x20) != 0 || (status & 0x40) == 0,
                Pending = false
            });
        }

        return Distinct(codes);
    }

    public static List<ModuleDtc> ParseGmlan(IReadOnlyList<byte[]> frames)
    {
        // Each frame: 81 <DTC high> <DTC low> <failure type> <status> …; 00 00 ends the list.
        var codes = new List<ModuleDtc>();
        foreach (var frame in frames)
        {
            if (frame.Length < 5 || frame[0] != 0x81)
                continue;

            var high = frame[1];
            var low = frame[2];
            if (high == 0 && low == 0)
                continue;

            var status = frame[4];
            codes.Add(new ModuleDtc
            {
                Code = FormatCode(high, low),
                RawBytesHex = $"{high:X2}{low:X2}{frame[3]:X2}",
                FailureType = frame[3],
                StatusByte = status,
                Active = (status & 0x01) != 0 || (status & 0x02) != 0,
                Confirmed = (status & 0x08) != 0 || (status & 0x01) == 0,
                Pending = false
            });
        }

        return Distinct(codes);
    }

    public static string FormatCode(byte high, byte low)
    {
        var letter = (high >> 6) switch { 0 => 'P', 1 => 'C', 2 => 'B', _ => 'U' };
        return $"{letter}{(high >> 4) & 0x03}{high & 0x0F:X}{low >> 4:X}{low & 0x0F:X}";
    }

    private static List<ModuleDtc> Distinct(List<ModuleDtc> codes) =>
        codes
            .GroupBy(code => (code.Code, code.FailureType))
            .Select(group => group.First())
            .ToList();
}
