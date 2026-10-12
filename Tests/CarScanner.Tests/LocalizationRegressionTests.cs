using System.Text.RegularExpressions;
using CarScanner.Services.Coding;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Language switching. The translation of every Label/Button is a handler-mapper hook; MAUI's Label replaces its Text
/// mapping in a static constructor, which discarded the hook when it was registered first (the screenshots: only
/// Entry placeholders and view-model L(…) texts followed the language). These tests pin the fix and the data the
/// hook depends on. They read the source files, because the localization service itself needs MAUI.
/// </summary>
public sealed class LocalizationRegressionTests
{
    private static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "CarScanner.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir) ?? dir;
            }

            throw new DirectoryNotFoundException("repository root not found");
        }
    }

    private static IEnumerable<string> SourceFiles()
    {
        var skip = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}Backend{Path.DirectorySeparatorChar}" };
        return Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories).Where(path => !skip.Any(part => path.Contains(part, StringComparison.Ordinal)));
    }

    private static readonly Regex Georgian = new("[\u10A0-\u10FF]", RegexOptions.Compiled);
    private const string Lit = "\"((?:[^\"\\\\]|\\\\.)*)\"";
    private static readonly Regex Pair = new(@"\[\s*" + Lit + @"\s*\]\s*=\s*" + Lit, RegexOptions.Compiled);
    private static readonly Regex Tuple = new(@"\(\s*" + Lit + @"\s*,\s*" + Lit + @"\s*\)", RegexOptions.Compiled);
    private static readonly Regex Row = new(@"R\(\s*" + Lit + @"((?:\s*,\s*" + Lit + @")*)\s*\)", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.Compiled);

    private static string Unescape(string text) => text.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");

    private static readonly string[] Languages = { "tr", "de", "pl", "es", "pt", "fr", "it" };

    // ---------------------------------------------------------------- the root cause

    [Fact]
    public void TheLabelAndButtonTypeInitializers_RunBeforeTheTranslationHookIsAppended()
    {
        var code = File.ReadAllText(Path.Combine(Root, "Services", "Localization", "LocalizationService.cs"));
        var method = code[code.IndexOf("public static void RegisterDisplayTranslation()", StringComparison.Ordinal)..];

        var label = method.IndexOf("RunClassConstructor(typeof(Label).TypeHandle)", StringComparison.Ordinal);
        var button = method.IndexOf("RunClassConstructor(typeof(Button).TypeHandle)", StringComparison.Ordinal);
        var firstAppend = method.IndexOf("AppendToMapping", StringComparison.Ordinal);

        Assert.True(label > 0 && button > 0, "the type initializers must be forced");
        Assert.True(label < firstAppend && button < firstAppend, "…before the hook is appended, or MAUI replaces the mapping again");
    }

    [Fact]
    public void TheHook_IsRegisteredOnceAndOnEveryPlatformThatNeedsIt()
    {
        var code = File.ReadAllText(Path.Combine(Root, "Services", "Localization", "LocalizationService.cs"));
        var method = code[code.IndexOf("public static void RegisterDisplayTranslation()", StringComparison.Ordinal)..];

        Assert.Contains("_displayTranslationRegistered", method);
        Assert.Contains("#if ANDROID", method);
        Assert.Contains("#elif IOS || MACCATALYST", method);
        // Label and Button on both platforms
        Assert.Equal(4, Regex.Matches(method, "AppendToMapping\\(nameof\\((Label|Button)\\.Text\\)").Count);
    }

    // ---------------------------------------------------------------- the data the hook depends on

    private static HashSet<string> GeorgianKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Services", "Localization"), "*.cs")
                     .Where(path => !Path.GetFileName(path).StartsWith("LanguagePacks", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Pair.Matches(text))
                if (Georgian.IsMatch(match.Groups[1].Value)) keys.Add(Unescape(match.Groups[1].Value));
            foreach (Match match in Tuple.Matches(text))
                if (Georgian.IsMatch(match.Groups[1].Value)) keys.Add(Unescape(match.Groups[1].Value));
        }

        return keys;
    }

    private static bool Covered(string text, HashSet<string> keys)
    {
        text = text.Trim();
        if (!Georgian.IsMatch(text) || keys.Contains(text)) return true;
        var stripped = Regex.Replace(text, @"^[^\p{L}\p{N}{(]+", string.Empty);
        if (stripped != text && keys.Contains(stripped)) return true;
        if (text.Contains('\n')) return text.Split('\n').All(line => Covered(line, keys));
        if (text.Contains(" · ")) return text.Split(" · ").All(part => Covered(part, keys));
        var colon = text.IndexOf(':');
        return colon > 0 && keys.Contains(text[..colon].Trim());
    }

    private static IEnumerable<(string File, string Text)> XamlLiterals()
    {
        var attribute = new Regex("\\b(Text|Title|Placeholder|Header|Description|Subtitle|Content|ButtonText|Caption)\\s*=\\s*\"([^\"{][^\"]*)\"");
        var files = Directory.EnumerateFiles(Path.Combine(Root, "Views"), "*.xaml").Append(Path.Combine(Root, "AppShell.xaml"));
        foreach (var file in files)
            foreach (Match match in attribute.Matches(File.ReadAllText(file)))
                yield return (Path.GetFileName(file), match.Groups[2].Value.Replace("&#10;", "\n").Replace("&amp;", "&"));
    }

    [Fact]
    public void EveryGeorgianTextInXaml_HasAnEnglishTranslation()
    {
        var keys = GeorgianKeys();

        var missing = XamlLiterals()
            .Where(item => Georgian.IsMatch(item.Text) && !Covered(item.Text, keys))
            .Select(item => $"{item.File}: {item.Text}")
            .ToList();

        Assert.True(missing.Count == 0, "Georgian XAML text without a translation:\n" + string.Join("\n", missing));
    }

    private sealed class PackIndex
    {
        public HashSet<string> Exact { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Regex> Templates { get; } = new();

        public bool Has(string english)
        {
            var key = english.Trim();
            return Exact.Contains(key) || Templates.Any(template => template.IsMatch(key));
        }

        public void Add(string english)
        {
            english = english.Trim();
            if (english.Contains('{'))
            {
                var pattern = Regex.Escape(english);
                for (var i = 0; i < 10; i++)
                    pattern = pattern.Replace("\\{" + i + "}", "(.+?)");
                Templates.Add(new Regex("^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant));
            }

            Exact.Add(english);
        }
    }

    private static Dictionary<string, PackIndex> LoadPacks(out List<(string File, string En, string[] Translations)> rows)
    {
        var packs = Languages.ToDictionary(code => code, _ => new PackIndex());
        rows = new List<(string, string, string[])>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Services", "Localization"), "LanguagePacks.*.cs"))
        {
            var name = Path.GetFileName(file);
            var text = File.ReadAllText(file);
            var single = Regex.Match(name, @"LanguagePacks\.(Tr|De|Pl|Es|Pt|Fr|It)\.cs");
            if (single.Success)
            {
                foreach (Match match in Pair.Matches(text))
                    packs[single.Groups[1].Value.ToLowerInvariant()].Add(Unescape(match.Groups[1].Value));
                continue;
            }

            foreach (Match match in Row.Matches(text))
            {
                var english = Unescape(match.Groups[1].Value);
                var translations = Regex.Matches(match.Groups[2].Value, Lit).Select(item => Unescape(item.Groups[1].Value)).ToArray();
                rows.Add((name, english, translations));
                for (var i = 0; i < Languages.Length && i < translations.Length; i++)
                    if (!string.IsNullOrWhiteSpace(translations[i]))
                        packs[Languages[i]].Add(english);
            }
        }

        return packs;
    }

    private static readonly HashSet<string> NotTranslated = new(StringComparer.OrdinalIgnoreCase)
    {
        // Names, units, file formats and addresses are the same in every language.
        "GCar360", "OBD360", "Georgian Car Scanner", "RPM", "OBD", "PDF", "CSV", "JSON", "PRO", "VIN", "Mode 05", "Mode 06",
        "name@gmail.com", "gcar360support@gmail.com", "Bluetooth", "Bluetooth / OBD2", "VIN Check", "GCar360 AI", "OBD360 AI", "copy"
    };

    [Fact]
    public void EveryEnglishTextInXaml_IsInEveryLanguagePack()
    {
        var packs = LoadPacks(out _);

        var missing = XamlLiterals()
            .Where(item => !Georgian.IsMatch(item.Text) && Regex.IsMatch(item.Text, "[A-Za-z]{3,}") && !NotTranslated.Contains(item.Text.Trim()))
            .SelectMany(item => Languages.Where(code => !packs[code].Has(item.Text)).Select(code => $"{code}: {item.File}: {item.Text}"))
            .ToList();

        Assert.True(missing.Count == 0, "English XAML text missing from a language pack:\n" + string.Join("\n", missing.Take(40)));
    }

    [Fact]
    public void EachLanguagePack_CoversAtLeastNinetyFivePercentOfTheEnglishTexts_ReachableFromGeorgian()
    {
        var packs = LoadPacks(out _);
        var english = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Services", "Localization"), "*.cs")
                     .Where(path => !Path.GetFileName(path).StartsWith("LanguagePacks", StringComparison.Ordinal)))
            foreach (Match match in Pair.Matches(File.ReadAllText(file)))
                if (Georgian.IsMatch(match.Groups[1].Value) && !Georgian.IsMatch(match.Groups[2].Value))
                    english.Add(Unescape(match.Groups[2].Value).Trim());

        Assert.True(english.Count > 1500, "the dictionaries were not read");
        foreach (var code in Languages)
        {
            var covered = english.Count(text => packs[code].Has(text));
            Assert.True(covered >= english.Count * 0.95, $"{code}: only {covered} of {english.Count} English texts have a translation");
        }
    }

    [Fact]
    public void EveryTranslationRow_KeepsTheSamePlaceholdersAsItsEnglishText()
    {
        LoadPacks(out var rows);

        var broken = new List<string>();
        foreach (var (file, english, translations) in rows)
        {
            var expected = Placeholder.Matches(english).Select(item => item.Value).OrderBy(item => item).ToList();
            for (var i = 0; i < translations.Length && i < Languages.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(translations[i])) continue;
                var actual = Placeholder.Matches(translations[i]).Select(item => item.Value).OrderBy(item => item).ToList();
                if (!expected.SequenceEqual(actual))
                    broken.Add($"{file} [{Languages[i]}] {english}");
            }
        }

        Assert.True(broken.Count == 0, "placeholders differ from the English text:\n" + string.Join("\n", broken.Take(30)));
    }

    // ---------------------------------------------------------------- English texts written in code

    private static readonly Regex CodePair = new(@"\bL\(\s*\$?@?" + Lit + @"\s*,\s*\$?@?" + Lit + @"\s*\)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CodeTernary = new(@"(?:IsEnglish|isEnglish|\ben\b|english|English)\s*\?\s*\$?@?" + Lit + @"\s*:\s*\$?@?" + Lit, RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Interpolation = new(@"\{[^{}]*\}", RegexOptions.Compiled);
    private static readonly Regex LeadingMarks = new(@"^[•\-–*]\s*", RegexOptions.Compiled);

    private static bool LineCovered(PackIndex pack, string text)
    {
        if (pack.Has(text) || pack.Has(LeadingMarks.Replace(text.Trim(), "")))
            return true;
        return text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => Regex.IsMatch(Placeholder.Replace(line, ""), "[A-Za-z]{3,}"))
            .All(line => pack.Has(line) || pack.Has(LeadingMarks.Replace(line, "")));
    }

    [Fact]
    public void EnglishTextsInViewModelsAndServices_AreCoveredInEveryLanguagePack()
    {
        // Every L(ka, en) / IsEnglish ? en : ka text in code reaches the user in English for any non-Georgian
        // language unless a pack row translates it. This pins the coverage measured for the OBD360 localization pass.
        var packs = LoadPacks(out _);
        var english = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var skip = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}Backend{Path.DirectorySeparatorChar}",
            Path.Combine("Services", "Localization") };

        foreach (var file in Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories).Where(path => !skip.Any(part => path.Contains(part, StringComparison.Ordinal))))
        {
            var source = File.ReadAllText(file);
            foreach (var match in CodePair.Matches(source).Cast<Match>().Concat(CodeTernary.Matches(source)))
            {
                var first = match.Groups[1].Value;
                var second = match.Groups[2].Value;
                var isTernary = match.Value.TrimStart().Length > 0 && !match.Value.StartsWith("L(", StringComparison.Ordinal);
                string? en = null;
                if (isTernary)
                    en = !Georgian.IsMatch(first) ? first : (!Georgian.IsMatch(second) ? second : null);
                else if (Georgian.IsMatch(first) && !Georgian.IsMatch(second))
                    en = second;
                else if (!Georgian.IsMatch(first) && Georgian.IsMatch(second))
                    en = first;
                if (en is null)
                    continue;

                var numbered = 0;
                var template = Interpolation.Replace(Unescape(en), _ => "{" + numbered++ + "}");
                if (Regex.IsMatch(template, "[A-Za-z]{3,}"))
                    english.Add(template);
            }
        }

        Assert.True(english.Count > 1500, $"too few English code texts were read ({english.Count})");

        foreach (var code in Languages)
        {
            var missing = english.Where(text => !LineCovered(packs[code], text)).ToList();
            var coverage = 100.0 * (english.Count - missing.Count) / english.Count;
            Assert.True(coverage >= 98.0, $"{code}: {coverage:F1}% of {english.Count} code texts translated; examples:\n" + string.Join("\n", missing.Take(15)));
        }
    }

    private static readonly Regex AnyLiteral = new("\\$?@?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    [Fact]
    public void AdjacentGeorgianEnglishPairsInCode_AreCoveredInEveryLanguagePack()
    {
        // Texts written as "ქართული", "English" next to each other (records, tuples, switch results) and not through
        // L(). About 2 % are identifiers, URLs or interpolations the scanner cannot split; the rest must have a row.
        var packs = LoadPacks(out _);
        var english = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skip = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}Backend{Path.DirectorySeparatorChar}",
            Path.Combine("Services", "Localization") };

        foreach (var file in Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories).Where(path => !skip.Any(part => path.Contains(part, StringComparison.Ordinal))))
        {
            var source = File.ReadAllText(file);
            var tokens = AnyLiteral.Matches(source).Cast<Match>().ToList();
            for (var i = 0; i + 1 < tokens.Count; i++)
            {
                var a = tokens[i];
                var b = tokens[i + 1];
                var between = source.Substring(a.Index + a.Length, b.Index - (a.Index + a.Length));
                if (!Regex.IsMatch(between, @"^\s*,\s*$"))
                    continue;

                var aKa = Georgian.IsMatch(a.Groups[1].Value);
                var bKa = Georgian.IsMatch(b.Groups[1].Value);
                var en = aKa && !bKa ? b.Groups[1].Value : !aKa && bKa ? a.Groups[1].Value : null;
                if (en is null)
                    continue;

                var numbered = 0;
                var template = Interpolation.Replace(Unescape(en), _ => "{" + numbered++ + "}");
                if (Regex.IsMatch(template, "[A-Za-z]{4,}"))
                    english.Add(template);
            }
        }

        Assert.True(english.Count > 1500, $"too few adjacent pairs were read ({english.Count})");
        foreach (var code in Languages)
        {
            var missing = english.Where(text => !LineCovered(packs[code], text)).ToList();
            var coverage = 100.0 * (english.Count - missing.Count) / english.Count;
            Assert.True(coverage >= 97.0, $"{code}: {coverage:F1}% of {english.Count} paired texts translated; examples:\n" + string.Join("\n", missing.Take(15)));
        }
    }

    // ---------------------------------------------------------------- resource parity and runtime behaviour

    [Fact]
    public void EveryTranslationRow_HasAllSevenLanguages_AndNothingLeftBlank()
    {
        LoadPacks(out var rows);
        var broken = rows
            .Where(row => row.Translations.Length != Languages.Length || row.Translations.Any(string.IsNullOrWhiteSpace))
            .Select(row => $"{row.File}: {row.En}")
            .ToList();

        Assert.True(broken.Count == 0, "rows with a missing or blank translation:\n" + string.Join("\n", broken.Take(30)));
    }

    [Fact]
    public void TheNineAdvertisedLanguages_AreAllInTheCatalog_AndSevenHavePacks()
    {
        var catalog = File.ReadAllText(Path.Combine(Root, "Services", "Localization", "LanguageCatalog.cs"));
        foreach (var code in new[] { "ka", "en", "tr", "de", "pl", "es", "pt", "fr", "it" })
            Assert.Contains($"\"{code}\"", catalog);

        // None of them may be dropped to improve a coverage figure: each added language keeps its own pack file.
        foreach (var name in new[] { "Tr", "De", "Pl", "Es", "Pt", "Fr", "It" })
            Assert.True(File.Exists(Path.Combine(Root, "Services", "Localization", $"LanguagePacks.{name}.cs")), $"pack for {name} is missing");
    }

    [Theory]
    [InlineData("Refresh"), InlineData("Update"), InlineData("Loading…"), InlineData("Connecting…"), InlineData("Connect")]
    [InlineData("Ready"), InlineData("Not ready"), InlineData("Unsupported"), InlineData("Not supported")]
    [InlineData("Unavailable"), InlineData("Not available"), InlineData("Retry"), InlineData("Cancel"), InlineData("Close")]
    [InlineData("Start"), InlineData("Stop"), InlineData("Connected"), InlineData("Disconnected"), InlineData("Error")]
    [InlineData("Dark"), InlineData("Light"), InlineData("System"), InlineData("Metric"), InlineData("Imperial")]
    public void CommonStatusAndOptionWords_HaveATranslationInEveryAddedLanguage(string word)
    {
        var packs = LoadPacks(out _);
        var missing = Languages.Where(code => !packs[code].Has(word)).ToList();
        Assert.True(missing.Count == 0, $"\"{word}\" has no translation in: {string.Join(", ", missing)}");
    }

    [Fact]
    public void NativeDialogs_AlwaysGoThroughTheLocalizingWrappers()
    {
        // An alert, prompt or action sheet is not part of the page tree, so the label hook cannot translate it.
        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            if (file.EndsWith("PageDialogExtensions.cs", StringComparison.Ordinal))
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], @"\b(?:DisplayAlertAsync|DisplayPromptAsync|DisplayActionSheetAsync|DisplayAlert|DisplayActionSheet|DisplayPrompt)\("))
                    offenders.Add($"{Path.GetRelativePath(Root, file)}:{i + 1}");
        }

        Assert.True(offenders.Count == 0, "direct native dialog calls (use the *Localized* wrappers):\n" + string.Join("\n", offenders.Take(20)));
    }

    [Fact]
    public void TheActionSheetWrapper_ReturnsTheCallersOwnText_NotTheTranslatedOne()
    {
        var code = File.ReadAllText(Path.Combine(Root, "Services", "PageDialogExtensions.cs"));
        Assert.Contains("return index >= 0 ? buttons[index] : picked;", code);
        Assert.Contains("return cancel;", code);
        Assert.Contains("return destruction;", code);
    }

    [Fact]
    public void ScheduledAndForegroundNotifications_AreTranslatedBeforeTheyAreShown()
    {
        foreach (var file in SourceFiles().Where(path => File.ReadAllText(path).Contains("new NotificationRequest", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (Match request in Regex.Matches(text, @"new NotificationRequest\b[\s\S]{0,900}?\}\);?"))
                Assert.True(request.Value.Contains("LocalizationService.T(", StringComparison.Ordinal),
                    $"{Path.GetRelativePath(Root, file)}: a notification is shown without translation");
        }
    }

    [Fact]
    public void PickersOfStoredOptionValues_ShowTranslatedText_WhileKeepingTheStoredValue()
    {
        foreach (var (page, source) in new[]
        {
            ("SettingsPage.xaml", "ThemeOptions"), ("SettingsPage.xaml", "UnitOptions"), ("SettingsPage.xaml", "PollingOptions"),
            ("FuelPage.xaml", "FuelKindOptions"), ("BatteryPage.xaml", "BatteryTypeOptions")
        })
        {
            var xaml = File.ReadAllText(Path.Combine(Root, "Views", page));
            var picker = Regex.Match(xaml, $@"<Picker[^>]*ItemsSource=""\{{Binding {source}\}}""[^>]*>");
            Assert.True(picker.Success, $"{page}: picker for {source} not found");
            Assert.Contains("LocalizedTextConverter", picker.Value);
        }

        var converter = File.ReadAllText(Path.Combine(Root, "Converters", "LocalizedTextConverter.cs"));
        Assert.Contains("NotSupportedException", converter);                    // the stored value is never written back from the display text
    }

    [Fact]
    public void TheChosenLanguage_IsStored_ClearsTheCache_IsAnnounced_AndRedrawsThePage()
    {
        var service = File.ReadAllText(Path.Combine(Root, "Services", "Localization", "LocalizationService.cs"));
        var start = service.IndexOf("public void SetLanguage(string language)", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = service.Substring(start, 700);

        var set = body.IndexOf("Preferences.Default.Set(", StringComparison.Ordinal);
        var clear = body.IndexOf("ClearTranslateCache();", StringComparison.Ordinal);
        var announce = body.IndexOf("LanguageChanged?.Invoke(", StringComparison.Ordinal);
        var redraw = body.IndexOf("ApplyCurrentPage();", StringComparison.Ordinal);
        Assert.True(set >= 0 && set < clear && clear < announce && announce < redraw, "order: store, clear cache, announce, redraw");

        var catalog = File.ReadAllText(Path.Combine(Root, "Services", "Localization", "LanguageCatalog.cs"));
        Assert.Contains("Preferences.Default.Get(PreferenceKey", catalog);       // the stored choice is read back on the next start
    }

    // ---------------------------------------------------------------- the Coding page texts

    [Fact]
    public void EveryEnglishCodingText_HasATranslationInEveryLanguage()
    {
        var packs = LoadPacks(out _);
        var texts = new HashSet<string>();

        foreach (var forCar in new[] { 0, 2 })
            foreach (var backups in new[] { 0, 3 })
                foreach (var connected in new[] { true, false })
                    foreach (var can in new[] { true, false })
                        foreach (var pro in new[] { true, false })
                            foreach (var restorable in new[] { 0, 1 })
                            foreach (var gm in new[] { false, true })
                            {
                                var input = new CodingInputs(connected, can, true, forCar, 7, backups, restorable, pro, gm);
                                foreach (var status in CodingCapabilities.Evaluate(input))
                                {
                                    texts.Add(CodingTexts.Title(status.Feature, true));
                                    texts.Add(CodingTexts.StateText(status.State, true));
                                    texts.Add(CodingTexts.Reason(status, input, true));
                                }
                            }

        foreach (var input in new[]
        {
            new CodingReadinessInput(false, false, null, null, null, false),
            new CodingReadinessInput(true, false, null, null, null, false),
            new CodingReadinessInput(true, true, "1G1RA6E49EU123456", 0, 13.1, true),
            new CodingReadinessInput(true, true, null, 42, 11.8, false),
            new CodingReadinessInput(true, true, "1G1RA6E49EU123456", null, null, true)
        })
            foreach (var check in CodingCapabilities.Readiness(input))
                texts.Add(check.TextEn);

        texts.Add(CodingTexts.ExtendedReadTitle(true));
        texts.Add(CodingTexts.ExtendedReadHelp(true));

        // The translator works line by line, so a text made of several lines is checked per line.
        var lines = texts.SelectMany(text => text.Split('\n')).ToHashSet();
        var missing = lines
            .SelectMany(text => Languages.Where(code => !packs[code].Has(text)).Select(code => $"{code}: {text}"))
            .ToList();

        Assert.True(missing.Count == 0, "Coding text without a translation:\n" + string.Join("\n", missing.Take(30)));
    }
}
