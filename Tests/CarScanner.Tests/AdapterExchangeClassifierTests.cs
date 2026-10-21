using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

/// <summary>
/// Stage 0: the labels the connection log and the optional raw capture put on an exchange. Pure text in, a label out; none
/// of this sends anything or decides anything in the app. Frames are copied from the captures other tests already use.
/// </summary>
public sealed class AdapterExchangeClassifierTests
{
    [Theory]
    [InlineData("ATL0", "OK\r\r>", ExchangeKind.Accepted)]
    [InlineData("ATL0", "?\r\r>", ExchangeKind.AdapterRejected)]
    [InlineData("0100", "NO DATA\r\r>", ExchangeKind.NoData)]
    [InlineData("0100", "41 00 BE 3F A8 13\r\r>", ExchangeKind.Data)]
    [InlineData("0100", "CAN ERROR\r\r>", ExchangeKind.AdapterStatus)]
    [InlineData("0100", "BUFFER FULL", ExchangeKind.AdapterStatus)]
    [InlineData("0100", "UNABLE TO CONNECT", ExchangeKind.AdapterStatus)]
    [InlineData("0100", "BUS INIT: ERROR", ExchangeKind.AdapterStatus)]
    [InlineData("0100", "SEARCHING...", ExchangeKind.Searching)]
    [InlineData("0100", ">", ExchangeKind.Empty)]
    [InlineData("0100", "", ExchangeKind.Empty)]
    [InlineData("0100", null, ExchangeKind.Empty)]
    [InlineData("ATI", "ELM327 v1.5\r\r>", ExchangeKind.Text)]
    [InlineData("ATRV", "12.6V\r\r>", ExchangeKind.Text)]
    [InlineData("03", "43 01 33 00 00 00 00", ExchangeKind.Data)]
    [InlineData("22119F", "7E8 04 62 11 9F 80", ExchangeKind.Data)]
    public void AnAdapterReply_IsPutInTheKindItBelongsTo(string command, string? reply, ExchangeKind expected) =>
        Assert.Equal(expected, AdapterExchangeClassifier.Classify(command, reply).Kind);

    [Theory]
    // An adapter that went on echoing although ATE0 was sent: the reply starts with the command that was sent.
    [InlineData("ATL0", "ATL0\rOK\r\r>", ExchangeKind.Echoed)]
    [InlineData("ATSH7E1", "ATSH7E1 OK", ExchangeKind.Echoed)]
    [InlineData("0100", "0100\r41 00 BE 3F A8 13\r\r>", ExchangeKind.Echoed)]
    [InlineData("22119F", "22119F\r7E8 04 62 11 9F 80", ExchangeKind.Echoed)]
    // The command that turns echo off is legitimately echoed (echo was still on when it arrived); the banner after ATZ is not an echo verdict either.
    [InlineData("ATE0", "ATE0\rOK\r\r>", ExchangeKind.Accepted)]
    [InlineData("ATZ", "ATZ\r\rELM327 v1.5\r\r>", ExchangeKind.Text)]
    // Too short to tell an echo from data: not judged.
    [InlineData("03", "03\r43 00", ExchangeKind.Data)]
    public void Echo_IsRecognisedOnlyWhereItIsAFinding(string command, string reply, ExchangeKind expected) =>
        Assert.Equal(expected, AdapterExchangeClassifier.Classify(command, reply).Kind);

    [Theory]
    [InlineData(ExchangeKind.Accepted, ExchangeLayer.None)]
    [InlineData(ExchangeKind.Data, ExchangeLayer.None)]
    [InlineData(ExchangeKind.Cancelled, ExchangeLayer.None)]
    [InlineData(ExchangeKind.TimedOut, ExchangeLayer.Transport)]
    [InlineData(ExchangeKind.LinkLost, ExchangeLayer.Transport)]
    [InlineData(ExchangeKind.AdapterRejected, ExchangeLayer.Adapter)]
    [InlineData(ExchangeKind.AdapterStatus, ExchangeLayer.Adapter)]
    [InlineData(ExchangeKind.Echoed, ExchangeLayer.Adapter)]
    [InlineData(ExchangeKind.Empty, ExchangeLayer.Adapter)]
    [InlineData(ExchangeKind.NoData, ExchangeLayer.Ecu)]
    [InlineData(ExchangeKind.NegativeResponse, ExchangeLayer.Ecu)]
    [InlineData(ExchangeKind.ForeignReply, ExchangeLayer.Ecu)]
    [InlineData(ExchangeKind.Incomplete, ExchangeLayer.Reply)]
    [InlineData(ExchangeKind.Unexpected, ExchangeLayer.Reply)]
    [InlineData(ExchangeKind.Fault, ExchangeLayer.Transport)]
    public void EachKind_PointsAtOneLayer_SoALogCanSeparateTransportFromAdapterFromCarFromParsing(ExchangeKind kind, ExchangeLayer layer) =>
        Assert.Equal(layer, AdapterExchangeClassifier.LayerOf(kind));

    [Fact]
    public void ExceptionsAreSortedByWhereTheyCameFrom()
    {
        Assert.Equal(ExchangeKind.TimedOut, AdapterExchangeClassifier.Classify(new Elm327TimeoutException("t")).Kind);
        Assert.Equal(ExchangeKind.TimedOut, AdapterExchangeClassifier.Classify(new TimeoutException()).Kind);
        Assert.Equal(ExchangeKind.LinkLost, AdapterExchangeClassifier.Classify(new ObdConnectionLostException("x")).Kind);
        Assert.Equal(ExchangeKind.LinkLost, AdapterExchangeClassifier.Classify(new Elm327NotConnectedException("x")).Kind);
        Assert.Equal(ExchangeKind.Cancelled, AdapterExchangeClassifier.Classify(new OperationCanceledException()).Kind);
        Assert.Equal(ExchangeKind.Cancelled, AdapterExchangeClassifier.Classify(new TaskCanceledException()).Kind);
        Assert.Equal(ExchangeKind.AdapterRejected, AdapterExchangeClassifier.Classify(new Elm327CommandException("x", "?")).Kind);
        Assert.Equal(ExchangeKind.AdapterStatus, AdapterExchangeClassifier.Classify(new Elm327CommandException("x", "CAN ERROR")).Kind);
        Assert.Equal(ExchangeKind.NoData, AdapterExchangeClassifier.Classify(new Elm327CommandException("x", "NO DATA")).Kind);
        // Plain data or text inside a command exception is not a verdict.
        Assert.Equal(ExchangeKind.Unexpected, AdapterExchangeClassifier.Classify(new Elm327CommandException("x", "41 00 BE")).Kind);
        // An exception that is none of the above is a fault before any reply was read, not "a reply that matched nothing".
        Assert.Equal(ExchangeKind.Fault, AdapterExchangeClassifier.Classify(new InvalidCastException()).Kind);
    }

    [Fact]
    public void AnUnrecognisedFault_IsATransportLevelFinding_NeverAReplyThatFailedToParse()
    {
        var faults = new Exception[]
        {
            new NullReferenceException(),                  // what a BLE link lost during a request surfaces as (see BleTransportScenarioTests)
            new InvalidOperationException("not connected"), // what a send on a transport that is already down throws
            new ObjectDisposedException("transport")
        };

        foreach (var fault in faults)
        {
            var assessment = AdapterExchangeClassifier.Classify(fault);

            Assert.Equal(ExchangeKind.Fault, assessment.Kind);
            Assert.Equal(ExchangeLayer.Transport, assessment.Layer);
            Assert.Equal("Fault [transport]", assessment.ToString());
        }
    }

    [Theory]
    [InlineData("7E8 04 62 11 9F 80", ExchangeKind.Data)]
    [InlineData("7E80462119F80", ExchangeKind.Data)]
    [InlineData("NO DATA", ExchangeKind.NoData)]
    [InlineData("?", ExchangeKind.AdapterRejected)]
    [InlineData("CAN ERROR", ExchangeKind.AdapterStatus)]
    [InlineData("7E8 03 7F 22 31", ExchangeKind.NegativeResponse)]
    [InlineData("7E8 03 7F 22 78", ExchangeKind.ResponsePending)]
    [InlineData("7E8 10 08 62 11 9F 80", ExchangeKind.Incomplete)]
    [InlineData("7E9 04 62 11 9F 11", ExchangeKind.ForeignReply)]
    [InlineData("7E8 04 62 4D C0 80", ExchangeKind.Unexpected)]
    public void RepliesThatCarryFrames_AreJudgedAgainstTheAskedModule(string reply, ExchangeKind expected) =>
        Assert.Equal(expected, AdapterExchangeClassifier.Classify("22119F", reply, 0x7E8, false).Kind);

    [Fact]
    public void TheOldFailureVocabulary_MapsOntoTheExchangeKinds_WithoutLosingAnyOfIt()
    {
        foreach (var kind in Enum.GetValues<AdapterFailureKind>())
        {
            var mapped = AdapterExchangeClassifier.FromFailure(new AdapterFailure(kind, "x")).Kind;
            Assert.True(Enum.IsDefined(mapped));
            // The only kind that means "nothing failed" must stay the one that is not a failure.
            Assert.Equal(kind == AdapterFailureKind.None, mapped == ExchangeKind.Data);
        }
    }

    [Theory]
    [InlineData(0x7E0, false, 0x7E8)]
    [InlineData(0x7E1, false, 0x7E9)]
    [InlineData(0x7E7, false, 0x7EF)]
    public void TheIsoPairingIsAppliedOnlyToTheEightStandardRequestIds(int tx, bool ext29, int expected) =>
        Assert.Equal(expected, AdapterExchangeClassifier.StandardReplyId(tx, ext29));

    [Theory]
    [InlineData(0x7DF, false)]
    [InlineData(0x7E8, false)]
    [InlineData(0x726, false)]
    [InlineData(0x7B0, false)]
    [InlineData(0x7E1, true)]
    [InlineData(0x18DA10F1, true)]
    public void NoOtherIdIsGivenAnAssumedReplyId(int tx, bool ext29) =>
        Assert.Null(AdapterExchangeClassifier.StandardReplyId(tx, ext29));

    // ------------------------------------------------------------------ the one line a physical read leaves in the log

    // Copied from the Ford capture the mileage tests use: "22 DD 01" addressed to 0x7E1 was answered by the engine ECU on 0x7E8.
    private const string FordDd01FromEngine = "7E8 06 62 DD 01 03 B5 8F";

    [Fact]
    public void AReadAnsweredByTheEngineEcu_ForAnotherModulesRequest_IsLoggedAsAForeignReply_WithoutTheDataBytes()
    {
        var line = AdapterExchangeClassifier.DescribeRead(0x7E1, null, false, "22DD01", FordDd01FromEngine, TimeSpan.FromMilliseconds(412));

        Assert.Contains("tx 7E1", line);
        Assert.Contains("standard pair 7E9", line);          // what ISO 15765-4 pairs with 7E1; stated, not assumed to be right
        Assert.Contains("frames from 7E8x1", line);
        Assert.Contains("ForeignReply [ecu]", line);
        Assert.Contains("412 ms", line);
        Assert.DoesNotContain("B5", line);                     // the value is not part of the always-on log
        Assert.DoesNotContain("03 B5 8F", line);
    }

    [Fact]
    public void AReadAnsweredByTheAskedModule_IsLoggedAsData()
    {
        var line = AdapterExchangeClassifier.DescribeRead(0x7E0, 0x7E8, false, "22119F", "7E8 04 62 11 9F 80", TimeSpan.FromMilliseconds(30));

        Assert.Contains("reply id 7E8", line);
        Assert.Contains("frames from 7E8x1", line);
        Assert.Contains("; Data;", line);
    }

    [Fact]
    public void AReadWithNoAnswer_IsLoggedAsSilence_NotAsAnAdapterFailure()
    {
        var line = AdapterExchangeClassifier.DescribeRead(0x7E0, 0x7E8, false, "22119F", "NO DATA", TimeSpan.FromMilliseconds(5000));

        Assert.Contains("frames from none", line);
        Assert.Contains("NoData [ecu]", line);
        Assert.DoesNotContain("[adapter]", line);
    }

    [Fact]
    public void AModuleWithNoGivenReplyId_AndNoStandardPair_IsNotJudged_OnlyItsSendersAreListed()
    {
        var line = AdapterExchangeClassifier.DescribeRead(0x726, null, false, "22DD01", FordDd01FromEngine, TimeSpan.FromMilliseconds(80));

        Assert.Contains("reply id not given;", line);
        Assert.DoesNotContain("standard pair", line);
        Assert.Contains("frames from 7E8x1", line);
        Assert.DoesNotContain("ForeignReply", line);            // nothing is known about which id 726 answers on
    }

    // ------------------------------------------------------------------ the module-session count

    [Fact]
    public void TheTally_CountsKindsAndSenders_InAStableOrder()
    {
        var tally = new ExchangeTally();
        tally.Add(new ExchangeAssessment(ExchangeKind.NoData));
        tally.Add(new ExchangeAssessment(ExchangeKind.Data));
        tally.Add(new ExchangeAssessment(ExchangeKind.Data));
        tally.NoteSenders("7EF05624181CED8", 0x7EF, false);
        tally.NoteSenders("7EF 05 62 41 82 CE D8", 0x7EF, false);
        tally.NoteSenders("7E8 06 62 DD 01 03 B5 8F", 0x7EF, false);   // only another module answered this one
        tally.NoteSenders("NO DATA", 0x7EF, false);

        var text = tally.ToString();

        Assert.Equal(3, tally.Requests);
        Assert.Contains("3 request(s): Data x2, NoData x1", text);
        Assert.Contains("frames from 7E8 x1, 7EF x2", text);
        Assert.Contains("1 reply came only from other modules", text);
    }

    [Fact]
    public void AnEmptyTally_SaysSo()
    {
        Assert.Equal("0 request(s): nothing sent; frames from none", new ExchangeTally().ToString());
    }
}
