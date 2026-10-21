using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Mileage;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stage 1: adapter behaviours that differ between cheap and expensive ELM327 adapters, replayed against the real
/// <see cref="Elm327Protocol"/>. SIMULATION ONLY — each adapter here is a software model of a behaviour; nothing proves that
/// a particular real adapter does any of this, and nothing here ran against a car.
///
/// These tests describe what the code does TODAY. Where that exposes a limitation (an echo that reaches the caller, a module
/// session that cannot start on a chip without ATS0) the test says so in its comment and pins the current behaviour, so that
/// a later, deliberately approved change has to change the assertion on purpose. Nothing here fixes anything.
/// Frames are the ones other tests already use (Ford and Toyota captures, Volt/Bolt BECM answers) or plainly made up.
/// </summary>
public sealed class AdapterCompatibilityScenarioTests
{
    // From MileageAttributionTests (a real capture): "22 DD 01" addressed to 0x7E1 was answered by the engine ECU on 0x7E8.
    private const string FordDd01FromEngine = "7E8 06 62 DD 01 03 B5 8F";
    private static readonly byte[] Dd01Request = { 0x22, 0xDD, 0x01 };

    private static SimulatedElm327 EngineAnswersFunctionalRequests()
    {
        var sim = new SimulatedElm327();
        // What answers when a request goes out on the functional header 7DF: the engine ECU. Nothing answers on 7E1 itself.
        sim.ModuleAnswers["7DF|22DD01"] = FordDd01FromEngine;
        return sim;
    }

    private static void NoReceiveFilterCommands(SimulatedElm327 sim)
    {
        foreach (var command in new[] { "ATCRA", "ATAR", "ATCF" })
            sim.RejectedAt.Add(command);
    }

    // =========================================================================================
    // A. The adapter says OK to ATSH but goes on transmitting on its default header
    // =========================================================================================

    [Fact]
    public async Task HeaderIgnored_ReplyIdUnknown_TheEnginesFramesReachTheCaller_AndTheMileagePipelineNamesThemAsForeign()
    {
        var car = EngineAnswersFunctionalRequests();
        var adapter = new HeaderIgnoringAdapter(car);
        var rig = await AdapterRigs.CreateAsync(adapter);
        using var log = new LogScope();

        // The plain-address overload: no reply id (the case the Original Mileage read is in when the resolver gives none).
        var raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(0x7E1, Dd01Request, TimeSpan.FromSeconds(2));

        Assert.Contains("7E1", adapter.RequestedHeaders);                // the app did ask for 7E1, and was told OK
        Assert.DoesNotContain(car.Sent, sent => sent.StartsWith("ATSH7E1", StringComparison.Ordinal));   // ...but the adapter never applied it
        Assert.Equal(FordDd01FromEngine, raw);                           // so the engine ECU's frames come back as the "answer"

        // The log now says so in one line.
        var line = log.Find("Read 22DD01")!;
        Assert.Contains("tx 7E1", line);
        Assert.Contains("frames from 7E8x1", line);
        Assert.Contains("ForeignReply [ecu]", line);

        // The mileage code's own steps (CatalogOdometerProvider.ReadAsync / Build), in their order, on this very text:
        var payload = OdometerResponseDecoder.ExtractPayload(raw, 0x22, 0xDD01, null, out var failure, out var responder);
        Assert.Equal(OdometerDecodeFailure.None, failure);               // with no reply id the value is not rejected here...
        Assert.NotNull(payload);
        Assert.Equal(0x7E8, responder);                                  // ...but the responder is named, never assumed to be 7E1
        Assert.True(OdometerResponseDecoder.IsResponderForeign(0x7E1, null, responder));   // and it is flagged as somebody else
    }

    [Fact]
    public async Task HeaderIgnored_ReplyIdKnown_AndTheAdapterHonoursAtcra_TheCallerSeesSilence_AndTheLogCannotTellItFromAnAbsentModule()
    {
        // A finding about observability: when a hardware receive filter hides the foreign frames, nothing in the app can
        // see that the header was ignored. Only an explicit probe (proposed, not enabled) could tell.
        var adapter = new HeaderIgnoringAdapter(EngineAnswersFunctionalRequests()) { HonorReceiveFilter = true };
        var rig = await AdapterRigs.CreateAsync(adapter);
        using var log = new LogScope();
        var address = new ServiceDiagnosticAddress(0x7E1, 0x7E9, false, null, "11-bit CAN", "module 7E1");

        var raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(address, Dd01Request, TimeSpan.FromSeconds(2));

        Assert.Equal("NO DATA", raw);
        var line = log.Find("Read 22DD01")!;
        Assert.Contains("frames from none", line);
        Assert.Contains("NoData [ecu]", line);
        Assert.Contains("filter: ATCRA", log.Find("Read setup")!);
        // The mileage decoder files every adapter negative word, "NO DATA" included, under Negative (the provider reports it as
        // "reading unavailable"): silence and an ECU refusal are not told apart at that layer.
        Assert.Equal(OdometerDecodeFailure.Negative, ExtractFailure(raw, 0x7E9));
    }

    [Fact]
    public async Task HeaderIgnored_ReplyIdKnown_AndNoReceiveFilterAtAll_TheAppDropsTheForeignFrames_ButTheLogNamesThem()
    {
        var car = EngineAnswersFunctionalRequests();
        NoReceiveFilterCommands(car);
        var rig = await AdapterRigs.CreateAsync(new HeaderIgnoringAdapter(car));
        using var log = new LogScope();
        var address = new ServiceDiagnosticAddress(0x7B0, 0x7B8, false, null, "11-bit CAN", "module 7B0");
        car.ModuleAnswers["7DF|22DD01"] = FordDd01FromEngine;

        var raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(address, Dd01Request, TimeSpan.FromSeconds(2));

        Assert.Equal("NO DATA", raw);                                    // unchanged: the caller is told nothing arrived from the asked module
        var line = log.Find("Read 22DD01")!;
        Assert.Contains("frames from 7E8x1", line);                      // the evidence that was invisible before
        Assert.Contains("ForeignReply [ecu]", line);
        Assert.Contains("no hardware receive filter (frames filtered in the app)", log.Find("Read setup")!);
    }

    [Fact]
    public async Task HeaderIgnored_TheVoltsBatteryModuleIsNeverReached_EveryCellReadFindsNo7EfFrames()
    {
        // The Volt hypothesis, on the same pieces the Volt cell reader uses: a session request to the BECM (7E7 → 7EF) goes
        // out on the functional header and is answered by the engine ECU on 7E8; the cell reader looks for 7EF frames.
        var car = new SimulatedElm327();
        foreach (var did in new[] { "224181", "224182", "224183" })
            car.ModuleAnswers["7DF|" + did] = "7E8 06 62 DD 01 03 B5 8F";    // made-up engine answer on the functional header
        var rig = await AdapterRigs.CreateAsync(new HeaderIgnoringAdapter(car));
        using var log = new LogScope();
        var cells = new List<double?>();

        await rig.Obd.RunDtcModuleSessionAsync(async session =>
        {
            for (var index = 0; index < 3; index++)
            {
                var request = GmVoltBoltBattery.CellRequest(index);
                var raw = await session.SendAsync(request.Tx, request.Rx, false, request.Payload, false, default);
                cells.Add(GmVoltBoltBattery.DecodeCell(HvPackProtocol.ExtractData(raw, request)));
            }
        });

        Assert.All(cells, cell => Assert.Null(cell));                    // the reader would report "BECM does not return cell voltage"
        var summary = log.Find("Module session over")!;
        Assert.Contains("frames from 7E8 x", summary);                   // ...and the log says who answered instead
        Assert.Contains("replies came only from other modules", summary);
    }

    [Fact]
    public async Task HeaderHonoured_TheSameVoltReads_FindTheBatteryModulesFrames()
    {
        // The control for the test above: an adapter that applies ATSH reaches the BECM.
        var car = new SimulatedElm327();
        car.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";             // recorded BECM answer (GmVoltBoltBatteryTests)
        var rig = await AdapterRigs.CreateAsync(car);
        double? cell = null;

        await rig.Obd.RunDtcModuleSessionAsync(async session =>
        {
            var request = GmVoltBoltBattery.CellRequest(0);
            var raw = await session.SendAsync(request.Tx, request.Rx, false, request.Payload, false, default);
            cell = GmVoltBoltBattery.DecodeCell(HvPackProtocol.ExtractData(raw, request));
        });

        Assert.NotNull(cell);
        Assert.InRange(cell!.Value, 2.0, 4.5);
    }

    private static OdometerDecodeFailure ExtractFailure(string raw, int replyId)
    {
        OdometerResponseDecoder.ExtractPayload(raw, 0x22, 0xDD01, replyId, out var failure);
        return failure;
    }

    // =========================================================================================
    // B. The adapter keeps echoing the command although ATE0 was sent
    // =========================================================================================

    [Fact]
    public async Task EchoOn_InitializationStillCompletes_AndTheProtocolIsFoundFromItsDescription()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        using var log = new LogScope();

        var rig = await AdapterRigs.CreateAsync(new EchoingAdapter(sim));

        Assert.True(rig.Obd.IsInitialized);
        Assert.Equal(ObdProtocol.ISO_15765_4_CAN_11BIT_500K, rig.Elm.CurrentProtocol);
        // ATDPN's reply "ATDPN A6" does not parse as a protocol number; the text description of ATDP rescues it.
        Assert.Contains("Protocol: ATDPN \"ATDPN A6\"", log.Find("Protocol: ATDPN")!);
        Assert.Contains("Echoed [adapter]", log.Find("Init ATL0")!);
        Assert.Contains("Echoed [adapter]", log.Find("Init 0100 probe")!);
    }

    [Fact]
    public async Task EchoOn_APhysicalReadStillSucceeds_ButTheEchoIsPartOfWhatTheCallerGets()
    {
        // Characterization: the reply comes back as "22119F 7E8 04 62 11 9F 80" — the sent command in front of the frame.
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        var rig = await AdapterRigs.CreateAsync(new EchoingAdapter(sim));
        using var log = new LogScope();

        var raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(0x7E0, new byte[] { 0x22, 0x11, 0x9F }, TimeSpan.FromSeconds(2));

        Assert.Equal("22119F 7E8 04 62 11 9F 80", raw);
        Assert.Contains("; Data;", log.Find("Read 22119F")!);            // the frame was found despite the echo
        // The odometer decoder skips the echo text and still finds the frame.
        var payload = OdometerResponseDecoder.ExtractPayload(raw, 0x22, 0x119F, null, out var failure, out var responder);
        Assert.NotNull(payload);
        Assert.Equal(OdometerDecodeFailure.None, failure);
        Assert.Equal(0x7E8, responder);
    }

    [Fact]
    public async Task EchoOn_ABatteryCellRead_ThroughTheModuleSession_IsLost_BecauseTheEchoAndTheFrameAreJoinedIntoOneLine()
    {
        // Characterization of a LIMITATION, not a fix: the protocol layer joins the adapter's lines with spaces, so
        // "224181 7EF05624181CED8" reaches HvPackProtocol.ExtractData (which expects one frame per line) as a single line
        // that starts with the echo, not with 7EF, and no data is found. Without the echo the same frame decodes.
        static async Task<double?> ReadCellAsync(IBluetoothService link)
        {
            var rig = await AdapterRigs.CreateAsync(link);
            double? cell = null;
            await rig.Obd.RunDtcModuleSessionAsync(async session =>
            {
                var request = GmVoltBoltBattery.CellRequest(0);
                var raw = await session.SendAsync(request.Tx, request.Rx, false, request.Payload, false, default);
                cell = GmVoltBoltBattery.DecodeCell(HvPackProtocol.ExtractData(raw, request));
            });
            return cell;
        }

        static SimulatedElm327 Becm()
        {
            var sim = new SimulatedElm327();
            sim.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";
            return sim;
        }

        Assert.NotNull(await ReadCellAsync(Becm()));
        Assert.Null(await ReadCellAsync(new EchoingAdapter(Becm())));
    }

    // =========================================================================================
    // C. An older chip that answers "?" to ATS0 — module sessions require it, physical reads do not
    // =========================================================================================

    [Fact]
    public async Task OlderChip_TheSamePhysicalRead_Works_ButTheModuleSessionCannotStart()
    {
        // Characterization of the divergence: the physical read path treats ATS1/ATD0/ATCFC1/ATCRA as optional, the module
        // session requires ATS0. On a chip that refuses ATS, one works and the other fails before sending anything.
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        sim.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";
        var rig = await AdapterRigs.CreateAsync(sim, () =>
        {
            foreach (var command in new[] { "ATS1", "ATS0", "ATD0", "ATD1", "ATCFC1", "ATCRA", "ATAR" })
                sim.RejectedAt.Add(command);
        });

        var physical = await rig.Obd.ReadPhysicalCanReadOnlyAsync(0x7E0, new byte[] { 0x22, 0x11, 0x9F }, TimeSpan.FromSeconds(2));
        using var log = new LogScope();
        sim.Sent.Clear();
        var session = await Assert.ThrowsAsync<Elm327CommandException>(() =>
            rig.Obd.RunDtcModuleSessionAsync(async s => await s.SendAsync(0x7E7, 0x7EF, false, new byte[] { 0x22, 0x41, 0x81 }, false, default)));

        Assert.Contains("62 11 9F 80", physical);
        Assert.Equal("ATS0", session.UnsupportedCommand);
        Assert.DoesNotContain("224181", sim.Sent);                       // the request itself was never sent
        Assert.Equal(AdapterFailureKind.AdapterCommandUnsupported, AdapterReadFailure.Classify(session).Kind);
        Assert.NotNull(log.Find("Setup: required command ATS0 NOT accepted"));
        Assert.True(rig.Obd.IsInitialized);                              // the adapter is left usable for the next operation
    }

    [Fact]
    public async Task OlderChip_AModuleSessionIsNotRememberedAsImpossible_AFaultyReplyOnceDoesNotBlockTheNextTry()
    {
        // A single "?" must not be remembered: the next session on the same adapter tries ATS0 again.
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.RejectedAt.Add("ATS0");
        await Assert.ThrowsAsync<Elm327CommandException>(() =>
            rig.Obd.RunDtcModuleSessionAsync(s => s.SendAsync(0x7E7, 0x7EF, false, new byte[] { 0x22, 0x41, 0x81 }, false, default)));

        sim.RejectedAt.Remove("ATS0");
        string? raw = null;
        await rig.Obd.RunDtcModuleSessionAsync(async s => raw = await s.SendAsync(0x7E7, 0x7EF, false, new byte[] { 0x22, 0x41, 0x81 }, false, default));

        Assert.Contains("7EF05624181CED8", raw);
    }

    // =========================================================================================
    // D. Replies that need more time, replies for another request, retries and cancellation
    // =========================================================================================

    [Fact]
    public async Task AStaleReplyForAnotherPid_IsNotTakenAsTheAnswer_TheRetryGetsTheRealOne()
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        // The adapter first delivers the answer to the PREVIOUS request (010C), then the real one.
        scripted.Reply("010D", "41 0C 1A F8\r\r>", "41 0D 3C\r\r>");
        var rig = await AdapterRigs.CreateAsync(scripted);

        var answer = await rig.Elm.SendCommandAsync("010D", TimeSpan.FromSeconds(1), 3, true);

        Assert.Equal("41 0D 3C", answer);
        Assert.Equal(2, scripted.Sent.Count(sent => sent == "010D"));
    }

    [Fact]
    public async Task AStaleReplyThatNeverGoesAway_IsReturnedAsText_SoTheCallerMustCheckThePid()
    {
        // Characterization: after the retries the protocol layer returns the mismatched text instead of throwing. The
        // parser is what refuses it (the PID is not in it), which is why callers must always extract by PID.
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        scripted.Reply("010D", "41 0C 1A F8\r\r>");
        var rig = await AdapterRigs.CreateAsync(scripted);

        var answer = await rig.Elm.SendCommandAsync("010D", TimeSpan.FromSeconds(1), 2, true);

        Assert.Equal("41 0C 1A F8", answer);
        Assert.Empty(ObdResponseParser.ExtractPayload(answer, 0x41, 0x0D));
        Assert.Equal(2, scripted.Sent.Count(sent => sent == "010D"));
    }

    [Fact]
    public async Task ATransientMissOnTheCommand_IsRecoveredByTheRetry_AndNothingIsLoggedAsAFailure()
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["01A5"] = "41 A5 12 34";                           // made-up PID
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.TimeoutOnNextSend.Add("01A5");
        using var log = new LogScope();

        var answer = await rig.Elm.SendCommandAsync("01A5", TimeSpan.FromMilliseconds(150), 3, true);

        Assert.Equal("41 A5 12 34", answer);
        Assert.Null(log.Find("Timeout: 01A5"));                          // the request as a whole succeeded
        Assert.Equal(0, rig.Elm.FailureStreak);
    }

    [Fact]
    public async Task ResponsePendingFollowedByTheAnswer_IsAskedAgainOnce_AndLeavesAnEntryInTheLog()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        sim.PendingBeforeAnswer = 1;
        var rig = await AdapterRigs.CreateAsync(sim);
        using var log = new LogScope();

        var raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(new ServiceDiagnosticAddress(0x7E0, 0x7E8, false, null, "11-bit CAN", "m"), new byte[] { 0x22, 0x11, 0x9F }, TimeSpan.FromSeconds(2));

        Assert.Contains("62 11 9F 80", raw);
        Assert.NotNull(log.Find("Response pending for 22119F: asking once more"));
        Assert.Contains("; Data;", log.Find("Read 22119F")!);             // the line describes the final answer, not the pending one
    }

    // =========================================================================================
    // E. Framing: the text an adapter wraps around a reply
    // =========================================================================================

    [Theory]
    [InlineData("41 00 BE 3F A8 13\r\r>")]
    [InlineData("41 00 BE 3F A8 13\r\n\r\n>")]
    [InlineData("\r\n41 00 BE 3F A8 13\r\n>")]
    [InlineData("41 00 BE 3F A8 13>")]
    [InlineData("41 00 BE 3F A8 13\n>")]
    [InlineData("  41   00 BE 3F A8 13 \r\r>")]
    [InlineData("41 00 BE 3F A8 13\r\r\r\r>")]
    [InlineData("\r41 00 BE 3F A8 13\r\r>")]
    public async Task EveryCommonFramingOfTheSameReply_BecomesTheSameText(string raw)
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        scripted.Reply("0100", raw);
        var rig = await AdapterRigs.CreateAsync(scripted);

        var answer = await rig.Elm.SendCommandAsync("0100", TimeSpan.FromSeconds(1), 1, true);

        Assert.Equal("41 00 BE 3F A8 13", answer);
    }

    [Fact]
    public async Task AReplyWithSeveralCanFramesInOneResponse_IsOneLine_AndEveryFramesSenderCanStillBeTold()
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        scripted.Reply("0100", "7E8 06 41 00 BE 3F A8 13\r7E9 06 41 00 98 3A 80 11\r\r>");
        var rig = await AdapterRigs.CreateAsync(scripted);

        var answer = await rig.Elm.SendCommandAsync("0100", TimeSpan.FromSeconds(1), 1, true);

        Assert.Equal("7E8 06 41 00 BE 3F A8 13 7E9 06 41 00 98 3A 80 11", answer);
        var split = ModuleReplyAnalysis.Split(answer, 0x7E8, false);
        Assert.Single(split.Own);
        Assert.Equal(new[] { "7E9" }, split.Foreign.Select(item => item.Header));
        Assert.Equal(new[] { 0x7E8, 0x7E9 }, ObdResponseParser.ExtractCanIdentifiers(answer).OrderBy(id => id));
    }

    [Fact]
    public async Task LowercaseHex_IsReturnedAsSent_AndStillParsed()
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        scripted.Reply("0100", "41 00 be 3f a8 13\r\r>");
        var rig = await AdapterRigs.CreateAsync(scripted);

        var answer = await rig.Elm.SendCommandAsync("0100", TimeSpan.FromSeconds(1), 1, true);

        Assert.Equal("41 00 be 3f a8 13", answer);                        // the case is not changed by the protocol layer
        Assert.Equal(new byte[] { 0xBE, 0x3F, 0xA8, 0x13 }, ObdResponseParser.ExtractPayload(answer, 0x41, 0x00));
    }

    [Fact]
    public async Task SearchingBeforeTheAnswer_IsRetried_AndTheRetryAnswerIsTheResult()
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        scripted.Reply("010C", "SEARCHING...\r41 0C 1A F8\r\r>", "41 0C 1A F8\r\r>");
        var rig = await AdapterRigs.CreateAsync(scripted);

        var answer = await rig.Elm.SendCommandAsync("010C", TimeSpan.FromSeconds(1), 3, true);

        Assert.Equal("41 0C 1A F8", answer);
        Assert.Equal(2, scripted.Sent.Count(sent => sent == "010C"));
    }
}
