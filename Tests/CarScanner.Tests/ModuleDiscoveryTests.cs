using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Module discovery against replies copied from four real capture files (Toyota Camry, Ford Escape 2012,
/// BMW 320i 2018, Chevrolet Volt 2013). The recurring finding in them: the engine ECU's reply (0x7E8) was
/// delivered for requests addressed to 20+ other modules, and a 439-byte Ford DTC list stopped after its first
/// frame. Nothing here proves what any module IS; it proves the app no longer mistakes those replies for it.
/// </summary>
public sealed class ModuleDiscoveryTests
{
    // ---- fixtures: adapter text exactly as the capture files hold it (frames joined by a space) ----
    private const string ToyotaEngineRefusal = "7E8037F1911";                    // 19 02 FF → 7F 19 11
    private const string ToyotaMultiFrame = "7E8101561412E662851 7E8213400002DA32851 7E82234883420080007 7E823AC000000000000"; // 21 41
    private const string FordDtcListFirstFrameOnly = "7E811B75902FF001100";     // 19 02 FF → FF, 0x1B7 bytes announced
    private const string FordDidReply = "7E80662DD0103B58F";                    // 22 DD 01
    private const string BmwLateModeThreeFrame = "7E80443010440";               // a Mode 03 style frame under 6F1 → 601
    private const string BmwDtcList = "7E8075902FF190F0868";                    // 19 02 FF → 59 02 FF 19 0F 08 68

    private static Func<byte[], bool, Task<string>> Script(Dictionary<string, string> replies, List<(string Request, bool LongWait)>? log = null) =>
        (payload, longWait) =>
        {
            var key = Convert.ToHexString(payload);
            log?.Add((key, longWait));
            return Task.FromResult(replies.TryGetValue(key, out var raw) ? raw : "NO DATA");
        };

    // ------------------------------------------------------------------ Split

    [Fact]
    public void Split_KeepsOnlyFramesOfTheAskedSender_AndReportsTheOthers()
    {
        var result = ModuleReplyAnalysis.Split("7E8037F1911 7E9037F1911", 0x7E9, false);

        Assert.Single(result.Own);
        Assert.Equal(new byte[] { 0x03, 0x7F, 0x19, 0x11 }, result.Own[0]);
        var foreign = Assert.Single(result.Foreign);
        Assert.Equal("7E8", foreign.Header);
        Assert.Equal(1, foreign.Frames);
        // two identical frames from the asked sender stay two frames: duplicates are not merged away here
        Assert.Equal(2, ModuleReplyAnalysis.Split("7E9037F1911 7E9037F1911", 0x7E9, false).Own.Count);
    }

    [Fact]
    public void Split_RealCapture_EngineReplyForAnotherAddress_IsForeignNotOwn()
    {
        // Toyota capture: request to 0x700, expected reply id 0x708, the answer carried id 0x7E8.
        var result = ModuleReplyAnalysis.Split(ToyotaEngineRefusal, 0x708, false);

        Assert.Empty(result.Own);
        Assert.Equal("7E8", Assert.Single(result.Foreign).Header);
    }

    [Fact]
    public void Split_NoData_IsAStatusNotAFrame()
    {
        var result = ModuleReplyAnalysis.Split("NO DATA", 0x7E8, false);

        Assert.Empty(result.Own);
        Assert.Empty(result.Foreign);
        Assert.Equal("NO DATA", result.Status);
    }

    [Fact]
    public void Split_UnderstandsSpacedFrames_AndKeepsFramesApart()
    {
        var result = ModuleReplyAnalysis.Split("7E8 03 7F 19 11 7E8 05 62 F4 0C 1A 2B", 0x7E8, false);

        Assert.Equal(2, result.Own.Count);
        Assert.Equal(new byte[] { 0x03, 0x7F, 0x19, 0x11 }, result.Own[0]);
        Assert.Equal(new byte[] { 0x05, 0x62, 0xF4, 0x0C, 0x1A, 0x2B }, result.Own[1]);
    }

    [Theory]
    [InlineData("7E8037F191")]     // odd number of hex digits: cut in the middle of a byte
    [InlineData("7E8")]            // an id without data
    [InlineData("ZZZ037F1911")]    // not hex
    public void Split_GarbledOutput_IsNeverTurnedIntoAFrame(string raw)
    {
        var result = ModuleReplyAnalysis.Split(raw, 0x7E8, false);

        Assert.Empty(result.Own);
        Assert.Empty(result.Foreign);
    }

    [Fact]
    public void Split_29Bit_UsesTheEightDigitId()
    {
        var result = ModuleReplyAnalysis.Split("18DAF110037F1911 18DAF118037F1911", 0x18DAF110, true);

        Assert.Single(result.Own);
        Assert.Equal("18DAF118", Assert.Single(result.Foreign).Header);
    }

    // ------------------------------------------------------------------ Assemble

    [Fact]
    public void Assemble_RealToyotaLocalIdentifierReply_IsOneCompleteMessage()
    {
        var frames = ModuleReplyAnalysis.Split(ToyotaMultiFrame, 0x7E8, false).Own;

        var assembly = ModuleReplyAnalysis.Assemble(frames);

        var message = Assert.Single(assembly.Messages);
        Assert.Equal(0x15, message.Length);              // announced length from the first frame
        Assert.Equal(new byte[] { 0x61, 0x41 }, message[..2]);
        Assert.Empty(assembly.Incomplete);
    }

    [Fact]
    public void Assemble_FirstFrameOnly_IsReportedAsIncomplete_NotDropped()
    {
        // Ford capture: the 439-byte DTC list stopped after its first frame.
        var frames = ModuleReplyAnalysis.Split(FordDtcListFirstFrameOnly, 0x7E8, false).Own;

        var assembly = ModuleReplyAnalysis.Assemble(frames);

        Assert.Empty(assembly.Messages);
        var fragment = Assert.Single(assembly.Incomplete);
        Assert.Equal(0x1B7, fragment.ExpectedLength);
        Assert.Equal(new byte[] { 0x59, 0x02, 0xFF, 0x00, 0x11, 0x00 }, fragment.Data);
    }

    [Fact]
    public void Assemble_LostConsecutiveFrame_LeavesAFragment_NotAMessage()
    {
        var frames = new List<byte[]>
        {
            new byte[] { 0x10, 0x0E, 0x59, 0x02, 0xFF, 0x01, 0x02, 0x03 },
            new byte[] { 0x22, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A }   // sequence 2 where 1 is due
        };

        var assembly = ModuleReplyAnalysis.Assemble(frames);

        Assert.Empty(assembly.Messages);
        Assert.Single(assembly.Incomplete);
    }

    [Fact]
    public void Assemble_DuplicatedConsecutiveFrame_DoesNotBreakTheMessage()
    {
        var frames = new List<byte[]>
        {
            new byte[] { 0x10, 0x14, 0x59, 0x02, 0xFF, 0x01, 0x02, 0x03 },
            new byte[] { 0x21, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A },
            new byte[] { 0x21, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A },   // delivered twice
            new byte[] { 0x22, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11 }
        };

        var assembly = ModuleReplyAnalysis.Assemble(frames);

        var message = Assert.Single(assembly.Messages);
        Assert.Equal(20, message.Length);
        Assert.Equal(0x11, message[^1]);
        Assert.Empty(assembly.Incomplete);
    }

    [Fact]
    public void Assemble_ImpossibleLengths_AreCountedUnusable()
    {
        var assembly = ModuleReplyAnalysis.Assemble(new List<byte[]>
        {
            new byte[] { 0x07, 0x59, 0x02 },                 // single frame announcing 7 bytes, holds 2
            new byte[] { 0x00, 0x59 },                       // single frame of length 0
            new byte[] { 0x10, 0x03, 0x59, 0x02, 0xFF },     // first frame announcing less than 8 bytes
            new byte[] { 0x21, 0x01, 0x02 }                  // consecutive frame without a first frame
        });

        Assert.Empty(assembly.Messages);
        Assert.Empty(assembly.Incomplete);
        Assert.Equal(4, assembly.Unusable);
    }

    // ------------------------------------------------------------------ Exchange

    [Fact]
    public async Task Exchange_ResponsePending_IsAskedOnceMoreWithALongerWindow()
    {
        var windows = new List<bool>();
        var exchange = await ModuleReplyAnalysis.ExchangeAsync(longWait =>
        {
            windows.Add(longWait);
            return Task.FromResult(longWait ? "7E8055902FF0000" : "7E8037F1978");
        }, 0x7E8, false, null, 0x19);

        Assert.Equal(new[] { false, true }, windows);
        Assert.NotNull(exchange.Positive);
        Assert.Equal(2, exchange.Attempts);
    }

    [Fact]
    public async Task Exchange_ImmediateAnswer_IsNotRepeated()
    {
        var calls = 0;
        var exchange = await ModuleReplyAnalysis.ExchangeAsync(_ => { calls++; return Task.FromResult(ToyotaEngineRefusal); }, 0x7E8, false, null, 0x19);

        Assert.Equal(1, calls);
        Assert.Equal((byte)0x11, exchange.Nrc);
        Assert.True(exchange.AnyReply);
        Assert.Null(exchange.Positive);
    }

    [Fact]
    public async Task Exchange_NoData_IsOneAttemptAndSilence()
    {
        var calls = 0;
        var exchange = await ModuleReplyAnalysis.ExchangeAsync(_ => { calls++; return Task.FromResult("NO DATA"); }, 0x7E8, false, null, 0x19);

        Assert.Equal(1, calls);
        Assert.False(exchange.AnyReply);
        Assert.Equal("NO DATA", exchange.Status);
    }

    [Fact]
    public async Task Exchange_StalledMultiFrame_IsAskedOnceMore_AndKeepsTheFragmentIfStillStalled()
    {
        var calls = 0;
        var exchange = await ModuleReplyAnalysis.ExchangeAsync(_ => { calls++; return Task.FromResult(FordDtcListFirstFrameOnly); }, 0x7E8, false, null, 0x19);

        Assert.Equal(2, calls);
        Assert.True(exchange.Incomplete);
        Assert.True(exchange.AnyReply);
        Assert.True(exchange.FragmentIsPositiveFor(0x19));
    }

    // ------------------------------------------------------------------ Probe (decision tree)

    [Fact]
    public async Task Probe_RealToyotaCapture_AddressThatOnlyHeardTheEngine_IsNotAModule()
    {
        // 0x700 → expected 0x708; the capture shows 0x7E8's refusal instead.
        var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = ToyotaEngineRefusal }), null, 0x708, false, null);

        Assert.False(result.Responded);
        Assert.Equal(ModuleProbeState.ForeignReplyOnly, result.State);
        Assert.Equal("0x7E8", result.ForeignSender);
        Assert.Contains("not attributed", result.Detail);
    }

    [Fact]
    public async Task Probe_RealToyotaCapture_EngineRefusal_IsAnsweredNegativeOnly_WithTheRefusalCode()
    {
        var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = ToyotaEngineRefusal }), null, 0x7E8, false, null);

        Assert.True(result.Responded);
        Assert.Equal(ModuleProbeState.AnsweredNegativeOnly, result.State);
        Assert.Equal("—", result.Protocol);
        Assert.Equal((byte)0x11, result.Nrc);
        Assert.Contains("serviceNotSupported", result.Detail);
        Assert.Empty(result.GmlanFrames);
    }

    [Fact]
    public async Task Probe_KwpFallbackIsTried_WhenTheModuleRefusedUds_ButNotWhenItWasSilent()
    {
        var log = new List<(string, bool)>();
        await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = ToyotaEngineRefusal }, log), null, 0x7E8, false, null);
        Assert.Contains(log, item => item.Item1 == "1802FF00");

        log.Clear();
        var silent = await ModuleDtcProbe.ProbeAsync(Script(new(), log), null, 0x7E8, false, null);
        Assert.DoesNotContain(log, item => item.Item1 == "1802FF00");
        Assert.Equal(ModuleProbeState.NoReply, silent.State);
    }

    [Fact]
    public async Task Probe_NoData_IsNoReply_NotAbsent()
    {
        var result = await ModuleDtcProbe.ProbeAsync(Script(new()), null, 0x73F, false, null);

        Assert.False(result.Responded);
        Assert.Equal(ModuleProbeState.NoReply, result.State);
        Assert.Equal("NO DATA", result.Detail);
        Assert.Contains("not reachable", ModuleProbe.Describe(result.State));
    }

    [Fact]
    public async Task Probe_RealFordCapture_DtcListStoppedAfterFirstFrame_IsAnsweredButIncomplete()
    {
        var log = new List<(string, bool)>();
        var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = FordDtcListFirstFrameOnly }, log), null, 0x7E8, false, null);

        Assert.True(result.Responded);
        Assert.Equal(ModuleProbeState.AnsweredIncomplete, result.State);
        Assert.Equal("UDS", result.Protocol);
        Assert.True(result.Incomplete);
        Assert.Equal(new byte[] { 0x59, 0x02, 0xFF, 0x00, 0x11, 0x00 }, result.Message);
        Assert.Contains("6 of 439", result.Detail);
        // one retry with the longer window, nothing else is asked of a module whose answer is cut off
        Assert.Equal(new[] { ("1902FF", false), ("1902FF", true) }, log.Select(item => (item.Item1, item.Item2)).ToArray());
    }

    [Fact]
    public async Task Probe_RealFordCapture_TwentyFourAddressesAllHearingTheEngineFirstFrame_YieldNoModules()
    {
        var expectedReplyIds = new[]
        {
            0x768, 0x73F, 0x72E, 0x728, 0x738, 0x73B, 0x71E, 0x7D8, 0x72F, 0x70E, 0x76C, 0x73E, 0x72C, 0x748, 0x749,
            0x7E9, 0x7EA, 0x7EB, 0x7EC, 0x7ED, 0x7EE, 0x7EF
        };

        foreach (var rx in expectedReplyIds)
        {
            var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = FordDtcListFirstFrameOnly }), null, rx, false, null);
            Assert.False(result.Responded);
            Assert.Equal(ModuleProbeState.ForeignReplyOnly, result.State);
        }
    }

    [Fact]
    public async Task Probe_RealBmwCapture_LateModeThreeFrameUnderTheBmwAddress_IsNotAModule()
    {
        // BMW capture: 0x6F1 → 0x601 (target 0x01) was filed with a Mode 03 style frame from 0x7E8.
        var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = BmwLateModeThreeFrame }), null, 0x601, false, 0x01);

        Assert.False(result.Responded);
        Assert.Equal(ModuleProbeState.ForeignReplyOnly, result.State);
    }

    [Fact]
    public async Task Probe_RealBmwCapture_PositiveDtcListFromTheEngine_IsAnswered_AndDuplicatesDoNotMatter()
    {
        var once = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = BmwDtcList }), null, 0x7E8, false, null);
        var twice = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = BmwDtcList + " " + BmwDtcList }), null, 0x7E8, false, null);

        Assert.Equal(ModuleProbeState.Answered, once.State);
        Assert.Equal("UDS", once.Protocol);
        Assert.Equal(new byte[] { 0x59, 0x02, 0xFF, 0x19, 0x0F, 0x08, 0x68 }, once.Message);
        Assert.Equal(once.Message, twice.Message);
    }

    [Fact]
    public async Task Probe_PartialDiscovery_OneAnswerAndOneSilenceInTheSameRun()
    {
        var answered = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = BmwDtcList }), null, 0x7E8, false, null);
        var silent = await ModuleDtcProbe.ProbeAsync(Script(new()), null, 0x7E9, false, null);

        Assert.True(answered.Responded);
        Assert.False(silent.Responded);
    }

    [Fact]
    public async Task Probe_Gmlan_IsOnlyTriedWhenOffered_AndAnUnsupportedAdapterIsNotAFinding()
    {
        // Volt-like: no UDS answer, GMLAN frames on 0x500 | (rx & 0xFF).
        var gm = await ModuleDtcProbe.ProbeAsync(Script(new()),
            _ => Task.FromResult("5EA8112340801"), 0x7EA, false, null);
        Assert.Equal(ModuleProbeState.Answered, gm.State);
        Assert.Equal("GMLAN", gm.Protocol);
        Assert.Equal(new byte[] { 0x81, 0x12, 0x34, 0x08, 0x01 }, Assert.Single(gm.GmlanFrames));

        // The adapter cannot send raw frames: silence, not "module absent" and not a crash.
        var unsupported = await ModuleDtcProbe.ProbeAsync(Script(new()),
            _ => throw new NotSupportedException("raw frames"), 0x7EA, false, null);
        Assert.Equal(ModuleProbeState.NoReply, unsupported.State);

        // A reply on a different 0x5xx id is not this module's.
        var other = await ModuleDtcProbe.ProbeAsync(Script(new()),
            _ => Task.FromResult("5EB8112340801"), 0x7EA, false, null);
        Assert.Equal(ModuleProbeState.ForeignReplyOnly, other.State);
    }

    [Fact]
    public async Task Probe_Gmlan_DoesNotOverrideAModuleThatAnsweredUdsWithARefusal()
    {
        var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = "7EA037F1911" }),
            _ => Task.FromResult("NO DATA"), 0x7EA, false, null);

        Assert.Equal(ModuleProbeState.AnsweredNegativeOnly, result.State);
    }

    [Fact]
    public async Task Probe_ExtendedAddressing_StripsTheTesterByte()
    {
        // BMW style: replies start with the tester address 0xF1 before the PCI byte.
        var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = "612F1065902FF190F08" }), null, 0x612, false, 0x12);

        // frame data F1 06 59 02 FF 19 0F 08: after stripping the tester byte F1 it is a single frame
        Assert.Equal(ModuleProbeState.Answered, result.State);
        Assert.Equal((byte)0x19, result.Message![3]);
    }

    [Fact]
    public async Task Probe_29BitAddressing_AttributesByTheFullId()
    {
        var own = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = "18DAF110037F1911" }), null, 0x18DAF110, true, null);
        var foreign = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = "18DAF118037F1911" }), null, 0x18DAF110, true, null);

        Assert.Equal(ModuleProbeState.AnsweredNegativeOnly, own.State);
        Assert.Equal(ModuleProbeState.ForeignReplyOnly, foreign.State);
        Assert.Equal("0x18DAF118", foreign.ForeignSender);
    }

    [Fact]
    public async Task Probe_PositiveDtcListWithAlsoForeignFrames_StaysAnswered_AndSaysSo()
    {
        var result = await ModuleDtcProbe.ProbeAsync(Script(new() { ["1902FF"] = "7E9075902FF190F0868 7E8037F1911" }), null, 0x7E9, false, null);

        Assert.Equal(ModuleProbeState.Answered, result.State);
        Assert.Contains("0x7E8", result.Detail);
    }

    // ------------------------------------------------------------------ Capture attribution

    [Theory]
    [InlineData("7E8037F1911", 0x708, 0x7E8)]                    // Toyota: the engine answered a request meant for 0x700
    [InlineData("7E8037F1911", 0x7E8, 0x7E8)]                    // asked and answered by the same id
    [InlineData("7E9037F1911 7E8037F1911", 0x7E8, 0x7E8)]       // the asked id is present among others: it wins
    [InlineData("NO DATA", 0x73F, 0x73F)]                        // no frame: nothing to correct
    [InlineData("", 0x73F, 0x73F)]
    [InlineData("7E8 03 7F 19 11", 0x7E9, 0x7E8)]                // spaced output
    public void RealSenderHeader_NamesTheIdTheFramesCameFrom(string answer, int asked, int expected) =>
        Assert.Equal(expected, Elm327Protocol.RealSenderHeader(answer, false, asked));

    [Fact]
    public void RealSenderHeader_29Bit() =>
        Assert.Equal(0x18DAF118, Elm327Protocol.RealSenderHeader("18DAF118037F1911", true, 0x18DAF110));

    // ------------------------------------------------------------------ Summary wording

    [Fact]
    public void ProbeState_Wording_NeverCallsSilenceAbsence()
    {
        Assert.DoesNotContain("absent.", ModuleProbe.Describe(ModuleProbeState.NoReply));
        Assert.Contains("not reachable", ModuleProbe.Describe(ModuleProbeState.NoReply));
        Assert.Contains("0x7E8", ModuleProbe.Describe(ModuleProbeState.ForeignReplyOnly, "0x7E8"));
    }

    [Fact]
    public void ProbeAddressText_ShowsTheExtendedAddress()
    {
        Assert.Equal("0x6F1/12", new ModuleProbe("ECM", 0x6F1, 0x612, false, 0x12, ModuleProbeState.NoReply, "", null, "").AddressText);
        Assert.Equal("0x18DA10F1", new ModuleProbe("ECM", 0x18DA10F1, 0x18DAF110, true, null, ModuleProbeState.NoReply, "", null, "").AddressText);
    }
}
