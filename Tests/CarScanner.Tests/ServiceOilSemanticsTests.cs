using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

/// <summary>
/// What a service signal measures, and how far a reset may go. Names and units in the first group are the ones the public
/// OBDb signal sets actually use (the repository named in each comment); the decoding formulas are OBDb's and have not been
/// verified on a vehicle by OBD360. Nothing here ran on a car.
/// </summary>
public sealed class ServiceOilSemanticsTests
{
    // ---- oil life remaining vs. oil degradation (opposite scales) ----

    [Theory]
    [InlineData("Engine oil life remaining", "GM_OIL_LIFE")]          // Chevrolet-Volt, GM family
    [InlineData("Oil life remaining", "")]                             // Ford-Mustang 22 054B
    [InlineData("Remaining oil life", "")]                             // several makes
    public void OilLifeRemaining_IsOilLifePercent(string name, string id)
    {
        Assert.Equal(ServiceMetricKind.OilLifePercent, ServiceMetricClassifier.Classify(name, id));
    }

    [Theory]
    [InlineData("Engine oil degradation", "JEEP_OIL_DEGRAD")]          // Jeep, Chrysler, Dodge, Ram, Fiat, Maserati: 22 3813 @ DA10, percent
    [InlineData("Engine oil degradation", "PEUGEOT_OIL_DEGRAD")]       // Peugeot, Citroen, DS, VauxhallOpel: same signal in OBDb
    [InlineData("Engine oil degradation", "")]
    public void OilDegradation_IsItsOwnKind_NeverOilLifeRemaining(string name, string id)
    {
        var kind = ServiceMetricClassifier.Classify(name, id);

        Assert.Equal(ServiceMetricKind.OilDegradationPercent, kind);
        Assert.NotEqual(ServiceMetricKind.OilLifePercent, kind);
        Assert.True(ServiceMetricClassifier.IsServiceRelevantName(name));
    }

    // ---- "maintenance" in names that are not a service schedule ----

    [Theory]
    [InlineData("Temperature of the electronic power unit of the voltage maintenance device")]  // Peugeot, Jeep: the 12 V maintenance device
    [InlineData("Centralized voltage maintenance device temperature limiter state")]            // Peugeot, Jeep
    [InlineData("Parking brake normal 0 /maintenance 32")]                                       // Volvo: the parking brake's maintenance state
    public void AMaintenanceWordThatIsNotAServiceSchedule_IsNotAServiceSignal(string name)
    {
        Assert.False(ServiceMetricClassifier.IsServiceRelevantName(name));
        Assert.Equal(ServiceMetricKind.Other, ServiceMetricClassifier.Classify(name, string.Empty));
    }

    [Theory]
    [InlineData("Maintenance, oil & filter")]                    // Honda, Acura (Maintenance Minder items)
    [InlineData("Maintenance, general inspection")]
    [InlineData("Maintenance autonomy interval in km")]          // Renault
    public void ARealMaintenanceItem_StaysAServiceSignal(string name)
    {
        Assert.True(ServiceMetricClassifier.IsServiceRelevantName(name));
        Assert.Equal(ServiceMetricKind.MaintenanceCounter, ServiceMetricClassifier.Classify(name, string.Empty));
    }

    [Fact]
    public void ANameThatSaysOilLife_IsAlwaysRemainingLife_EvenWithDegradationInTheId()
    {
        Assert.Equal(ServiceMetricKind.OilLifePercent, ServiceMetricClassifier.Classify("Engine oil life remaining", "X_OIL_DEGRAD"));
    }

    [Theory]
    [InlineData("Fluid degradation")]                  // Mitsubishi CVT: a gearbox-fluid scalar, not engine oil
    [InlineData("Fluid degradation, output 3")]
    public void GearboxFluidDegradation_IsNotAnEngineOilValue(string name)
    {
        Assert.Equal(ServiceMetricKind.Other, ServiceMetricClassifier.Classify(name, ""));
        Assert.False(ServiceMetricClassifier.IsServiceRelevantName(name));
    }

    [Fact]
    public void ADegradationValue_IsPlausibleOnlyBetweenZeroAndOneHundredPercent()
    {
        Assert.True(ServiceMetricClassifier.IsPlausible(ServiceMetricKind.OilDegradationPercent, "percent", 0));
        Assert.True(ServiceMetricClassifier.IsPlausible(ServiceMetricKind.OilDegradationPercent, "percent", 100));
        Assert.False(ServiceMetricClassifier.IsPlausible(ServiceMetricKind.OilDegradationPercent, "percent", 100.5));
        Assert.False(ServiceMetricClassifier.IsPlausible(ServiceMetricKind.OilDegradationPercent, "percent", -1));
        Assert.False(ServiceMetricClassifier.IsPlausible(ServiceMetricKind.OilDegradationPercent, "percent", double.NaN));
    }

    // ---- the other service names (existing behaviour must not change; new names are the ones OBDb uses) ----

    [Theory]
    [InlineData("Distance since last oil change", ServiceMetricKind.DistanceSinceOilChange)]
    [InlineData("Distance since oil change", ServiceMetricKind.DistanceSinceOilChange)]
    [InlineData("Distance since oil changed", ServiceMetricKind.DistanceSinceOilChange)]
    [InlineData("Mileage since last oil change", ServiceMetricKind.DistanceSinceOilChange)]
    [InlineData("Distance to next service", ServiceMetricKind.DistanceToNextService)]
    [InlineData("Distance to next oil change", ServiceMetricKind.DistanceToNextService)]
    [InlineData("Estimated distance to oil change", ServiceMetricKind.DistanceToNextService)]   // Mazda-3 22 0542, kilometers
    [InlineData("Distance before next oil change", ServiceMetricKind.DistanceToNextService)]
    [InlineData("Days to next service", ServiceMetricKind.DaysToNextService)]
    [InlineData("Max distance to oil change", ServiceMetricKind.OilChangeInterval)]             // an interval, not "remaining"
    [InlineData("Oil change interval in km", ServiceMetricKind.OilChangeInterval)]
    [InlineData("Maintenance, oil & filter", ServiceMetricKind.MaintenanceCounter)]
    [InlineData("Maximum oil level for the service interval", ServiceMetricKind.OilLevel)]
    public void ServiceNames_AreClassifiedByWhatTheyMeasure(string name, ServiceMetricKind expected)
    {
        Assert.Equal(expected, ServiceMetricClassifier.Classify(name, ""));
    }

    [Fact]
    public void TheSevereConditionsCounter_IsNotMistakenForTheOrdinaryDistanceSinceOilChange()
    {
        // OBDb lists a separate "…under severe vehicle operating conditions" counter: a different value.
        Assert.Equal(ServiceMetricKind.Other,
            ServiceMetricClassifier.Classify("Vehicle mileage since the last oil change under severe vehicle operating conditions", ""));
    }

    [Fact]
    public void AnUnrelatedSignal_IsNotServiceRelevant()
    {
        Assert.Equal(ServiceMetricKind.Other, ServiceMetricClassifier.Classify("Front right tire pressure", "TAYCAN_TP_FR"));
        Assert.False(ServiceMetricClassifier.IsServiceRelevantName("Front right tire pressure"));
    }

    // ---- how the page treats the two ----

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    [Fact]
    public void ThePage_NeverShowsADegradationValueInTheOilLifeCard_OrJudgesItLikeRemainingLife()
    {
        var vm = Read("ViewModels", "AdvancedViewModels.cs");

        // The oil card is chosen by "remaining life" only.
        Assert.Contains("_lastServiceReadings.FirstOrDefault(item => item.Kind == ServiceMetricKind.OilLifePercent)", vm);

        // Its own color (neutral), name and note: no green / amber / red verdict is drawn from a degradation figure.
        Assert.Contains("ServiceMetricKind.OilDegradationPercent => \"#5D636B\"", vm);
        Assert.Contains("Engine oil degradation (higher = more worn)", vm);
        Assert.Contains("not oil life remaining", vm);

        // The remaining-life colors (≤10 red, ≤25 amber) stay tied to OilLifePercent.
        Assert.Contains("ServiceMetricKind.OilLifePercent when reading.NumericValue <= 10", vm);
    }

    [Fact]
    public void TheReaderDelegatesClassification_ToTheOneSharedClassifier()
    {
        var reader = Read("Services", "OemDiagnostics", "ServiceDataReaderService.cs");

        Assert.Contains("ServiceMetricClassifier.Classify(name, parameterId)", reader);
        Assert.Contains("ServiceMetricClassifier.IsPlausible(kind, unit, value)", reader);
        Assert.Contains("ServiceMetricKind.OilDegradationPercent or", reader);   // still read brand-wide, like every other oil value
    }

    // ---- reset to 100% is not "set to any percentage" ----

    [Fact]
    public void ThePercentTargetsOtherThan100_NeedTheirOwnEvidence_AndTheReviewedCatalogIsStillEmpty()
    {
        var reader = Read("Services", "OemDiagnostics", "ServiceDataReaderService.cs");

        // Default is "no": an entry must say so explicitly.
        Assert.Contains("bool AllowsPercentTargets = false", reader);
        Assert.Contains("CanResetLearnedOilLife &&\n        (percent == 100 || _reviewedGmReset is { AllowsPercentTargets: true })", reader);

        // Both write paths refuse before any vehicle I/O.
        Assert.Contains("if (targetPercent != 100 && !entry.AllowsPercentTargets)", reader);
        Assert.Contains("if (reading.Kind == ServiceMetricKind.OilLifePercent && Math.Abs(target - 100d) > 0.001)", reader);

        // No reviewed entry exists, so no car is offered any reset today (unchanged).
        Assert.Contains("ReviewedGmOilLifeResets =\n        Array.Empty<ReviewedGmOilLifeReset>();", reader);
    }

    [Fact]
    public void TheHalfButton_IsBoundToItsOwnCapability_NotToTheGeneralResetCapability()
    {
        var xaml = Read("Views", "ServicePage.xaml");
        var vm = Read("ViewModels", "AdvancedViewModels.cs");

        Assert.Contains("IsVisible=\"{Binding CanSetOilLifeHalf}\"", xaml);
        Assert.Contains("_serviceDataReader.CanOfferOilLifeTarget(50)", vm);
        Assert.Contains("Grid.ColumnSpan=\"{Binding OilResetFullColumnSpan}\"", xaml);
    }
}
