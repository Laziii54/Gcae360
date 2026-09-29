using System.Text.Json;
using System.Text.RegularExpressions;
using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// Cleans the Georgian texts of the DTC catalog. Many catalog texts were
/// machine-translated only partly ("Worn out აალების სანთელიs …"). English
/// words left in them are replaced from an offline glossary
/// (Resources/Raw/dtc_ka_glossary.json). A text that still contains English
/// words afterwards is dropped, so the app shows the clean English text
/// instead of a mixed one.
/// </summary>
public static class DtcGeorgianTextRepair
{
    private static readonly Regex LatinWord = new("[A-Za-z][A-Za-z']*", RegexOptions.Compiled);
    private static readonly Regex GeorgianPlural = new("([\u10D0-\u10FF]+?)ი?s\\b", RegexOptions.Compiled);
    private static readonly Regex GeorgianSuffix = new("([\u10D0-\u10FF])(ing|ed|er|es)\\b", RegexOptions.Compiled);
    private static readonly Regex LatinThenGeorgian = new("([A-Za-z])(?=[\u10D0-\u10FF])", RegexOptions.Compiled);
    private static readonly Regex DetachedSuffix = new("\\s+-(?=[\u10D0-\u10FF])", RegexOptions.Compiled);
    private static readonly Regex Spaces = new("\\s{2,}", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunctuation = new("\\s+([,.;:])", RegexOptions.Compiled);
    private static readonly Regex Acronym = new("^[A-Z]+[a-z]?[A-Z]+[a-z]?$", RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Georgian)[] Phrases =
    {
        (new Regex("\\bworn out\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "გაცვეთილი"),
        (new Regex("\\bstuck on\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "გაჭედილია ჩართულად"),
        (new Regex("\\bstuck off\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "გაჭედილია გამორთულად"),
        (new Regex("\\brich to lean\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "მდიდრიდან ღარიბზე"),
        (new Regex("\\blean to rich\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ღარიბიდან მდიდარზე"),
        (new Regex("\\bnot responding\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "არ პასუხობს"),
        (new Regex("\\bup to\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "-მდე"),
        (new Regex("\\bi\\.e\\.", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ანუ")
    };

    private static Dictionary<string, string>? _glossary;
    private static Dictionary<string, CuratedDtcText>? _curated;
    private static readonly SemaphoreSlim LoadGate = new(1, 1);

    public static async Task EnsureLoadedAsync()
    {
        if (_glossary is not null)
            return;

        await LoadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_glossary is not null)
                return;

            try
            {
                await using var curatedStream = await FileSystem.OpenAppPackageFileAsync("dtc_ka_curated.json").ConfigureAwait(false);
                var curated = await JsonSerializer.DeserializeAsync<Dictionary<string, CuratedDtcText>>(curatedStream).ConfigureAwait(false);
                _curated = new Dictionary<string, CuratedDtcText>(curated ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Curated DTC texts not loaded: {ex.Message}");
                _curated = new Dictionary<string, CuratedDtcText>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync("dtc_ka_glossary.json").ConfigureAwait(false);
                var items = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream).ConfigureAwait(false);
                _glossary = new Dictionary<string, string>(items ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DTC glossary not loaded: {ex.Message}");
                _glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            LoadGate.Release();
        }
    }

    /// <summary>Cleans all Georgian fields of a catalog entry.</summary>
    public static DtcCatalogEntry Apply(DtcCatalogEntry entry)
    {
        entry.TitleKa = Repair(entry.TitleKa) ?? string.Empty;
        entry.DescriptionKa = Repair(entry.DescriptionKa) ?? string.Empty;
        entry.CausesKa = RepairList(entry.CausesKa);
        entry.SymptomsKa = RepairList(entry.SymptomsKa);

        // Hand-written Georgian text for the most common generic (SAE) codes.
        // Manufacturer-specific definitions of a code are left as they are.
        if (IsGenericCode(entry.Code, entry.Scope) && TryGetCurated(entry.Code, out var curated))
        {
            entry.TitleKa = curated.Title;
            entry.DescriptionKa = curated.Description;
            if (curated.Causes.Count > 0)
                entry.CausesKa = curated.Causes;
            entry.ChecksKa = curated.Checks;
            entry.SeverityLevel = curated.Severity;
            entry.IsCurated = true;
        }

        return entry;
    }

    /// <summary>Hand-written Georgian text for a generic code, when available.</summary>
    public static bool TryGetCurated(string? code, out CuratedDtcText text)
    {
        text = null!;
        var map = _curated;
        if (map is null || string.IsNullOrWhiteSpace(code))
            return false;
        return map.TryGetValue(code.Trim(), out text!) && text is not null && !string.IsNullOrWhiteSpace(text.Title);
    }

    /// <summary>
    /// SAE-defined code ranges: P0xxx, P2xxx, P34xx–P39xx, U0xxx, U3xxx, C0xxx, B0xxx.
    /// </summary>
    public static bool IsGenericCode(string? code, string? scope = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length < 5)
            return false;

        if (string.Equals(scope?.Trim(), "generic", StringComparison.OrdinalIgnoreCase))
            return true;

        var c = char.ToUpperInvariant(code[0]);
        var d = code[1];
        return c switch
        {
            'P' => d == '0' || d == '2' || (d == '3' && code[2] >= '4'),
            'U' => d == '0' || d == '3',
            'C' or 'B' => d == '0',
            _ => false
        };
    }

    private static IReadOnlyList<string> RepairList(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return items;

        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            var repaired = Repair(item);
            if (!string.IsNullOrWhiteSpace(repaired))
                result.Add(repaired);
        }

        return result;
    }

    /// <summary>Returns clean Georgian text, or null when it cannot be made clean.</summary>
    public static string? Repair(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var glossary = _glossary;
        var value = text;
        foreach (var (pattern, georgian) in Phrases)
            value = pattern.Replace(value, georgian);
        value = GeorgianPlural.Replace(value, match => match.Groups[1].Value + "ები");
        value = GeorgianSuffix.Replace(value, "$1");
        value = LatinThenGeorgian.Replace(value, "$1 ");

        value = LatinWord.Replace(value, match =>
        {
            var word = match.Value;
            if (IsAcronym(word) || glossary is null)
                return word;
            return glossary.TryGetValue(word, out var georgian) ? georgian : word;
        });

        value = DetachedSuffix.Replace(value, "-");
        value = Spaces.Replace(value, " ").Trim();
        value = SpaceBeforePunctuation.Replace(value, "$1");

        foreach (Match match in LatinWord.Matches(value))
        {
            if (!IsAcronym(match.Value))
                return null;
        }

        return value;
    }

    private static bool IsAcronym(string word) =>
        word.Length == 1 ||
        word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)) ||
        word.Any(char.IsDigit) ||
        Acronym.IsMatch(word);
}

/// <summary>One hand-written Georgian DTC text (Resources/Raw/dtc_ka_curated.json).</summary>
public sealed class CuratedDtcText
{
    [System.Text.Json.Serialization.JsonPropertyName("t")] public string Title { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("d")] public string Description { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("c")] public List<string> Causes { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("k")] public List<string> Checks { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("s")] public int Severity { get; set; }
}
