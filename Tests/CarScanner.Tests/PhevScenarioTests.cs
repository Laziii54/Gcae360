using CarScanner.Services.Energy;

namespace CarScanner.Tests;

/// <summary>A whole plug-in hybrid trip through the production classes, with the expected figures worked out by hand.</summary>
public sealed class PhevScenarioTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static (FuelTripEngine Engine, EvEnergyAccumulator Battery) DrivePhevTrip(bool batteryCurrentAvailable)
    {
        var engine = new FuelTripEngine { Kind = FuelKind.Gasoline };
        var battery = new EvEnergyAccumulator();
        battery.LockSign(1);

        // Phase 1 — 10 min, 60 km/h, engine stopped, battery supplies 400 V × 50 A = 20 kW
        // Phase 2 — 10 min, 100 km/h, engine running at 6 L/h, battery idle
        for (var second = 0; second <= 1200; second++)
        {
            var time = T0.AddSeconds(second);
            var electricPhase = second < 600;
            engine.Signals.Set(FuelSignal.SpeedKph, electricPhase ? 60 : 100, time);
            engine.Signals.Set(FuelSignal.Rpm, electricPhase ? 0 : 2200, time);
            engine.Signals.Set(FuelSignal.DirectFuelRateLph, electricPhase ? 0.0 : 6.0, time);
            engine.Tick(time);

            if (batteryCurrentAvailable)
                battery.Add(400, electricPhase ? 50 : 0, second == 0 ? 0 : 1, electricPhase ? 60 : 100, engineRunning: !electricPhase);
        }

        return (engine, battery);
    }

    [Fact]
    public void FuelAndElectricity_AreSeparatedAndEachDividedByTheRightDistance()
    {
        var (engine, battery) = DrivePhevTrip(batteryCurrentAvailable: true);
        var trip = engine.Snapshot();

        // distance: 60 km/h × 600 s = 10 km electric, 100 km/h × 600 s = 16.667 km with the engine → 26.667 km
        Assert.Equal(26.6667, trip.DistanceKm, 2);
        Assert.Equal(10.0, trip.EngineOffKm, 1);
        Assert.Equal(16.6667, trip.EngineOnKm, 1);

        // fuel: 6 L/h × 600 s = 1.0 L, all of it with the engine on
        Assert.Equal(1.0, trip.FuelLiters, 2);
        Assert.Equal(3.75, trip.AverageLPer100Km!.Value, 2);        // 1.0 L ÷ 26.667 km — the whole trip
        Assert.Equal(6.0, trip.EngineOnLPer100Km!.Value, 2);         // 1.0 L ÷ 16.667 km — engine kilometres only
        Assert.Equal(TripFuelQuality.Direct, trip.Quality);

        // electricity: 20 kW for the 599 one-second intervals that precede the 0 A sample at t=600 s
        // = 20 × 599 / 3600 = 3.32778 kWh, divided by the ~10 km driven electrically — not by 26.7 km
        var electric = ElectricTripCalculator.Compute(EnergyMode.Hybrid, trip, battery.HasSample, battery.ConsumedKWh, battery.RegeneratedKWh, 90, 75, 14);
        Assert.Equal(EnergyQuality.Measured, electric.Quality);
        Assert.Equal(20.0 * 599 / 3600, electric.NetKWh!.Value, 3);
        Assert.Equal(33.333, electric.KWhPer100Km!.Value, 1);
        Assert.Equal(ElectricBasis.EngineOffDistance, electric.Basis);
        Assert.True(electric.ChargeBalanceNotable);                  // SOC 90 → 75
    }

    [Fact]
    public void WithoutBatteryCurrent_ElectricityIsAnEstimate_OrMissing_NeverMeasured()
    {
        var (engine, battery) = DrivePhevTrip(batteryCurrentAvailable: false);
        var trip = engine.Snapshot();

        Assert.False(battery.HasSample);
        var estimated = ElectricTripCalculator.Compute(EnergyMode.Hybrid, trip, battery.HasSample, battery.ConsumedKWh, battery.RegeneratedKWh, 90, 70, 14);
        Assert.Equal(EnergyQuality.Estimated, estimated.Quality);     // 20 % of 14 kWh = 2.8 kWh
        Assert.Equal(2.8, estimated.NetKWh!.Value, 9);
        Assert.Equal(28.0, estimated.KWhPer100Km!.Value, 1);          // 2.8 kWh ÷ 10 km
        Assert.Null(estimated.RegeneratedKWh);

        var none = ElectricTripCalculator.Compute(EnergyMode.Hybrid, trip, battery.HasSample, 0, 0, 90, 70, null);
        Assert.Equal(EnergyQuality.Unavailable, none.Quality);
        Assert.Null(none.NetKWh);
        Assert.Null(none.KWhPer100Km);

        // The fuel side is unaffected by the missing battery data.
        Assert.Equal(1.0, trip.FuelLiters, 2);
    }

    [Fact]
    public void PureElectricStretch_ShowsNoFuel_AndEngineOffState()
    {
        var engine = new FuelTripEngine { Kind = FuelKind.Gasoline };
        for (var second = 0; second <= 300; second++)
        {
            var time = T0.AddSeconds(second);
            engine.Signals.Set(FuelSignal.SpeedKph, 50, time);
            engine.Signals.Set(FuelSignal.Rpm, 0, time);
            engine.Signals.Set(FuelSignal.DirectFuelRateLph, 0.0, time);
            engine.Tick(time);
        }

        var trip = engine.Snapshot();
        Assert.Equal(0, trip.FuelLiters);
        Assert.Null(trip.AverageLPer100Km);                   // no fuel: no L/100 km, rather than a reassuring 0.0
        Assert.Equal(0, trip.EngineOnKm);
        Assert.True(trip.EngineOffKm > 4);
        Assert.Equal(InstantFuelState.EngineOff, engine.Instant.State);
    }

    [Fact]
    public void BatteryElectricCar_NeverGetsALiquidFuelFigure()
    {
        var engine = new FuelTripEngine { Kind = FuelKind.Electric };
        for (var second = 0; second <= 120; second++)
        {
            var time = T0.AddSeconds(second);
            engine.Signals.Set(FuelSignal.SpeedKph, 80, time);
            engine.Signals.Set(FuelSignal.DirectFuelRateLph, 5.0, time);   // even a stray value must not become "fuel"
            engine.Tick(time);
        }

        var trip = engine.Snapshot();
        Assert.Equal(0, trip.FuelLiters);
        Assert.Equal(InstantFuelState.Unavailable, engine.Instant.State);
        Assert.Equal(FuelRateIssue.ElectricVehicle, engine.Instant.Issue);

        var electric = ElectricTripCalculator.Compute(EnergyMode.Electric, trip, true, 2.0, 0.4, 80, 78, 60);
        Assert.Equal(1.6, electric.NetKWh!.Value, 9);
        Assert.Equal(1.6 / trip.DistanceKm * 100, electric.KWhPer100Km!.Value, 6);
        Assert.Equal(0.4, electric.RegeneratedKWh);
    }

    [Fact]
    public void RegularHybrid_FuelPerTotalDistance_IsTheTripFigure_AndABatteryDriftIsFlagged()
    {
        var (engine, _) = DrivePhevTrip(batteryCurrentAvailable: false);
        var trip = engine.Snapshot();

        var balanced = ElectricTripCalculator.Compute(EnergyMode.Hybrid, trip, false, 0, 0, 55, 54, null);
        Assert.False(balanced.ChargeBalanceNotable);                    // charge-sustaining: fuel tells the story

        var drained = ElectricTripCalculator.Compute(EnergyMode.Hybrid, trip, false, 0, 0, 80, 60, null);
        Assert.True(drained.ChargeBalanceNotable);                      // the battery paid part of the trip
        Assert.Equal(-20, drained.SocChange);
    }
}
