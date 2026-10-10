using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Energy;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Real protocol layer + real OBDService + the production signal mapping and trip engine, driven by the software ELM327.
/// This shows that replies are decoded and used correctly. It does not show that any real car answers this way.
/// </summary>
public sealed class FuelSimulatorTests
{
    // Supported-PID bitmaps: 0C, 0D, 10 · 24, 2F · 51, 5B, 5E
    private static async Task<(SimulatedElm327 Sim, OBDService Obd)> CreateAsync(Action<SimulatedElm327>? configure = null)
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["0100"] = "41 00 00 19 00 01";   // 0C 0D 10 · next block
        sim.ObdAnswers["0120"] = "41 20 10 02 00 01";   // 24 2F · next block
        sim.ObdAnswers["0140"] = "41 40 00 00 80 25";   // 51 5B 5E · next block
        sim.ObdAnswers["0160"] = "41 60 00 00 00 00";
        sim.ObdAnswers["010C"] = "41 0C 1F 40";          // 8000 / 4 = 2000 rpm
        sim.ObdAnswers["010D"] = "41 0D 3C";             // 60 km/h
        sim.ObdAnswers["0110"] = "41 10 03 E8";          // 1000 / 100 = 10.00 g/s
        sim.ObdAnswers["0124"] = "41 24 C0 00";          // 49152 · 2 / 65536 = 1.5
        sim.ObdAnswers["012F"] = "41 2F 80";             // 128 · 100 / 255 = 50.2 %
        sim.ObdAnswers["0151"] = "41 51 01";             // gasoline
        sim.ObdAnswers["015B"] = "41 5B 80";
        sim.ObdAnswers["015E"] = "41 5E 00 78";          // 120 / 20 = 6.0 L/h
        configure?.Invoke(sim);

        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        return (sim, obd);
    }

    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    // ---- decoding ------------------------------------------------------------

    [Fact]
    public async Task FuelPids_AreDecodedWithTheDocumentedFormulas()
    {
        var (_, obd) = await CreateAsync();
        var values = (await obd.ReadLiveValuesAsync(new[] { 0x5E, 0x10, 0x24, 0x2F, 0x51, 0x0D, 0x0C })).ToDictionary(v => v.Pid, v => v.Value);

        Assert.Equal("6", values[0x5E]);          // (256·0 + 120) / 20 L/h
        Assert.Equal("10", values[0x10]);         // (256·3 + 232) / 100 g/s
        Assert.Equal("1.5", values[0x24]);        // 49152 · 2 / 65536
        Assert.Equal("50.2", values[0x2F]);       // 128 · 100 / 255
        Assert.Equal("1", values[0x51]);
        Assert.Equal("60", values[0x0D]);
        Assert.Equal("2000", values[0x0C]);
    }

    [Fact]
    public async Task AdvertisedPids_DecideWhatCanBeRead()
    {
        var (sim, obd) = await CreateAsync(s => s.ObdAnswers["0140"] = "41 40 00 00 80 21"); // 51, 5B — but no 5E
        Assert.DoesNotContain(0x5E, obd.AdvertisedPids!);
        var values = await obd.ReadLiveValuesAsync(new[] { 0x5E, 0x10 });
        Assert.DoesNotContain(values, v => v.Pid == 0x5E);
        Assert.DoesNotContain(sim.Sent, c => c.Replace(" ", "").Equals("015E", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("41 5E 00")]          // truncated
    [InlineData("41 5E")]
    [InlineData("NO DATA")]
    [InlineData("41 0C 1F 40")]       // a different PID's answer
    [InlineData("garbage \u0001\u0002")]
    [InlineData("7F 01 12")]
    public async Task BadFuelRateReplies_ProduceNoValue(string reply)
    {
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["015E"] = reply);
        var values = await obd.ReadLiveValuesAsync(new[] { 0x5E });
        Assert.DoesNotContain(values, v => v.Pid == 0x5E);
    }

    [Fact]
    public async Task SaturatedFuelRate_IsDecoded_ButNeverAccepted()
    {
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["015E"] = "41 5E FF FF");   // 3 276.75 L/h
        var value = Assert.Single(await obd.ReadLiveValuesAsync(new[] { 0x5E }));
        Assert.Equal("3276.75", value.Value);

        var cache = new FuelSignalCache();
        FuelSignalMapper.Apply(cache, new[] { value }, T0);
        var rate = FuelRateResolver.Resolve(cache, T0, FuelKind.Gasoline);
        Assert.Null(rate.LitersPerHour);
        Assert.Equal(FuelRateIssue.Implausible, rate.Issue);
    }

    [Theory]
    [InlineData(SimFault.Timeout)]
    [InlineData(SimFault.Disconnect)]
    [InlineData(SimFault.Garbage)]
    [InlineData(SimFault.NoData)]
    public async Task AdapterProblems_GiveNoFuelValues_AndNoException(SimFault fault)
    {
        var (sim, obd) = await CreateAsync();
        sim.Fault = fault;
        sim.FaultAfter = 0;
        // A damaged or missing reply gives no value; a dead link surfaces as an I/O exception, which the live-data loop catches.
        IReadOnlyList<SensorValue> values = Array.Empty<SensorValue>();
        var error = await Record.ExceptionAsync(async () => values = await obd.ReadLiveValuesAsync(new[] { 0x5E, 0x10, 0x2F }));
        if (error is not null)
            Assert.True(error is IOException or TimeoutException or Elm327TimeoutException or Elm327CommandException or Elm327NotConnectedException, error.GetType().FullName);
        Assert.Empty(values);
    }

    // ---- the whole chain: ECU reply → signals → engine → trip ---------------------------

    [Fact]
    public async Task DirectFuelRate_FlowsToTheTrip()
    {
        var (_, obd) = await CreateAsync();
        var engine = new FuelTripEngine { Kind = FuelKind.Gasoline };

        for (var second = 0; second <= 360; second += 2)
        {
            var at = T0.AddSeconds(second);
            FuelSignalMapper.Apply(engine.Signals, await obd.ReadLiveValuesAsync(new[] { 0x5E, 0x2F }), at);
            FuelSignalMapper.Apply(engine.Signals, new LiveDataModel { Speed = 60, HasSpeed = true, Rpm = 2000, HasRpm = true }, at);
            engine.Tick(at);
        }

        var trip = engine.Snapshot();
        Assert.Equal(6.0, trip.DistanceKm, 6);               // 60 km/h × 0.1 h
        Assert.Equal(0.6, trip.FuelLiters, 6);                // 6 L/h × 0.1 h
        Assert.Equal(10.0, trip.AverageLPer100Km!.Value, 6);
        Assert.Equal(TripFuelQuality.Direct, trip.Quality);
        Assert.Equal(InstantFuelState.Driving, engine.Instant.State);
        Assert.Equal(10.0, engine.Instant.LitersPer100Km!.Value, 6);
    }

    [Fact]
    public async Task AirFlowOnly_Gasoline_IsAnEstimate_ThatTheEngineLabelsAsSuch()
    {
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["0140"] = "41 40 00 00 80 21");   // no 5E
        var engine = new FuelTripEngine { Kind = FuelKind.Gasoline };

        for (var second = 0; second <= 360; second += 2)
        {
            var at = T0.AddSeconds(second);
            FuelSignalMapper.Apply(engine.Signals, await obd.ReadLiveValuesAsync(new[] { 0x10 }), at);
            FuelSignalMapper.Apply(engine.Signals, new LiveDataModel { Speed = 60, HasSpeed = true, Rpm = 2000, HasRpm = true }, at);
            engine.Tick(at);
        }

        var trip = engine.Snapshot();
        Assert.Equal(TripFuelQuality.Estimated, trip.Quality);
        Assert.Equal(3.28722 * 0.1, trip.FuelLiters, 4);      // 10 g/s → 3.28722 L/h for 0.1 h
        Assert.True(engine.Instant.IsEstimate);
    }

    [Fact]
    public async Task Diesel_AirFlowWithMeasuredLambda_IsEstimated_WithoutItIsRefused()
    {
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["0140"] = "41 40 00 00 80 21");
        var withLambda = new FuelTripEngine { Kind = FuelKind.Diesel };
        var withoutLambda = new FuelTripEngine { Kind = FuelKind.Diesel };

        for (var second = 0; second <= 60; second += 2)
        {
            var at = T0.AddSeconds(second);
            var sensors = await obd.ReadLiveValuesAsync(new[] { 0x10, 0x24 });
            FuelSignalMapper.Apply(withLambda.Signals, sensors, at);
            FuelSignalMapper.Apply(withoutLambda.Signals, sensors.Where(v => v.Pid != 0x24), at);
            foreach (var engine in new[] { withLambda, withoutLambda })
            {
                FuelSignalMapper.Apply(engine.Signals, new LiveDataModel { Speed = 80, HasSpeed = true, Rpm = 1800, HasRpm = true }, at);
                engine.Tick(at);
            }
        }

        Assert.Equal(TripFuelQuality.Estimated, withLambda.Snapshot().Quality);
        // 10 g/s ÷ (14.5 × 1.5) ÷ 835 × 3600 = 1.98224 L/h
        Assert.Equal(1.98224, withLambda.Instant.LitersPerHour!.Value, 3);
        Assert.Equal(TripFuelQuality.None, withoutLambda.Snapshot().Quality);
        Assert.Equal(FuelRateIssue.DieselNeedsLambda, withoutLambda.Instant.Issue);
    }

    [Fact]
    public async Task ConnectionLoss_StopsTheFuelFlow_InsteadOfRepeatingTheLastValue()
    {
        var (sim, obd) = await CreateAsync();
        var engine = new FuelTripEngine { Kind = FuelKind.Gasoline };

        for (var second = 0; second <= 20; second += 2)
        {
            var at = T0.AddSeconds(second);
            FuelSignalMapper.Apply(engine.Signals, await obd.ReadLiveValuesAsync(new[] { 0x5E }), at);
            FuelSignalMapper.Apply(engine.Signals, new LiveDataModel { Speed = 60, HasSpeed = true, Rpm = 2000, HasRpm = true }, at);
            engine.Tick(at);
        }

        var before = engine.Snapshot();
        sim.Drop();                                     // the adapter disappears
        for (var second = 22; second <= 120; second += 2)
        {
            var at = T0.AddSeconds(second);
            var failure = await Record.ExceptionAsync(() => obd.ReadLiveValuesAsync(new[] { 0x5E }));
            Assert.NotNull(failure);   // the dead link is reported, never answered with the last value
            engine.Tick(at);                            // time keeps passing, no new readings arrive
        }

        var after = engine.Snapshot();
        // At most the few seconds that were still fresh when the link dropped can be added — never 100 s at 6 L/h.
        Assert.True(after.FuelLiters - before.FuelLiters < 6.0 * 8 / 3600 + 1e-9, $"{after.FuelLiters - before.FuelLiters}");
        Assert.True(after.DistanceKm - before.DistanceKm < 60.0 * 8 / 3600 + 1e-9);
        Assert.Equal(InstantFuelState.Unavailable, engine.Instant.State);
        Assert.True(after.MissingFuelSeconds > 60);   // the silent time is counted as missing fuel data
    }
}

public sealed class FuelSignalMapperTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OnlyTheDocumentedPidsFeedTheCalculation()
    {
        var cache = new FuelSignalCache();
        foreach (var pid in new[] { 0x0D, 0x0C, 0x11, 0x04, 0x5E, 0x10, 0x24, 0x34, 0x2F })
            Assert.True(FuelSignalMapper.TryApplyPid(cache, pid, 1, T0), $"PID {pid:X2}");
        foreach (var pid in new[] { 0x05, 0x06, 0x42, 0x51, 0x9A, 0xA6 })
            Assert.False(FuelSignalMapper.TryApplyPid(cache, pid, 1, T0), $"PID {pid:X2}");
    }

    [Fact]
    public void LiveDataModel_OnlyCopiesWhatTheEcuAnswered()
    {
        var cache = new FuelSignalCache();
        FuelSignalMapper.Apply(cache, new LiveDataModel { Speed = 55, HasSpeed = true, Rpm = 1500 /* HasRpm false */ }, T0);
        Assert.Equal(55, cache.Get(FuelSignal.SpeedKph, T0, TimeSpan.FromSeconds(1)));
        Assert.Null(cache.Get(FuelSignal.Rpm, T0, TimeSpan.FromSeconds(1)));
        Assert.False(cache.HasEverReceived(FuelSignal.Rpm));
    }

    [Fact]
    public void UnparsableOrNonNumericValues_AreIgnored()
    {
        var cache = new FuelSignalCache();
        FuelSignalMapper.Apply(cache, new[]
        {
            new SensorValue { Pid = 0x5E, Value = "abc" },
            new SensorValue { Pid = 0x10, Value = "" },
            new SensorValue { Pid = 0x0D, Value = "NaN" }
        }, T0);
        Assert.False(cache.HasEverReceived(FuelSignal.DirectFuelRateLph));
        Assert.False(cache.HasEverReceived(FuelSignal.MafGramsPerSecond));
        Assert.False(cache.HasEverReceived(FuelSignal.SpeedKph));
    }

    [Fact]
    public void AnalyticsPids_PreferTheDirectRate_AndAddLambdaOnlyWhereItIsUseful()
    {
        Assert.Equal(new[] { 0x5E, 0x2F, 0x51 }, FuelSignalMapper.AnalyticsPids(true, true, true));
        Assert.Equal(new[] { 0x10, 0x2F, 0x51 }, FuelSignalMapper.AnalyticsPids(false, true, false));
        Assert.Equal(new[] { 0x10, 0x24, 0x2F, 0x51 }, FuelSignalMapper.AnalyticsPids(false, true, true));
        Assert.Equal(new[] { 0x10, 0x2F, 0x51 }, FuelSignalMapper.AnalyticsPids(false, false, true));   // no λ sensor to read
    }
}
