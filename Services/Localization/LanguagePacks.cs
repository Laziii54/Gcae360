using System.Text.RegularExpressions;

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
            MergeExtras(code, pack);
            Loaded[code] = pack;
            return pack;
        }
    }

    // Extra rows (LanguagePacks.Extra*.cs): one row = the English text + its translation in every added language,
    // always in this order. A row never overrides a line that a language file already has.
    private static readonly string[] ExtraOrder = { "tr", "de", "pl", "es", "pt", "fr", "it" };

    private static readonly Func<IEnumerable<(string En, string[] T)>>[] ExtraProviders =
    {
        ExtraRows1, ExtraRows2, ExtraRows3, ExtraRows4, ExtraRows5, ExtraRows6, ExtraRows7, ExtraRows8,
    };

    private static (string En, string[] T) R(string en, params string[] translations) => (en, translations);

    private static void MergeExtras(string code, Dictionary<string, string> pack)
    {
        var index = Array.IndexOf(ExtraOrder, code);
        if (index < 0)
            return;

        foreach (var provider in ExtraProviders)
            foreach (var (en, translations) in provider())
                if (index < translations.Length && !string.IsNullOrWhiteSpace(translations[index]))
                    pack.TryAdd(en, translations[index]);
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
        {
            // Templates with {0}, {1} … (live values inside a sentence).
            if (!TryTemplate(language.Code, pack, key, out value))
                return false;
        }

        var leading = english[..(english.Length - english.TrimStart().Length)];
        var trailing = english[english.TrimEnd().Length..];

        result = leading + MatchCase(key, value, language) + trailing;
        return true;
    }

    private static readonly Dictionary<string, List<(Regex Pattern, string Target)>> TemplateCache = new();

    private static bool TryTemplate(string code, Dictionary<string, string> pack, string key, out string value)
    {
        value = string.Empty;
        List<(Regex Pattern, string Target)> templates;
        lock (Gate)
        {
            if (!TemplateCache.TryGetValue(code, out templates!))
            {
                templates = new List<(Regex, string)>();
                foreach (var (templateKey, target) in pack)
                {
                    if (templateKey.IndexOf('{') < 0 || string.IsNullOrWhiteSpace(target))
                        continue;

                    var pattern = Regex.Escape(templateKey);
                    for (var i = 0; i < 10; i++)
                        pattern = pattern.Replace("\\{" + i + "}", "(.+?)");
                    templates.Add((new Regex("^" + pattern + "$",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant), target));
                }

                TemplateCache[code] = templates;
            }
        }

        foreach (var (pattern, target) in templates)
        {
            var match = pattern.Match(key);
            if (!match.Success)
                continue;

            var result = target;
            for (var i = 1; i < match.Groups.Count; i++)
                result = result.Replace("{" + (i - 1) + "}", match.Groups[i].Value);
            value = result;
            return true;
        }

        return false;
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
