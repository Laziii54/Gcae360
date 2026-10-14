using System.Text.RegularExpressions;
using CarScanner.Controls;
using CarScanner.Services.Energy;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Enforces "NO EMOJI-STYLE ICONS IN THE OBD360 USER INTERFACE" (DEVELOPMENT_GUIDE.md, chapter "Icon policy").
/// The scans read source files; they prove the repository text contains no emoji-style characters in UI strings, not that a
/// screen looks right on a device.
/// </summary>
public sealed class IconPolicyTests
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

    private static readonly string[] SkippedFolders = { "obj", "bin", ".git", ".vs", "Tests", "node_modules", "Raw", "DtcCatalog", "wwwroot", "StoreRelease/output" };

    private static IEnumerable<string> Files(string pattern, string? under = null)
    {
        var start = under is null ? Root : Path.Combine(Root, under);
        foreach (var file in Directory.EnumerateFiles(start, pattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (SkippedFolders.Any(folder => relative.StartsWith(folder + "/", StringComparison.Ordinal) || relative.Contains("/" + folder + "/", StringComparison.Ordinal)))
                continue;
            yield return file;
        }
    }

    // ---- the detector itself: what it flags and what it must leave alone ----

    [Theory]
    [InlineData("✓ Ready")]
    [InlineData("Ready ✓")]
    [InlineData("⚠ Check")]
    [InlineData("⚡ On electric")]
    [InlineData("⛽ Fuel")]
    [InlineData("🚗 Welcome")]
    [InlineData("🇬🇧")]
    [InlineData("★ Favorite")]
    [InlineData("● Connected")]
    [InlineData("▶ Start")]
    [InlineData("☰")]
    [InlineData("ℹ️ Note")]
    [InlineData("Clipboard &#128203; hint")]
    [InlineData("Done &#10003;")]
    [InlineData("ⓘ")]
    public void TheDetector_FlagsEmojiAndSymbolIcons(string text) =>
        Assert.NotEmpty(IconPolicy.FindBanned(text));

    [Theory]
    [InlineData("Ready")]
    [InlineData("მზადაა — გაზომე 0–100 კმ/სთ")]
    [InlineData("Speed → 120 km/h ← back ↑ up ↓ down")]
    [InlineData("A ↔ B")]
    [InlineData("≈ 5.8 L/100 km · ≤ 0.10 V · ± 2 × 3 − 1 ° C")]
    [InlineData("₾ 10 · € 5 · $ 3 · £ 2")]
    [InlineData("© 2026 OBD360 ® ™")]
    [InlineData("Ünïcödé Türkçe Polski Español Português Français Italiano Deutsch")]
    [InlineData("├── Engine ─ │ └── Gateway")]
    [InlineData("“quotes” ‘single’ « French » … – —")]
    [InlineData("&amp; &lt; &nbsp; &#169; &#8212;")]
    public void TheDetector_LeavesLocalizedTextMathAndTechnicalCharactersAlone(string text) =>
        Assert.Empty(IconPolicy.FindBanned(text));

    [Theory]
    [InlineData("›", true)]
    [InlineData("‹", true)]
    [InlineData("×", true)]
    [InlineData("Next ›", false)]
    [InlineData("2 × 3", false)]
    public void ALonePunctuationMark_IsAnIconSubstitute_ButNotInsideASentence(string literal, bool expected) =>
        Assert.Equal(expected, IconPolicy.IsLoneIconPunctuation(literal));

    // ---- the repository ----

    [Fact]
    public void NoXamlFile_ContainsAnEmojiOrSymbolIcon_OrALoneChevronLabel()
    {
        Assert.True(Files("*.xaml").Count() > 30, "the XAML scan must actually see the pages");
        var offenders = new List<string>();
        foreach (var file in Files("*.xaml"))
        {
            var text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            foreach (var hit in IconPolicy.FindBanned(text))
                offenders.Add($"{Path.GetRelativePath(Root, file)}: {hit}");
            foreach (Match attr in Regex.Matches(text, "(?:Text|Content|Title|Placeholder)=\"([^\"]*)\""))
                if (IconPolicy.IsLoneIconPunctuation(attr.Groups[1].Value))
                    offenders.Add($"{Path.GetRelativePath(Root, file)}: text \"{attr.Groups[1].Value}\" used as an icon");
        }

        Assert.True(offenders.Count == 0, "Emoji-style icons in XAML (use controls:Icon / controls:IconButton or plain text):\n" + string.Join("\n", offenders.Distinct()));
    }

    [Fact]
    public void NoCSharpStringLiteral_ContainsAnEmojiOrSymbolIcon()
    {
        Assert.True(Files("*.cs").Count() > 300, "the C# scan must actually see the product sources");
        var offenders = new List<string>();
        foreach (var file in Files("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (var literal in IconPolicy.LiteralsOf(lines[i]))
                {
                    foreach (var hit in IconPolicy.FindBanned(literal))
                        offenders.Add($"{Path.GetRelativePath(Root, file)}:{i + 1}: {hit}");
                    if (IconPolicy.IsLoneIconPunctuation(literal.Trim('"')))
                        offenders.Add($"{Path.GetRelativePath(Root, file)}:{i + 1}: literal {literal} used as an icon");
                }
        }

        Assert.True(offenders.Count == 0, "Emoji-style icons in C# strings (use an icon kind from IconCatalog or plain text):\n" + string.Join("\n", offenders.Distinct()));
    }

    [Fact]
    public void TheVisualProposalsPageAndTheStoreListings_AreFreeOfEmojiToo()
    {
        var offenders = new List<string>();
        var files = new List<string> { Path.Combine(Root, "OBD360_VISUAL_PROPOSALS.html") };
        files.AddRange(Directory.EnumerateFiles(Path.Combine(Root, "StoreRelease"), "STORE_LISTING*.md"));
        foreach (var file in files.Where(File.Exists))
            foreach (var hit in IconPolicy.FindBanned(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)}: {hit}");

        Assert.True(offenders.Count == 0, string.Join("\n", offenders.Distinct()));
    }

    [Fact]
    public void TheBundledDataTexts_ShownInTheApp_AreFreeOfEmojiAndSymbolIcons()
    {
        // Service procedures and other bundled JSON are shown to people as text. Buttons printed on a car are named in words.
        var offenders = new List<string>();
        var raw = Path.Combine(Root, "Resources", "Raw");
        Assert.True(Directory.EnumerateFiles(raw, "*.json").Any(), "the bundled JSON must be found");
        foreach (var file in Directory.EnumerateFiles(raw, "*.json"))
            foreach (var hit in IconPolicy.FindBanned(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)}: {hit}");

        Assert.True(offenders.Count == 0, string.Join("\n", offenders.Distinct()));
    }

    // ---- the icon catalog ----

    [Fact]
    public void EveryIconKindNamedInXaml_ExistsInTheCatalog()
    {
        var missing = new List<string>();
        foreach (var file in Files("*.xaml"))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "(?:controls:Icon|controls:IconButton)\\b[^>]*?\\sKind=\"([a-z][a-z-]*)\""))
                if (IconCatalog.PathData(match.Groups[1].Value) is null)
                    missing.Add($"{Path.GetRelativePath(Root, file)}: {match.Groups[1].Value}");

        Assert.True(missing.Count == 0, "Unknown icon kind:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryCatalogIcon_IsAValidOutlinePath_OnTheShared24Grid()
    {
        foreach (var name in IconCatalog.Names)
        {
            var data = IconCatalog.PathData(name)!;
            Assert.Matches(@"^[MLCAZ0-9 ,.\-]+$", data);                          // absolute commands only
            foreach (Match number in Regex.Matches(data, @"-?\d+(\.\d+)?"))
                Assert.InRange(double.Parse(number.Value, System.Globalization.CultureInfo.InvariantCulture), 0, 24.0001); // radii and coordinates stay on the grid
        }
    }

    [Fact]
    public void TheIconKindsProducedByViewModels_AreInTheCatalog()
    {
        foreach (EnergySupport support in Enum.GetValues<EnergySupport>())
            Assert.NotNull(IconCatalog.PathData(FuelPresenter.Mark(support)));

        foreach (var kind in new[] { "check", "close", "warning", "info", "circle", "minus", "refresh", "chevron-up", "chevron-down", "play", "pause", "approx", "star" })
            Assert.NotNull(IconCatalog.PathData(kind));
    }

    [Fact]
    public void TheDevelopmentGuide_StatesThePolicy_AndNamesTheCheck()
    {
        var guide = File.ReadAllText(Path.Combine(Root, "DEVELOPMENT_GUIDE.md"));
        Assert.Contains("NO EMOJI-STYLE ICONS IN THE OBD360 USER INTERFACE", guide);
        Assert.Contains("IconPolicyTests", guide);
        Assert.Contains("IconCatalog", guide);
        foreach (var cp in IconPolicy.Allowed)
            Assert.Contains($"U+{cp:X4}", guide);                                   // every allowed exception is written down
    }
}
