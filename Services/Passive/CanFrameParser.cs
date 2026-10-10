using System.Globalization;

namespace CarScanner.Services.Passive;

/// <summary>Parses the text an ELM327 prints in monitor mode ("ATMA" with headers on).</summary>
public static class CanFrameParser
{
    private static readonly char[] WhiteSpaceAndPrompt = { '\r', '\n', '\t', ' ', '>' };

    /// <summary>
    /// "7E8064100BE3FA813" → (0x7E8, "064100BE3FA813"). The module session prints every frame as one token (ATS0),
    /// and the protocol layer joins the lines with spaces, so frames are separated by any white space. Status
    /// words, noise and tokens of the wrong size are skipped.
    /// </summary>
    public static IEnumerable<(int Id, string Data)> Parse(string raw, bool ext29)
    {
        var width = ext29 ? 8 : 3;
        foreach (var token in (raw ?? string.Empty).Split(WhiteSpaceAndPrompt, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = token.Trim().ToUpperInvariant();
            if (text.Length < width + 2 || text.Length > width + 16 || !text.All(Uri.IsHexDigit))
                continue;
            if (!int.TryParse(text[..width], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                continue;
            yield return (id, text[width..]);
        }
    }

    /// <summary>Same, but only complete frames (whole bytes); a frame cut off mid-byte is treated as corrupt and dropped.</summary>
    public static IEnumerable<(int Id, byte[] Data)> ParseFrames(string raw, bool ext29)
    {
        foreach (var (id, data) in Parse(raw, ext29))
        {
            if (data.Length % 2 != 0)
                continue;
            yield return (id, Convert.FromHexString(data));
        }
    }
}
