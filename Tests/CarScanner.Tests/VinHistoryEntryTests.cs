namespace CarScanner.Tests;

/// <summary>
/// Home is the app's landing screen and the VIN entry lives there as a quiet row.
///
/// This pins the layout decision made with the owner:
///  • OBD360 opens straight on Home — no "choose OBD / VIN" screen in front of it;
///  • the VIN entry is a text row at the END of the TECHNICAL section, not a big
///    card above it, so it cannot out-shout the diagnostic tiles;
///  • it is NEVER dimmed by the adapter state — it is the one car feature that
///    works without hardware, so a reviewer with no adapter must be able to tap it;
///  • the label is translated in every supported language.
/// </summary>
public sealed class VinHistoryEntryTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    // ---- Home is the landing page -------------------------------------------

    [Fact]
    public void Home_IsTheFirstShellContent()
    {
        var shell = Read("AppShell.xaml");

        var home = shell.IndexOf("Route=\"HomePage\"", StringComparison.Ordinal);
        var start = shell.IndexOf("Route=\"StartPage\"", StringComparison.Ordinal);

        Assert.True(home >= 0, "HomePage must be a ShellContent");
        Assert.True(start >= 0, "the old StartPage runtime route should still exist");
        Assert.True(home < start, "HomePage must come first so the app opens straight into it");
    }

    [Fact]
    public void NothingNavigatesToTheOldStartScreen()
    {
        Assert.DoesNotContain("//StartPage", Read("AppShell.xaml.cs"));
        Assert.DoesNotContain("//StartPage", Read("Views", "HomePage.xaml.cs"));
        Assert.DoesNotContain("OnBackToStartTapped", Read("Views", "HomePage.xaml"));
        Assert.DoesNotContain("OnBackToStartTapped", Read("Views", "HomePage.xaml.cs"));
    }

    [Fact]
    public void BackOnHome_ClosesTheApp_InsteadOfBouncingToAChooser()
    {
        var shell = Read("AppShell.xaml.cs");
        var method = shell[shell.IndexOf("protected override bool OnBackButtonPressed()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.DoesNotContain("StartPage", method);
        Assert.Contains("base.OnBackButtonPressed()", method);
    }

    // ---- the VIN row itself --------------------------------------------------

    /// <summary>The VIN row markup only.</summary>
    private static string VinRow()
    {
        var page = Read("Views", "HomePage.xaml");
        var start = page.IndexOf("<!-- VIN history:", StringComparison.Ordinal);
        Assert.True(start >= 0, "the VIN history row must be present on Home");
        var end = page.IndexOf("BoxView HeightRequest=\"1\" Color=\"{DynamicResource DividerColor}\" Margin=\"2,0,2,2\"", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return page[start..end];
    }

    [Fact]
    public void TheVinRow_IsNotDimmedByTheAdapterState()
    {
        var row = VinRow();

        // Every diagnostic tile fades to 40% until an adapter is connected. The VIN
        // row must not: it is exactly the thing that works without one.
        Assert.DoesNotContain("IsConnected", row);
        Assert.DoesNotContain("Opacity", row);
    }

    [Fact]
    public void TheVinRow_OpensTheLookupPage_WithoutACar()
    {
        var code = Read("Views", "HomePage.xaml.cs");
        var handler = code[code.IndexOf("private async void OnVinHistoryTapped", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("nameof(VinLookupPage)", handler);
        Assert.DoesNotContain("RequireObdConnection", handler);
        Assert.DoesNotContain("ProGate", handler);

        // ... and it is a registered route, otherwise the tap would fail.
        Assert.Contains("Routing.RegisterRoute(nameof(VinLookupPage)", Read("AppShell.xaml.cs"));
    }

    [Fact]
    public void TheVinRow_SitsAfterTheTechnicalTiles_NotAboveThem()
    {
        var page = Read("Views", "HomePage.xaml");

        var ecuTile = page.IndexOf("GoToEcuCommand", StringComparison.Ordinal);
        var vinRow = page.IndexOf("<!-- VIN history:", StringComparison.Ordinal);
        var serviceSection = page.IndexOf("GoToServiceCommand", StringComparison.Ordinal);

        Assert.True(ecuTile >= 0 && vinRow > ecuTile,
            "the VIN row must come after the technical tiles so it does not push them down");
        Assert.True(serviceSection > vinRow,
            "the VIN row belongs to the technical section, before the service section");
    }

    // ---- translations --------------------------------------------------------

    [Fact]
    public void TheVinLabel_IsTranslatedInEverySupportedLanguage()
    {
        var pack = Read("Services", "Localization", "LanguagePacks.Extra40.cs");

        Assert.Contains("R(\"VIN History\",", pack);

        // tr, de, pl, es, pt, fr, it — one translation per added language.
        foreach (var language in new[]
                 {
                     "\"VIN Geçmişi\"", "\"VIN-Verlauf\"", "\"Historia VIN\"",
                     "\"Historial del VIN\"", "\"Histórico VIN\"", "\"Historique VIN\"", "\"Storico VIN\""
                 })
        {
            Assert.Contains(language, pack);
        }

        Assert.Contains("ExtraRows40", Read("Services", "Localization", "LanguagePacks.cs"));
    }

    [Fact]
    public void TheVinLabel_IsGeorgianOnTheGeorgianBuildAndTranslatedOtherwise()
    {
        var code = Read("Views", "HomePage.xaml.cs");

        Assert.Contains("LocalizationService.T(\"VIN History\")", code);
        Assert.Contains("\"VIN ისტორია\"", code);
        Assert.Contains("UpdateVinHistoryLabel", code);
    }
}
