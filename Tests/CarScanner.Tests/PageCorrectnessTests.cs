using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Page-level correctness found in the OBD360 review. Software ELM327 and source-reading tests only: none of this proves
/// anything on a phone or a car.
/// </summary>
public sealed class PageCorrectnessTests
{
    private sealed class Collector<T> : IProgress<T>
    {
        public List<T> Items { get; } = new();
        public void Report(T value) => Items.Add(value);
    }

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

    [Fact]
    public async Task AllSensorsProgress_KeepsOneTotal_AndNeverMovesBackwards()
    {
        var sim = new SimulatedElm327();
        // PIDs 03 and 01 are status-type (read as text after the numeric ones); 05, 0C, 0D are decodable.
        sim.ObdAnswers["0100"] = "41 00 A8 18 00 00";
        sim.ObdAnswers["0103"] = "41 03 01 00";
        sim.ObdAnswers["0101"] = "41 01 00 07 E5 00";
        sim.ObdAnswers["0105"] = "41 05 7B";
        sim.ObdAnswers["010C"] = "41 0C 1A F8";
        sim.ObdAnswers["010D"] = "41 0D 3C";
        await sim.ConnectAsync(new BleDeviceItem());
        var obd = new OBDService(new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance), new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await obd.InitializeAsync();

        var collector = new Collector<SensorScanProgress>();
        var result = await obd.ReadExpandedSupportedSensorValuesAsync(collector);

        Assert.NotEmpty(collector.Items);
        var totals = collector.Items.Select(item => item.Total).Distinct().ToList();
        Assert.Single(totals);                                                   // the old scan went 33 / 33 and then 34 / 38
        Assert.True(totals[0] >= 4, "decodable and status-type PIDs both count in the total");
        Assert.Equal(totals[0], result.Stats.Advertised);

        var completed = collector.Items.Select(item => item.Completed).ToList();
        Assert.Equal(completed.OrderBy(value => value), completed);              // the bar never goes back
        Assert.Equal(totals[0], completed[^1]);
    }

    [Theory]
    // Standard lead-acid: the long-standing ranges.
    [InlineData(Battery12VType.Standard, 14.2, 800.0, VoltageLevel.Good)]
    [InlineData(Battery12VType.Standard, 12.9, 800.0, VoltageLevel.Problem)]
    [InlineData(Battery12VType.Standard, 12.9, null, VoltageLevel.Neutral)]
    [InlineData(Battery12VType.Standard, 12.6, 0.0, VoltageLevel.Good)]
    // AGM/EFB on a start-stop car: a smart alternator may sit at 13.0 V on purpose.
    [InlineData(Battery12VType.AgmEfb, 13.0, 1500.0, VoltageLevel.Attention)]
    [InlineData(Battery12VType.AgmEfb, 12.2, 1500.0, VoltageLevel.Problem)]
    [InlineData(Battery12VType.AgmEfb, 14.4, 1500.0, VoltageLevel.Good)]
    // Hybrid / EV auxiliary: no engine rpm in READY, so the rpm must not decide the verdict.
    [InlineData(Battery12VType.HybridAuxiliary, 13.8, 0.0, VoltageLevel.Good)]
    [InlineData(Battery12VType.HybridAuxiliary, 13.8, null, VoltageLevel.Good)]
    [InlineData(Battery12VType.HybridAuxiliary, 12.7, null, VoltageLevel.Good)]
    [InlineData(Battery12VType.HybridAuxiliary, 12.1, null, VoltageLevel.Attention)]
    [InlineData(Battery12VType.HybridAuxiliary, 11.5, null, VoltageLevel.Problem)]
    [InlineData(Battery12VType.HybridAuxiliary, 15.4, null, VoltageLevel.Problem)]
    // Lithium: resting 12.5 V is low (flat curve), and without rpm nothing is judged.
    [InlineData(Battery12VType.Lithium, 12.5, 0.0, VoltageLevel.Attention)]
    [InlineData(Battery12VType.Lithium, 13.2, 0.0, VoltageLevel.Good)]
    [InlineData(Battery12VType.Lithium, 13.2, null, VoltageLevel.Neutral)]
    [InlineData(Battery12VType.Lithium, 14.2, 1200.0, VoltageLevel.Good)]
    public void TheVoltageVerdict_FollowsTheBatteryType(Battery12VType type, double volts, double? rpm, VoltageLevel expected) =>
        Assert.Equal(expected, BatteryVoltageGuide.Describe(volts, rpm, type).Level);

    [Fact]
    public void ASameReading_IsJudgedDifferently_ForDifferentBatteryTypes()
    {
        // 12.9 V with the engine at idle: a fault for a lead-acid car, ordinary for a hybrid's DC-DC fed battery.
        Assert.Equal(VoltageLevel.Problem, BatteryVoltageGuide.Describe(12.9, 800, Battery12VType.Standard).Level);
        Assert.Equal(VoltageLevel.Good, BatteryVoltageGuide.Describe(12.9, 800, Battery12VType.HybridAuxiliary).Level);
        foreach (var type in Enum.GetValues<Battery12VType>())
            Assert.False(string.IsNullOrWhiteSpace(BatteryVoltageGuide.Hint(type)));
    }

    [Fact]
    public void TheBatteryPage_HasNoHighVoltageSection_AndOffersTheBatteryTypePicker()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "BatteryPage.xaml"));
        Assert.DoesNotContain("High Voltage Battery", xaml);                    // the traction battery lives on the Hybrid Battery page
        Assert.DoesNotContain("HighVoltage", xaml);
        Assert.Contains("SelectedIndex=\"{Binding SelectedTypeIndex}\"", xaml);

        var code = File.ReadAllText(Path.Combine(Root, "ViewModels", "BatteryViewModel.cs"));
        Assert.DoesNotContain("RefreshHighVoltageStatusAsync", code);
        Assert.Contains("BatteryVoltageGuide.Describe", code);
    }

    [Fact]
    public void TheHomePage_HasOneReportEntry_ThatOpensTheFullVehicleReport()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "HomePage.xaml"));

        // The full report (PDF / QR / text export, earlier reports, scan history) is the only report entry.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xaml, "GoToDiagnosticReportCommand"));
        Assert.DoesNotContain("GoToVehicleJournalCommand", xaml);
        Assert.Contains("მანქანის ანგარიში", xaml);
        Assert.DoesNotContain("Text=\"ანგარიში\"", xaml);                        // the generic duplicate tile is gone

        var code = File.ReadAllText(Path.Combine(Root, "ViewModels", "HomeViewModel.cs"));
        Assert.Contains("nameof(Views.VehicleReportPage)", code);
        Assert.DoesNotContain("GoToVehicleJournal", code);

        // Saved reports and every export stay on the report page.
        var report = File.ReadAllText(Path.Combine(Root, "Views", "VehicleReportPage.xaml"));
        Assert.Contains("ExportPdfCommand", report);
        Assert.Contains("CreateQrCommand", report);
        Assert.Contains("ShareTextCommand", report);
        Assert.Contains("ShareHistoryCommand", report);
    }

    [Fact]
    public void TheAccelerationPage_FollowsTheUnitSystem_NotAFixedZeroToHundred()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "AccelerationPage.xaml"));
        Assert.DoesNotContain("Text=\"0–100\"", xaml);                           // the heading
        Assert.DoesNotContain("Title=\"0–100\"", xaml);
        Assert.DoesNotContain("Text=\"0–100 რეიტინგი\"", xaml);                  // the ranking row
        Assert.Contains("{Binding AccelerationHeadline}", xaml);
        Assert.Contains("{Binding AccelerationRankingRowText}", xaml);

        var home = File.ReadAllText(Path.Combine(Root, "ViewModels", "HomeViewModel.cs"));
        Assert.Contains("AccelerationHeadline => _units.PerformanceTestLabel", home);

        // Metric users get 0–100 km/h, imperial users 0–60 mph — by the unit setting only, never by the language.
        var tests = File.ReadAllText(Path.Combine(Root, "Models", "PerformanceTestDefinition.cs"));
        Assert.Contains("units.IsImperial", tests);
        Assert.Contains("\"0-60mph\", \"0–60 mph\", 0, 60 * 1.609344d", tests);
        var units = File.ReadAllText(Path.Combine(Root, "Services", "UnitSystemService.cs"));
        Assert.Contains("PerformanceTestLabel => IsImperial ? \"0–60 mph\" : \"0–100 km/h\"", units);

        var code = File.ReadAllText(Path.Combine(Root, "Views", "AccelerationPage.xaml.cs"));
        Assert.DoesNotContain("gcar360-0-100", code);                            // old brand and fixed test in the share file name
    }

    [Fact]
    public void GraphStudio_ExpandsAGraphOnTap_WithTheCloseButtonAtTheUpperLeft_AndKeepsItLive()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "GraphStudioPage.xaml")).Replace("\r\n", "\n");

        var start = xaml.IndexOf("IsVisible=\"{Binding ShowExpandedChannel}\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the expanded graph must be shown for the separate and grid views");
        var card = xaml.Substring(start, xaml.IndexOf("</Border>", start, StringComparison.Ordinal) - start);

        // Close (X) is the first column, i.e. the upper-left corner, before the name and the value.
        var close = card.IndexOf("DynamicTextExpandedClose", StringComparison.Ordinal);
        var name = card.IndexOf("DynamicTextExpandedName", StringComparison.Ordinal);
        Assert.True(close > 0 && name > close, "the X button must come before the name (upper-left)");
        Assert.DoesNotContain("Grid.Column=\"2\" Text=\"\"", card);
        Assert.Contains("ToggleExpandCommand", card);

        // The enlarged chart binds the channel's own live series (no copy), so it keeps updating.
        Assert.Contains("Series=\"{Binding ExpandedChannel.Series}\"", card);

        // Tapping a graph expands it in the separate view as well as in the grid view.
        Assert.True(System.Text.RegularExpressions.Regex.Matches(xaml, "TapCommand=\"\\{Binding ToggleExpandCommand").Count >= 2);

        var code = File.ReadAllText(Path.Combine(Root, "ViewModels", "GraphStudioViewModel.cs"));
        Assert.Contains("ShowExpandedChannel => HasExpandedChannel && (IsSeparateView || IsGridView)", code);
    }

    [Theory]
    [InlineData("Services/ObdReportService.cs", "GEORGIAN CAR SCANNER")]
    [InlineData("Services/DataCollection/LocalCaptureService.cs", "gcar360-capture")]
    [InlineData("ViewModels/CodingViewModel.cs", "gcar360_coding")]
    [InlineData("ViewModels/GraphStudioViewModel.cs", "gcar360_graph")]
    [InlineData("ViewModels/PerformanceVideoViewModel.cs", "gcar360_perf")]
    [InlineData("Views/AccelerationPage.xaml.cs", "gcar360-0-100")]
    public void ReportHeadersAndExportedFileNames_CarryTheNewName(string file, string oldName)
    {
        var code = File.ReadAllText(Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar)));
        Assert.DoesNotContain(oldName, code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obd360", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SmallText_StaysRare_AndNeverGoesBelowTen()
    {
        // OBD360_SMALL_TEXT_AUDIT.md: 254 declarations were below 12 before the typography pass; 19 are left on purpose.
        var sizes = new List<double>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}Backend{Path.DirectorySeparatorChar}") &&
                                    // Mode 05 / Mode 06 pages are frozen by project rule (no UI edits), so their six 11-point labels stay as they were.
                                    !path.EndsWith("Mode05Page.xaml", StringComparison.Ordinal) && !path.EndsWith("Mode06Page.xaml", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, "FontSize=\"([0-9.]+)\""))
                sizes.Add(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, "Property=\"FontSize\"\\s+Value=\"([0-9.]+)\""))
                sizes.Add(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.True(sizes.Count > 900, "the XAML files were not read");
        Assert.True(sizes.Count(size => size < 12) <= 20, $"{sizes.Count(size => size < 12)} text sizes are below 12");
        Assert.DoesNotContain(sizes, size => size < 9.5);                        // nothing below 10 except in the chart canvas code
    }

    [Fact]
    public void ChangingTheLanguage_NeverTouchesTheMeasurementUnits()
    {
        var units = File.ReadAllText(Path.Combine(Root, "Services", "UnitSystemService.cs"));
        Assert.DoesNotContain("LocalizationService", units);
        Assert.DoesNotContain("LanguageCatalog", units);
        Assert.DoesNotContain("AppLanguage", units);
        Assert.DoesNotContain("LanguageChanged", units);

        var localization = File.ReadAllText(Path.Combine(Root, "Services", "Localization", "LocalizationService.cs"));
        Assert.DoesNotContain("UnitSystemService", localization);
        Assert.DoesNotContain("\"units\"", localization);                        // the preference key of the unit system
        Assert.DoesNotContain("fuel_economy_unit", localization);
    }

    [Fact]
    public void TheServicePage_OffersBothOilResetTargets_AndNothingForACarThatCannotReset()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "ServicePage.xaml"));
        Assert.Contains("ResetOilLifeFullCommand", xaml);
        Assert.Contains("ResetOilLifeHalfCommand", xaml);

        // Both buttons live in one container that is visible only when the car supports the checked write-back.
        var start = xaml.IndexOf("IsVisible=\"{Binding CanResetOilLife}\" ColumnDefinitions", StringComparison.Ordinal);
        Assert.True(start > 0, "the 50% / 100% buttons must sit in a CanResetOilLife container");
        var block = xaml.Substring(start, xaml.IndexOf("</Grid>", start, StringComparison.Ordinal) - start);
        Assert.Contains("ResetOilLifeFullCommand", block);
        Assert.Contains("ResetOilLifeHalfCommand", block);
        Assert.Contains("CanPressOilReset", block);                              // disabled while a reset runs

        // A car without a supported reset says why instead of showing nothing.
        Assert.Contains("IsVisible=\"{Binding ShowOilResetUnavailable}\"", xaml);

        var code = File.ReadAllText(Path.Combine(Root, "ViewModels", "AdvancedViewModels.cs"));
        Assert.Contains("ResetOilLifeAsync(100)", code);
        Assert.Contains("ResetOilLifeAsync(50)", code);
        Assert.Contains("!CanResetOilLife", code);                               // the command itself refuses when unsupported
    }
}
