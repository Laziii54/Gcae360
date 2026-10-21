using CarScanner.Services;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stage 2C: replies with several CAN frames in one text. The protocol layer joins the adapter's lines with spaces, so one text can
/// hold frames of several modules, which the battery parser (<c>HvPackProtocol.ExtractData</c>) and the app-side module filter
/// (<c>ApplySoftwareFilter</c>, tested through a real module session) used to judge line by line. Both now split such a line into
/// frames first (<c>ModuleReplyAnalysis.SplitFrameTexts</c>), keep the asked id's frames and never merge frames of other ids.
/// Everything here runs the production parsers; the frames are made-up (the recorded Volt cell frame is the one used by
/// GmVoltBoltBatteryTests). SIMULATION ONLY: nothing here shows what any real adapter prints.
/// </summary>
public sealed class FrameAwareParsingTests
{
    private static readonly HvRequest Cell1 = GmVoltBoltBattery.CellRequest(0);                    // 7E7 -> 7EF, 22 41 81
    private static readonly HvRequest Multi = new(0x7E7, 0x7EF, new byte[] { 0x22, 0xAB, 0xCD });   // made-up multi-frame answer, 7EF

    private const string CellFrame = "7EF05624181CED8";                                            // 05 62 41 81 CE D8
    private const string Two = "7EF 10 0A 62 AB CD 01 02 03 7EF 21 04 05 06 07 00 00 00";
    private const string Three = "7EF 10 11 62 AB CD 01 02 03 7EF 21 04 05 06 07 08 09 0A 7EF 22 0B 0C 0D 0E 00 00 00";
    private static readonly byte[] TwoData = { 1, 2, 3, 4, 5, 6, 7 };
    private static readonly byte[] ThreeData = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
    private static readonly double Cell1Volts = Math.Round(0xCED8 * 5d / 65535d, 4);

    private static byte[]? Data(string raw, HvRequest request) => HvPackProtocol.ExtractData(raw, request);

    private static double? Volts(string raw) => GmVoltBoltBattery.DecodeCell(Data(raw, Cell1));

    // ------------------------------------------------------------------ 1. single frame (unchanged)

    [Theory]
    [InlineData("7EF05624181CED8")]
    [InlineData("7EF 05 62 41 81 CE D8")]
    [InlineData("7EF05624181CED8\r\r>")]
    [InlineData("7EF05 62 41 81 CE D8")]                    // mixed grouping: the text of one frame, read as before
    [InlineData("7EF 0562 4181 CED8")]                      // even-length groups: not splittable, judged as one line as before
    public void AValidSingleFrame_IsReadAsBefore_InEveryLayout(string raw) => Assert.Equal(Cell1Volts, Volts(raw));

    // ------------------------------------------------------------------ 2-5. several frames: lines, spaces, compact tokens

    [Fact]
    public void TwoAndThreeFrames_OnSeparateLines_AreReassembled_AsBefore()
    {
        Assert.Equal(TwoData, Data(Two.Replace(" 7EF 21", "\r7EF 21"), Multi));
        Assert.Equal(ThreeData, Data(Three.Replace(" 7EF 2", "\r7EF 2"), Multi));
    }

    [Fact]
    public void TwoFrames_InOneSpaceSeparatedText_AreReassembled()
    {
        Assert.Equal(TwoData, Data(Two, Multi));
        Assert.Equal(TwoData, Data(Two + "\r\r>", Multi));
    }

    [Fact]
    public void ThreeFrames_InOneSpaceSeparatedText_AreReassembledWithTheRightBytes()
    {
        Assert.Equal(ThreeData, Data(Three, Multi));
    }

    [Fact]
    public void CompactFrames_JoinedBySpaces_AreReassembled_AsTheSessionReturnsThem()
    {
        Assert.Equal(TwoData, Data("7EF100A62ABCD010203 7EF2104050607000000", Multi));
        Assert.Equal(ThreeData, Data("7EF101162ABCD010203 7EF210405060708090A 7EF220B0C0D0E000000", Multi));
    }

    // ------------------------------------------------------------------ 6-7. frames of different modules

    [Theory]
    [InlineData("7E8 05 62 41 81 AA BB")]                                  // only the engine ECU
    [InlineData("7E8 05 62 41 81 AA BB 7E9 05 62 41 81 CC DD")]            // two other modules, none of them the asked one
    [InlineData("7E805624181AABB 7E905624181CCDD")]
    public void FramesOnlyFromOtherModules_NeverGiveAnAnswer(string raw) => Assert.Null(Volts(raw));

    [Theory]
    [InlineData("7E8 05 62 41 81 AA BB 7EF05624181CED8")]                  // another module first
    [InlineData("7EF05624181CED8 7E8 05 62 41 81 AA BB")]                  // another module last
    [InlineData("7E8 05 62 41 81 AA BB 7EF 05 62 41 81 CE D8 7E9 05 62 41 81 CC DD")]   // between two others
    public void TheAskedModulesFrame_IsFound_WhateverOtherModulesFramesSitAroundIt(string raw) => Assert.Equal(Cell1Volts, Volts(raw));

    [Fact]
    public void AMultiFrameAnswer_WithAnotherModulesFramesBetweenItsFrames_ComesBackWithoutThem()
    {
        const string mixed = "7E8 10 0A 62 AB CD 11 22 33 7EF 10 0A 62 AB CD 01 02 03 7E8 21 44 55 66 77 00 00 00 7EF 21 04 05 06 07 00 00 00";

        Assert.Equal(TwoData, Data(mixed, Multi));                          // only 7EF's frames were assembled
        Assert.NotEqual(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77 }, Data(mixed, Multi));
    }

    [Fact]
    public void FramesOfTwoModules_AreNeverMergedIntoOneMessage()
    {
        // Each module's first frame announces 10 bytes. Merging the two would complete a message of the wrong module's bytes;
        // only the asked id's own frames count, and they are incomplete here.
        const string raw = "7EF 10 0A 62 AB CD 01 02 03 7E8 21 04 05 06 07 00 00 00";

        Assert.Null(Data(raw, Multi));
    }

    // ------------------------------------------------------------------ 8-10. missing, truncated and malformed

    [Theory]
    [InlineData("05 62 41 81 CE D8")]                                      // no identifier at all
    [InlineData("62 41 81 CE D8")]
    [InlineData("7EF")]                                                    // an identifier and no data
    [InlineData("7EF 7EF 05 62 41 81 CE D8 7EF")]                          // identifiers with nothing behind them
    public void TextWithoutAUsableIdentifier_IsNeverAnAnswer(string raw) => Assert.Null(Volts(raw));

    [Theory]
    [InlineData("7EF05624181CE")]                                          // cut in the middle of a frame
    [InlineData("7EF05624181CE 7E8 05 62 41 81 AA BB")]
    [InlineData("7EF05624181CED")]                                         // cut in the middle of a byte
    [InlineData("7EF 05 62 41 81 CE")]
    public void ATruncatedSingleFrame_IsNeverAnAnswer(string raw) => Assert.Null(Volts(raw));

    [Fact]
    public void AMultiFrameAnswerThatNeverCompletes_IsNeverAnswer_AndNoByteIsInvented()
    {
        Assert.Null(Data("7EF 10 0A 62 AB CD 01 02 03", Multi));                                // the consecutive frame is missing
        Assert.Null(Data("7EF 10 0A 62 AB CD 01 02 03 7EF 21 04 05", Multi));                    // the last frame is cut short
        Assert.Null(Data("7EF 10 11 62 AB CD 01 02 03 7EF 21 04 05 06 07 08 09 0A", Multi));     // the third frame is missing
        Assert.Null(Data("7EF 21 04 05 06 07 00 00 00 7EF 22 0B 0C 0D 0E 00 00 00", Multi));    // consecutive frames with no first frame
    }

    [Theory]
    [InlineData("7EF 0F 62 41 81 CE D8")]                                  // the length byte promises more than the frame holds
    [InlineData("7EF 00 62 41 81 CE D8")]                                  // an impossible length of zero
    [InlineData("7EF05624181CED8 GARBAGE")]                                // not frame text
    [InlineData("7EF05624181ZZ")]
    public void AnImpossibleLengthOrGarbage_IsNeverAnAnswer(string raw) => Assert.Null(Volts(raw));

    // ------------------------------------------------------------------ 11-12. echoes, negative responses, other supported formats

    [Fact]
    public void AnEchoOnItsOwnLine_IsIgnored_AndAnEchoJoinedOntoAFrame_StaysUnreadAsBefore()
    {
        Assert.Equal(Cell1Volts, Volts("224181\r7EF05624181CED8"));
        Assert.Null(Volts("224181 7EF05624181CED8"));                       // unchanged: not frame text alone, judged as one line
        Assert.Null(Data("22ABCD 7EF 10 0A 62 AB CD 01 02 03 7EF 21 04 05 06 07 00 00 00", Multi));   // echo + two frames: unchanged, not split
    }

    [Theory]
    [InlineData("7EF037F2231")]
    [InlineData("7EF 03 7F 22 31")]
    [InlineData("7E8 05 62 41 81 AA BB 7EF 03 7F 22 31")]                  // the module's refusal next to another module's frame
    [InlineData("NO DATA")]
    [InlineData("?")]
    [InlineData("")]
    public void NegativeAndStatusText_NeverGiveAnAnswer(string raw) => Assert.Null(Volts(raw));

    [Fact]
    public void AResponsePending_IsStillRecognised_InEveryLayout()
    {
        Assert.True(HvPackProtocol.IsResponsePending("7EF037F2278", Cell1));
        Assert.True(HvPackProtocol.IsResponsePending("7EF 03 7F 22 78", Cell1));
        Assert.True(HvPackProtocol.IsResponsePending("7E8 05 62 41 81 AA BB 7EF 03 7F 22 78", Cell1));
        Assert.False(HvPackProtocol.IsResponsePending("7EF 03 7F 22 31", Cell1));
    }

    [Fact]
    public void ExtendedAddressing_StillStripsTheTesterAddress_FromEveryFrame()
    {
        // BMW-style extended addressing: every frame of the answer starts with the tester address (F1).
        var bmw = new HvRequest(0x6F1, 0x607, new byte[] { 0x22, 0xAB, 0xCD }, ExtendedAddress: 0x07);
        const string joined = "607 F1 10 0A 62 AB CD 01 02 607 F1 21 03 04 05 06 07";

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, Data(joined, bmw));
        Assert.Null(Data("607 10 0A 62 AB CD 01 02 607 21 03 04 05 06 07", bmw));      // no tester address: not this reply
    }

    // ------------------------------------------------------------------ the splitter itself

    [Fact]
    public void TheSplitter_AndTheExistingSplit_FindTheSameFramesForTheSameText()
    {
        foreach (var text in new[] { Two, Three, "7E8 05 62 41 81 AA BB 7EF05624181CED8", "7EF05624181CED8 7E9 03 7F 22 31" })
        {
            var frames = ModuleReplyAnalysis.SplitFrameTexts(text, false)!;
            var split = ModuleReplyAnalysis.Split(text, 0x7EF, false);

            var ownFromTexts = frames.Where(frame => frame.Header == "7EF")
                .Select(frame => ModuleReplyAnalysis.Split(frame.Text, 0x7EF, false).Own.Single()).ToList();
            Assert.Equal(split.Own.Count, ownFromTexts.Count);
            for (var i = 0; i < ownFromTexts.Count; i++)
                Assert.Equal(split.Own[i], ownFromTexts[i]);
            Assert.Equal(split.Foreign.Sum(item => item.Frames), frames.Count(frame => frame.Header != "7EF"));
        }
    }

    [Theory]
    [InlineData("NO DATA")]
    [InlineData("224181 7EF05624181CED8")]
    [InlineData("7EF")]
    [InlineData("05 62 41 81")]
    [InlineData("7EF05624181CE7EF")]
    [InlineData("")]
    public void TheSplitter_RefusesTextThatIsNotMadeOfFramesAlone(string raw) => Assert.Null(ModuleReplyAnalysis.SplitFrameTexts(raw, false));

    [Fact]
    public void TheSplitter_KeepsEachFramesTextAndId_AndHandles29BitIds()
    {
        var frames = ModuleReplyAnalysis.SplitFrameTexts("7E8 05 62 41 81 AA BB 7EF05624181CED8", false)!;

        Assert.Equal(new[] { "7E8", "7EF" }, frames.Select(frame => frame.Header));
        Assert.Equal(new[] { "7E8 05 62 41 81 AA BB", "7EF05624181CED8" }, frames.Select(frame => frame.Text));

        var wide = ModuleReplyAnalysis.SplitFrameTexts("18DAF11005624181 18DAF1100562419A", true)!;
        Assert.Equal(new[] { "18DAF110", "18DAF110" }, wide.Select(frame => frame.Header));
    }

    // ------------------------------------------------------------------ through a real module session (the app-side filter)

    private static SimulatedElm327 ChipWithoutReceiveFilters(string moduleAnswer)
    {
        var sim = new SimulatedElm327();
        foreach (var command in new[] { "ATCRA", "ATAR", "ATCF" })
            sim.RejectedAt.Add(command);
        sim.ModuleAnswers["7E7|22ABCD"] = moduleAnswer;
        sim.ModuleAnswers["7E7|224181"] = moduleAnswer;
        return sim;
    }

    private static async Task<string> SessionReplyAsync(SimulatedElm327 sim, HvRequest request)
    {
        var rig = await AdapterRigs.CreateAsync(sim);
        var reply = string.Empty;
        await rig.Obd.RunDtcModuleSessionAsync(async session =>
            reply = await session.SendAsync(request.Tx, request.Rx, false, request.Payload, false, default));
        return reply;
    }

    [Fact]
    public async Task TheAppSideFilter_KeepsTheAskedModulesFrames_OneToALine_AndDropsTheOthers_OnAChipWithoutReceiveFilters()
    {
        var sim = ChipWithoutReceiveFilters("7E8 05 62 AB CD AA BB\r7EF 10 0A 62 AB CD 01 02 03\r7E9 03 7F 22 31\r7EF 21 04 05 06 07 00 00 00");

        var reply = await SessionReplyAsync(sim, Multi);

        // One frame to a line, the bytes untouched (spacing aside: the session asks the adapter for compact output).
        Assert.Equal(new[] { "7EF100A62ABCD010203", "7EF2104050607000000" }, reply.Split('\r').Select(line => line.Replace(" ", string.Empty)));
        Assert.Equal(TwoData, HvPackProtocol.ExtractData(reply, Multi));
    }

    [Fact]
    public async Task TheAppSideFilter_ReturnsNoDataWhenOnlyOtherModulesAnswered_AsBefore()
    {
        var sim = ChipWithoutReceiveFilters("7E8 05 62 AB CD AA BB\r7E9 03 7F 22 31");

        Assert.Equal("NO DATA", await SessionReplyAsync(sim, Multi));
    }

    [Fact]
    public async Task TheAppSideFilter_JudgesAnEchoLineAStatusWordAndASingleFrame_AsItDidBefore()
    {
        // An echo or status text in the joined line is not frame text alone: the line is judged as one line, exactly as before
        // (the echo makes the line start with something else than the id, so the whole line is dropped).
        var echo = ChipWithoutReceiveFilters("224181\r" + CellFrame);
        Assert.Equal("NO DATA", await SessionReplyAsync(echo, Cell1));

        var status = ChipWithoutReceiveFilters("NO DATA");
        Assert.Equal("NO DATA", await SessionReplyAsync(status, Cell1));

        var single = ChipWithoutReceiveFilters(CellFrame);                     // a plain single frame passes unchanged
        Assert.Equal(CellFrame, (await SessionReplyAsync(single, Cell1)).Replace(" ", string.Empty));
    }

    [Fact]
    public async Task TheVoltCellReader_StillReadsEveryCell_WithAndWithoutAHardwareFilter_AndWhenTheEngineAnswersToo()
    {
        foreach (var chipWithFilters in new[] { true, false })
        foreach (var engineAlso in new[] { false, true })
        {
            var sim = new SimulatedElm327();
            if (!chipWithFilters)
                foreach (var command in new[] { "ATCRA", "ATAR", "ATCF" })
                    sim.RejectedAt.Add(command);
            for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            {
                var did = GmVoltBoltBattery.CellDid(index);
                var frame = "7EF05" + "62" + did.ToString("X4") + (index == 0 ? "CED8" : "CE80");
                sim.ModuleAnswers[$"7E7|22{did:X4}"] = engineAlso && !chipWithFilters ? "7E8 05 62 41 81 AA BB\r" + frame : frame;
            }

            var rig = await AdapterRigs.CreateAsync(sim);
            var reading = await new HybridHealthService(rig.Obd).ReadGmPackAsync(
                new CarScanner.Models.VehicleInfo { Manufacturer = "Chevrolet", Model = "Volt", Year = "2012" }, false,
                new System.Text.StringBuilder(), null, CancellationToken.None);

            Assert.True(reading is not null, $"filters: {chipWithFilters}, engine also: {engineAlso}");
            Assert.Equal(96, reading!.Blocks.Length);
            Assert.Equal(Cell1Volts, reading.Blocks[0], 4);
        }
    }

    // ------------------------------------------------------------------ what did not change (pinned from the source)

    [Fact]
    public void TheRequestsAndTheProtocolLayersTextCleaning_AreNotTouched()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        var protocol = File.ReadAllText(Path.Combine(dir!.FullName, "Services", "OBD", "Elm327Protocol.cs")).Replace("\r\n", "\n");

        // CleanResponse still joins the lines with a space for every consumer: the fix lives in the two line-based parsers.
        var clean = protocol[protocol.IndexOf("private static string CleanResponse(string raw)", StringComparison.Ordinal)..];
        clean = clean[..clean.IndexOf("private static bool ContainsToken", StringComparison.Ordinal)];
        Assert.Contains("var joined = string.Join(\" \", lines);", clean);
        Assert.Contains("joined = Regex.Replace(joined, @\"\\s+\", \" \").Trim();", clean);
        // The filter's decisions about a line that is not several frames are the old ones.
        Assert.Contains("compact.Length > id.Length && compact.StartsWith(id, StringComparison.Ordinal)", protocol);
        Assert.Contains("return \"NO DATA\";", protocol);
    }
}
