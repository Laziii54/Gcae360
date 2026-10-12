using CarScanner.Services.Energy;

namespace CarScanner.Tests;

/// <summary>PID 01 9D (engine fuel rate, g/s) as a fuel source. Pure calculation checks - no car was involved.</summary>
public sealed class FuelMassRateTests
{
    private static readonly DateTime T0 = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static FuelSignalCache Cache(Action<FuelSignalCache> fill)
    {
        var cache = new FuelSignalCache();
        cache.Set(FuelSignal.Rpm, 2000, T0);
        fill(cache);
        return cache;
    }

    private static FuelRate Resolve(FuelSignalCache cache, FuelKind kind) => FuelRateResolver.Resolve(cache, T0, kind);

    [Fact]
    public void AGramsPerSecondRate_BecomesLitres_WithTheDensityOfTheSelectedFuel()
    {
        // 1.86 g/s of petrol at 0.745 kg/L = 1.86 / 745 * 3600 = 8.99 L/h
        var rate = Resolve(Cache(c => c.Set(FuelSignal.EngineFuelRateGramsPerSecond, 1.86, T0)), FuelKind.Gasoline);
        Assert.Equal(8.99, rate.LitersPerHour!.Value, 2);
        Assert.Equal(FuelDataQuality.Direct, rate.Quality);
        Assert.Contains("9D", rate.Source);
        Assert.Contains("0.745", rate.Assumption);                              // the density assumption is written down
    }

    [Fact]
    public void ADieselRate_UsesTheDieselDensity_AndNeedsNoLambda()
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.EngineFuelRateGramsPerSecond, 2.0, T0)), FuelKind.Diesel);
        Assert.Equal(2.0 / 835 * 3600, rate.LitersPerHour!.Value, 3);
        Assert.Equal(FuelRateIssue.None, rate.Issue);
    }

    [Fact]
    public void ARealZero_StaysZero_AndIsNotReplacedByAnAirFlowEstimate()
    {
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.EngineFuelRateGramsPerSecond, 0, T0);
            c.Set(FuelSignal.MafGramsPerSecond, 20, T0);
        }), FuelKind.Gasoline);
        Assert.Equal(0, rate.LitersPerHour!.Value);
        Assert.Equal(FuelDataQuality.Direct, rate.Quality);
    }

    [Fact]
    public void TheLitresPid5E_StillWinsOverTheMassRate()
    {
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.DirectFuelRateLph, 6.0, T0);
            c.Set(FuelSignal.EngineFuelRateGramsPerSecond, 3.0, T0);
        }), FuelKind.Gasoline);
        Assert.Equal(6.0, rate.LitersPerHour!.Value);
        Assert.Contains("5E", rate.Source);
    }

    [Theory]
    [InlineData(FuelKind.Unknown)]
    [InlineData(FuelKind.Mixed)]
    [InlineData(FuelKind.Cng)]
    public void WithoutAKnownLiquidFuel_TheMassRateIsNotConverted(FuelKind kind)
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.EngineFuelRateGramsPerSecond, 2.0, T0)), kind);
        Assert.Null(rate.LitersPerHour);                                         // nothing is invented
    }

    [Fact]
    public void AnImplausibleMassRate_IsRejected_NotUsed()
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.EngineFuelRateGramsPerSecond, 5000, T0)), FuelKind.Gasoline);
        Assert.Null(rate.LitersPerHour);
        Assert.Equal(FuelRateIssue.Implausible, rate.Issue);
    }

    [Fact]
    public void AStaleMassRate_IsNotUsed()
    {
        var rate = FuelRateResolver.Resolve(Cache(c => c.Set(FuelSignal.EngineFuelRateGramsPerSecond, 2.0, T0)), T0.AddSeconds(30), FuelKind.Gasoline);
        Assert.Null(rate.LitersPerHour);
    }

    [Fact]
    public void ThePidMapper_FeedsPid9D_AndReadsItWhenNoLitresPidExists()
    {
        var cache = new FuelSignalCache();
        Assert.True(FuelSignalMapper.TryApplyPid(cache, 0x9D, 1.5, T0));
        Assert.Equal(1.5, cache.Get(FuelSignal.EngineFuelRateGramsPerSecond, T0, TimeSpan.FromSeconds(1)));

        Assert.Equal(new[] { 0x5E, 0x2F, 0x51 }, FuelSignalMapper.AnalyticsPids(true, true, true, true));          // 5E first
        Assert.Equal(new[] { 0x9D, 0x2F, 0x51 }, FuelSignalMapper.AnalyticsPids(false, true, true, true));          // then 9D
        Assert.Equal(new[] { 0x10, 0x2F, 0x51 }, FuelSignalMapper.AnalyticsPids(false, true, false));                // MAF otherwise (unchanged)
    }

    [Fact]
    public void TheCompatibilityList_CallsPid9D_CalculatedFromReportedData_NotMeasuredLitres()
    {
        var facts = new EnergyFacts(EnergyMode.Fuel, FuelKind.Gasoline, new HashSet<int> { 0x9D }, false, false, false);
        var fuel = EnergyCompatibility.Evaluate(facts).Single(item => item.Metric == EnergyMetric.InstantFuel);
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, fuel.Support);
        Assert.Equal(EnergyReason.FuelMassRatePid, fuel.Reason);

        var unknownFuel = new EnergyFacts(EnergyMode.Fuel, FuelKind.Unknown, new HashSet<int> { 0x9D }, false, false, false);
        Assert.Equal(EnergySupport.InsufficientData, EnergyCompatibility.Evaluate(unknownFuel).Single(item => item.Metric == EnergyMetric.InstantFuel).Support);
    }
}
