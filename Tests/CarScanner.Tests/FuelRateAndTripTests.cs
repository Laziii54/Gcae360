using CarScanner.Services.Energy;

namespace CarScanner.Tests;

public sealed class FuelRateResolverTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static FuelSignalCache Cache(Action<FuelSignalCache> fill)
    {
        var cache = new FuelSignalCache();
        fill(cache);
        return cache;
    }

    private static FuelRate Resolve(FuelSignalCache cache, FuelKind kind, double afterSeconds = 0) =>
        FuelRateResolver.Resolve(cache, T0.AddSeconds(afterSeconds), kind);

    // ---- the car's own fuel rate -----------------------------------------

    [Fact]
    public void DirectFuelRate_IsUsedAsReported()
    {
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.DirectFuelRateLph, 6.0, T0);
            c.Set(FuelSignal.Rpm, 2000, T0);
        }), FuelKind.Gasoline);

        Assert.Equal(6.0, rate.LitersPerHour!.Value);
        Assert.Equal(FuelDataQuality.Direct, rate.Quality);
        Assert.Contains("5E", rate.Source);
    }

    [Fact]
    public void DirectZero_IsARealZero_NotReplacedByTheAirFlowEstimate()
    {
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.DirectFuelRateLph, 0.0, T0);
            c.Set(FuelSignal.MafGramsPerSecond, 12.0, T0);
            c.Set(FuelSignal.Rpm, 2500, T0);
        }), FuelKind.Gasoline);

        Assert.Equal(0.0, rate.LitersPerHour!.Value);
        Assert.Equal(FuelDataQuality.Direct, rate.Quality);
    }

    [Fact]
    public void DirectRate_WinsOverAirFlow_WhenBothArrive()
    {
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.DirectFuelRateLph, 5.0, T0);
            c.Set(FuelSignal.MafGramsPerSecond, 30.0, T0);
        }), FuelKind.Gasoline);
        Assert.Equal(5.0, rate.LitersPerHour!.Value);
        Assert.Equal(FuelDataQuality.Direct, rate.Quality);
    }

    [Fact]
    public void ImplausibleDirectValue_IsRejected_AndNeverShown()
    {
        // 0xFFFF / 20 = 3 276.75 L/h
        var only = Resolve(Cache(c => c.Set(FuelSignal.DirectFuelRateLph, 3276.75, T0)), FuelKind.Gasoline);
        Assert.Null(only.LitersPerHour);
        Assert.Equal(FuelRateIssue.Implausible, only.Issue);

        var negative = Resolve(Cache(c => c.Set(FuelSignal.DirectFuelRateLph, -1, T0)), FuelKind.Gasoline);
        Assert.Null(negative.LitersPerHour);

        // With air flow available the estimate is used instead — and is labelled as one.
        var withMaf = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.DirectFuelRateLph, 3276.75, T0);
            c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0);
        }), FuelKind.Gasoline);
        Assert.Equal(FuelDataQuality.Estimated, withMaf.Quality);
    }

    // ---- air-flow estimates ---------------------------------------------

    [Fact]
    public void Gasoline_MafEstimate_MatchesTheHandCalculation()
    {
        // 10 g/s air ÷ 14.7 = 0.680272 g/s fuel ÷ 745 g/L = 0.000913118 L/s × 3600 = 3.28722 L/h
        var rate = Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0)), FuelKind.Gasoline);
        Assert.Equal(3.28722, rate.LitersPerHour!.Value, 4);
        Assert.Equal(FuelDataQuality.Estimated, rate.Quality);
        Assert.Contains("AFR 14.7", rate.Assumption);
        Assert.Contains("air flow", rate.Source);
    }

    [Fact]
    public void Ethanol_And_Lpg_UseTheirOwnConstants()
    {
        // ethanol: 10 / 9.0 / 789 × 3600 = 5.06971 L/h
        Assert.Equal(5.06971, Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0)), FuelKind.Ethanol).LitersPerHour!.Value, 4);
        // LPG: 10 / 15.5 / 550 × 3600 = 4.22287 L/h
        Assert.Equal(4.22287, Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0)), FuelKind.Lpg).LitersPerHour!.Value, 4);
    }

    [Fact]
    public void Diesel_WithoutLambda_HasNoAirFlowEstimate()
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 20.0, T0)), FuelKind.Diesel);
        Assert.Null(rate.LitersPerHour);
        Assert.Equal(FuelRateIssue.DieselNeedsLambda, rate.Issue);
    }

    [Fact]
    public void Diesel_WithMeasuredLambda_UsesTheLeanRatio_NotTheGasolineOne()
    {
        // 20 g/s ÷ (14.5 × 1.5) = 0.919540 g/s ÷ 835 g/L × 3600 = 3.96448 L/h
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.MafGramsPerSecond, 20.0, T0);
            c.Set(FuelSignal.EquivalenceRatio, 1.5, T0);
        }), FuelKind.Diesel);
        Assert.Equal(3.96448, rate.LitersPerHour!.Value, 4);
        Assert.Equal(FuelDataQuality.Estimated, rate.Quality);
        Assert.Contains("λ 1.50", rate.Assumption);

        // The same air flow treated as a stoichiometric petrol would give 6.58 L/h — the old, wrong answer.
        var asGasoline = Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 20.0, T0)), FuelKind.Gasoline);
        Assert.Equal(6.5744, asGasoline.LitersPerHour!.Value, 3);
        Assert.True(asGasoline.LitersPerHour > rate.LitersPerHour * 1.6);
    }

    [Theory]
    [InlineData(2.0)]    // sensor range exhausted: the real λ is unknown
    [InlineData(1.99)]
    [InlineData(0.2)]
    [InlineData(0.0)]
    public void Diesel_LambdaOutsideTheUsableRange_IsRefused(double lambda)
    {
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.MafGramsPerSecond, 20.0, T0);
            c.Set(FuelSignal.EquivalenceRatio, lambda, T0);
        }), FuelKind.Diesel);
        Assert.Null(rate.LitersPerHour);
        Assert.Equal(FuelRateIssue.DieselNeedsLambda, rate.Issue);
    }

    [Fact]
    public void Diesel_DirectRate_IsStillUsed()
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.DirectFuelRateLph, 4.2, T0)), FuelKind.Diesel);
        Assert.Equal(4.2, rate.LitersPerHour!.Value);
        Assert.Equal(FuelDataQuality.Direct, rate.Quality);
    }

    [Theory]
    [InlineData(FuelKind.Unknown, FuelRateIssue.FuelTypeUnknown)]
    [InlineData(FuelKind.Mixed, FuelRateIssue.FuelTypeUnknown)]
    [InlineData(FuelKind.Cng, FuelRateIssue.FuelTypeUnsupported)]
    [InlineData(FuelKind.Electric, FuelRateIssue.ElectricVehicle)]
    public void FuelsWithoutAnEstimate_NeverGetOne(FuelKind kind, FuelRateIssue issue)
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0)), kind);
        Assert.Null(rate.LitersPerHour);
        Assert.Equal(issue, rate.Issue);
    }

    [Fact]
    public void ElectricVehicle_HasNoLiquidFuel_EvenIfADirectValueArrives()
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.DirectFuelRateLph, 5.0, T0)), FuelKind.Electric);
        Assert.Null(rate.LitersPerHour);
        Assert.Equal(FuelRateIssue.ElectricVehicle, rate.Issue);
    }

    [Theory]
    [InlineData(-5d)]
    [InlineData(5000d)]
    public void ImplausibleAirFlow_IsRejected(double maf)
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, maf, T0)), FuelKind.Gasoline);
        Assert.Null(rate.LitersPerHour);
        Assert.Equal(FuelRateIssue.Implausible, rate.Issue);
    }

    [Fact]
    public void NonFiniteValues_AreIgnoredOnArrival()
    {
        var cache = Cache(c =>
        {
            c.Set(FuelSignal.MafGramsPerSecond, double.NaN, T0);
            c.Set(FuelSignal.DirectFuelRateLph, double.PositiveInfinity, T0);
        });
        Assert.False(cache.HasEverReceived(FuelSignal.MafGramsPerSecond));
        Assert.Equal(FuelRateIssue.NoFuelData, Resolve(cache, FuelKind.Gasoline).Issue);
    }

    // ---- freshness ---------------------------------------------------------

    [Fact]
    public void NothingReceived_IsNoFuelData() =>
        Assert.Equal(FuelRateIssue.NoFuelData, Resolve(new FuelSignalCache(), FuelKind.Gasoline).Issue);

    [Fact]
    public void OldReading_IsStale_AndNotUsed()
    {
        var cache = Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0));
        Assert.NotNull(Resolve(cache, FuelKind.Gasoline, 5.9).LitersPerHour);
        var stale = Resolve(cache, FuelKind.Gasoline, 6.5);
        Assert.Null(stale.LitersPerHour);
        Assert.Equal(FuelRateIssue.Stale, stale.Issue);
    }

    [Fact]
    public void StaleDirectRate_FallsBackToFreshAirFlow_LabelledAsEstimate()
    {
        var cache = Cache(c =>
        {
            c.Set(FuelSignal.DirectFuelRateLph, 5.0, T0);
            c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0.AddSeconds(9));
        });
        var rate = Resolve(cache, FuelKind.Gasoline, 10);
        Assert.Equal(FuelDataQuality.Estimated, rate.Quality);
    }

    [Fact]
    public void ReadingFromTheFuture_Or_OutOfOrder_IsNotUsed()
    {
        var cache = Cache(c =>
        {
            c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0.AddSeconds(5));
            c.Set(FuelSignal.MafGramsPerSecond, 99.0, T0); // older: ignored
        });
        Assert.Equal(3.28722, Resolve(cache, FuelKind.Gasoline, 5).LitersPerHour!.Value, 4);
        Assert.Null(Resolve(cache, FuelKind.Gasoline, 0).LitersPerHour); // asked before it arrived
    }

    // ---- engine state and fuel cut --------------------------------------

    [Fact]
    public void EngineStopped_IsZeroFlow_NotAnEstimate()
    {
        var rate = Resolve(Cache(c =>
        {
            c.Set(FuelSignal.Rpm, 0, T0);
            c.Set(FuelSignal.MafGramsPerSecond, 3.0, T0);
        }), FuelKind.Gasoline);
        Assert.Equal(0.0, rate.LitersPerHour!.Value);
        Assert.Equal(FuelRateIssue.EngineOff, rate.Issue);
    }

    [Fact]
    public void UnknownRpm_DoesNotMeanEngineOff()
    {
        var rate = Resolve(Cache(c => c.Set(FuelSignal.MafGramsPerSecond, 10.0, T0)), FuelKind.Gasoline);
        Assert.NotEqual(FuelRateIssue.EngineOff, rate.Issue);
    }

    private static FuelSignalCache Coasting(Action<FuelSignalCache>? change = null)
    {
        var cache = Cache(c =>
        {
            c.Set(FuelSignal.MafGramsPerSecond, 12.0, T0);
            c.Set(FuelSignal.ThrottlePercent, 0, T0);
            c.Set(FuelSignal.LoadPercent, 15, T0);
            c.Set(FuelSignal.SpeedKph, 60, T0);
            c.Set(FuelSignal.Rpm, 2500, T0);
        });
        change?.Invoke(cache);
        return cache;
    }

    [Fact]
    public void Coasting_OnAirFlow_IsTreatedAsFuelCut_AndSaysSo()
    {
        var rate = Resolve(Coasting(), FuelKind.Gasoline);
        Assert.True(rate.FuelCutAssumed);
        Assert.Equal(0.0, rate.LitersPerHour!.Value);
        Assert.Equal(FuelDataQuality.Estimated, rate.Quality);
        Assert.Contains("fuel cut", rate.Source);
    }

    [Theory]
    [InlineData(FuelSignal.ThrottlePercent, 5)]     // pedal not closed
    [InlineData(FuelSignal.LoadPercent, 60)]        // engine pulling
    [InlineData(FuelSignal.SpeedKph, 10)]           // too slow
    [InlineData(FuelSignal.Rpm, 900)]               // idle-like revs
    public void FuelCutNeedsEveryCondition(FuelSignal signal, double value)
    {
        var rate = Resolve(Coasting(c => c.Set(signal, value, T0.AddMilliseconds(1))), FuelKind.Gasoline, 0.001);
        Assert.False(rate.FuelCutAssumed);
        Assert.True(rate.LitersPerHour > 0);
    }

    [Fact]
    public void FuelCut_IsNotAssumed_WhenAReadingIsMissing()
    {
        var cache = Cache(c =>
        {
            c.Set(FuelSignal.MafGramsPerSecond, 12.0, T0);
            c.Set(FuelSignal.ThrottlePercent, 0, T0);
            c.Set(FuelSignal.SpeedKph, 60, T0);
            c.Set(FuelSignal.Rpm, 2500, T0);
        });
        Assert.False(Resolve(cache, FuelKind.Gasoline).FuelCutAssumed); // no engine load reading
    }

    [Fact]
    public void FuelCut_IsNeverInferred_ForDiesel_Or_WhenTheCarReportsItsOwnRate()
    {
        var diesel = Resolve(Coasting(c => c.Set(FuelSignal.EquivalenceRatio, 1.5, T0)), FuelKind.Diesel);
        Assert.False(diesel.FuelCutAssumed);

        var direct = Resolve(Coasting(c => c.Set(FuelSignal.DirectFuelRateLph, 0.4, T0)), FuelKind.Gasoline);
        Assert.False(direct.FuelCutAssumed);
        Assert.Equal(0.4, direct.LitersPerHour!.Value);
    }
}

public sealed class FuelTripEngineTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Feeds a steady condition once a second (ticks and readings at the same instants).</summary>
    private static DateTime Steady(
        FuelTripEngine engine, DateTime from, int seconds, double? speed, double? rpm, double? direct = null, double? maf = null,
        double? throttle = null, double? load = null, double? lambda = null, double step = 1)
    {
        var time = from;
        for (var elapsed = 0d; elapsed <= seconds + 1e-9; elapsed += step)
        {
            time = from.AddSeconds(elapsed);
            if (speed is { } s) engine.Signals.Set(FuelSignal.SpeedKph, s, time);
            if (rpm is { } r) engine.Signals.Set(FuelSignal.Rpm, r, time);
            if (direct is { } d) engine.Signals.Set(FuelSignal.DirectFuelRateLph, d, time);
            if (maf is { } m) engine.Signals.Set(FuelSignal.MafGramsPerSecond, m, time);
            if (throttle is { } t) engine.Signals.Set(FuelSignal.ThrottlePercent, t, time);
            if (load is { } l) engine.Signals.Set(FuelSignal.LoadPercent, l, time);
            if (lambda is { } x) engine.Signals.Set(FuelSignal.EquivalenceRatio, x, time);
            engine.Tick(time);
        }

        return time;
    }

    private static FuelTripEngine Engine(FuelKind kind = FuelKind.Gasoline) => new() { Kind = kind };

    // ---- trip totals ------------------------------------------------------

    [Fact]
    public void SteadyCruise_DirectFuelRate_GivesTheHandCalculatedAverage()
    {
        var engine = Engine();
        Steady(engine, T0, 600, speed: 60, rpm: 2000, direct: 6.0);
        var trip = engine.Snapshot();

        Assert.Equal(10.0, trip.DistanceKm, 6);            // 60 km/h × 600 s
        Assert.Equal(1.0, trip.FuelLiters, 6);              // 6 L/h × 600 s
        Assert.Equal(10.0, trip.AverageLPer100Km!.Value, 6);
        Assert.Equal(60.0, trip.AverageSpeedKph!.Value, 6);
        Assert.Equal(600, trip.MovingSeconds, 6);
        Assert.Equal(TripFuelQuality.Direct, trip.Quality);
        Assert.Equal(1.0, trip.FuelCoverage, 6);
        Assert.True(trip.HasReliableAverage);
        Assert.Equal(0, trip.EstimatedLiters);
    }

    [Fact]
    public void SteadyCruise_AirFlowEstimate_IsKeptApartAsEstimated()
    {
        var engine = Engine(FuelKind.Gasoline);
        Steady(engine, T0, 600, speed: 60, rpm: 2000, maf: 10.0);
        var trip = engine.Snapshot();

        // 3.28722 L/h × 600 s = 0.547870 L over 10 km
        Assert.Equal(0.54787, trip.FuelLiters, 4);
        Assert.Equal(5.4787, trip.AverageLPer100Km!.Value, 3);
        Assert.Equal(TripFuelQuality.Estimated, trip.Quality);
        Assert.Equal(0, trip.DirectLiters);
        Assert.Equal(trip.FuelLiters, trip.EstimatedLiters, 9);
        Assert.True(engine.Instant.IsEstimate);
    }

    [Fact]
    public void DieselOnAirFlowOnly_HasNoFuelAndNoAverage()
    {
        var engine = Engine(FuelKind.Diesel);
        Steady(engine, T0, 300, speed: 80, rpm: 1800, maf: 25.0);
        var trip = engine.Snapshot();

        Assert.True(trip.DistanceKm > 6);
        Assert.Equal(0, trip.FuelLiters);
        Assert.Null(trip.AverageLPer100Km);
        Assert.Equal(TripFuelQuality.None, trip.Quality);
        Assert.Equal(InstantFuelState.Unavailable, engine.Instant.State);
        Assert.Equal(FuelRateIssue.DieselNeedsLambda, engine.Instant.Issue);
        Assert.True(trip.MissingFuelSeconds > 250);
    }

    [Fact]
    public void LinearAcceleration_DistanceIsTheExactTrapezoid()
    {
        var engine = Engine();
        for (var second = 0; second <= 30; second++)
        {
            var time = T0.AddSeconds(second);
            engine.Signals.Set(FuelSignal.SpeedKph, second * 2.0, time);   // 0 → 60 km/h in 30 s
            engine.Signals.Set(FuelSignal.Rpm, 2500, time);
            engine.Signals.Set(FuelSignal.DirectFuelRateLph, 12.0, time);
            engine.Tick(time);
        }

        var trip = engine.Snapshot();
        Assert.Equal(0.25, trip.DistanceKm, 9);              // ½ · 60 km/h · 30 s = 900 km·s/h = 0.25 km
        Assert.Equal(12.0 * 30 / 3600, trip.FuelLiters, 9);
    }

    [Fact]
    public void Idling_UsesFuelButAddsNoDistance_AndNoLPer100Km()
    {
        var engine = Engine();
        Steady(engine, T0, 3600, speed: 0, rpm: 800, direct: 0.8, step: 5);
        var trip = engine.Snapshot();

        Assert.Equal(0, trip.DistanceKm);
        Assert.Equal(0.8, trip.FuelLiters, 6);
        Assert.Equal(3600, trip.IdleSeconds, 6);
        Assert.Null(trip.AverageLPer100Km);                  // no distance: never "infinity"
        Assert.Null(trip.AverageSpeedKph);
        Assert.Equal(InstantFuelState.Idle, engine.Instant.State);
        Assert.Null(engine.Instant.LitersPer100Km);
        Assert.Equal(0.8, engine.Instant.LitersPerHour!.Value, 6);
    }

    [Fact]
    public void EngineOffAtStandstill_CountsNoFuelAndNoIdleTime()
    {
        var engine = Engine();
        Steady(engine, T0, 120, speed: 0, rpm: 0, direct: 0.0);
        var trip = engine.Snapshot();

        Assert.Equal(0, trip.FuelLiters);
        Assert.Equal(0, trip.IdleSeconds);
        Assert.Equal(120, trip.EngineOffSeconds, 6);
        Assert.Equal(InstantFuelState.EngineOff, engine.Instant.State);
        Assert.Null(engine.Instant.LitersPer100Km);
    }

    [Fact]
    public void GapLongerThanTheLimit_IsNotIntegrated()
    {
        var engine = Engine();
        Steady(engine, T0, 10, speed: 60, rpm: 2000, direct: 6.0);
        var before = engine.Snapshot();

        var resumed = T0.AddSeconds(10 + 20);
        engine.Signals.Set(FuelSignal.SpeedKph, 60, resumed);
        engine.Signals.Set(FuelSignal.Rpm, 2000, resumed);
        engine.Signals.Set(FuelSignal.DirectFuelRateLph, 6.0, resumed);
        engine.Tick(resumed);
        var after = engine.Snapshot();

        Assert.Equal(before.DistanceKm, after.DistanceKm, 12);
        Assert.Equal(before.FuelLiters, after.FuelLiters, 12);
        Assert.Equal(20, after.GapSeconds, 6);

        // …and the trip continues normally afterwards.
        Steady(engine, resumed, 10, speed: 60, rpm: 2000, direct: 6.0);
        Assert.True(engine.Snapshot().DistanceKm > after.DistanceKm + 0.1);
    }

    [Fact]
    public void SameOrEarlierTimestamp_IsInvalid_AndChangesNothing()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 10, speed: 60, rpm: 2000, direct: 6.0);
        var before = engine.Snapshot();

        engine.Tick(end);                    // same moment
        engine.Tick(end.AddSeconds(-3));     // clock went backwards
        var after = engine.Snapshot();

        Assert.Equal(2, after.InvalidTimestamps);
        Assert.Equal(before.DistanceKm, after.DistanceKm, 12);
        Assert.Equal(before.FuelLiters, after.FuelLiters, 12);
    }

    [Fact]
    public void StaleSpeed_AddsNoDistance_AndOnlyTheFlowIsShown()
    {
        var engine = Engine();
        // speed arrived once at T0 and never again; fuel keeps coming
        engine.Signals.Set(FuelSignal.SpeedKph, 80, T0);
        engine.Signals.Set(FuelSignal.Rpm, 2000, T0);
        engine.Tick(T0);
        for (var second = 1; second <= 20; second++)
        {
            var time = T0.AddSeconds(second);
            engine.Signals.Set(FuelSignal.Rpm, 2000, time);
            engine.Signals.Set(FuelSignal.DirectFuelRateLph, 7.0, time);
            engine.Tick(time);
        }

        var trip = engine.Snapshot();
        // Only the first ~5 s (while the speed was still fresh) can contribute distance.
        Assert.True(trip.DistanceKm < 80 * 6 / 3600d + 1e-9, $"distance {trip.DistanceKm}");
        Assert.Equal(InstantFuelState.FlowOnly, engine.Instant.State);
        Assert.Null(engine.Instant.LitersPer100Km);
        Assert.Equal(7.0, engine.Instant.LitersPerHour!.Value, 6);
    }

    [Fact]
    public void MissingFuel_KeepsDistance_ButGivesNoAverage()
    {
        var engine = Engine();
        Steady(engine, T0, 600, speed: 60, rpm: 2000);   // no fuel PID at all
        var trip = engine.Snapshot();

        Assert.Equal(10.0, trip.DistanceKm, 6);
        Assert.Equal(0, trip.FuelLiters);
        Assert.Null(trip.AverageLPer100Km);
        Assert.Equal(TripFuelQuality.None, trip.Quality);
        Assert.Equal(0, trip.FuelCoverage);
        Assert.False(trip.HasReliableAverage);
        Assert.Equal(FuelRateIssue.NoFuelData, engine.Instant.Issue);
    }

    [Fact]
    public void HalfOfTheTripWithoutFuel_IsFlaggedAsNotReliable()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 300, speed: 60, rpm: 2000, direct: 6.0);
        Steady(engine, end.AddSeconds(1), 300, speed: 60, rpm: 2000);          // fuel stops arriving
        var trip = engine.Snapshot();

        Assert.InRange(trip.FuelCoverage, 0.4, 0.6);
        Assert.NotNull(trip.AverageLPer100Km);        // there is a number…
        Assert.False(trip.HasReliableAverage);        // …but it is not presented as a trip average
        Assert.True(trip.MissingFuelSeconds > 100);
    }

    [Fact]
    public void ReceivedDirectThenEstimate_IsReportedAsMixed()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 200, speed: 60, rpm: 2000, direct: 6.0);
        // direct value goes stale; air flow takes over
        var later = end.AddSeconds(8);
        Steady(engine, later, 200, speed: 60, rpm: 2000, maf: 10.0);
        var trip = engine.Snapshot();

        Assert.Equal(TripFuelQuality.Mixed, trip.Quality);
        Assert.True(trip.DirectLiters > 0);
        Assert.True(trip.EstimatedLiters > 0);
    }

    [Fact]
    public void CityAndHighwaySplit_UsesTheIntervalSpeed()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 360, speed: 30, rpm: 1500, direct: 3.0);
        Steady(engine, end.AddSeconds(1), 360, speed: 90, rpm: 2500, direct: 9.0);
        var trip = engine.Snapshot();

        Assert.Equal(3.0, trip.CityKm, 1);           // 30 km/h × 360 s = 3 km
        Assert.Equal(9.0, trip.HighwayKm, 1);
        Assert.Equal(0.30, trip.CityLiters, 2);      // 3 L/h × 0.1 h
        Assert.Equal(0.90, trip.HighwayLiters, 2);
        Assert.Equal(10.0, trip.CityLiters / trip.CityKm * 100, 0);
        Assert.Equal(10.0, trip.HighwayLiters / trip.HighwayKm * 100, 0);
    }

    [Fact]
    public void MaximumAndAverageSpeed()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 100, speed: 50, rpm: 2000, direct: 5.0);
        Steady(engine, end.AddSeconds(1), 100, speed: 130, rpm: 3000, direct: 15.0);
        var trip = engine.Snapshot();
        Assert.Equal(130, trip.MaximumSpeedKph);
        Assert.InRange(trip.AverageSpeedKph!.Value, 88, 92);
    }

    [Fact]
    public void ImplausibleSpeed_IsIgnored()
    {
        var engine = Engine();
        Steady(engine, T0, 60, speed: 9999, rpm: 2000, direct: 5.0);
        var trip = engine.Snapshot();
        Assert.Equal(0, trip.DistanceKm);
        Assert.Equal(0, trip.MaximumSpeedKph);
    }

    [Fact]
    public void FuelCut_OnDirectZero_ShowsFuelCut_AndAddsNoFuel()
    {
        var engine = Engine();
        Steady(engine, T0, 60, speed: 80, rpm: 3000, direct: 0.0);
        Assert.Equal(InstantFuelState.FuelCut, engine.Instant.State);
        Assert.Equal(0, engine.Snapshot().FuelLiters);
        Assert.Equal(0, engine.Instant.LitersPer100Km!.Value, 9);
        Assert.Equal(FuelDataQuality.Direct, engine.Instant.Quality);
    }

    [Fact]
    public void FuelCut_OnAirFlow_IsInferred_AndLabelled()
    {
        var engine = Engine();
        Steady(engine, T0, 30, speed: 70, rpm: 2600, maf: 14.0, throttle: 0, load: 12);
        Assert.Equal(InstantFuelState.FuelCut, engine.Instant.State);
        Assert.Equal(FuelDataQuality.Estimated, engine.Instant.Quality);
        Assert.Equal(0, engine.Snapshot().FuelLiters);
        Assert.True(engine.Snapshot().FuelCutSeconds > 25);
    }

    // ---- instantaneous consumption -----------------------------------------

    [Theory]
    [InlineData(4.9, InstantFuelState.Idle)]
    [InlineData(5.0, InstantFuelState.Driving)]
    [InlineData(0.0, InstantFuelState.Idle)]
    public void LowSpeed_ShowsTheFlow_NotAnExplodingLPer100Km(double speed, InstantFuelState expected)
    {
        var engine = Engine();
        Steady(engine, T0, 5, speed: speed, rpm: 900, direct: 0.6);
        Assert.Equal(expected, engine.Instant.State);
        if (expected == InstantFuelState.Idle)
            Assert.Null(engine.Instant.LitersPer100Km);
        else
            Assert.Equal(0.6 / 5.0 * 100, engine.Instant.LitersPer100Km!.Value, 6);   // 12 L/100 km
    }

    [Fact]
    public void AbsurdLPer100Km_IsRejected()
    {
        var engine = Engine();
        Steady(engine, T0, 5, speed: 5, rpm: 3000, direct: 20.0);   // 400 L/100 km
        Assert.Equal(InstantFuelState.Unavailable, engine.Instant.State);
        Assert.Equal(FuelRateIssue.Implausible, engine.Instant.Issue);
        Assert.Null(engine.Instant.LitersPer100Km);
    }

    [Fact]
    public void CruisingInstant_EqualsFlowOverSpeed()
    {
        var engine = Engine();
        Steady(engine, T0, 30, speed: 100, rpm: 2500, direct: 7.0);
        Assert.Equal(InstantFuelState.Driving, engine.Instant.State);
        Assert.Equal(7.0, engine.Instant.LitersPer100Km!.Value, 6);
        Assert.Equal(7.0, engine.Instant.LitersPerHour!.Value, 6);
        Assert.Equal(FuelDataQuality.Direct, engine.Instant.Quality);
    }

    [Fact]
    public void Smoothing_FollowsAStep_WithoutJumpingOrLagging()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 20, speed: 100, rpm: 2500, direct: 5.0);
        Assert.Equal(5.0, engine.Instant.LitersPer100Km!.Value, 6);

        // the flow doubles
        var time = end;
        var values = new List<double>();
        for (var i = 1; i <= 15; i++)
        {
            time = end.AddSeconds(i);
            engine.Signals.Set(FuelSignal.SpeedKph, 100, time);
            engine.Signals.Set(FuelSignal.Rpm, 2500, time);
            engine.Signals.Set(FuelSignal.DirectFuelRateLph, 10.0, time);
            engine.Tick(time);
            values.Add(engine.Instant.LitersPer100Km!.Value);
        }

        Assert.InRange(values[0], 5.5, 7.0);                   // moved, but not all at once
        Assert.True(values.Zip(values.Skip(1), (a, b) => b >= a).All(x => x));   // monotonic rise
        Assert.InRange(values[^1], 9.7, 10.0);                 // caught up within ~15 s
    }

    [Fact]
    public void Smoothing_NeverChangesTheTripTotals()
    {
        var engine = Engine();
        var rng = new Random(7);
        double expectedLiters = 0;
        var previousRate = 0d;
        for (var second = 0; second <= 120; second++)
        {
            var time = T0.AddSeconds(second);
            var rate = 4 + rng.NextDouble() * 6;
            engine.Signals.Set(FuelSignal.SpeedKph, 60, time);
            engine.Signals.Set(FuelSignal.Rpm, 2000, time);
            engine.Signals.Set(FuelSignal.DirectFuelRateLph, rate, time);
            engine.Tick(time);
            if (second > 0)
                expectedLiters += (previousRate + rate) / 2 / 3600;
            previousRate = rate;
        }

        Assert.Equal(expectedLiters, engine.Snapshot().FuelLiters, 9);
    }

    [Fact]
    public void AfterAGap_SmoothingStartsOver()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 20, speed: 100, rpm: 2500, direct: 5.0);
        var later = end.AddSeconds(30);
        engine.Signals.Set(FuelSignal.SpeedKph, 100, later);
        engine.Signals.Set(FuelSignal.Rpm, 2500, later);
        engine.Signals.Set(FuelSignal.DirectFuelRateLph, 12.0, later);
        engine.Tick(later);
        Assert.Equal(12.0, engine.Instant.LitersPer100Km!.Value, 6);
    }

    // ---- hybrids ------------------------------------------------------------

    [Fact]
    public void Hybrid_EngineOffDistance_IsTrackedApart_AndFuelIsNotSpreadOverIt()
    {
        var engine = Engine(FuelKind.Gasoline);
        var end = Steady(engine, T0, 300, speed: 50, rpm: 0, direct: 0.0);                 // electric: engine stopped
        Steady(engine, end.AddSeconds(1), 300, speed: 50, rpm: 1800, direct: 5.0);          // engine running
        var trip = engine.Snapshot();

        Assert.Equal(4.1667, trip.EngineOffKm, 1);
        Assert.Equal(4.1667, trip.EngineOnKm, 1);
        Assert.Equal(5.0 * 300 / 3600, trip.FuelLiters, 2);                                 // 0.4167 L, burned only with the engine on
        // Two different statements about the same trip:
        Assert.Equal(5.0, trip.AverageLPer100Km!.Value, 1);        // fuel ÷ ALL distance (0.4167 L / 8.33 km)
        Assert.Equal(10.0, trip.EngineOnLPer100Km!.Value, 1);       // fuel ÷ distance with the engine on
        Assert.True(trip.EngineOnLPer100Km > trip.AverageLPer100Km);
    }

    [Fact]
    public void Hybrid_EngineStoppedWhileMoving_IsNotZeroEnergy_ItHasNoFuelButIsNotCountedAsIdle()
    {
        var engine = Engine();
        Steady(engine, T0, 120, speed: 40, rpm: 0, direct: 0.0);
        var trip = engine.Snapshot();
        Assert.Equal(0, trip.FuelLiters);
        Assert.Equal(0, trip.IdleSeconds);
        Assert.True(trip.EngineOffKm > 1);
        Assert.Equal(InstantFuelState.EngineOff, engine.Instant.State);
    }

    [Fact]
    public void UnknownEngineState_IsNotAssumedElectric()
    {
        var engine = Engine();
        Steady(engine, T0, 120, speed: 40, rpm: null, direct: 3.0);   // rpm never arrives
        var trip = engine.Snapshot();
        Assert.Equal(0, trip.EngineOffKm);
        Assert.Equal(0, trip.EngineOnKm);
        Assert.True(trip.UnknownEngineKm > 1);
    }

    // ---- reset and restore ----------------------------------------------------

    [Fact]
    public void Reset_ClearsEverything_ButKeepsTheFuelKind()
    {
        var engine = Engine(FuelKind.Diesel);
        Steady(engine, T0, 60, speed: 60, rpm: 2000, direct: 5.0);
        engine.Reset();

        var trip = engine.Snapshot();
        Assert.Equal(0, trip.DistanceKm);
        Assert.Equal(0, trip.FuelLiters);
        Assert.Equal(InstantFuelState.Unavailable, engine.Instant.State);
        Assert.Equal(FuelKind.Diesel, engine.Kind);
        Assert.Null(engine.StartedAt);
    }

    [Fact]
    public void Checkpoint_RoundTrips_AndRestoresAnInterruptedTrip()
    {
        var engine = Engine(FuelKind.Gasoline);
        var end = Steady(engine, T0, 300, speed: 60, rpm: 2000, direct: 6.0);
        var original = engine.Snapshot();

        var json = engine.ExportCheckpoint(end, "Fuel", null, null, false).ToJson();
        var parsed = FuelTripCheckpoint.TryParse(json);
        Assert.NotNull(parsed);
        Assert.True(parsed!.IsValid(end.AddMinutes(30)));

        var restored = Engine(FuelKind.Unknown);
        Assert.True(restored.TryRestore(parsed, end.AddMinutes(30)));
        var snapshot = restored.Snapshot();

        Assert.True(snapshot.IsInterrupted);
        Assert.Equal(original.DistanceKm, snapshot.DistanceKm, 9);
        Assert.Equal(original.FuelLiters, snapshot.FuelLiters, 9);
        Assert.Equal(original.AverageLPer100Km!.Value, snapshot.AverageLPer100Km!.Value, 9);
        Assert.Equal(FuelKind.Gasoline, restored.Kind);
        Assert.Equal(T0, restored.StartedAt);
    }

    [Fact]
    public void CheckpointKeepsEnergyValues()
    {
        var engine = Engine();
        var end = Steady(engine, T0, 60, speed: 50, rpm: 0, direct: 0.0);
        var checkpoint = FuelTripCheckpoint.TryParse(engine.ExportCheckpoint(end, "Electric", 1.25, 0.2, true).ToJson())!;
        Assert.Equal(1.25, checkpoint.EnergyNetKWh);
        Assert.Equal(0.2, checkpoint.EnergyRegeneratedKWh);
        Assert.True(checkpoint.EnergyIsEstimate);
        Assert.Equal("Electric", checkpoint.Mode);
    }

    private static FuelTripCheckpoint Good(DateTime now) =>
        new(1, now.AddMinutes(-20), now.AddMinutes(-5), 12.5, 0.8, 0.1, 900, 100, 0, 0, 0, 0, 1000, 990, 110, 4, 0.4, 8.5, 0.5, 6, 2, 4.5, "Gasoline", "Fuel", null, null, false);

    [Fact]
    public void ValidCheckpoint_IsAccepted() =>
        Assert.True(Good(T0).IsValid(T0));

    [Fact]
    public void InvalidCheckpoints_AreRefused()
    {
        var now = T0;
        var good = Good(now);
        Assert.False((good with { Version = 2 }).IsValid(now));
        Assert.False((good with { DistanceKm = -1 }).IsValid(now));
        Assert.False((good with { DirectLiters = double.NaN }).IsValid(now));
        Assert.False((good with { EstimatedLiters = double.PositiveInfinity }).IsValid(now));
        Assert.False((good with { SavedAt = good.StartedAt.AddMinutes(-1) }).IsValid(now));         // saved before it started
        Assert.False((good with { SavedAt = now.AddHours(2) }).IsValid(now));                       // from the future
        Assert.False((good with { StartedAt = default }).IsValid(now));
        Assert.False(good.IsValid(now.AddDays(2)));                                                  // too old
        Assert.False((good with { DistanceKm = 5000 }).IsValid(now));                                // impossible in 17 minutes
        Assert.False((good with { EnergyNetKWh = double.NaN }).IsValid(now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("42")]
    public void DamagedCheckpointFile_YieldsNothing(string? json) =>
        Assert.Null(FuelTripCheckpoint.TryParse(json));

    [Fact]
    public void RefusedCheckpoint_LeavesTheEngineUntouched()
    {
        var engine = Engine();
        Steady(engine, T0, 30, speed: 60, rpm: 2000, direct: 5.0);
        var before = engine.Snapshot();
        Assert.False(engine.TryRestore(Good(T0) with { DistanceKm = -4 }, T0));
        Assert.Equal(before.DistanceKm, engine.Snapshot().DistanceKm, 12);
        Assert.False(engine.Snapshot().IsInterrupted);
    }
}
