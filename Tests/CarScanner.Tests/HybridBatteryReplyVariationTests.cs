using CarScanner.Services;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stage 1: how the hybrid/EV battery readers' parser (<see cref="HvPackProtocol.ExtractData"/>) copes with the same ECU
/// frame as different adapters and the protocol layer deliver it. The frames are the recorded Chevrolet Volt/Bolt BECM
/// answers from GmVoltBoltBatteryTests (and plainly made-up multi-frame ones). SIMULATION ONLY.
///
/// These tests pin what the parser does TODAY. Two results are limitations, marked as such: a frame that is joined onto the
/// echo or onto another module's frame in ONE line is not found, and a multi-frame reply joined into one line is rejected
/// (even number of frames) or can come back as wrong bytes (odd number). Nothing here changes the parser or the request sequence.
/// </summary>
public sealed class HybridBatteryReplyVariationTests
{
    // Recorded BECM answer for cell 1 (22 41 81): 05 62 41 81 CE D8  → 0xCED8 × 5 / 65535 V.
    private static readonly HvRequest Cell1 = GmVoltBoltBattery.CellRequest(0);
    private static readonly double? Cell1Volts = Math.Round(0xCED8 * 5d / 65535d, 4);

    private static double? Volts(string raw) => GmVoltBoltBattery.DecodeCell(HvPackProtocol.ExtractData(raw, Cell1));

    // ------------------------------------------------------------------ one frame, many ways of delivering it

    [Theory]
    [InlineData("7EF05624181CED8")]                         // compact (ATS0), as the module session asks for
    [InlineData("7EF 05 62 41 81 CE D8")]                   // spaced (ATS1)
    [InlineData("7EF05624181CED8\r\r>")]                    // with the end of the reply
    [InlineData("\r\n7EF05624181CED8\r\n>")]
    [InlineData("7ef05624181ced8")]                         // lowercase
    [InlineData("224181\r7EF05624181CED8")]                 // the echo on a line of its own
    [InlineData("\u0001@@##\r7EF05624181CED8\r?!?")]       // noise lines around it
    [InlineData("7E8 05 62 41 81 AA BB\r7EF05624181CED8")]  // another module's frame on another line
    public void TheSameFrame_IsFound_WhateverTheFramingAroundItOnItsLine(string raw) =>
        Assert.Equal(Cell1Volts, Volts(raw));

    [Theory]
    [InlineData("7E8 05 62 41 81 CE D8")]                   // only the engine ECU answered
    [InlineData("NO DATA")]
    [InlineData("")]
    [InlineData("7EF037F2231")]                             // the module refused the request
    [InlineData("7EF037F2278")]                             // the module asked for more time
    [InlineData("7EF05624181CE")]                           // cut off in the middle of a byte
    [InlineData("7EF05624182CED8")]                         // an answer for another cell
    public void ANonAnswer_IsNeverTurnedIntoAVoltage(string raw) =>
        Assert.Null(Volts(raw));

    [Fact]
    public void AResponsePending_IsRecognisedAsSuch()
    {
        Assert.True(HvPackProtocol.IsResponsePending("7EF037F2278", Cell1));
        Assert.False(HvPackProtocol.IsResponsePending("7EF037F2231", Cell1));
        Assert.False(HvPackProtocol.IsResponsePending("7EF05624181CED8", Cell1));
    }

    // ------------------------------------------------------------------ LIMITATIONS: lines joined by the protocol layer

    [Fact]
    public void AnEchoJoinedOntoTheFrameInOneLine_IsStillNotRead_AsBefore()
    {
        // Unchanged by Stage 2C: text that is not made of frames alone (here an echoed command) is not split, and a line that
        // does not start with the asked id is not read. Reading past an echo would be a separate decision.
        Assert.Null(Volts("224181 7EF05624181CED8"));
    }

    [Theory]
    [InlineData("7E8 05 62 41 81 AA BB 7EF05624181CED8")]   // two modules' frames, joined with a space (another module first)
    [InlineData("7EF05624181CED8 7E8 05 62 41 81 AA BB")]   // ...and the other way round
    public void AFrameBesideAnotherModulesFrameInOneLine_IsFound_Since2C(string joined)
    {
        // The protocol layer joins the adapter's lines with spaces (CleanResponse) before the session returns them. Before
        // Stage 2C the parser expected one frame per line, so the second frame of such a line was missed.
        Assert.Equal(Cell1Volts, Volts(joined));
    }

    // A made-up multi-frame answer to "22 AB CD" from 7E7 (answers on 7EF): ISO-TP first frame + consecutive frames.
    private static readonly HvRequest MultiFrame = new(0x7E7, 0x7EF, new byte[] { 0x22, 0xAB, 0xCD });
    private const string TwoFramesAsLines = "7EF 10 0A 62 AB CD 01 02 03\r7EF 21 04 05 06 07 00 00 00";
    private const string ThreeFramesAsLines = "7EF 10 11 62 AB CD 01 02 03\r7EF 21 04 05 06 07 08 09 0A\r7EF 22 0B 0C 0D 0E 00 00 00";

    private static readonly byte[] TwoFramesData = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
    private static readonly byte[] ThreeFramesData = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E };

    [Fact]
    public void MultiFrameReplies_OnSeparateLines_AreReassembled()
    {
        Assert.Equal(TwoFramesData, HvPackProtocol.ExtractData(TwoFramesAsLines, MultiFrame));
        Assert.Equal(ThreeFramesData, HvPackProtocol.ExtractData(ThreeFramesAsLines, MultiFrame));
    }

    [Fact]
    public void ATwoFrameReply_JoinedIntoOneLine_IsReassembled_Since2C()
    {
        // Stage 2C. Before it, this joined line (what the protocol layer hands on) was rejected: the parser read it as one frame.
        var joined = TwoFramesAsLines.Replace('\r', ' ');

        Assert.Equal(TwoFramesData, HvPackProtocol.ExtractData(joined, MultiFrame));
    }

    [Fact]
    public void AThreeFrameReply_JoinedIntoOneLine_ComesBackWithTheRightBytes_Since2C()
    {
        // Stage 2C. Before it, three frames of 19 characters made a line that still looked like ONE frame of valid length: the
        // later frames' header characters were read as data, so the right number of bytes came back but not the right bytes.
        var joined = ThreeFramesAsLines.Replace('\r', ' ');

        Assert.Equal(ThreeFramesData, HvPackProtocol.ExtractData(joined, MultiFrame));
    }

    [Fact]
    public void TheModuleScansParser_AndTheBatteryParser_NowAgreeOnTheSameJoinedText()
    {
        var joined = ThreeFramesAsLines.Replace('\r', ' ');

        var split = ModuleReplyAnalysis.Split(joined, 0x7EF, false);
        var assembly = ModuleReplyAnalysis.Assemble(split.Own);

        var message = Assert.Single(assembly.Messages);
        Assert.Equal(new byte[] { 0x62, 0xAB, 0xCD }.Concat(ThreeFramesData), message);
        Assert.Equal(message[3..], HvPackProtocol.ExtractData(joined, MultiFrame));     // the same data bytes, whichever parser reads it
    }

    // ------------------------------------------------------------------ the same read through a simulated module session

    private static async Task<double?> FirstCellThroughASessionAsync(AdapterRig rig)
    {
        double? volts = null;
        await rig.Obd.RunDtcModuleSessionAsync(async session =>
        {
            var raw = await session.SendAsync(Cell1.Tx, Cell1.Rx, false, Cell1.Payload, false, default);
            volts = GmVoltBoltBattery.DecodeCell(HvPackProtocol.ExtractData(raw, Cell1));
        });
        return volts;
    }

    private static SimulatedElm327 Becm()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E7|224181"] = "7EF05624181CED8";
        return sim;
    }

    [Fact]
    public async Task ThroughASession_AFullFeaturedAdapter_ReadsTheCell()
    {
        var rig = await AdapterRigs.CreateAsync(Becm());

        Assert.Equal(Cell1Volts, await FirstCellThroughASessionAsync(rig));
    }

    private static SimulatedElm327 BecmOnAChipWithoutReceiveFilters()
    {
        var sim = Becm();
        foreach (var command in new[] { "ATCRA", "ATAR", "ATCF" })
            sim.RejectedAt.Add(command);
        return sim;
    }

    [Fact]
    public async Task ThroughASession_AnAdapterWithoutAnyReceiveFilter_StillReadsTheCell_WhenOnlyTheBecmAnswers()
    {
        var rig = await AdapterRigs.CreateAsync(BecmOnAChipWithoutReceiveFilters());

        Assert.Equal(Cell1Volts, await FirstCellThroughASessionAsync(rig));
    }

    [Fact]
    public async Task ThroughASession_AnAdapterWithoutAnyReceiveFilter_ReadsTheCell_EvenWhenTheEngineEcuAnswersFirstToo_Since2C()
    {
        // Stage 2C. The app-side filter (ApplySoftwareFilter) used to judge the line the protocol layer had joined by its first
        // frame: the line started with the engine ECU's frame, so the BECM's frame behind it was dropped with it and the caller
        // saw "NO DATA". The filter now works frame by frame; the engine ECU's frame is dropped and the BECM's is kept.
        var sim = BecmOnAChipWithoutReceiveFilters();
        sim.ModuleAnswers["7E7|224181"] = "7E8 05 62 41 81 AA BB\r7EF05624181CED8";
        var rig = await AdapterRigs.CreateAsync(sim);
        using var log = new LogScope();

        Assert.Equal(Cell1Volts, await FirstCellThroughASessionAsync(rig));
        Assert.Contains("frames from 7E8 x1, 7EF x1", log.Find("Module session over")!);   // the log shows both modules did answer
    }
}
