using CarScanner.Services.Energy;

namespace CarScanner.Tests;

public sealed class FuelPresenterTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static FuelTripEngine Run(FuelKind kind, double speed, double rpm, double? direct = null, double? maf = null,
        double? throttle = null, double? load = null, int seconds = 30)
    {
        var engine = new FuelTripEngine { Kind = kind };
        for (var s = 0; s <= seconds; s++)
        {
            var t = T0.AddSeconds(s);
            engine.Signals.Set(FuelSignal.SpeedKph, speed, t);
            engine.Signals.Set(FuelSignal.Rpm, rpm, t);
            if (direct is { } d) engine.Signals.Set(FuelSignal.DirectFuelRateLph, d, t);
            if (maf is { } m) engine.Signals.Set(FuelSignal.MafGramsPerSecond, m, t);
            if (throttle is { } th) engine.Signals.Set(FuelSignal.ThrottlePercent, th, t);
            if (load is { } l) engine.Signals.Set(FuelSignal.LoadPercent, l, t);
            engine.Tick(t);
        }

        return engine;
    }

    // ---- instantaneous display ------------------------------------------------

    [Fact]
    public void DirectReading_IsShownWithoutTheEstimateMark()
    {
        var display = FuelPresenter.Instant(Run(FuelKind.Gasoline, 100, 2500, direct: 7.0).Instant, FuelEconomyUnit.LitersPer100Km, true);
        Assert.True(display.IsAvailable);
        Assert.Equal("7.0", display.Value);
        Assert.Equal("L/100 km", display.Unit);
        Assert.DoesNotContain("≈", display.Value);
        Assert.Equal(FuelDataQuality.Direct, display.Quality);
        Assert.Contains("Direct", display.Note);
    }

    [Fact]
    public void AirFlowReading_IsAlwaysMarkedAsAnEstimate()
    {
        // 10 g/s → 3.28722 L/h at 100 km/h = 3.287 L/100 km
        var display = FuelPresenter.Instant(Run(FuelKind.Gasoline, 100, 2500, maf: 10.0).Instant, FuelEconomyUnit.LitersPer100Km, true);
        Assert.StartsWith("≈ ", display.Value);
        Assert.Equal("≈ 3.3", display.Value);
        Assert.Equal(FuelDataQuality.Estimated, display.Quality);
        Assert.Contains("Estimate", display.Note);
        Assert.DoesNotContain("Direct", display.Note);
    }

    [Theory]
    [InlineData(FuelEconomyUnit.LitersPer100Km, "7.0", "L/100 km")]
    [InlineData(FuelEconomyUnit.KmPerLiter, "14.3", "km/L")]
    [InlineData(FuelEconomyUnit.MpgUs, "33.6", "MPG (US)")]
    [InlineData(FuelEconomyUnit.MpgUk, "40.4", "MPG (UK)")]
    public void ChangingTheUnit_ChangesTheValueAndTheLabel_NotTheMeasurement(FuelEconomyUnit unit, string value, string label)
    {
        var instant = Run(FuelKind.Gasoline, 100, 2500, direct: 7.0).Instant;
        var display = FuelPresenter.Instant(instant, unit, true);
        Assert.Equal(value, display.Value);
        Assert.Equal(label, display.Unit);
        Assert.Equal(7.0, instant.LitersPer100Km!.Value, 9);   // the underlying figure never changes with the display unit
    }

    [Fact]
    public void Idle_ShowsTheFlow_NeverInfinityOrAnArbitraryNumber()
    {
        var idle = Run(FuelKind.Gasoline, 0, 800, direct: 0.8).Instant;
        foreach (var unit in Enum.GetValues<FuelEconomyUnit>())
        {
            var display = FuelPresenter.Instant(idle, unit, true);
            Assert.True(display.IsAvailable);
            Assert.DoesNotContain("∞", display.Value);
            Assert.DoesNotContain("Infinity", display.Value);
            Assert.Contains("not defined", display.Note);
        }

        Assert.Equal("0.80", FuelPresenter.Instant(idle, FuelEconomyUnit.LitersPer100Km, true).Value);
        Assert.Equal("L/h", FuelPresenter.Instant(idle, FuelEconomyUnit.LitersPer100Km, true).Unit);
        Assert.Equal("0.21", FuelPresenter.Instant(idle, FuelEconomyUnit.MpgUs, true).Value);          // 0.8 L/h = 0.2113 gal/h
        Assert.Equal("gal (US)/h", FuelPresenter.Instant(idle, FuelEconomyUnit.MpgUs, true).Unit);
    }

    [Fact]
    public void EngineOff_ShowsNothingInsteadOfAConsumption()
    {
        var display = FuelPresenter.Instant(Run(FuelKind.Gasoline, 0, 0, direct: 0).Instant, FuelEconomyUnit.LitersPer100Km, true);
        Assert.Equal("—", display.Value);
        Assert.Contains("Engine off", display.Note);
    }

    [Fact]
    public void FuelCut_ShowsZeroInLPer100Km_AndADashForTheDistancePerVolumeUnits()
    {
        var cut = Run(FuelKind.Gasoline, 80, 3000, direct: 0.0).Instant;
        Assert.Equal("0.0", FuelPresenter.Instant(cut, FuelEconomyUnit.LitersPer100Km, true).Value);
        foreach (var unit in new[] { FuelEconomyUnit.KmPerLiter, FuelEconomyUnit.MpgUs, FuelEconomyUnit.MpgUk })
            Assert.Equal("—", FuelPresenter.Instant(cut, unit, true).Value);
        Assert.Contains("Fuel cut", FuelPresenter.Instant(cut, FuelEconomyUnit.LitersPer100Km, true).Note);
    }

    [Fact]
    public void InferredFuelCut_SaysItIsInferred()
    {
        var cut = Run(FuelKind.Gasoline, 70, 2600, maf: 14.0, throttle: 0, load: 12).Instant;
        var display = FuelPresenter.Instant(cut, FuelEconomyUnit.LitersPer100Km, true);
        Assert.StartsWith("≈ ", display.Value);
        Assert.Contains("inferred", display.Note);
    }

    [Theory]
    [InlineData(FuelKind.Unknown, "Fuel type unknown")]
    [InlineData(FuelKind.Cng, "cannot be calculated in litres")]
    [InlineData(FuelKind.Electric, "no liquid fuel")]
    public void WhenNoFuelValueCanBeGiven_TheReasonIsShown(FuelKind kind, string expected)
    {
        var display = FuelPresenter.Instant(Run(kind, 80, 2500, maf: 10.0).Instant, FuelEconomyUnit.LitersPer100Km, true);
        Assert.False(display.IsAvailable);
        Assert.Equal("—", display.Value);
        Assert.Contains(expected, display.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FuelDataQuality.None, display.Quality);
    }

    [Fact]
    public void DieselAirFlowOnly_SaysWhyThereIsNoNumber()
    {
        var display = FuelPresenter.Instant(Run(FuelKind.Diesel, 80, 1800, maf: 25.0).Instant, FuelEconomyUnit.LitersPer100Km, true);
        Assert.False(display.IsAvailable);
        Assert.Contains("λ", display.Note);
    }

    [Fact]
    public void NoReadingAtAll_IsNotAFabricatedValue()
    {
        var display = FuelPresenter.Instant(InstantFuel.None, FuelEconomyUnit.MpgUs, true);
        Assert.False(display.IsAvailable);
        Assert.Equal("—", display.Value);
        Assert.Contains("not reported", display.Note);
    }

    [Fact]
    public void AllTextsExistInBothLanguages()
    {
        foreach (var english in new[] { true, false })
        {
            foreach (var issue in Enum.GetValues<FuelRateIssue>())
                Assert.False(string.IsNullOrWhiteSpace(FuelPresenter.IssueText(issue, english)));
            foreach (var quality in Enum.GetValues<FuelDataQuality>())
                Assert.False(string.IsNullOrWhiteSpace(FuelPresenter.QualityLabel(quality, english)));
            foreach (var quality in Enum.GetValues<TripFuelQuality>())
                Assert.False(string.IsNullOrWhiteSpace(FuelPresenter.TripQualityLabel(quality, 1, english)));
            foreach (var metric in Enum.GetValues<EnergyMetric>())
                Assert.False(string.IsNullOrWhiteSpace(FuelPresenter.MetricName(metric, english)));
            foreach (var support in Enum.GetValues<EnergySupport>())
                Assert.False(string.IsNullOrWhiteSpace(FuelPresenter.SupportLabel(support, english)));
        }
    }

    // ---- trip average and totals ---------------------------------------------

    private static FuelTripSnapshot Cruise(double? direct, double? maf, FuelKind kind = FuelKind.Gasoline, int seconds = 600) =>
        Run(kind, 60, 2000, direct: direct, maf: maf, seconds: seconds).Snapshot();

    [Fact]
    public void Average_DirectAndComplete_HasNoMark()
    {
        Assert.Equal("10.0 L/100 km", FuelPresenter.Average(Cruise(6.0, null), FuelEconomyUnit.LitersPer100Km));
        Assert.Equal("23.5 MPG (US)", FuelPresenter.Average(Cruise(6.0, null), FuelEconomyUnit.MpgUs));
    }

    [Fact]
    public void Average_FromAirFlow_IsMarkedAsAnEstimate()
    {
        var text = FuelPresenter.Average(Cruise(null, 10.0), FuelEconomyUnit.LitersPer100Km);
        Assert.StartsWith("≈ ", text);
        Assert.Equal("≈ 5.5 L/100 km", text);
    }

    [Fact]
    public void Average_WithoutDistanceOrFuel_IsADash()
    {
        Assert.Equal("—", FuelPresenter.Average(Cruise(null, null), FuelEconomyUnit.LitersPer100Km));
        Assert.Equal("—", FuelPresenter.Average(Run(FuelKind.Gasoline, 0, 800, direct: 0.8).Snapshot(), FuelEconomyUnit.LitersPer100Km));
        Assert.Equal("—", FuelPresenter.Average(Run(FuelKind.Gasoline, 60, 2000, direct: 6.0, seconds: 4).Snapshot(), FuelEconomyUnit.LitersPer100Km));   // 67 m
    }

    [Fact]
    public void Average_OnPartialData_IsNotShownAsExact()
    {
        var engine = new FuelTripEngine { Kind = FuelKind.Gasoline };
        for (var s = 0; s <= 600; s++)
        {
            var t = T0.AddSeconds(s);
            engine.Signals.Set(FuelSignal.SpeedKph, 60, t);
            engine.Signals.Set(FuelSignal.Rpm, 2000, t);
            if (s < 200) engine.Signals.Set(FuelSignal.DirectFuelRateLph, 6.0, t);   // fuel stops arriving after 200 s
            engine.Tick(t);
        }

        var trip = engine.Snapshot();
        Assert.False(trip.HasReliableAverage);
        Assert.StartsWith("≈ ", FuelPresenter.Average(trip, FuelEconomyUnit.LitersPer100Km));
        Assert.Contains("of the running time", FuelPresenter.TripQualityLabel(trip.Quality, trip.FuelCoverage, true));
    }

    [Fact]
    public void FuelUsed_IsMarkedWhenEstimated_AndHiddenWhenZero()
    {
        Assert.Equal("1.00 L", FuelPresenter.FuelUsed(Cruise(6.0, null), FuelEconomyUnit.LitersPer100Km));
        Assert.StartsWith("≈ ", FuelPresenter.FuelUsed(Cruise(null, 10.0), FuelEconomyUnit.LitersPer100Km));
        Assert.Equal("—", FuelPresenter.FuelUsed(Cruise(null, null), FuelEconomyUnit.LitersPer100Km));
        Assert.Equal("0.26 gal (US)", FuelPresenter.FuelUsed(Cruise(6.0, null), FuelEconomyUnit.MpgUs));
    }

    [Theory]
    [InlineData(TripFuelQuality.Direct, "Direct")]
    [InlineData(TripFuelQuality.Estimated, "Estimate")]
    [InlineData(TripFuelQuality.Mixed, "Mixed")]
    [InlineData(TripFuelQuality.None, "No fuel data")]
    public void TripQuality_NamesTheSource(TripFuelQuality quality, string expected) =>
        Assert.Contains(expected, FuelPresenter.TripQualityLabel(quality, 1, true));

    // ---- what is saved ----------------------------------------------------------

    [Theory]
    [InlineData(TripFuelQuality.Direct, EnergyQuality.Unavailable, false, "reported by the ECU", "estimate")]
    [InlineData(TripFuelQuality.Estimated, EnergyQuality.Unavailable, false, "estimate from air flow", "reported by the ECU")]
    [InlineData(TripFuelQuality.Mixed, EnergyQuality.Unavailable, false, "partly estimated", "")]
    [InlineData(TripFuelQuality.None, EnergyQuality.Measured, false, "voltage × current", "estimate")]
    [InlineData(TripFuelQuality.None, EnergyQuality.Estimated, false, "estimate from battery %", "voltage × current")]
    [InlineData(TripFuelQuality.None, EnergyQuality.Unavailable, false, "No fuel or energy data", "estimate")]
    [InlineData(TripFuelQuality.Direct, EnergyQuality.Estimated, true, "recovered after the app stopped", "")]
    public void SavedSourceText_NeverCallsAnEstimateAMeasurement(TripFuelQuality fuel, EnergyQuality energy, bool interrupted, string must, string mustNot)
    {
        var text = FuelPresenter.TripSource(fuel, FuelKind.Gasoline, energy, interrupted);
        Assert.Contains(must, text);
        if (mustNot.Length > 0)
            Assert.DoesNotContain(mustNot, text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- what the car supports ---------------------------------------------------

    [Fact]
    public void Marks_DistinguishReportedEstimatedMissingAndUnavailable()
    {
        Assert.Equal("✓", FuelPresenter.Mark(EnergySupport.DirectlySupported));
        Assert.Equal("✓", FuelPresenter.Mark(EnergySupport.CalculatedFromAvailableData));
        Assert.Equal("≈", FuelPresenter.Mark(EnergySupport.Estimated));
        Assert.Equal("·", FuelPresenter.Mark(EnergySupport.InsufficientData));
        Assert.Equal("✕", FuelPresenter.Mark(EnergySupport.Unsupported));
    }

    [Fact]
    public void EveryReasonUsedByTheEvaluatorHasText()
    {
        foreach (var reason in Enum.GetValues<EnergyReason>().Where(r => r != EnergyReason.None))
        {
            Assert.False(string.IsNullOrWhiteSpace(FuelPresenter.ReasonText(reason, true)), reason.ToString());
            Assert.False(string.IsNullOrWhiteSpace(FuelPresenter.ReasonText(reason, false)), reason.ToString());
        }
    }
}
