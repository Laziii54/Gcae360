using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stage 0: what the connection log now says about initialization, setup commands, physical reads, timeouts and module
/// sessions — enough to tell a dead link from an adapter that refused something, from a car that stayed silent. The real
/// <see cref="Elm327Protocol"/> runs against a software adapter; every frame is a made-up or previously captured fixture.
/// SIMULATION ONLY: this proves what the log says for adapters that behave like these, nothing about a real car or adapter.
/// </summary>
public sealed class AdapterObservabilityTests
{
    private static readonly ServiceDiagnosticAddress VoltEcm = new(0x7E0, 0x7E8, false, null, "11-bit CAN", "module 7E0");
    private static readonly byte[] OilLifeRequest = { 0x22, 0x11, 0x9F };

    private static SimulatedElm327 VoltLikeAdapter()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        return sim;
    }

    // A v1.0..v1.2 style chip, as in AdapterCloneCompatibilityTests: no ATS, ATD, ATCRA, ATAR or flow-control switch.
    private static void OlderMicrocode(SimulatedElm327 sim)
    {
        foreach (var command in new[] { "ATS1", "ATS0", "ATD0", "ATD1", "ATCFC1", "ATCRA", "ATAR" })
            sim.RejectedAt.Add(command);
    }

    // ------------------------------------------------------------------ initialization

    [Fact]
    public async Task Initialization_LeavesTheBanner_EachInitCommandsVerdict_TheProbeAndTheProtocol()
    {
        using var log = new LogScope();

        var rig = await AdapterRigs.CreateAsync(VoltLikeAdapter());

        Assert.True(rig.Obd.IsInitialized);
        log.AssertInOrder(
            "Init ATZ: banner \"ELM327 v1.5\" over",
            "Init ATE0: accepted (reply \"OK\", Accepted)",
            "Init ATL0: accepted",
            "Init ATS1: accepted",
            "Init ATH0: accepted",
            "Init ATSP0: accepted",
            "Init ATAT2: accepted",
            "Init 0100 probe: Data",
            "Protocol: ATDPN \"A6\", ATDP \"AUTO, ISO 15765-4 (CAN 11/500)\" -> ISO_15765_4_CAN_11BIT_500K");
        Assert.Matches(new Regex(@"Init ATE0: accepted .*, \d+ ms"), log.Find("Init ATE0")!);   // how long the adapter took
    }

    [Fact]
    public async Task ACommandTheAdapterRefuses_IsLoggedAsNotAccepted_AndInitializationGoesOn()
    {
        var sim = VoltLikeAdapter();
        sim.RejectedAt.Add("ATS1");
        sim.RejectedAt.Add("ATAT2");
        using var log = new LogScope();

        var rig = await AdapterRigs.CreateAsync(sim);

        Assert.True(rig.Obd.IsInitialized);
        Assert.Equal("Init ATS1: NOT accepted (reply \"?\", AdapterRejected [adapter])",
            Regex.Replace(log.Find("Init ATS1")!, @"^\S+\s+|, \d+ ms$", string.Empty));   // without the time stamp and the duration
        Assert.NotNull(log.Find("Init ATAT2: NOT accepted"));
        Assert.NotNull(log.Find("Init ATE0: accepted"));             // the rest was not affected
    }

    [Fact]
    public async Task AnAdapterThatEchoesAfterAte0_IsRecognisedInTheInitLines_AndInitializationStillCompletes()
    {
        using var log = new LogScope();

        var rig = await AdapterRigs.CreateAsync(new EchoingAdapter(VoltLikeAdapter()));

        Assert.True(rig.Obd.IsInitialized);
        Assert.NotNull(log.Find("Init ATE0: accepted (reply \"ATE0 OK\", Accepted)"));    // echoed while echo was still legitimately on
        Assert.NotNull(log.Find("Init ATL0: accepted (reply \"ATL0 OK\", Echoed [adapter])"));   // the finding: echo is still on
        Assert.NotNull(log.Find("Init ATH0: accepted (reply \"ATH0 OK\", Echoed [adapter])"));
    }

    [Fact]
    public async Task TheAutomaticProtocolSearchFailing_LeavesTheExplicitAttemptsInTheLog()
    {
        var sim = VoltLikeAdapter();
        var scripted = new ScriptedAdapter(sim);
        // Automatic search: no data. ATSP6: no data. ATSP8: answers.
        scripted.Reply("0100", "NO DATA\r\r>", "NO DATA\r\r>", "41 00 BE 3F A8 13\r\r>");
        using var log = new LogScope();

        var rig = await AdapterRigs.CreateAsync(scripted);

        Assert.True(rig.Obd.IsInitialized);
        log.AssertInOrder(
            "Protocol search: ATSP6 -> 0100 NoData [ecu]",
            "Protocol search: ATSP8 -> 0100 Data",
            "Init 0100 probe: Data");
    }

    [Fact]
    public async Task AnAdapterThatStopsAnsweringDuringInitialization_IsLoggedAtTheStepItStoppedOn()
    {
        var scripted = new ScriptedAdapter(VoltLikeAdapter());
        scripted.Fail("ATE0", () => new TimeoutException("simulated: no reply"));
        var elm = new Elm327Protocol(scripted, Microsoft.Extensions.Logging.Abstractions.NullLogger<Elm327Protocol>.Instance);
        await scripted.ConnectAsync(new CarScanner.Models.BleDeviceItem());
        using var log = new LogScope();

        var failure = await Record.ExceptionAsync(() => elm.InitializeAsync());

        Assert.IsType<Elm327InitializationException>(failure);
        log.AssertInOrder(
            "Init ATZ: banner",
            "Timeout: ATE0",
            "Init ATE0: no reply in time");
        Assert.Contains("initialization stops here", log.Find("Init ATE0: no reply in time")!);
        Assert.Null(log.Find("Init ATL0"));                              // nothing after the step it stopped on
        Assert.Equal(ExchangeLayer.Transport, AdapterExchangeClassifier.Classify(failure!.InnerException!).Layer);
    }

    // ------------------------------------------------------------------ a physically addressed read

    [Fact]
    public async Task APhysicalRead_LeavesOneLineForTheAnswerAndOneForTheSetup()
    {
        var rig = await AdapterRigs.CreateAsync(VoltLikeAdapter());
        using var log = new LogScope();

        var raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(VoltEcm, OilLifeRequest, TimeSpan.FromSeconds(2));

        Assert.Contains("62 11 9F 80", raw);
        log.AssertInOrder(
            "Read 22119F: tx 7E0, reply id 7E8; frames from 7E8x1; Data;",
            "Read setup for tx 7E0: setup: all accepted; filter: ATCRA");
        Assert.DoesNotContain("9F 80", log.Text);                       // the value itself is not logged
        Assert.Matches(new Regex(@"Read 22119F: .*; \d+ ms"), log.Find("Read 22119F")!);
    }

    [Fact]
    public async Task WhenOnlyAnotherModuleAnswered_AFilterlessAdapterStillReturnsNoData_ButTheLogNowNamesWhoAnswered()
    {
        var sim = VoltLikeAdapter();
        sim.ModuleAnswers["7E0|22119F"] = "7E9 04 62 11 9F 11";          // the transmission answered, not the engine
        var rig = await AdapterRigs.CreateAsync(sim, () => OlderMicrocode(sim));
        using var log = new LogScope();

        var raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(VoltEcm, OilLifeRequest, TimeSpan.FromSeconds(2));

        Assert.Equal("NO DATA", raw);                                    // unchanged behaviour: the caller still sees silence
        var line = log.Find("Read 22119F")!;
        Assert.Contains("frames from 7E9x1", line);                      // ...but the log shows frames did arrive, from another module
        Assert.Contains("ForeignReply [ecu]", line);
        Assert.Contains("automatic receive address (frames also filtered in the app)", log.Find("Read setup")!);
    }

    [Fact]
    public async Task ARequiredSetupCommandTheAdapterRefuses_IsLoggedWithItsReply_BeforeTheReadFails()
    {
        var sim = VoltLikeAdapter();
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.RejectedAt.Add("ATH1");
        using var log = new LogScope();

        var failure = await Assert.ThrowsAsync<Elm327CommandException>(() =>
            rig.Obd.ReadPhysicalCanReadOnlyAsync(VoltEcm, OilLifeRequest, TimeSpan.FromSeconds(2)));

        Assert.Equal("ATH1", failure.UnsupportedCommand);
        Assert.NotNull(log.Find("Setup: required command ATH1 NOT accepted (reply \"?\", AdapterRejected [adapter])"));
        Assert.Null(log.Find("Read 22119F"));                            // no request went out, so no answer line
        Assert.NotNull(log.Find("Read setup for tx 7E0"));               // the setup is still reported
    }

    [Fact]
    public async Task ARequestWithNoReplyInTime_LeavesTheBudgetTheAttemptsAndTheTimeSpent()
    {
        var sim = VoltLikeAdapter();
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.Fault = SimFault.Timeout;
        using var log = new LogScope();

        var failure = await Record.ExceptionAsync(() => rig.Elm.SendCommandAsync("010C", TimeSpan.FromMilliseconds(100), 2, true));

        Assert.IsType<Elm327TimeoutException>(failure);
        Assert.Equal(1, rig.Elm.FailureStreak);
        Assert.Matches(new Regex(@"Timeout: 010C \(streak 1\); no reply within 0\.1 s, 2 attempt\(s\), \d+ ms spent"), log.Find("Timeout: 010C")!);
        Assert.Equal(AdapterFailureKind.NoReplyInTime, AdapterReadFailure.Classify(failure!).Kind);
        Assert.Equal(ExchangeLayer.Transport, AdapterExchangeClassifier.Classify(failure!).Layer);
    }

    [Fact]
    public async Task ACancelledRequest_IsNotLoggedAsATimeout_AndDoesNotCountAgainstTheAdapter()
    {
        var sim = VoltLikeAdapter();
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.Fault = SimFault.Timeout;
        using var log = new LogScope();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        var failure = await Record.ExceptionAsync(() => rig.Elm.SendCommandAsync("010C", TimeSpan.FromSeconds(5), 3, true, cancel.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Null(log.Find("Timeout: 010C"));
        Assert.Equal(0, rig.Elm.FailureStreak);
        Assert.Equal(ExchangeKind.Cancelled, AdapterExchangeClassifier.Classify(failure!).Kind);
    }

    // ------------------------------------------------------------------ a module session

    private static byte[] CellRequest(int did) => new byte[] { 0x22, (byte)(did >> 8), (byte)did };

    [Fact]
    public async Task AModuleSession_LeavesOneSummaryLine_WithWhatTheRequestsTurnedOutToBeAndWhoAnswered()
    {
        var sim = new SimulatedElm327();
        // Frames copied from the recorded Chevrolet Bolt/Volt BECM answers used in GmVoltBoltBatteryTests.
        sim.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";
        sim.ModuleAnswers["7E7|224182"] = "7EF 05 62 41 82 C9 36 ";
        var rig = await AdapterRigs.CreateAsync(sim);
        using var log = new LogScope();

        await rig.Obd.RunDtcModuleSessionAsync(async session =>
        {
            foreach (var did in new[] { 0x4181, 0x4182, 0x4183 })
                await session.SendAsync(0x7E7, 0x7EF, false, CellRequest(did), false, default);
        });

        var line = log.Find("Module session over")!;
        Assert.Matches(new Regex(@"Module session over .*: 3 send\(s\) in \d+ ms; 3 request\(s\): Data x2, NoData x1; frames from 7EF x2$"), line);
        Assert.DoesNotContain("CE D8", log.Text);
        Assert.DoesNotContain("CED8", log.Text);
    }

    [Fact]
    public async Task ASessionAnsweredByAnotherModule_ShowsTheForeignSender_EvenWhenTheAppFiltersItOut()
    {
        var sim = new SimulatedElm327();
        foreach (var command in new[] { "ATCRA", "ATAR", "ATCF" })       // no receive-filter command at all: the app filters by CAN id
            sim.RejectedAt.Add(command);
        sim.ModuleAnswers["7E7|224181"] = "7E8 06 62 DD 01 03 B5 8F";    // the engine ECU answered, not the battery module
        var rig = await AdapterRigs.CreateAsync(sim);
        using var log = new LogScope();
        string? answer = null;

        await rig.Obd.RunDtcModuleSessionAsync(async session =>
            answer = await session.SendAsync(0x7E7, 0x7EF, false, CellRequest(0x4181), false, default));

        Assert.Equal("NO DATA", answer);                                 // unchanged: the app-side filter still hides the foreign frame from the caller
        Assert.NotNull(log.Find("Module session: no receive-filter command accepted; frames are filtered by CAN id in the app"));
        var summary = log.Find("Module session over")!;
        Assert.Contains("frames from 7E8 x1", summary);                  // ...but the summary says who answered
        Assert.Contains("1 reply came only from other modules", summary);
    }

    [Fact]
    public async Task ASessionTheAdapterCannotEvenSetUp_SaysWhichRequiredCommandItRefused()
    {
        var sim = new SimulatedElm327();
        OlderMicrocode(sim);                                            // refuses ATS0
        sim.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";
        var rig = await AdapterRigs.CreateAsync(sim);
        using var log = new LogScope();

        var failure = await Assert.ThrowsAsync<Elm327CommandException>(() =>
            rig.Obd.RunDtcModuleSessionAsync(async session =>
                await session.SendAsync(0x7E7, 0x7EF, false, CellRequest(0x4181), false, default)));

        Assert.Equal("ATS0", failure.UnsupportedCommand);
        log.AssertInOrder(
            "Setup: required command ATS0 NOT accepted (reply \"?\", AdapterRejected [adapter])",
            "Module session over");
        Assert.Contains("0 send(s)", log.Find("Module session over")!);
        Assert.Equal(AdapterFailureKind.AdapterCommandUnsupported, AdapterReadFailure.Classify(failure).Kind);
    }

    [Fact]
    public async Task OptionalSessionSettingsTheAdapterRefuses_AreLoggedOnce_NotOncePerRequest()
    {
        var sim = new SimulatedElm327();
        sim.RejectedAt.Add("ATD0");
        sim.RejectedAt.Add("ATCFC1");
        sim.RejectedAt.Add("ATFCS");
        sim.RejectedAt.Add("ATST");
        sim.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";
        var rig = await AdapterRigs.CreateAsync(sim);
        using var log = new LogScope();

        await rig.Obd.RunDtcModuleSessionAsync(async session =>
        {
            for (var i = 0; i < 4; i++)
                await session.SendAsync(0x7E7, 0x7EF, false, CellRequest(0x4181), i == 3, default);   // the last one asks for the long wait
        });

        Assert.Single(log.Lines, line => line.Contains("Module session setup: ATD0 not accepted"));
        Assert.Single(log.Lines, line => line.Contains("Module session setup: ATCFC1 not accepted"));
        Assert.Single(log.Lines, line => line.Contains("user flow control"));
        Assert.Single(log.Lines, line => line.Contains("ATST64 not accepted"));
        Assert.Single(log.Lines, line => line.Contains("ATSTFF not accepted"));
    }

    // ------------------------------------------------------------------ the Adapter Center test

    [Fact]
    public async Task TheAdapterTest_PutsItsResultsInTheLog_SoACopiedLogCarriesThem()
    {
        var rig = await AdapterRigs.CreateAsync(VoltLikeAdapter());
        using var log = new LogScope();

        var snapshot = await rig.Elm.DetectAdapterCapabilitiesAsync();

        Assert.NotEmpty(snapshot.Checks);
        Assert.Matches(new Regex(@"Adapter test over .*: \d+/\d+ safe commands passed; 0100 latency min/avg/max \d+/\d+/\d+ ms"), log.Find("Adapter test over")!);
        foreach (var check in snapshot.Checks)
            Assert.NotNull(log.Find($"Adapter test: {check.Id} = {check.State}"));
    }

    // ------------------------------------------------------------------ nothing personal

    [Fact]
    public async Task NothingTheLogWritesContainsAVinOrADataValue_EvenWhenTheCarAnswersTheVinRequest()
    {
        var sim = VoltLikeAdapter();
        // A made-up VIN as the multi-frame Mode 09 answer an adapter test asks for.
        sim.ObdAnswers["0902"] = "49 02 01 31 48 47 43 4D 38 32 36 33 33 41 30 30 34 33 35 32";
        using var log = new LogScope();
        var rig = await AdapterRigs.CreateAsync(sim);

        await rig.Elm.DetectAdapterCapabilitiesAsync();
        await rig.Obd.ReadPhysicalCanReadOnlyAsync(VoltEcm, OilLifeRequest, TimeSpan.FromSeconds(2));

        Assert.DoesNotContain("1HGCM", log.Text);
        Assert.DoesNotContain("31 48 47", log.Text);
        Assert.DoesNotContain("314847", log.Text);
        Assert.DoesNotContain("62 11 9F 80", log.Text);
    }
}
