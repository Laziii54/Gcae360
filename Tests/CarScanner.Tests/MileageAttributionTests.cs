using CarScanner.Services.Mileage;
using CarScanner.Services.OBD;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// A positive reply that echoes the asked identifier is not proof of WHO sent it. The Ford capture shows
/// "22 DD 01" addressed to 0x7E1 (reply id 0x7E9) answered by the engine ECU on 0x7E8; the old fallback
/// accepted any id and would have credited the value to the transmission. Frames: copied from that capture.
/// </summary>
public sealed class MileageAttributionTests
{
    private const string FordDd01FromEngine = "7E8 06 62 DD 01 03 B5 8F";        // asked 0x7E0 → replied 0x7E8 (consistent)
    private const string ToyotaRefusalFromEngine = "7E8 03 7F 22 11";            // asked another module, 0x7E8 refused

    private static OdometerSignalSpec Dd01() => new(BitOffset: 0, BitLength: 24, Mul: 1, Div: 1, Add: 0, Miles: false, Meters: false);

    [Fact]
    public void RealFordReply_FromTheAskedModule_IsAccepted_AndNamesTheResponder()
    {
        var payload = OdometerResponseDecoder.ExtractPayload(FordDd01FromEngine, 0x22, 0xDD01, 0x7E8, out var failure, out var responder);

        Assert.Equal(OdometerDecodeFailure.None, failure);
        Assert.Equal(0x7E8, responder);
        Assert.Equal(new byte[] { 0x03, 0xB5, 0x8F }, payload);
        // 0x03B58F = 243 087: a number, whose unit this repository does not know. The decoder only applies the spec it is given.
        Assert.Equal(243_087, OdometerResponseDecoder.Decode(Dd01(), payload!).Km!.Value, 6);
    }

    [Fact]
    public void RealFordReply_ForAnotherModulesRequest_IsRejected_AsForeign()
    {
        // Same frames, but the request was addressed to 0x7E1 (reply id 0x7E9).
        var payload = OdometerResponseDecoder.ExtractPayload(FordDd01FromEngine, 0x22, 0xDD01, 0x7E9, out var failure, out var responder);

        Assert.Null(payload);
        Assert.Equal(OdometerDecodeFailure.ForeignReply, failure);
        Assert.Equal(0x7E8, responder);
    }

    [Fact]
    public void RealToyotaRefusal_FromAnotherModule_IsNotReportedAsThatModulesRefusal()
    {
        var payload = OdometerResponseDecoder.ExtractPayload(ToyotaRefusalFromEngine, 0x22, 0x1017, 0x708, out var failure, out _);

        Assert.Null(payload);
        Assert.Equal(OdometerDecodeFailure.ForeignReply, failure);
    }

    [Fact]
    public void WithoutAKnownReplyId_TheResponderIsStillNamed_NotAssumed()
    {
        var payload = OdometerResponseDecoder.ExtractPayload(FordDd01FromEngine, 0x22, 0xDD01, null, out var failure, out var responder);

        Assert.NotNull(payload);
        Assert.Equal(OdometerDecodeFailure.None, failure);
        Assert.Equal(0x7E8, responder);
    }

    [Fact]
    public void ReplyThatCarriesBothTheAskedModuleAndAnother_UsesTheAskedModule()
    {
        // The engine ECU's frame arrives first, the asked module's own reply second.
        const string both = "7E8 06 62 DD 01 00 00 01 7E9 06 62 DD 01 03 B5 8F";

        var payload = OdometerResponseDecoder.ExtractPayload(both, 0x22, 0xDD01, 0x7E9, out var failure, out var responder);

        Assert.Equal(OdometerDecodeFailure.None, failure);
        Assert.Equal(0x7E9, responder);
        Assert.Equal(new byte[] { 0x03, 0xB5, 0x8F }, payload);
    }

    [Fact]
    public void AdapterThatPrintsNoIds_StillWorks_WhenAReplyIdIsExpected()
    {
        // Headers off: nothing can be attributed or rejected by id, so the reply is judged on its content as before.
        var payload = OdometerResponseDecoder.ExtractPayload("06 62 DD 01 03 B5 8F", 0x22, 0xDD01, 0x7E8, out var failure);

        Assert.Equal(OdometerDecodeFailure.None, failure);
        Assert.NotNull(payload);
    }

    [Fact]
    public void TwentyNineBitIds_AreCompared_ByTheFullId()
    {
        // eight-digit ids are printed without spaces by the adapter
        const string spaced = "18DAF118 06 62 DD 01 03 B5 8F";

        Assert.Null(OdometerResponseDecoder.ExtractPayload(spaced, 0x22, 0xDD01, 0x18DAF110, out var failure));
        Assert.Equal(OdometerDecodeFailure.ForeignReply, failure);
        Assert.NotNull(OdometerResponseDecoder.ExtractPayload(spaced, 0x22, 0xDD01, 0x18DAF118, out _));
    }

    [Theory]
    [InlineData("7E8 06 62 DD 01 03 B5 8F", 0x7E9, true)]
    [InlineData("7E8 06 62 DD 01 03 B5 8F", 0x7E8, false)]
    [InlineData("7E8 06 62 DD 01 03 B5 8F", null, false)]
    [InlineData("NO DATA", 0x7E9, false)]
    [InlineData("", 0x7E9, false)]
    public void IsOnlyFromOtherModules_OnlyFiresWhenFramesArrivedAndNoneIsFromTheAskedModule(string raw, int? expected, bool foreign) =>
        Assert.Equal(foreign, ObdResponseParser.IsOnlyFromOtherModules(raw, expected, out _));

    [Theory]
    [InlineData(0x7C0, null, 0x7E8, true)]      // Toyota capture: cluster address, the engine ECU answered
    [InlineData(0x7C0, 0x7C8, 0x7C8, false)]    // the cluster answered on its own id
    [InlineData(0x7C0, 0x7C8, 0x7E8, true)]     // reply id known: anything else is foreign
    [InlineData(0x7E0, null, 0x7E8, false)]     // ISO 15765-4 pair
    [InlineData(0x7E1, null, 0x7E8, true)]      // Ford capture: transmission address, engine answered
    [InlineData(0x7E1, null, 0x7E9, false)]
    [InlineData(0x7DF, null, 0x7E8, false)]     // functional request: any OBD ECU may answer
    [InlineData(0x7B0, null, 0x7B8, false)]     // other module with its own, unknown reply id: not judged
    [InlineData(0x7E1, null, null, false)]      // no frames with ids: nothing to judge
    [InlineData(0x18DA10F1, null, 0x18DAF110, false)] // 29-bit ids are not judged here
    public void IsResponderForeign_FollowsTheIsoPairs(int tx, int? expected, int? responder, bool foreign) =>
        Assert.Equal(foreign, OdometerResponseDecoder.IsResponderForeign(tx, expected, responder));

    [Fact]
    public void A_Foreign_Reply_Never_Becomes_An_Odometer_Reading()
    {
        // End to end through the pure pieces: nothing decodes, so nothing can reach the analyzer.
        var payload = OdometerResponseDecoder.ExtractPayload(FordDd01FromEngine, 0x22, 0xDD01, 0x7E9, out _);
        var evidence = new List<MileageEvidence>();
        if (payload is not null)
        {
            var decoded = OdometerResponseDecoder.Decode(Dd01(), payload);
            evidence.Add(new MileageEvidence("test", "Transmission", decoded.Km!.Value, 0.8, ""));
        }

        var analysis = MileageAnalyzer.Analyze(evidence);
        Assert.Equal(MileageOutcome.NoReading, analysis.Outcome);
        Assert.Null(analysis.Primary);
    }
}
