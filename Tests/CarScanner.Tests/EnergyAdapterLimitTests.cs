using CarScanner.Services.Energy;

namespace CarScanner.Tests;

/// <summary>
/// The fuel/energy page must not say "this car has no battery data" when the ADAPTER could not address the battery module
/// (a cheap clone refusing the commands for a physically addressed read). Rules and wiring only: nothing here ran on a car
/// or a real adapter.
/// </summary>
public sealed class EnergyAdapterLimitTests
{
    private static EnergyFacts Facts(bool batterySource, bool adapterLimited, bool soc = false) =>
        new(EnergyMode.Hybrid, FuelKind.Gasoline, new HashSet<int> { 0x0C, 0x0D, 0x10 }, batterySource, soc, false, false, adapterLimited);

    private static EnergyCapability Row(IEnumerable<EnergyCapability> rows, EnergyMetric metric) => rows.Single(row => row.Metric == metric);

    [Fact]
    public void WhenTheAdapterCouldNotRead_TheBatteryRowsSayNotRead_NotNotSupported()
    {
        var rows = EnergyCompatibility.Evaluate(Facts(batterySource: false, adapterLimited: true));

        foreach (var metric in new[] { EnergyMetric.Regeneration, EnergyMetric.ElectricPower, EnergyMetric.ElectricEnergy, EnergyMetric.StateOfCharge })
        {
            var row = Row(rows, metric);
            Assert.Equal(EnergySupport.InsufficientData, row.Support);
            Assert.Equal(EnergyReason.AdapterCouldNotRead, row.Reason);
        }
    }

    [Fact]
    public void WhenNoAdapterProblemWasSeen_TheOldWording_NoBatteryData_Stays()
    {
        var rows = EnergyCompatibility.Evaluate(Facts(batterySource: false, adapterLimited: false));

        var regeneration = Row(rows, EnergyMetric.Regeneration);
        Assert.Equal(EnergySupport.Unsupported, regeneration.Support);
        Assert.Equal(EnergyReason.NoBatterySource, regeneration.Reason);
    }

    [Fact]
    public void ASourceThatWasFound_AlwaysWins_OverTheAdapterFlag()
    {
        var rows = EnergyCompatibility.Evaluate(Facts(batterySource: true, adapterLimited: true));

        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Row(rows, EnergyMetric.Regeneration).Support);
        Assert.Equal(EnergyReason.BatteryCurrentSource, Row(rows, EnergyMetric.Regeneration).Reason);
    }

    [Fact]
    public void AStateOfChargeThatIsRead_IsStillUsed_EvenWhenTheCurrentCouldNotBeRead()
    {
        var rows = EnergyCompatibility.Evaluate(Facts(batterySource: false, adapterLimited: true, soc: true));

        Assert.Equal(EnergySupport.DirectlySupported, Row(rows, EnergyMetric.StateOfCharge).Support);
    }

    [Fact]
    public void TheReasonIsExplained_InBothLanguages_AndNeverBlamesTheCar()
    {
        var english = FuelPresenter.ReasonText(EnergyReason.AdapterCouldNotRead, true);
        var georgian = FuelPresenter.ReasonText(EnergyReason.AdapterCouldNotRead, false);

        Assert.Contains("adapter", english);
        Assert.DoesNotContain("not supported", english, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(georgian));
        Assert.NotEqual(english, georgian);
    }

    // ---- wiring (source) ----

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    [Fact]
    public void TheReader_NotesAdapterSideFailures_InEveryPlaceItSwallowsAnException()
    {
        var source = Read("Services", "Energy", "EvEnergyReader.cs");

        Assert.Contains("AdapterReadFailure.Classify(exception)", source);
        Assert.Contains("failure.IsAdapterSide", source);
        // Both the resolve loop and the safe reader record it; the field is reset on every resolve and never persisted.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"NoteAdapterFailure\(ex\);").Count);
        Assert.Contains("_adapterFailure = null;", source);
        Assert.DoesNotContain("Preferences", source);
    }

    [Fact]
    public void ThePage_PassesTheFlagToTheRules_AndTellsTheUserTheAdapterIsTheLimit()
    {
        var energy = Read("ViewModels", "FuelViewModel.Energy.cs");
        var engine = Read("ViewModels", "FuelViewModel.Engine.cs");

        Assert.Contains("_evAdapterLimited = source is null && _evReader.AdapterLimit is not null;", energy);
        Assert.Contains("_evAdapterLimited);", engine);
        Assert.Contains("The adapter could not read the battery module", energy);
        Assert.Contains("This does not mean the car lacks this data", energy);
    }
}
