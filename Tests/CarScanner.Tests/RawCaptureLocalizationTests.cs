using System.Text.RegularExpressions;

namespace CarScanner.Tests;

/// <summary>
/// The Adapter Center's "Raw capture" button and the copy-log message that says the capture was included, in every
/// language the app offers. The app's LocalizationService depends on MAUI and cannot run in this project, so — like the
/// existing localization regression tests — these read the source files: where the text comes from, that every key has
/// exactly one pair and one pack row, that all seven added languages are filled in, and that the page lets it wrap.
/// Machine-written translations: they are checked for presence and consistency, not for native-speaker quality.
/// </summary>
public sealed class RawCaptureLocalizationTests
{
    // The three texts, as (Georgian key, English text, whether it holds a number).
    private static readonly (string Ka, string En, bool Number)[] Texts =
    {
        ("Raw capture: გამორთულია — დასაწყებად დააჭირე (დიაგნოსტიკისთვის)", "Raw capture: OFF — tap to start (for support)", false),
        ("Raw capture: ჩართულია, დარჩა დაახლოებით {0} წთ — გასაჩერებლად დააჭირე", "Raw capture: ON, about {0} min left — tap to stop", true),
        ("კავშირის ლოგი და raw capture დაკოპირდა — გამომიგზავნე.", "Connection log and raw capture copied — send it to me.", false),
    };

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

    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex Row = new(@"R\(((?:\s*""(?:[^""\\]|\\.)*""\s*,?)+)\)", RegexOptions.Compiled);

    private static IReadOnlyList<(string En, string[] Translations)> PackRows(string source) =>
        Row.Matches(source).Cast<Match>()
            .Select(match => Literal.Matches(match.Groups[1].Value).Cast<Match>().Select(item => item.Groups[1].Value).ToArray())
            .Select(all => (all[0], all.Skip(1).ToArray()))
            .ToList();

    private static int Occurrences(string haystack, string needle) =>
        Regex.Matches(haystack, Regex.Escape(needle)).Count;

    private static int Placeholders(string text) => Regex.Matches(text, @"\{\d+\}").Count;

    // ------------------------------------------------------------------ where the text comes from

    [Fact]
    public void TheButtonText_IsBuiltFromTheTranslationKeys_NotFromHardCodedEnglish()
    {
        var code = Read("ViewModels", "AdapterCenterViewModel.cs");
        var method = code[code.IndexOf("private void RefreshCaptureText()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("/// <summary>Copies the adapter link diary", StringComparison.Ordinal)];

        Assert.Contains("_localization.Translate($\"Raw capture: ჩართულია, დარჩა დაახლოებით {(int)Math.Ceiling(remaining.TotalMinutes)} წთ — გასაჩერებლად დააჭირე\")", method);
        Assert.Contains("_localization.Translate(\"Raw capture: გამორთულია — დასაწყებად დააჭირე (დიაგნოსტიკისთვის)\")", method);
        // The old English-only literals must be gone, so the text lives in one place only.
        Assert.DoesNotContain("Raw capture: ON", code);
        Assert.DoesNotContain("Raw capture: OFF", code);
        Assert.DoesNotContain("tap to start", code);
    }

    [Fact]
    public void TheCopiedMessage_WhenTheCaptureIsIncluded_IsTranslated_AndTheOldMessageIsUntouched()
    {
        var code = Read("ViewModels", "AdapterCenterViewModel.cs");

        Assert.Contains("_localization.Translate(\"კავშირის ლოგი და raw capture დაკოპირდა — გამომიგზავნე.\")", code);
        // The message for a log without a capture is exactly the one that existed before.
        Assert.Contains("ObdLang.L(_localization.Translate(\"კავშირის ლოგი დაკოპირდა — გამომიგზავნე.\"), \"Connection log copied — send it to me.\")", code);
    }

    [Fact]
    public void TheViewModel_RefreshesTheCaptureText_OnConstructionAndOnEveryLiveLanguageChange()
    {
        var code = Read("ViewModels", "AdapterCenterViewModel.cs");
        var ctor = code[code.IndexOf("public AdapterCenterViewModel(", StringComparison.Ordinal)..];
        ctor = ctor[..ctor.IndexOf("private void RefreshIdleTexts()", StringComparison.Ordinal)];

        Assert.Contains("RefreshCaptureText();", ctor[..ctor.IndexOf("WeakEventRelay.Attach", StringComparison.Ordinal)]);

        var handler = ctor[ctor.IndexOf("static viewModel =>", StringComparison.Ordinal)..];
        Assert.Contains("viewModel.RefreshCaptureText();", handler);
        // Not behind the "busy" check: the state of the capture does not depend on whether a test is running.
        Assert.True(handler.IndexOf("viewModel.RefreshCaptureText();", StringComparison.Ordinal) < handler.IndexOf("IsBusy", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePage_BindsTheButtonToTheViewModelText_AndLetsALongTranslationWrap()
    {
        var xaml = Read("Views", "AdapterCenterPage.xaml");
        var line = xaml.Split('\n').Single(item => item.Contains("ToggleCaptureCommand", StringComparison.Ordinal));

        Assert.Contains("Text=\"{Binding CaptureButtonText}\"", line);
        Assert.Contains("LineBreakMode=\"WordWrap\"", line);          // a long German or French text goes onto a second line
        Assert.DoesNotContain("HeightRequest", line);                 // ...and the button may grow to fit it
        Assert.DoesNotContain("WidthRequest", line);
        // It sits below the two existing actions and does not replace them.
        Assert.True(xaml.IndexOf("CopyLinkLogCommand", StringComparison.Ordinal) < xaml.IndexOf("ToggleCaptureCommand", StringComparison.Ordinal));
        Assert.Contains("Command=\"{Binding TestCommand}\"", xaml);
    }

    // ------------------------------------------------------------------ Georgian <-> English

    [Fact]
    public void EveryGeorgianKey_AppearsExactlyOnceInTheLocalizationSources()
    {
        var all = AllLocalizationSources();

        foreach (var (ka, _, _) in Texts)
            Assert.Equal(1, Occurrences(all, "\"" + ka + "\""));      // no duplicate key anywhere in the localization sources
    }

    [Fact]
    public void TheGeorgianKeys_AreMappedToTheEnglishTexts_InTheExtraDictionaryAndTemplateTable()
    {
        var extra = Read("Services", "Localization", "LocalizationService.Extra.cs");

        Assert.Contains($"[\"{Texts[0].Ka}\"] = \"{Texts[0].En}\"", extra);
        Assert.Contains($"[\"{Texts[2].Ka}\"] = \"{Texts[2].En}\"", extra);
        Assert.Contains($"(\"{Texts[1].Ka}\", \"{Texts[1].En}\")", extra);       // the one with a number is a template
    }

    [Fact]
    public void TheNumberPlaceholder_IsTheSameInGeorgianAndEnglish()
    {
        foreach (var (ka, en, number) in Texts)
        {
            Assert.Equal(number ? 1 : 0, Placeholders(en));
            Assert.Equal(Placeholders(en), Placeholders(ka));
        }
    }

    // ------------------------------------------------------------------ the seven added languages

    private static IReadOnlyList<(string En, string[] Translations)> Batch37() => PackRows(Read("Services", "Localization", "LanguagePacks.Extra37.cs"));

    [Fact]
    public void TheNewBatch_IsRegistered_OnceAndInOrder()
    {
        var packs = Read("Services", "Localization", "LanguagePacks.cs");

        Assert.Equal(1, Occurrences(packs, "ExtraRows37"));
        Assert.Contains("ExtraRows36, ExtraRows37,", packs);
        Assert.Equal(1, Occurrences(Read("Services", "Localization", "LanguagePacks.Extra37.cs"), "ExtraRows37()"));
    }

    [Fact]
    public void EveryEnglishText_HasOneRow_WithAllSevenLanguagesFilled_AndNoneLeftInEnglish()
    {
        var rows = Batch37();
        var all = AllLocalizationSources();

        Assert.Equal(Texts.Length, rows.Count);
        foreach (var (_, en, _) in Texts)
        {
            var matching = rows.Where(row => row.En == en).ToList();
            var row = Assert.Single(matching);
            Assert.Equal(7, row.Translations.Length);
            Assert.All(row.Translations, translation =>
            {
                Assert.False(string.IsNullOrWhiteSpace(translation));
                Assert.NotEqual(en, translation);                                // a translation, not the English text again
            });
            Assert.Equal(1, Occurrences(all, "R(\"" + en + "\""));               // no second row for the same English text
            Assert.Equal(7, row.Translations.Distinct().Count());                // seven different texts, not one pasted seven times
            Assert.All(row.Translations, translation => Assert.Equal(Placeholders(en), Placeholders(translation)));
        }
    }

    [Fact]
    public void TheOrderOfTheSevenColumns_IsTheOrderTheAppReadsThem_AndEveryCatalogLanguageIsAccountedFor()
    {
        var order = Regex.Match(Read("Services", "Localization", "LanguagePacks.cs"), @"ExtraOrder = \{([^}]*)\}").Groups[1].Value;
        var added = Literal.Matches(order).Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "tr", "de", "pl", "es", "pt", "fr", "it" }, added);

        var catalog = Read("Services", "Localization", "LanguageCatalog.cs");
        var codes = Regex.Matches(catalog, @"new AppLanguage\(""([a-z]{2})""").Cast<Match>().Select(match => match.Groups[1].Value).ToArray();

        // Georgian is the source text of the pairs, English is its other half, the other seven come from the pack columns.
        Assert.Equal(new[] { "ka", "en" }.Concat(added), codes);
        Assert.Equal(9, codes.Length);
    }

    [Theory]
    [InlineData(0, "Ham kayıt", "KAPALI", "AÇIK", "dk", "Bağlantı günlüğü")]
    [InlineData(1, "Rohdaten-Aufzeichnung", "AUS", "AN", "Min.", "Verbindungsprotokoll")]
    [InlineData(2, "Surowy zapis", "WYŁ.", "WŁ.", "min", "Dziennik połączenia")]
    [InlineData(3, "Captura sin procesar", "DESACTIVADA", "ACTIVADA", "min", "Registro de conexión")]
    [InlineData(4, "Captura bruta", "DESLIGADA", "LIGADA", "min", "Registo de ligação")]
    [InlineData(5, "Capture brute", "DÉSACTIVÉE", "ACTIVÉE", "min", "Journal de connexion")]
    [InlineData(6, "Acquisizione grezza", "DISATTIVA", "ATTIVA", "min", "Registro di connessione")]
    public void EachLanguage_NamesTheFeatureTheSameWayInAllThreeTexts_AndShowsTheStateAndTheUnit(int column, string feature, string off, string on, string minutes, string connectionLog)
    {
        var rows = Batch37().ToDictionary(row => row.En, row => row.Translations[column]);

        var offText = rows[Texts[0].En];
        var onText = rows[Texts[1].En];
        var copied = rows[Texts[2].En];

        // The feature has one name in all three texts, and the two states show the state and the unit.
        Assert.StartsWith(feature, offText);
        Assert.StartsWith(feature, onText);
        Assert.Contains(off, offText);
        Assert.Contains(on + ",", onText);
        Assert.DoesNotContain(on + ",", offText);                          // the "off" text never reads as "on"
        Assert.Contains(minutes, onText);
        Assert.Contains(feature.ToLowerInvariant(), copied.ToLowerInvariant());

        // The log is called what the existing "Connection log" rows already call it in that language.
        Assert.StartsWith(connectionLog, copied);
        var existing = File.ReadAllText(Path.Combine(Root, "Services", "Localization", "LanguagePacks.Extra33.cs"));
        Assert.Contains(connectionLog.ToLowerInvariant(), existing.ToLowerInvariant());
    }

    // ------------------------------------------------------------------ the layout

    [Fact]
    public void NoText_IsLongerThanWhatWrapsToThreeLines_OnTheNarrowestPhone()
    {
        // 12 sp text on a 320 dp wide screen, minus the page's 16 dp margins and the button's 12 dp padding, fits about 38
        // characters a line (a generous 0.55 em average); three lines are 100 characters. The button grows in height, so a
        // text that stays below this never clips and never pushes the actions above it.
        // Only the two button texts matter here (the copied message is shown in the status line, which wraps freely).
        var buttonEnglish = new[] { Texts[0].En, Texts[1].En };
        var longest = Batch37().Where(row => buttonEnglish.Contains(row.En)).SelectMany(row => row.Translations)
            .Concat(buttonEnglish)
            .Concat(new[] { Texts[0].Ka, Texts[1].Ka.Replace("{0}", "30") })
            .Max(text => text.Length);

        Assert.True(longest <= 100, $"the longest button text has {longest} characters");
    }
}
