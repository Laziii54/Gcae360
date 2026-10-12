using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CarScanner.Tests;

/// <summary>
/// The detector behind "NO EMOJI-STYLE ICONS IN THE OBD360 USER INTERFACE" (see DEVELOPMENT_GUIDE.md).
/// It looks at characters, not at meaning, so it is deliberately narrow: it flags emoji and the Unicode symbol blocks that
/// are used as decorative icons (dingbats, miscellaneous symbols, geometric shapes, technical symbols, regional-indicator
/// flags, variation selectors) and a few punctuation marks that are used as chevron / close icons when they stand alone.
/// It never flags letters of any script, digits, currency signs, math operators, plain arrows, box-drawing characters or
/// ordinary punctuation, so localized text and technical data pass.
/// </summary>
internal static class IconPolicy
{
    /// <summary>Characters that sit in a flagged block but are legitimate text and stay allowed.</summary>
    public static readonly IReadOnlySet<int> Allowed = new HashSet<int>
    {
        0x00A9, // © copyright
        0x00AE, // ® registered
        0x2122, // ™ trade mark
        0x2194, // ↔ both ways (distance ranges, "A ↔ B")
    };

    /// <summary>Punctuation that is fine inside a sentence but is an icon substitute when it is the whole text of a label or button.</summary>
    public static readonly string AloneIsAnIcon = "‹›×«»✕✓";

    public static bool IsBannedRune(int cp)
    {
        if (Allowed.Contains(cp))
            return false;
        if (cp == 0xFE0F || cp == 0x200D || cp == 0x20E3)                      // emoji presentation selector, joiner, keycap
            return true;
        if (cp >= 0x1F1E6 && cp <= 0x1F1FF)                                    // regional indicators (flags)
            return true;
        if (cp >= 0x1F000)                                                     // emoji and pictograph planes
            return true;
        if (cp is >= 0x2300 and <= 0x23FF)                                     // miscellaneous technical (hourglass, timers, media keys)
            return true;
        if (cp is >= 0x25A0 and <= 0x25FF)                                     // geometric shapes (bullets, squares, triangles)
            return true;
        if (cp is >= 0x2600 and <= 0x27BF)                                     // miscellaneous symbols + dingbats (check, cross, star, warning)
            return true;
        if (cp is >= 0x2900 and <= 0x297F)                                     // supplemental arrows-B
            return true;
        if (cp is >= 0x2B00 and <= 0x2BFF)                                     // miscellaneous symbols and arrows (star, large arrows)
            return true;
        if (cp is >= 0x21B0 and <= 0x21BB)                                     // curved / refresh arrows used as buttons
            return true;
        if (cp is >= 0x2460 and <= 0x24FF)                                     // enclosed alphanumerics (circled i)
            return true;
        return false;
    }

    /// <summary>All flagged characters in <paramref name="text"/> (also decodes numeric HTML entities such as &amp;#9889;).</summary>
    public static IReadOnlyList<string> FindBanned(string text)
    {
        var found = new List<string>();
        foreach (var rune in text.EnumerateRunes())
            if (IsBannedRune(rune.Value))
                found.Add(Describe(rune.Value));

        foreach (Match entity in NumericEntity.Matches(text))
        {
            var value = entity.Groups[1].Success
                ? int.Parse(entity.Groups[1].Value, CultureInfo.InvariantCulture)
                : int.Parse(entity.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (value is > 0 and <= 0x10FFFF && !(value is >= 0xD800 and <= 0xDFFF) && IsBannedRune(value))
                found.Add(Describe(value) + " (as an HTML entity)");
        }

        return found;
    }

    /// <summary>True when the whole text is a single punctuation mark that is used as an icon (for example a lone chevron).</summary>
    public static bool IsLoneIconPunctuation(string literal)
    {
        var trimmed = literal.Trim();
        return trimmed.Length == 1 && AloneIsAnIcon.Contains(trimmed[0]);
    }

    private static string Describe(int cp) => $"U+{cp:X4} ({char.ConvertFromUtf32(cp)})";

    private static readonly Regex NumericEntity = new(@"&#(?:(\d{3,7})|[xX]([0-9a-fA-F]{3,6}));", RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new("\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"", RegexOptions.Compiled);

    /// <summary>String literals of one C# line (regular, interpolated and the quoted parts of verbatim strings).</summary>
    public static IEnumerable<string> LiteralsOf(string codeLine)
    {
        var trimmed = codeLine.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal))
            yield break;                                                        // comments are not user-facing

        foreach (Match match in StringLiteral.Matches(codeLine))
            yield return match.Value;
    }
}
