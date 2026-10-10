using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Mileage;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

[CollectionDefinition("MileageTrace", DisableParallelization = true)]
public sealed class MileageTraceCollection;

/// <summary>
/// Drives the real OBDService, the real standard-PID providers and the real analysis against the software ELM327.
/// What this proves is that the code reads, decodes, rejects and compares correctly for the simulated replies — it
/// does NOT prove that any real vehicle answers this way.
/// </summary>
[Collection("MileageTrace")]
public sealed class MileageSimulatorTests
{
    private static VehicleInfo Car(string fuel = "Gasoline") =>
        new() { Manufacturer = "Volkswagen", Model = "Golf", Year = "2016", FuelType = fuel };

    private static async Task<(SimulatedElm327 Sim, OBDService Obd)> CreateAsync(Action<SimulatedElm327>? configure = null, bool a6Advertised = true)
    {
        var sim = new SimulatedElm327();
        // Supported-PID bitmaps: 0100 → 0120 → 0140 → 0160 → 0180 → 01A0 (A6 is bit 6 of that block).
        sim.ObdAnswers["0100"] = "41 00 BE 3F A8 13";
        sim.ObdAnswers["0120"] = "41 20 80 00 80 01";   // 21, 31 · next block
        sim.ObdAnswers["0140"] = "41 40 00 0C 00 01";   // 4D, 4E · next block
        sim.ObdAnswers["0160"] = "41 60 00 00 00 01";
        sim.ObdAnswers["0180"] = "41 80 00 00 00 01";
        sim.ObdAnswers["01A0"] = a6Advertised ? "41 A0 04 00 00 00" : "41 A0 00 00 00 00";
        sim.ObdAnswers["01A6"] = "41 A6 00 0F 42 40";   // 100 000.0 km
        sim.ObdAnswers["0121"] = "41 21 00 0A";          // 10 km with MIL on
        sim.ObdAnswers["0131"] = "41 31 00 0C";          // 12 km since codes cleared
        sim.ObdAnswers["014D"] = "41 4D 00 1E";          // 30 min
        sim.ObdAnswers["014E"] = "41 4E 00 3C";          // 60 min
        configure?.Invoke(sim);

        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        return (sim, obd);
    }

    private static bool WasSent(SimulatedElm327 sim, string command) =>
        sim.Sent.Any(c => c.Replace(" ", string.Empty).Equals(command, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ the odometer PID

    [Fact]
    public async Task AdvertisedOdometerPid_IsReadAndDecoded()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync();

        var evidence = Assert.Single(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));

        Assert.Equal(MileageMeasurement.TotalOdometer, evidence.Measurement);
        Assert.Equal(100_000.0, evidence.ValueKm, 6);
        Assert.Equal("km", evidence.Unit);
        Assert.Equal(MileageSourceKind.StandardObd, evidence.SourceKind);
        Assert.Equal("01 A6", evidence.Method);
        Assert.Equal(MileageReadStatus.Ok, evidence.Status);
        Assert.True(WasSent(sim, "01A6"));

        var attempt = Assert.Single(MileageTrace.AttemptsSnapshot());
        Assert.Equal(MileageAttemptOutcome.Read, attempt.Outcome);
        Assert.Equal(MileageSourceStatus.Supported, attempt.Compatibility);
    }

    [Fact]
    public async Task OdometerPid_NotAdvertised_IsNeverRequested()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync(a6Advertised: false);

        Assert.Empty(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));

        Assert.False(WasSent(sim, "01A6"), "an unadvertised PID must not be requested");
        var attempt = Assert.Single(MileageTrace.AttemptsSnapshot());
        Assert.Equal(MileageAttemptOutcome.NotTried, attempt.Outcome);
        Assert.Equal(MileageSourceStatus.Unsupported, attempt.Compatibility);
        Assert.Equal(MileageSourceReason.PidNotAdvertised, attempt.Reason);
    }

    [Fact]
    public async Task CarThatAdvertisesNothing_IsNotAskedForTheOdometer()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync(s => s.ObdAnswers["0100"] = "41 00 00 00 00 00");

        Assert.Empty(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));

        Assert.False(WasSent(sim, "01A6"));
        Assert.Equal(MileageSourceReason.AdvertisedListUnavailable, Assert.Single(MileageTrace.AttemptsSnapshot()).Reason);
    }

    [Theory]
    [InlineData("41 A6 00 0F", MileageAttemptOutcome.InvalidReading)]            // truncated
    [InlineData("41 A6 FF FF FF FF", MileageAttemptOutcome.InvalidReading)]      // "not available"
    [InlineData("41 0C 1A F8", MileageAttemptOutcome.InvalidReading)]            // a different PID's reply
    [InlineData("NO DATA", MileageAttemptOutcome.ReadingUnavailable)]
    [InlineData("7F 01 12", MileageAttemptOutcome.ReadingUnavailable)]
    public async Task BadOdometerReplies_ProduceNoReading_AndAreClassifiedHonestly(string reply, MileageAttemptOutcome expected)
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["01A6"] = reply);

        Assert.Empty(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));
        Assert.Equal(expected, Assert.Single(MileageTrace.AttemptsSnapshot()).Outcome);
    }

    [Fact]
    public async Task ImplausibleOdometer_IsReturnedAsFlagged_AndNeverBecomesAReading()
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["01A6"] = "41 A6 FF FF FF FE"); // 429 496 729.4 km

        var evidence = Assert.Single(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));
        Assert.Equal(MileageReadStatus.Implausible, evidence.Status);

        var result = new ActualMileageResult();
        result.Evidence.Add(evidence);
        result.ApplyAnalysis();
        Assert.Null(result.FinalMileageKm);
        Assert.Equal(MileageOutcome.NoReading, result.Analysis.Outcome);
        Assert.Single(result.Analysis.Flagged);
        Assert.Equal(MileageAttemptOutcome.InvalidReading, Assert.Single(MileageTrace.AttemptsSnapshot()).Outcome);
    }

    [Theory]
    [InlineData(SimFault.Timeout)]
    [InlineData(SimFault.Disconnect)]
    [InlineData(SimFault.Garbage)]
    [InlineData(SimFault.Truncated)]
    [InlineData(SimFault.NoData)]
    public async Task AdapterProblems_NeverCrashTheProvider_AndNeverInventAValue(SimFault fault)
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync();
        sim.Fault = fault;
        sim.FaultAfter = 0;

        var result = await new GenericObdMileageProvider(obd).ReadAllAsync(Car());

        Assert.Empty(result);
        var attempt = Assert.Single(MileageTrace.AttemptsSnapshot());
        Assert.NotEqual(MileageAttemptOutcome.Read, attempt.Outcome);
    }

    [Fact]
    public async Task NoisyButValidReply_StillGivesTheRealValue_NotAnotherOne()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync();
        sim.Fault = SimFault.Noise;
        sim.FaultAfter = 0;

        var result = await new GenericObdMileageProvider(obd).ReadAllAsync(Car());
        Assert.All(result, e => Assert.Equal(100_000.0, e.ValueKm, 6));
    }

    [Fact]
    public async Task RecoversAfterTheLinkComesBack()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync();
        sim.Fault = SimFault.Timeout;
        sim.FaultAfter = 0;
        Assert.Empty(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));

        sim.Fault = SimFault.None;
        MileageTrace.Reset();
        Assert.Single(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        var (_, obd) = await CreateAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GenericObdMileageProvider(obd).ReadAllAsync(Car(), cts.Token));
    }

    // ------------------------------------------------------------------ the other counters

    [Fact]
    public async Task ContextCounters_AreReadSeparately_AndLabelledAsWhatTheyAre()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync();

        var list = await new GenericObdDistanceContextProvider(obd).ReadAllAsync(Car());

        Assert.Equal(4, list.Count);
        Assert.DoesNotContain(list, e => e.Measurement == MileageMeasurement.TotalOdometer);
        var sinceClear = list.Single(e => e.Measurement == MileageMeasurement.DistanceSinceDtcClear);
        Assert.Equal(12, sinceClear.ValueKm);
        var milOn = list.Single(e => e.Measurement == MileageMeasurement.DistanceWithMilOn);
        Assert.Equal(10, milOn.ValueKm);
        var minutes = list.Where(e => e.Measurement == MileageMeasurement.MinutesSinceDiagnosticEvent).ToList();
        Assert.Equal(2, minutes.Count);
        Assert.All(minutes, e => { Assert.Equal("min", e.Unit); Assert.False(e.IsDistance); });
        Assert.False(WasSent(sim, "01A6"), "the context provider must not request the odometer");
    }

    [Fact]
    public async Task ContextCounters_NotAdvertised_AreNotRequested()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync(s =>
        {
            s.ObdAnswers["0120"] = "41 20 80 00 00 01"; // 21 only
            s.ObdAnswers["0140"] = "41 40 00 00 00 00"; // neither 4D nor 4E
        });

        var list = await new GenericObdDistanceContextProvider(obd).ReadAllAsync(Car());

        Assert.Equal(MileageMeasurement.DistanceWithMilOn, Assert.Single(list).Measurement);
        Assert.True(WasSent(sim, "0121"));
        Assert.False(WasSent(sim, "0131"));
        Assert.False(WasSent(sim, "014D"));
        Assert.False(WasSent(sim, "014E"));
    }

    [Fact]
    public async Task SaturatedCounter_IsExplained()
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["0131"] = "41 31 FF FF");
        var list = await new GenericObdDistanceContextProvider(obd).ReadAllAsync(Car());
        Assert.Contains("at least", list.Single(e => e.Measurement == MileageMeasurement.DistanceSinceDtcClear).Details);
    }

    [Fact]
    public async Task BrokenContextReplies_AreSkipped_WithoutAffectingTheOthers()
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync(s => s.ObdAnswers["0131"] = "41 31 00");
        var list = await new GenericObdDistanceContextProvider(obd).ReadAllAsync(Car());
        Assert.Equal(3, list.Count);
        Assert.DoesNotContain(list, e => e.Measurement == MileageMeasurement.DistanceSinceDtcClear);
    }

    // ------------------------------------------------------------------ the whole picture

    [Fact]
    public async Task ConventionalCar_OdometerAndContext_AreSeparatedInTheResult()
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync();
        var result = new ActualMileageResult();
        result.Evidence.AddRange(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));
        result.Evidence.AddRange(await new GenericObdDistanceContextProvider(obd).ReadAllAsync(Car()));
        result.ApplyAnalysis();

        Assert.Equal(MileageOutcome.SingleSource, result.Analysis.Outcome);
        Assert.Equal(100_000.0, result.FinalMileageKm!.Value, 6);
        Assert.False(result.HasMismatch);
        Assert.Equal(4, result.Analysis.Context.Count);
        Assert.Equal(1, result.SourceCount);
    }

    [Fact]
    public async Task OnlyContextCountersAvailable_IsNoOdometer_NotAMileage()
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync(a6Advertised: false);
        var result = new ActualMileageResult();
        result.Evidence.AddRange(await new GenericObdMileageProvider(obd).ReadAllAsync(Car("Electric")));
        result.Evidence.AddRange(await new GenericObdDistanceContextProvider(obd).ReadAllAsync(Car("Electric")));
        result.ApplyAnalysis();

        Assert.Null(result.FinalMileageKm);
        Assert.Equal(MileageOutcome.NoReading, result.Analysis.Outcome);
        Assert.Contains(result.Analysis.Context, e => e.Measurement == MileageMeasurement.DistanceSinceDtcClear);
    }

    [Fact]
    public async Task StandardOdometerAgainstAnotherModule_ConflictIsReportedWithoutChoosingASide()
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync();
        var result = new ActualMileageResult();
        result.Evidence.AddRange(await new GenericObdMileageProvider(obd).ReadAllAsync(Car()));
        result.Evidence.Add(MileageTestData.Odo("Instrument cluster", 163_000, "Test cluster", "22 F40D @ 714"));
        result.ApplyAnalysis();

        Assert.True(result.HasMismatch);
        Assert.Null(result.FinalMileageKm);
        var conflict = Assert.Single(result.Analysis.Conflicts);
        Assert.Equal(63_000, conflict.DifferenceKm, 6);
    }

    [Fact]
    public async Task Hybrid_And_Conventional_UseTheSameStandardPath_AndGetTheirOwnNote()
    {
        MileageTrace.Reset();
        var (_, obd) = await CreateAsync();
        var hybrid = Assert.Single(await new GenericObdMileageProvider(obd).ReadAllAsync(Car("Hybrid")));
        MileageTrace.Reset();
        var petrol = Assert.Single(await new GenericObdMileageProvider(obd).ReadAllAsync(Car("Gasoline")));
        Assert.Equal(petrol.ValueKm, hybrid.ValueKm);

        Assert.NotEqual(string.Empty, MileageWording.PowertrainNote(true, true));
        Assert.Equal(string.Empty, MileageWording.PowertrainNote(false, true));
        Assert.Contains("not converted into mileage", MileageWording.PowertrainNote(true, true));
    }

    [Fact]
    public async Task NothingIsEverWrittenToTheCar()
    {
        MileageTrace.Reset();
        var (sim, obd) = await CreateAsync();
        var before = sim.Sent.Count;

        await new GenericObdMileageProvider(obd).ReadAllAsync(Car());
        await new GenericObdDistanceContextProvider(obd).ReadAllAsync(Car());

        foreach (var command in sim.Sent.Skip(before))
        {
            var text = command.Replace(" ", string.Empty).ToUpperInvariant();
            Assert.True(text.StartsWith("01", StringComparison.Ordinal), $"unexpected command: {command}");
            Assert.True(text.Length == 4, $"unexpected command: {command}");
        }
    }

    [Fact]
    public void TraceReset_ClearsAttemptsAndLog()
    {
        MileageTrace.Add("x");
        MileageTrace.Attempt(new MileageAttempt("s", "m", MileageAttemptOutcome.Read, ""));
        MileageTrace.Reset();
        Assert.Empty(MileageTrace.AttemptsSnapshot());
        Assert.Equal(string.Empty, MileageTrace.Snapshot());
    }
}
