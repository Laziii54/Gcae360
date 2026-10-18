namespace CarScanner.Tests;

/// <summary>
/// The live-graph bottom sheet: tapping a numeric value on All sensors, ECU Explorer and Module live data opens it.
/// Source-reading wiring checks plus the one pure rule (which values can be graphed). Unit/source only: how the sheet
/// looks and feels on a phone has not been checked.
/// </summary>
public sealed class SensorGraphSheetTests
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

    [Theory]
    [InlineData("12.5", true)]
    [InlineData("-4", true)]
    [InlineData("0", true)]
    [InlineData("—", false)]
    [InlineData("ON", false)]
    [InlineData("", false)]
    public void OnlyNumericSensorValues_CanBeGraphed(string value, bool expected)
    {
        Assert.Equal(expected, new CarScanner.Models.SensorValue { Value = value }.CanGraph);
    }

    [Theory]
    [InlineData("SensorsPage", "OnSensorTapped")]
    [InlineData("EcuIdentifiersPage", "OnPidTapped")]
    public void TheSensorAndEcuPages_HostTheSheet_AndTheirRowsAreTappable(string page, string handler)
    {
        var xaml = Read("Views", page + ".xaml");
        Assert.Contains("<controls:SensorGraphSheet x:Name=\"GraphSheet\" />", xaml);
        Assert.Contains("Tapped=\"" + handler + "\"", xaml);
        Assert.Contains("IsVisible=\"{Binding CanGraph}\"", xaml);

        var code = Read("Views", page + ".xaml.cs");
        Assert.Contains("GraphSheet.Show(", code);
        Assert.Contains("OnBackButtonPressed", code);          // the Android back button closes the sheet first
        Assert.Contains("GraphSheet.Close();", code);
    }

    [Fact]
    public void ModuleLiveData_ShowsTheSheet_InsteadOfOpeningTheFullGraphPage()
    {
        var vm = Read("ViewModels", "ModuleLiveDataViewModel.cs");
        Assert.Contains("GraphRequested", vm);

        var page = Read("Views", "ModuleLiveDataPage.xaml.cs");
        Assert.Contains("GraphRequested += OnGraphRequested", page);
        Assert.Contains("GraphRequested -= OnGraphRequested", page);   // no leak when the page goes away
        Assert.Contains("GraphSheet.Show(item)", page);
        Assert.Contains("<controls:SensorGraphSheet x:Name=\"GraphSheet\" />", Read("Views", "ModuleLiveDataPage.xaml"));
    }

    [Fact]
    public void TheSensorsGraph_PollsOnlyWhileTheSheetIsOpen_AndStopsWhenItCloses()
    {
        var page = Read("Views", "SensorsPage.xaml.cs");
        Assert.Contains("GraphSheet.Closed += (_, _) => vm.StopGraph();", page);

        var vm = Read("ViewModels", "SensorsViewModel.cs");
        Assert.Contains("ReadLiveValuesAsync(new[] { pid }, token)", vm);   // one PID, one short attempt, read-only
        Assert.Contains("if (!IsBusy)", vm);                                // never competes with a running scan
    }

    [Fact]
    public void TheGraphViewModel_UnderstandsEcuExplorerRows_AndTheLiveDataPageIsUnchanged()
    {
        var graph = Read("ViewModels", "SensorGraphViewModel.cs");
        Assert.Contains("EcuPidReading reading => (reading.Name, reading.Unit)", graph);

        // Live Data (and Fuel / Hybrid) still open the full graph page.
        Assert.Contains("SafeNavigation.GoToAsync(nameof(Views.SensorGraphPage)", Read("ViewModels", "LiveDataViewModel.cs"));
    }
}