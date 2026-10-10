using CarScanner.Services.Energy;

namespace CarScanner.Tests;

public sealed class EvEnergyIntegrationTests
{
    [Fact]
    public void ConstantPower_IntegratesToVoltsTimesAmpsTimesTime()
    {
        var accumulator = new EvEnergyAccumulator();
        accumulator.LockSign(1);
        for (var second = 0; second <= 60; second++)
            accumulator.Add(400, 50, second == 0 ? 0 : 1, 50);

        // 400 V · 50 A = 20 kW; 60 s → 20 000 W · 60 s / 3600 = 333.333 Wh
        Assert.Equal(0.333333, accumulator.ConsumedKWh, 5);
        Assert.Equal(0, accumulator.RegeneratedKWh);
        Assert.Equal(20.0, accumulator.PowerKw, 9);
    }

    [Fact]
    public void RegenerationIsCountedApart_AndNetIsTheDifference()
    {
        var accumulator = new EvEnergyAccumulator();
        accumulator.LockSign(1);
        for (var second = 0; second <= 60; second++)
            accumulator.Add(400, 50, second == 0 ? 0 : 1, 50);        // 0.333333 kWh out
        for (var second = 1; second <= 30; second++)
            accumulator.Add(400, -25, 1, 50);                           // 10 kW back for 30 s = 0.083333 kWh

        Assert.Equal(0.333333, accumulator.ConsumedKWh, 5);
        Assert.Equal(0.083333, accumulator.RegeneratedKWh, 5);
        Assert.Equal(0.25, accumulator.NetKWh, 5);
    }

    [Fact]
    public void ReversedSign_SwapsUsedAndRecovered()
    {
        var accumulator = new EvEnergyAccumulator();
        accumulator.LockSign(-1);   // this brand: negative current = discharging
        for (var second = 0; second <= 60; second++)
            accumulator.Add(400, -50, second == 0 ? 0 : 1, 50);
        Assert.Equal(0.333333, accumulator.ConsumedKWh, 5);
        Assert.Equal(0, accumulator.RegeneratedKWh);
    }

    [Fact]
    public void GapLongerThanFiveSeconds_IsNotIntegrated()
    {
        var accumulator = new EvEnergyAccumulator();
        accumulator.LockSign(1);
        accumulator.Add(400, 50, 0, 50);
        accumulator.Add(400, 50, 6, 50);     // a hole: the energy of those 6 s is unknown
        Assert.Equal(0, accumulator.ConsumedKWh);
        accumulator.Add(400, 50, 5, 50);     // exactly at the limit still counts
        Assert.Equal(20000 * 5 / 3600d / 1000d, accumulator.ConsumedKWh, 9);
    }

    [Theory]
    [InlineData(12, 50)]       // 12 V auxiliary battery
    [InlineData(5000, 50)]     // absurd pack voltage
    [InlineData(400, 99999)]   // absurd current
    [InlineData(double.NaN, 10)]
    [InlineData(400, double.NaN)]
    public void ImplausibleReadings_AreIgnored(double volts, double amps)
    {
        var accumulator = new EvEnergyAccumulator();
        accumulator.Add(volts, amps, 1, 50);
        Assert.False(accumulator.HasSample);
        Assert.Equal(0, accumulator.ConsumedKWh);
        Assert.Equal(0, accumulator.RegeneratedKWh);
    }

    [Fact]
    public void NonPositiveTimeStep_AddsNothing()
    {
        var accumulator = new EvEnergyAccumulator();
        accumulator.LockSign(1);
        accumulator.Add(400, 50, 0, 50);
        accumulator.Add(400, 50, -3, 50);
        accumulator.Add(400, 50, 0, 50);
        Assert.Equal(0, accumulator.ConsumedKWh);
    }

    [Fact]
    public void KWhPer100Km_NeedsADistance()
    {
        Assert.Equal(16.0, EvEnergyAccumulator.KWhPer100Km(8.0, 50)!.Value, 9);
        Assert.Null(EvEnergyAccumulator.KWhPer100Km(8.0, 0.05));
        Assert.Equal(20.0 / 80 * 100, EvEnergyAccumulator.InstantKWhPer100Km(20, 80)!.Value, 9);
        Assert.Null(EvEnergyAccumulator.InstantKWhPer100Km(20, 3));
    }
}

public sealed class SocEnergyEstimatorTests
{
    [Fact]
    public void KnownDrop_GivesTheHandCalculatedEnergy()
    {
        // 80 % → 70 % of a 60 kWh battery = 6 kWh
        var kwh = SocEnergyEstimator.KWh(80, 70, 60, out var issue);
        Assert.Equal(6.0, kwh!.Value, 9);
        Assert.Equal(SocEstimateIssue.None, issue);
    }

    [Theory]
    [InlineData(null, 70.0, 60.0, SocEstimateIssue.SocMissing)]
    [InlineData(80.0, null, 60.0, SocEstimateIssue.SocMissing)]
    [InlineData(80.0, 70.0, null, SocEstimateIssue.CapacityMissing)]
    [InlineData(80.0, 70.0, 1.0, SocEstimateIssue.CapacityImplausible)]
    [InlineData(80.0, 70.0, 900.0, SocEstimateIssue.CapacityImplausible)]
    [InlineData(80.0, 70.0, double.NaN, SocEstimateIssue.CapacityImplausible)]
    [InlineData(120.0, 70.0, 60.0, SocEstimateIssue.SocImplausible)]
    [InlineData(80.0, -5.0, 60.0, SocEstimateIssue.SocImplausible)]
    [InlineData(80.0, 79.0, 60.0, SocEstimateIssue.ChargeDidNotFall)]   // within the 1 % resolution
    [InlineData(70.0, 80.0, 60.0, SocEstimateIssue.ChargeDidNotFall)]   // the battery gained charge
    public void NoEstimate_WhenTheInputsDoNotSupportOne(double? start, double? now, double? capacity, SocEstimateIssue expected)
    {
        Assert.Null(SocEnergyEstimator.KWh(start, now, capacity, out var issue));
        Assert.Equal(expected, issue);
    }
}

public sealed class ElectricTripCalculatorTests
{
    private static FuelTripSnapshot Trip(double km = 50, double engineOffKm = 0) =>
        new(km, 0, 0, 0, 3000, 0, 0, 0, 0, 0, 0, 100, 60, null, 0, TripFuelQuality.None, 0, 0, 0, 0, 0, engineOffKm, 0, null, false);

    [Fact]
    public void Bev_MeasuredEnergy_DividesByTheWholeTrip()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Electric, Trip(50), true, 9.0, 1.0, 80, 65, 60);
        Assert.Equal(EnergyQuality.Measured, result.Quality);
        Assert.Equal(8.0, result.NetKWh!.Value, 9);
        Assert.Equal(16.0, result.KWhPer100Km!.Value, 9);
        Assert.Equal(ElectricBasis.WholeTrip, result.Basis);
        Assert.Equal(1.0, result.RegeneratedKWh);
        Assert.Equal(-15, result.SocChange);
    }

    [Fact]
    public void Bev_WithoutCurrent_UsesTheSocEstimate_AndSaysEstimated()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Electric, Trip(50), false, 0, 0, 80, 70, 60);
        Assert.Equal(EnergyQuality.Estimated, result.Quality);
        Assert.Equal(6.0, result.NetKWh!.Value, 9);
        Assert.Equal(12.0, result.KWhPer100Km!.Value, 9);
        Assert.Null(result.ConsumedKWh);        // an estimate cannot split used / recovered
        Assert.Null(result.RegeneratedKWh);
    }

    [Fact]
    public void Bev_WithNeitherCurrentNorCapacity_HasNoEnergy_NotAGuess()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Electric, Trip(50), false, 0, 0, 80, 70, null);
        Assert.Equal(EnergyQuality.Unavailable, result.Quality);
        Assert.Null(result.NetKWh);
        Assert.Null(result.KWhPer100Km);

        var noSoc = ElectricTripCalculator.Compute(EnergyMode.Electric, Trip(50), false, 0, 0, null, null, 60);
        Assert.Equal(EnergyQuality.Unavailable, noSoc.Quality);
    }

    [Fact]
    public void Bev_TooShortATrip_GivesEnergyButNoPer100Km()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Electric, Trip(0.3), true, 0.1, 0, null, null, null);
        Assert.Equal(0.1, result.NetKWh!.Value, 9);
        Assert.Null(result.KWhPer100Km);
    }

    [Fact]
    public void Phev_DividesByTheEngineOffDistance_NotByTheEnginesKilometres()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Hybrid, Trip(km: 100, engineOffKm: 40), true, 8.0, 0.0, 90, 40, null);
        Assert.Equal(ElectricBasis.EngineOffDistance, result.Basis);
        Assert.Equal(40, result.DistanceBasisKm);
        Assert.Equal(20.0, result.KWhPer100Km!.Value, 9);       // 8 kWh / 40 km
        // The same energy over the whole 100 km would have looked like 8 kWh/100 km — a misleading dilution.
        Assert.NotEqual(8.0, result.KWhPer100Km!.Value);
    }

    [Fact]
    public void Phev_WithAlmostNoElectricDriving_GivesNoPer100Km()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Hybrid, Trip(km: 30, engineOffKm: 0.2), true, 0.4, 0, null, null, null);
        Assert.Null(result.KWhPer100Km);
        Assert.Equal(0.4, result.NetKWh!.Value, 9);
    }

    [Theory]
    [InlineData(60, 50, true)]     // battery lost 10 points: fuel alone understates the energy used
    [InlineData(50, 60, true)]     // battery gained 10 points
    [InlineData(50, 49, false)]
    [InlineData(50, 53, true)]
    public void Hybrid_ChargeBalanceIsFlagged_WhenTheBatteryChangedNoticeably(double start, double now, bool notable)
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Hybrid, Trip(), false, 0, 0, start, now, null);
        Assert.Equal(notable, result.ChargeBalanceNotable);
    }

    [Fact]
    public void FuelOnlyCar_HasNoElectricTrip()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Fuel, Trip(), true, 5, 1, 80, 60, 60);
        Assert.Equal(EnergyQuality.Unavailable, result.Quality);
        Assert.Null(result.NetKWh);
        Assert.False(result.ChargeBalanceNotable);
    }

    [Fact]
    public void NonFiniteMeasuredValues_AreNotTrusted()
    {
        var result = ElectricTripCalculator.Compute(EnergyMode.Electric, Trip(), true, double.NaN, 0, 80, 70, 60);
        Assert.Equal(EnergyQuality.Estimated, result.Quality);   // falls back to the (labelled) estimate
    }

    [Fact]
    public void InstantElectricEconomy_NeedsMovement()
    {
        Assert.Equal(18.0, ElectricTripCalculator.InstantKWhPer100Km(18, 100)!.Value, 9);
        Assert.Null(ElectricTripCalculator.InstantKWhPer100Km(18, 2));
        Assert.Null(ElectricTripCalculator.InstantKWhPer100Km(18, null));
        Assert.Null(ElectricTripCalculator.InstantKWhPer100Km(double.NaN, 80));
        Assert.Equal(-12.0, ElectricTripCalculator.InstantKWhPer100Km(-12, 100)!.Value, 9);   // regeneration shows as negative
    }
}

public sealed class EnergyCompatibilityTests
{
    private static HashSet<int> Pids(params int[] pids) => new(pids);

    private static EnergySupport Support(IReadOnlyList<EnergyCapability> list, EnergyMetric metric) =>
        list.Single(c => c.Metric == metric).Support;

    private static EnergyReason Reason(IReadOnlyList<EnergyCapability> list, EnergyMetric metric) =>
        list.Single(c => c.Metric == metric).Reason;

    private static EnergyFacts Facts(EnergyMode mode, FuelKind kind, HashSet<int>? pids, bool current = false, bool soc = false, bool capacity = false, bool lambda = false) =>
        new(mode, kind, pids, current, soc, capacity, lambda);

    [Fact]
    public void Gasoline_WithTheFuelRatePid_IsDirectlySupported_AndTripFiguresAreCalculated()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Gasoline, Pids(0x0D, 0x0C, 0x5E, 0x2F)));
        Assert.Equal(EnergySupport.DirectlySupported, Support(list, EnergyMetric.InstantFuel));
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.AverageFuel));
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.TripFuel));
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.TripDistance));
        Assert.Equal(EnergySupport.DirectlySupported, Support(list, EnergyMetric.FuelLevel));
        Assert.Equal(EnergySupport.Unsupported, Support(list, EnergyMetric.ElectricPower));
    }

    [Fact]
    public void Gasoline_WithOnlyAirFlow_IsEstimated()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Gasoline, Pids(0x0D, 0x10)));
        Assert.Equal(EnergySupport.Estimated, Support(list, EnergyMetric.InstantFuel));
        Assert.Equal(EnergySupport.Estimated, Support(list, EnergyMetric.AverageFuel));
        Assert.Equal(EnergyReason.AirFlowEstimate, Reason(list, EnergyMetric.InstantFuel));
        Assert.Equal(EnergySupport.Unsupported, Support(list, EnergyMetric.FuelLevel));
    }

    [Fact]
    public void Diesel_WithOnlyAirFlow_HasInsufficientData_UntilALambdaExists()
    {
        var pids = Pids(0x0D, 0x10);
        var without = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Diesel, pids));
        Assert.Equal(EnergySupport.InsufficientData, Support(without, EnergyMetric.InstantFuel));
        Assert.Equal(EnergyReason.DieselNeedsLambda, Reason(without, EnergyMetric.InstantFuel));
        Assert.Equal(EnergySupport.InsufficientData, Support(without, EnergyMetric.AverageFuel));

        var with = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Diesel, pids, lambda: true));
        Assert.Equal(EnergySupport.Estimated, Support(with, EnergyMetric.InstantFuel));
    }

    [Fact]
    public void Diesel_WithTheFuelRatePid_IsDirectlySupported() =>
        Assert.Equal(EnergySupport.DirectlySupported,
            Support(EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Diesel, Pids(0x0D, 0x5E))), EnergyMetric.InstantFuel));

    [Theory]
    [InlineData(FuelKind.Unknown, EnergySupport.InsufficientData, EnergyReason.FuelTypeUnknown)]
    [InlineData(FuelKind.Mixed, EnergySupport.InsufficientData, EnergyReason.FuelTypeUnknown)]
    [InlineData(FuelKind.Cng, EnergySupport.Unsupported, EnergyReason.FuelTypeUnsupported)]
    public void AirFlowWithoutAUsableFuelType_IsNeverEstimated(FuelKind kind, EnergySupport support, EnergyReason reason)
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, kind, Pids(0x0D, 0x10)));
        Assert.Equal(support, Support(list, EnergyMetric.InstantFuel));
        Assert.Equal(reason, Reason(list, EnergyMetric.InstantFuel));
    }

    [Fact]
    public void NoFuelPidAtAll_IsUnsupported()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Gasoline, Pids(0x0D, 0x0C)));
        Assert.Equal(EnergySupport.Unsupported, Support(list, EnergyMetric.InstantFuel));
        Assert.Equal(EnergySupport.Unsupported, Support(list, EnergyMetric.AverageFuel));
        Assert.Equal(EnergySupport.Unsupported, Support(list, EnergyMetric.TripFuel));
    }

    [Fact]
    public void WithoutASpeedPid_TheAverageCannotBeCalculated()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Gasoline, Pids(0x5E)));
        Assert.Equal(EnergySupport.InsufficientData, Support(list, EnergyMetric.TripDistance));
        Assert.Equal(EnergySupport.InsufficientData, Support(list, EnergyMetric.AverageFuel));
        Assert.Equal(EnergyReason.SpeedMissing, Reason(list, EnergyMetric.AverageFuel));
        Assert.Equal(EnergySupport.DirectlySupported, Support(list, EnergyMetric.InstantFuel));
    }

    [Fact]
    public void ThePidListNotReadYet_IsReportedAsSuch_NotAsUnsupported()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Gasoline, null));
        Assert.Equal(EnergySupport.InsufficientData, Support(list, EnergyMetric.InstantFuel));
        Assert.Equal(EnergyReason.ListNotRead, Reason(list, EnergyMetric.InstantFuel));
        Assert.Equal(EnergyReason.ListNotRead, Reason(list, EnergyMetric.TripDistance));
    }

    [Fact]
    public void Bev_HasNoLiquidFuelMetrics_AndEnergyFromBatteryCurrent()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Electric, FuelKind.Electric, Pids(0x0D, 0x5B), current: true, soc: true));
        foreach (var metric in new[] { EnergyMetric.InstantFuel, EnergyMetric.AverageFuel, EnergyMetric.TripFuel, EnergyMetric.FuelLevel })
        {
            Assert.Equal(EnergySupport.Unsupported, Support(list, metric));
            Assert.Equal(EnergyReason.NotApplicableToThisPowertrain, Reason(list, metric));
        }

        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.ElectricEnergy));
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.ElectricPower));
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.Regeneration));
        Assert.Equal(EnergySupport.DirectlySupported, Support(list, EnergyMetric.StateOfCharge));
    }

    [Fact]
    public void Bev_WithOnlySoc_NeedsTheCapacity_ThenIsEstimated()
    {
        var pids = Pids(0x0D, 0x5B);
        var noCapacity = EnergyCompatibility.Evaluate(Facts(EnergyMode.Electric, FuelKind.Electric, pids, soc: true));
        Assert.Equal(EnergySupport.InsufficientData, Support(noCapacity, EnergyMetric.ElectricEnergy));
        Assert.Equal(EnergyReason.SocWithoutCapacity, Reason(noCapacity, EnergyMetric.ElectricEnergy));
        Assert.Equal(EnergySupport.Unsupported, Support(noCapacity, EnergyMetric.Regeneration));
        Assert.Equal(EnergySupport.Unsupported, Support(noCapacity, EnergyMetric.ElectricPower));

        var withCapacity = EnergyCompatibility.Evaluate(Facts(EnergyMode.Electric, FuelKind.Electric, pids, soc: true, capacity: true));
        Assert.Equal(EnergySupport.Estimated, Support(withCapacity, EnergyMetric.ElectricEnergy));
    }

    [Fact]
    public void Bev_WithNoBatteryDataAtAll_IsUnsupported()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Electric, FuelKind.Electric, Pids(0x0D)));
        Assert.Equal(EnergySupport.Unsupported, Support(list, EnergyMetric.ElectricEnergy));
        Assert.Equal(EnergySupport.Unsupported, Support(list, EnergyMetric.StateOfCharge));
    }

    [Fact]
    public void Phev_SeparatesFuelFromElectricity_AndMarksTheElectricShareAsDerived()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Hybrid, FuelKind.Gasoline, Pids(0x0D, 0x0C, 0x5E, 0x5B), current: true, soc: true));
        Assert.Equal(EnergySupport.DirectlySupported, Support(list, EnergyMetric.InstantFuel));
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.ElectricEnergy));
        Assert.Equal(EnergySupport.Estimated, Support(list, EnergyMetric.ElectricDriveShare));
        Assert.Equal(EnergyReason.DerivedFromRpm, Reason(list, EnergyMetric.ElectricDriveShare));
        Assert.Equal(EnergySupport.CalculatedFromAvailableData, Support(list, EnergyMetric.EngineState));
    }

    [Fact]
    public void Hybrid_WithoutRpm_HasNoEngineStateAndNoElectricShare()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Hybrid, FuelKind.Gasoline, Pids(0x0D, 0x5E)));
        Assert.Equal(EnergySupport.InsufficientData, Support(list, EnergyMetric.EngineState));
        Assert.Equal(EnergySupport.InsufficientData, Support(list, EnergyMetric.ElectricDriveShare));
    }

    [Fact]
    public void FuelOnlyCar_HasNoBatteryMetrics_AndNoEngineStateRow()
    {
        var list = EnergyCompatibility.Evaluate(Facts(EnergyMode.Fuel, FuelKind.Gasoline, Pids(0x0D, 0x5E)));
        foreach (var metric in new[] { EnergyMetric.StateOfCharge, EnergyMetric.ElectricPower, EnergyMetric.ElectricEnergy, EnergyMetric.Regeneration })
            Assert.Equal(EnergySupport.Unsupported, Support(list, metric));
        Assert.DoesNotContain(list, c => c.Metric == EnergyMetric.EngineState);
        Assert.DoesNotContain(list, c => c.Metric == EnergyMetric.ElectricDriveShare);
    }

    [Fact]
    public void Brand_AloneNeverMakesAnythingSupported()
    {
        // The evaluator has no notion of a brand: with no PIDs and no battery source nothing can be supported.
        foreach (var mode in Enum.GetValues<EnergyMode>())
        {
            var list = EnergyCompatibility.Evaluate(Facts(mode, FuelKind.Gasoline, Pids()));
            Assert.DoesNotContain(list, c => c.Support is EnergySupport.DirectlySupported or EnergySupport.CalculatedFromAvailableData or EnergySupport.Estimated);
        }
    }
}
