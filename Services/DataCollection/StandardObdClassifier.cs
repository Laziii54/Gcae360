using System.Globalization;
using CarScanner.Services.OBD;

namespace CarScanner.Services.DataCollection;

/// <summary>
/// Turns a standard OBD2 exchange into a compatibility record. The record says WHAT answered, never the
/// measured value:
///
///  • Mode 01, supported-PID blocks (00/20/40/60/80/A0/C0)  → "valid;bitmap=…" (the support bitmap itself)
///  • Mode 01, any other PID                                 → "valid" (the PID answered; the value is not sent)
///  • Mode 03 / 07 / 0A                                      → "valid;dtc=P0133,P0300" or "valid;dtc=none"
///  • explicit "not supported" negative reply (NRC 11 / 12)  → "unsupported;nrc=12"
///  • everything else (NO DATA, timeout, adapter error, malformed text, other negative replies)
///                                                           → "inconclusive;&lt;reason&gt;"
///
/// A missing, late or damaged answer is never reported as "unsupported".
/// </summary>
public static class StandardObdClassifier
{
    public const int MaxDtcCodes = 32;
    private const int MaxBitmaps = 4;

    public static CollectedExchange? Summarize(ObdRawExchange exchange)
    {
        if (exchange is null || exchange.Origin != ObdExchangeOrigin.StandardObd)
            return null;
        if (!ObdExchangeTap.IsObservableStandardRequest(exchange.RequestHex))
            return null;

        var request = exchange.RequestHex.Replace(" ", string.Empty).ToUpperInvariant();
        return new CollectedExchange(request, Classify(request, exchange.ResponseText ?? string.Empty, exchange.IsCan), 0x7DF, 0, false);
    }

    public static string Classify(string request, string responseText, bool? isCan)
    {
        var text = responseText.Trim().ToUpperInvariant();

        if (text.Length == 0)
            return "inconclusive;no-response";
        if (text == "TIMEOUT")
            return "inconclusive;timeout";

        var mode = Convert.ToInt32(request[..2], 16);
        var bytes = Bytes(text);

        var positive = mode + 0x40;
        if (mode == 0x01)
        {
            var pid = Convert.ToInt32(request[2..4], 16);
            if (pid % 0x20 == 0)
            {
                var maps = new List<string>();
                for (var i = 0; i + 5 < bytes.Count; i++)
                {
                    if (bytes[i] != 0x41 || bytes[i + 1] != pid)
                        continue;
                    var map = string.Concat(bytes.Skip(i + 2).Take(4).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
                    if (!maps.Contains(map))
                        maps.Add(map);
                }

                if (maps.Count > 0)
                    return "valid;bitmap=" + string.Join(",", maps.Take(MaxBitmaps));
            }
            else
            {
                for (var i = 0; i + 2 < bytes.Count; i++)
                {
                    if (bytes[i] == 0x41 && bytes[i + 1] == pid)
                        return "valid";
                }
            }
        }
        else if (bytes.Contains(positive))
        {
            try
            {
                // A damaged reply must never read as "no codes": check the layout first.
                var at = bytes.IndexOf(positive);
                var rest = bytes.Count - at - 1;
                if (rest < 1 || (isCan == true && rest < 1 + 2 * bytes[at + 1]) || (isCan == false && rest < 2))
                    return "inconclusive;malformed";

                var decoder = new DTCDecoder();
                var codes = mode switch
                {
                    0x03 => decoder.DecodeStored(text, isCan),
                    0x07 => decoder.DecodePending(text, isCan),
                    _ => decoder.DecodePermanent(text, isCan)
                };

                var list = codes
                    .Select(c => c.Code)
                    .Where(c => !string.IsNullOrEmpty(c) && CollectionSanitizer.IsDtcCode(c))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .Take(MaxDtcCodes)
                    .ToArray();

                return list.Length == 0 ? "valid;dtc=none" : "valid;dtc=" + string.Join(",", list);
            }
            catch (Exception)
            {
                return "inconclusive;malformed";
            }
        }

        // Only after the positive reply was looked for: data bytes may happen to contain 7F.
        // An explicit negative reply for exactly this service.
        for (var i = 0; i + 2 < bytes.Count; i++)
        {
            if (bytes[i] != 0x7F || bytes[i + 1] != mode)
                continue;
            return bytes[i + 2] is 0x11 or 0x12
                ? $"unsupported;nrc={bytes[i + 2]:X2}"
                : $"inconclusive;negative-{bytes[i + 2]:X2}";
        }

        return "inconclusive;" + Reason(text);
    }

    private static string Reason(string text)
    {
        if (text.Contains("NO DATA", StringComparison.Ordinal))
            return "no-data";
        if (text.Contains("UNABLE TO CONNECT", StringComparison.Ordinal) || text.Contains("CAN ERROR", StringComparison.Ordinal) ||
            text.Contains("BUS", StringComparison.Ordinal) || text.Contains("STOPPED", StringComparison.Ordinal) ||
            text.Contains("BUFFER FULL", StringComparison.Ordinal) || text.Contains('?'))
            return "adapter-error";
        return "malformed";
    }

    /// <summary>Bytes of the reply: tokens of exactly two hex digits, or longer even-length hex tokens split into bytes.</summary>
    private static List<int> Bytes(string text)
    {
        var bytes = new List<int>();
        foreach (var token in text.Split(new[] { ' ', '\r', '\n', '\t', '>' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length % 2 != 0 || !token.All(Uri.IsHexDigit))
                continue;
            for (var i = 0; i < token.Length; i += 2)
                bytes.Add(int.Parse(token.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return bytes;
    }
}
