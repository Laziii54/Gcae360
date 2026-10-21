using System.Text.RegularExpressions;
using CarScanner.Services;

namespace CarScanner.Tests;

/// <summary>
/// The sentences of the hybrid battery check that say why a Volt / Bolt check ended without a result, in every language the app
/// offers. As in the existing localization tests, the app's LocalizationService cannot run in this project, so these read the
/// source files (where each text comes from, that every key has exactly one pair and one pack row, that the seven added
/// languages are all filled in) and tie the Georgian keys to the production strings of <see cref="HybridReadDiagnosis"/>.
/// Machine-written translations: checked for presence and consistency, not for native-speaker quality.
/// </summary>
public sealed class HybridFailureLocalizationTests
{
    private sealed record Text(HybridFailureKind Kind, string Ka, string En, bool Template);

    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
                dir = dir.Parent;
            return dir!.FullName;
        }
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    private static string AllLocalizationSources() => string.Concat(
        Directory.EnumerateFiles(Path.Combine(Root, "Services", "Localization"), "*.cs").OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => File.ReadAllText(path).Replace("\r\n", "\n")));

    // The Georgian text is taken from the production code; the number or the command is replaced by the {0} the template table uses.
    private static readonly Text[] Texts =
    {
        new(HybridFailureKind.SetupNotAccepted,
            HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.SetupNotAccepted, "ATS0")).Replace("(ATS0)", "({0})"),
            "This adapter does not accept a setting the battery check needs ({0}). Try a different adapter.", true),
        new(HybridFailureKind.ModuleSilent,
            HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.ModuleSilent)),
            "The battery module did not respond to the requests. There can be several causes — for example the car is not switched on (READY), or the adapter cannot reach this module. Check the car's state and try again.", false),
        new(HybridFailureKind.OtherModuleAnswered,
            HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.OtherModuleAnswered)),
            "The adapter returned a reply from another module, not from the battery module. It may not support addressing a specific module.", false),
        new(HybridFailureKind.UnreadableReply,
            HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.UnreadableReply)),
            "A reply came back from the battery module, but it could not be read (damaged or unexpected format). Try again; if it keeps happening, send the connection log from the Adapter page.", false),
        new(HybridFailureKind.TooFewCells,
            HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.TooFewCells, Count: 70)).Replace("70-მა", "{0}-მა"),
            "Only {0} of the battery's 96 cells returned a valid value — not enough for a reliable result. Try again; if the same cells keep missing, send the connection log from the Adapter page.", true),
        new(HybridFailureKind.TransportLost,
            HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.TransportLost)),
            "The connection to the adapter was lost during the check. Reconnect the adapter and try again.", false),
    };

    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex Row = new(@"R\(((?:\s*""(?:[^""\\]|\\.)*""\s*,?)+)\)", RegexOptions.Compiled);

    private static IReadOnlyList<(string En, string[] Translations)> Batch38() =>
        Row.Matches(Read("Services", "Localization", "LanguagePacks.Extra38.cs")).Cast<Match>()
            .Select(match => Literal.Matches(match.Groups[1].Value).Cast<Match>().Select(item => item.Groups[1].Value).ToArray())
            .Select(all => (all[0], all.Skip(1).ToArray()))
            .ToList();

    private static int Occurrences(string haystack, string needle) => Regex.Matches(haystack, Regex.Escape(needle)).Count;

    private static int Placeholders(string text) => Regex.Matches(text, @"\{\d+\}").Count;

    // ------------------------------------------------------------------ Georgian <-> English

    [Fact]
    public void EveryFinding_ExceptNone_HasAGeorgianTextInTheProductionCode_AndSixTextsAreCovered()
    {
        var withText = Enum.GetValues<HybridFailureKind>().Where(kind => kind != HybridFailureKind.None).ToArray();

        Assert.Equal(withText, Texts.Select(text => text.Kind));
        Assert.Equal(6, Texts.Length);
        Assert.All(Texts, text => Assert.False(string.IsNullOrWhiteSpace(text.Ka)));
    }

    [Fact]
    public void EveryGeorgianText_AppearsExactlyOnceAsAKeyInTheLocalizationSources()
    {
        var all = AllLocalizationSources();

        foreach (var text in Texts)
            Assert.Equal(1, Occurrences(all, "\"" + text.Ka + "\""));          // no duplicate key anywhere
    }

    [Fact]
    public void TheGeorgianTexts_AreMappedToTheEnglishTexts_InTheExtraDictionaryAndTemplateTable()
    {
        var extra = Read("Services", "Localization", "LocalizationService.Extra.cs");
        var oneLine = Regex.Replace(extra, @"\]\s*=\s*\n\s+""", "] = \"");    // a long pair may continue on the next line

        foreach (var text in Texts.Where(text => !text.Template))
            Assert.Contains($"[\"{text.Ka}\"] = \"{text.En}\"", oneLine);

        var templates = Regex.Replace(extra, @",\s*\n\s+""", ", \"");
        foreach (var text in Texts.Where(text => text.Template))
            Assert.Contains($"(\"{text.Ka}\", \"{text.En}\")", templates);
    }

    [Fact]
    public void ThePlaceholder_IsTheSameInGeorgianAndEnglish_AndTheTemplateTableMatchesTheProductionText()
    {
        foreach (var text in Texts)
        {
            Assert.Equal(text.Template ? 1 : 0, Placeholders(text.En));
            Assert.Equal(Placeholders(text.En), Placeholders(text.Ka));
        }

        // The table turns {0} into "(.+?)" and anchors the pattern: the text the service really produces must match it.
        var setup = Texts.Single(text => text.Kind == HybridFailureKind.SetupNotAccepted);
        var setupPattern = new Regex("^" + Regex.Escape(setup.Ka).Replace("\\{0}", "(.+?)") + "$", RegexOptions.Singleline);
        var setupMatch = setupPattern.Match(HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.SetupNotAccepted, "ATS0")));
        Assert.True(setupMatch.Success);
        Assert.Equal("ATS0", setupMatch.Groups[1].Value);

        var cells = Texts.Single(text => text.Kind == HybridFailureKind.TooFewCells);
        var cellsPattern = new Regex("^" + Regex.Escape(cells.Ka).Replace("\\{0}", "(.+?)") + "$", RegexOptions.Singleline);
        var cellsMatch = cellsPattern.Match(HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.TooFewCells, Count: 79)));
        Assert.True(cellsMatch.Success);
        Assert.Equal("79", cellsMatch.Groups[1].Value);
    }

    [Fact]
    public void TheDiagnosisWritesGeorgianOnly_NoEnglishSentenceIsHardCodedInTheCode()
    {
        var code = Read("Services", "HybridReadDiagnosis.cs");

        foreach (var text in Texts)
            Assert.DoesNotContain(text.En, code);                                // the English lives in the translation tables only
    }

    // ------------------------------------------------------------------ the seven added languages

    [Fact]
    public void TheNewBatch_IsRegistered_OnceAndRightAfterTheBatchBeforeIt()
    {
        var packs = Read("Services", "Localization", "LanguagePacks.cs");

        Assert.Equal(1, Occurrences(packs, "ExtraRows38"));
        Assert.Contains("ExtraRows36, ExtraRows37, ExtraRows38,", packs);
        Assert.Equal(1, Occurrences(Read("Services", "Localization", "LanguagePacks.Extra38.cs"), "ExtraRows38()"));
    }

    [Fact]
    public void EveryEnglishText_HasOneRow_WithAllSevenLanguagesFilled_AndNoneLeftInEnglish()
    {
        var rows = Batch38();
        var all = AllLocalizationSources();

        Assert.Equal(Texts.Length, rows.Count);
        foreach (var text in Texts)
        {
            var row = Assert.Single(rows.Where(item => item.En == text.En));
            Assert.Equal(7, row.Translations.Length);
            Assert.All(row.Translations, translation =>
            {
                Assert.False(string.IsNullOrWhiteSpace(translation));
                Assert.NotEqual(text.En, translation);
                Assert.Equal(Placeholders(text.En), Placeholders(translation));
            });
            Assert.Equal(1, Occurrences(all, "R(\"" + text.En + "\""));          // no second row for the same English text
            Assert.Equal(7, row.Translations.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(0, "adaptör", "READY")]
    [InlineData(1, "Adapter", "READY")]
    [InlineData(2, "adapter", "READY")]
    [InlineData(3, "adaptador", "READY")]
    [InlineData(4, "adaptador", "READY")]
    [InlineData(5, "adaptateur", "READY")]
    [InlineData(6, "adattatore", "READY")]
    public void EachLanguage_KeepsTheAdapterTermAndTheDashboardTermREADY(int column, string adapterWord, string ready)
    {
        var rows = Batch38().ToDictionary(row => row.En, row => row.Translations[column]);

        // The adapter is called the same word in every text that names it; READY is the car's own display term and stays as it is.
        foreach (var text in Texts.Where(text => text.En.Contains("adapter", StringComparison.OrdinalIgnoreCase)))
            Assert.Contains(adapterWord, rows[text.En], StringComparison.OrdinalIgnoreCase);

        var silent = rows[Texts.Single(text => text.Kind == HybridFailureKind.ModuleSilent).En];
        Assert.Contains($"({ready})", silent);
        // Only the silence text mentions READY at all, and only as one example among several causes.
        foreach (var text in Texts.Where(text => text.Kind != HybridFailureKind.ModuleSilent))
            Assert.DoesNotContain(ready, rows[text.En]);
    }

    [Fact]
    public void NoText_IsLongerThanWhatTheStatusLineWrapsComfortably()
    {
        // The status line wraps freely (a label); the longest of these sentences is about 260 characters, six lines at 320 dp.
        var longest = Batch38().SelectMany(row => row.Translations).Concat(Texts.Select(text => text.Ka.Replace("{0}", "79"))).Max(text => text.Length);

        Assert.True(longest <= 300, $"the longest status sentence has {longest} characters");
    }
}
