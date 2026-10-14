using System.Globalization;

namespace CarScanner.Services.Localization;

/// <summary>One language the app can be switched to.</summary>
/// <param name="Code">Short code, e.g. "de".</param>
/// <param name="DisplayName">Native name. This exact text is what gets stored in Preferences["language"], which keeps
/// existing installs working ("ქართული" and "English" are unchanged).</param>
/// <param name="EnglishName">Used for the AI prompt ("Respond in German").</param>
/// <param name="Badge">Short upper-case code shown on the language button and in the picker (no flag emoji).</param>
/// <param name="Culture">Culture used only for upper-casing translated headings.</param>
public sealed record AppLanguage(
    string Code,
    string DisplayName,
    string EnglishName,
    string Badge,
    string Culture);

public static class LanguageCatalog
{
    public const string PreferenceKey = "language";
    public const string GeorgianName = "ქართული";

    public static readonly IReadOnlyList<AppLanguage> All = new[]
    {
        new AppLanguage("ka", GeorgianName, "Georgian", "KA", "ka-GE"),
        new AppLanguage("en", "English",    "English",  "EN", "en-US"),
        new AppLanguage("tr", "Türkçe",     "Turkish",  "TR", "tr-TR"),
        new AppLanguage("de", "Deutsch",    "German",   "DE", "de-DE"),
        new AppLanguage("pl", "Polski",     "Polish",   "PL", "pl-PL"),
        new AppLanguage("es", "Español",    "Spanish",  "ES", "es-ES"),
        new AppLanguage("pt", "Português",  "Portuguese", "PT", "pt-BR"),
        new AppLanguage("fr", "Français",   "French",   "FR", "fr-FR"),
        new AppLanguage("it", "Italiano",   "Italian",  "IT", "it-IT"),
    };

    public static AppLanguage Georgian => All[0];
    public static AppLanguage English => All[1];

    public static string[] DisplayNames => All.Select(l => l.DisplayName).ToArray();

    /// <summary>Resolves a stored value (display name, code or English name). Empty → English (the app's
    /// default); an unknown value → Georgian, exactly like the old "not English means Georgian" rule.</summary>
    public static AppLanguage Find(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return English;

        var value = stored.Trim();

        foreach (var language in All)
        {
            if (value.Equals(language.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                value.Equals(language.Code, StringComparison.OrdinalIgnoreCase) ||
                value.Equals(language.EnglishName, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return Georgian;
    }

    public static AppLanguage Current =>
        Find(Preferences.Default.Get(PreferenceKey, English.DisplayName));

    /// <summary>
    /// True for every language except Georgian. All pre-existing "IsEnglish ? english : georgian" decisions in the
    /// app (DTC descriptions, settings texts, ...) therefore show English for the new languages, and the
    /// translation packs are layered on top of that English text where a translation exists.
    /// </summary>
    public static bool UsesEnglishBase => Current.Code != "ka";

    /// <summary>Upper-cases a translated heading correctly for the language (Turkish dotted/dotless i).</summary>
    public static string ToUpper(string text, AppLanguage language)
    {
        if (language.Code == "tr")
        {
            return text
                .Replace('i', 'İ')
                .Replace('ı', 'I')
                .ToUpperInvariant();
        }

        try
        {
            return text.ToUpper(CultureInfo.GetCultureInfo(language.Culture));
        }
        catch (CultureNotFoundException)
        {
            return text.ToUpperInvariant();
        }
    }
}
