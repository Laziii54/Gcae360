namespace CarScanner.Services.Localization;

/// <summary>
/// Translation packs for the languages that sit on top of the English base text.
/// Each pack is keyed by the ENGLISH string (Georgian source text is converted to English first by
/// <see cref="LocalizationService"/>). The packs themselves live in LanguagePacks.Xx.cs, one file per language;
/// to add a language add a file with a BuildXx() method and register it in <see cref="Builders"/>.
/// </summary>
internal static partial class LanguagePacks
{
    // Builders are methods (not static fields) so initialisation order between the partial files never matters.
    private static readonly Dictionary<string, Func<Dictionary<string, string>>> Builders = new()
    {
        ["tr"] = BuildTurkish,
        ["de"] = BuildGerman,
        ["pl"] = BuildPolish,
        ["es"] = BuildSpanish,
        ["pt"] = BuildPortuguese,
        ["fr"] = BuildFrench,
        ["it"] = BuildItalian,
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Loaded = new();
    private static readonly object Gate = new();

    private static Dictionary<string, string>? GetPack(string code)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(code, out var pack))
                return pack;

            if (!Builders.TryGetValue(code, out var build))
                return null;

            pack = build();
            Loaded[code] = pack;
            return pack;
        }
    }

    /// <summary>Looks the English text up in the language's pack. Returns false (and the English text) when there
    /// is no translation, so the UI falls back to English instead of showing nothing.</summary>
    public static bool TryTranslate(AppLanguage language, string english, out string result)
    {
        result = english;

        var pack = GetPack(language.Code);
        if (pack is null || string.IsNullOrWhiteSpace(english))
            return false;

        var key = english.Trim();
        if (!pack.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return false;

        var leading = english[..(english.Length - english.TrimStart().Length)];
        var trailing = english[english.TrimEnd().Length..];

        result = leading + MatchCase(key, value, language) + trailing;
        return true;
    }

    // The pack lookup is case-insensitive, so "CLEAR DTC" and "Clear DTC" share one entry; keep headings upper-case.
    private static string MatchCase(string source, string translated, AppLanguage language)
    {
        var hasLetters = false;
        foreach (var ch in source)
        {
            if (char.IsLower(ch))
                return translated;

            if (char.IsLetter(ch))
                hasLetters = true;
        }

        var letterCount = source.Count(char.IsLetter);
        return hasLetters && letterCount >= 2
            ? LanguageCatalog.ToUpper(translated, language)
            : translated;
    }
}
