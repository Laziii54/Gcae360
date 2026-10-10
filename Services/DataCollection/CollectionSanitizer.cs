using System.Globalization;
using System.Text;
using CarScanner.Services.OBD;

namespace CarScanner.Services.DataCollection;

/// <summary>One anonymised request/answer pair as it leaves the phone.</summary>
public sealed record CollectedExchange(
    string Request,
    string Response,
    int TxCanId,
    int RxCanId,
    bool Extended29Bit)
{
    public string Key => $"{TxCanId:X}|{RxCanId:X}|{(Extended29Bit ? 1 : 0)}|{Request}|{Response}";
}

/// <summary>
/// Decides what may be sent to the server. The rule is "when in doubt, drop":
/// only read-only requests are kept, anything that identifies a car or a person
/// (VIN, serial numbers, VIN-looking text inside an answer) is removed entirely and
/// lines that are not valid hex or a known adapter status word are discarded as corrupt.
/// </summary>
public static class CollectionSanitizer
{
    public const int MaxLines = 24;
    public const int MaxLineLength = 48;

    // Read-only OBD / UDS services only.
    private static readonly HashSet<int> ReadServices = new() { 0x01, 0x02, 0x03, 0x07, 0x09, 0x0A, 0x19, 0x21, 0x22 };

    // Identification DIDs that describe software/hardware, not the car or its owner.
    // Every other F1xx identifier (VIN F190, serial numbers F18C, dates, tester data…) is dropped.
    private static readonly HashSet<int> SafeIdentificationDids = new()
    {
        0xF187, 0xF188, 0xF189, 0xF191, 0xF192, 0xF193, 0xF194, 0xF195
    };

    private static readonly char[] WhiteSpaceAndPrompt = { '\r', '\n', '\t', ' ', '>' };

    private static readonly string[] StatusWords =
    {
        "NO DATA", "CAN ERROR", "BUS ERROR", "UNABLE TO CONNECT", "BUFFER FULL", "STOPPED", "BUS INIT: ERROR"
    };

    public static CollectedExchange? Sanitize(ObdRawExchange exchange)
    {
        // Standard OBD2 answers are never forwarded raw: they go through StandardObdClassifier (compatibility summary).
        if (exchange is null || exchange.Origin != ObdExchangeOrigin.ModuleSession)
            return null;

        var request = NormalizeHex(exchange.RequestHex);
        if (request.Length < 2 || request.Length > 24 || request.Length % 2 != 0)
            return null;

        if (!IsAllowedRequest(request))
            return null;

        // The protocol layer joins the frames of an answer with spaces (one token per frame, ATS0).
        var lines = new List<string>();
        var text = (exchange.ResponseText ?? string.Empty).ToUpperInvariant();
        foreach (var word in StatusWords.OrderByDescending(w => w.Length))
        {
            if (text.Contains(word, StringComparison.Ordinal))
            {
                lines.Add(word);
                text = text.Replace(word, " ", StringComparison.Ordinal);
            }
        }

        var width = exchange.Extended29Bit ? 8 : 3;
        foreach (var token in text.Split(WhiteSpaceAndPrompt, StringSplitOptions.RemoveEmptyEntries))
        {
            // A frame is: CAN id (3 or 8 hex digits) + whole data bytes. Anything else is corruption.
            if (token.Length >= width + 2 && token.Length <= MaxLineLength && (token.Length - width) % 2 == 0 && token.All(Uri.IsHexDigit))
                lines.Add(token);
        }

        if (lines.Count == 0 || lines.Count > MaxLines)
            return null;

        var hexLines = lines.Where(l => l.All(Uri.IsHexDigit)).ToArray();
        if (hexLines.Any(ContainsVin) ||
            ContainsVin(string.Concat(hexLines)) ||
            ContainsVin(Reassemble(hexLines, exchange.Extended29Bit)))
            return null;

        return new CollectedExchange(
            request,
            string.Join("\n", lines),
            exchange.TxCanId,
            exchange.RxCanId,
            exchange.Extended29Bit);
    }

    public static bool IsAllowedRequest(string requestHex)
    {
        var request = NormalizeHex(requestHex);
        if (request.Length < 2 || !request.All(Uri.IsHexDigit))
            return false;

        var service = int.Parse(request[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (!ReadServices.Contains(service))
            return false;

        // Mode 09 PID 02 returns the VIN.
        if (service == 0x09 && request.Length >= 4 && request[2..4] == "02")
            return false;

        if (service == 0x22)
        {
            if (request.Length < 6)
                return false;
            var did = int.Parse(request[2..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (did is >= 0xF100 and <= 0xF1FF && !SafeIdentificationDids.Contains(did))
                return false;
        }

        return true;
    }

    /// <summary>
    /// True when plain text (a make, model, year or protocol field) contains a VIN-shaped run: 17 characters from
    /// the VIN alphabet with at least one letter and one digit. Ordinary long names made of letters are not matched.
    /// </summary>
    public static bool LooksLikeVin(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var run = 0;
        var letters = 0;
        var digits = 0;
        foreach (var raw in text)
        {
            var c = char.ToUpperInvariant(raw);
            if (c is >= 'A' and <= 'Z' && c is not 'I' and not 'O' and not 'Q')
            {
                run++; letters++;
            }
            else if (c is >= '0' and <= '9')
            {
                run++; digits++;
            }
            else
            {
                run = 0; letters = 0; digits = 0;
            }

            if (run >= 17 && letters >= 1 && digits >= 1)
                return true;
        }

        return false;
    }

    /// <summary>True when the hex text decodes to 17 VIN characters somewhere inside it.</summary>
    public static bool ContainsVin(string hex)
    {
        var clean = NormalizeHex(hex);
        if (clean.Length < 34)
            return false;

        for (var offset = 0; offset < 2; offset++)
        {
            var text = new StringBuilder();
            for (var i = offset; i + 1 < clean.Length; i += 2)
            {
                var b = int.Parse(clean.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                text.Append(b is >= 0x20 and < 0x7F ? (char)b : '\0');
            }

            if (HasVinRun(text.ToString()))
                return true;
        }

        return false;
    }

    /// <summary>Drops the CAN header and ISO-TP PCI bytes of every frame and joins the data (a VIN spans several frames).</summary>
    private static string Reassemble(IEnumerable<string> hexLines, bool ext29)
    {
        var width = ext29 ? 8 : 3;
        var joined = new StringBuilder();
        foreach (var line in hexLines)
        {
            if (line.Length <= width + 2)
                continue;

            var data = line[width..];
            var pci = int.Parse(data.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var skip = (pci >> 4) switch
            {
                0 => 2,
                1 => 4,
                2 => 2,
                _ => 0
            };
            if (data.Length > skip)
                joined.Append(data, skip, data.Length - skip);
        }

        return joined.ToString();
    }

    private static bool HasVinRun(string text)
    {
        var run = 0;
        var letters = 0;
        var digits = 0;
        foreach (var c in text)
        {
            if (char.IsAsciiLetterUpper(c) && c is not 'I' and not 'O' and not 'Q')
            {
                run++; letters++;
            }
            else if (char.IsAsciiDigit(c))
            {
                run++; digits++;
            }
            else
            {
                run = 0; letters = 0; digits = 0;
            }

            if (run >= 17 && letters >= 1 && digits >= 1)
                return true;
        }

        return false;
    }


    private static readonly System.Text.RegularExpressions.Regex DtcPattern = new("^[PCBU][0-3][0-9A-F]{3}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex SummaryPattern = new(
        "^(valid(;bitmap=[0-9A-F]{8}(,[0-9A-F]{8}){0,3}|;dtc=none|;dtc=[PCBU][0-3][0-9A-F]{3}(,[PCBU][0-3][0-9A-F]{3}){0,31})?" +
        "|unsupported;nrc=(11|12)" +
        "|inconclusive;(no-response|timeout|no-data|adapter-error|malformed|negative-[0-9A-F]{2}))$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsDtcCode(string code) => DtcPattern.IsMatch(code);

    /// <summary>
    /// Server-side check of a standard-OBD2 compatibility record (request is a Mode 01 PID or 03/07/0A and the
    /// answer is one of the fixed summary forms). Nothing free-form is accepted.
    /// </summary>
    public static CollectedExchange? ValidateStandardSummary(int tx, int rx, bool ext29, string? request, string? response)
    {
        if (tx != StandardTx || rx != 0 || ext29 || request is null || response is null)
            return null;

        var req = request.Trim().ToUpperInvariant();
        if (!ObdExchangeTap.IsObservableStandardRequest(req) || !SummaryPattern.IsMatch(response))
            return null;

        // A "valid" summary must have the form that belongs to the request.
        if (response.StartsWith("valid", StringComparison.Ordinal))
        {
            var isDtcMode = req is "03" or "07" or "0A";
            var isBlock = !isDtcMode && Convert.ToInt32(req[2..4], 16) % 0x20 == 0;
            var expected = isDtcMode ? "valid;dtc=" : isBlock ? "valid;bitmap=" : "valid";
            var matches = isDtcMode || isBlock
                ? response.StartsWith(expected, StringComparison.Ordinal)
                : response == "valid";
            if (!matches)
                return null;
        }

        return new CollectedExchange(req, response, StandardTx, 0, false);
    }

    /// <summary>Functional request address used for standard OBD2 records.</summary>
    public const int StandardTx = 0x7DF;

    private static string NormalizeHex(string value) =>
        new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
