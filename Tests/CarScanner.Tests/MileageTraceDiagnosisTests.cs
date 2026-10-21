using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Mileage;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stage 2A, fix C: the technical log of the Original Mileage scan tells apart what each request showed — no reply, a negative
/// response, an adapter or transport failure, a malformed reply, another ECU answering, a decoded value, a value that is not usable
/// as evidence. This is observability only: the outcome, the evidence and the mileage are decided by the same code as before.
/// The provider class (<c>CatalogOdometerProvider</c>) needs the app's SQLite catalog and cannot run here, so each test performs the
/// provider's own read steps on the REAL pieces (the protocol layer on a simulated adapter, then the decoder) and then calls the
/// same <see cref="MileageReadDiagnosis"/> functions the provider calls; the provider's use of them is pinned from its source.
/// SIMULATION ONLY. The Ford frame is the one in MileageAttributionTests (a real capture of 22 DD 01 answered by the engine ECU).
/// </summary>
public sealed class MileageTraceDiagnosisTests
{
    private const string FordDd01FromEngine = "7E8 06 62 DD 01 03 B5 8F";
    private static readonly byte[] Dd01 = { 0x22, 0xDD, 0x01 };
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromMilliseconds(1800);
    private const string Method = "0x22 0xDD01 @ 7E1";

    private static ServiceDiagnosticAddress Addr(int tx, int? rx) => new(tx, rx, false, null, "11-bit CAN", "module");

    private static OdometerSignalSpec Spec(int bitLength = 24, double div = 1) => new(BitOffset: 0, BitLength: bitLength, Mul: 1, Div: div, Add: 0, Miles: false, Meters: false);

    private sealed record Observed(MileageReadObservation? Observation, OdometerDecodeFailure Failure, byte[]? Payload, int? Responder, string? Raw, Exception? Error);

    /// <summary>The steps of CatalogOdometerProvider.ReadAsync, then the observation the provider records for them.</summary>
    private static async Task<Observed> ReadAsObservedAsync(AdapterRig rig, ServiceDiagnosticAddress address)
    {
        string raw;
        try
        {
            raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(address, Dd01, ProviderTimeout);
        }
        catch (Exception ex)
        {
            // As the provider does: the state of the link when the request failed goes with the exception.
            return new Observed(MileageReadDiagnosis.OfException(ex, linkDown: !rig.Obd.IsAdapterConnected), OdometerDecodeFailure.None, null, null, null, ex);
        }

        var data = OdometerResponseDecoder.ExtractPayload(raw, 0x22, 0xDD01, address.ReceiveCanId, out var failure, out var responder);
        var observation = data is null ? MileageReadDiagnosis.OfReply(raw, address.ReceiveCanId, 0x22, 0xDD01) : null;
        return new Observed(observation, failure, data, responder, raw, null);
    }

    private static async Task<Observed> ReadScriptedAsync(string reply, int? receiveId = 0x7E8)
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        var rig = await AdapterRigs.CreateAsync(scripted);
        scripted.Reply("22DD01", reply);
        return await ReadAsObservedAsync(rig, Addr(0x7E0, receiveId));
    }

    // ------------------------------------------------------------------ no reply

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task ASilentModule_IsNoReply_AndTheLogSaysNoData_OverEveryTransport(SimTransport transport)
    {
        var seen = await TransportRunner.RunAsync(transport, new SimulatedElm327(), rig => ReadAsObservedAsync(rig, Addr(0x7E0, 0x7E8)));

        Assert.Equal("NO DATA", seen.Raw);
        Assert.Equal(MileageReadCategory.NoReply, seen.Observation!.Category);
        Assert.Contains("NO DATA", seen.Observation.Text);
        Assert.Contains("does not prove the ECU lacks the function", seen.Observation.Text);    // silence is not a refusal
        Assert.Equal("[no reply] " + seen.Observation.Text, seen.Observation.ToString());
    }

    [Fact]
    public async Task ARequestThatTimesOut_IsNoReply_NotAnAdapterFailure()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22DD01"] = FordDd01FromEngine;
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.Fault = SimFault.Timeout;

        var seen = await ReadAsObservedAsync(rig, Addr(0x7E0, 0x7E8));

        Assert.IsType<Elm327TimeoutException>(seen.Error);
        Assert.Equal(MileageReadCategory.NoReply, seen.Observation!.Category);
        Assert.Equal("no reply in time", seen.Observation.Text);
        Assert.Equal("Elm327TimeoutException — [no reply] no reply in time", MileageReadDiagnosis.ErrorDetail(seen.Error!));
    }

    [Fact]
    public async Task AResponsePendingThatNeverGetsAFinalAnswer_IsNoReply_NotAMalformedReply()
    {
        var seen = await ReadScriptedAsync("7E8 03 7F 22 78");

        Assert.Equal(MileageReadCategory.NoReply, seen.Observation!.Category);
        Assert.Contains("response pending", seen.Observation.Text);
    }

    [Fact]
    public void AnEmptyReply_IsNoReply()
    {
        var observation = MileageReadDiagnosis.OfReply("   ", 0x7E8, 0x22, 0xDD01);

        Assert.Equal(MileageReadCategory.NoReply, observation.Category);
        Assert.Equal("no text came back", observation.Text);
    }

    // ------------------------------------------------------------------ an explicit negative response

    [Theory]
    [InlineData("7E8 03 7F 22 31", "NRC 0x31")]    // request out of range
    [InlineData("7E8 03 7F 22 11", "NRC 0x11")]    // service not supported
    [InlineData("7E8 03 7F 22 33", "NRC 0x33")]    // security access denied
    public async Task ANegativeResponse_IsToldApartFromSilence_WithItsCode(string refusal, string code)
    {
        var seen = await ReadScriptedAsync(refusal);

        Assert.Equal(OdometerDecodeFailure.Negative, seen.Failure);              // the decoder files it with silence, as before...
        Assert.Equal(MileageReadCategory.NegativeResponse, seen.Observation!.Category);   // ...the log does not
        Assert.Contains("ECU refused", seen.Observation.Text);
        Assert.Contains(code, seen.Observation.Text);
    }

    [Fact]
    public async Task ASilentModuleAndARefusingModule_ShareTheDecoderFailure_ButHaveDifferentLogLines()
    {
        var silent = await ReadScriptedAsync("NO DATA");
        var refusing = await ReadScriptedAsync("7E8 03 7F 22 31");

        Assert.Equal(silent.Failure, refusing.Failure);                          // both OdometerDecodeFailure.Negative: unchanged
        var silentLine = MileageReadDiagnosis.FailureLine(Method, silent.Failure, silent.Observation!, records: false);
        var refusingLine = MileageReadDiagnosis.FailureLine(Method, refusing.Failure, refusing.Observation!, records: false);
        Assert.NotEqual(silentLine, refusingLine);
        Assert.StartsWith($"{Method}: Negative — [no reply]", silentLine);
        Assert.StartsWith($"{Method}: Negative — [negative response] ECU refused:", refusingLine);
    }

    // ------------------------------------------------------------------ adapter or transport failure

    [Fact]
    public async Task AnAdapterThatRefusesASetupCommand_IsAnAdapterFailure_NamingTheCommand()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22DD01"] = FordDd01FromEngine;
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.RejectedAt.Add("ATH1");

        var seen = await ReadAsObservedAsync(rig, Addr(0x7E0, 0x7E8));

        Assert.IsType<Elm327CommandException>(seen.Error);
        Assert.Equal(MileageReadCategory.AdapterOrTransport, seen.Observation!.Category);
        Assert.Contains("does not accept ATH1", seen.Observation.Text);
        Assert.Contains("not of the car", seen.Observation.Text);
    }

    [Theory]
    [InlineData("?")]
    [InlineData("UNABLE TO CONNECT")]
    [InlineData("CAN ERROR")]
    public async Task AnAdapterThatAnswersWithAStatusInsteadOfAReply_IsAnAdapterFailure_WhetherItIsRaisedOrReturned(string status)
    {
        var seen = await ReadScriptedAsync(status);

        Assert.Equal(MileageReadCategory.AdapterOrTransport, seen.Observation!.Category);
    }

    [Fact]
    public async Task ALinkThatDropsWhileARequestIsOutstanding_IsAnAdapterOrTransportFailure_NotSilence()
    {
        // The protocol layer ends its retries of a request on a dropped link with its own error; the state of the link when the
        // request failed is what the log uses to say the link was down (it is existing evidence: OBDService.IsAdapterConnected).
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22DD01"] = FordDd01FromEngine;
        var ble = await BleHarness.CreateAsync(sim);
        var rig = await AdapterRigs.CreateAsync(ble.Service, connect: false);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line == "22DD01")
                ble.LoseTheLink();
            return Task.CompletedTask;
        };

        var seen = await ReadAsObservedAsync(rig, Addr(0x7E0, 0x7E8));

        Assert.NotNull(seen.Error);
        Assert.False(rig.Obd.IsAdapterConnected);
        Assert.Equal(MileageReadCategory.AdapterOrTransport, seen.Observation!.Category);
        Assert.Equal("the link to the adapter was down when the request failed", seen.Observation.Text);
        Assert.StartsWith($"{Method}: error ({seen.Error!.GetType().Name}) — [adapter/transport] the link to the adapter was down", MileageReadDiagnosis.ErrorLine(Method, seen.Error, records: false, linkDown: true));
        Assert.Equal(seen.Error.GetType().Name + " — [adapter/transport] the link to the adapter was down when the request failed", MileageReadDiagnosis.ErrorDetail(seen.Error, linkDown: true));
    }

    [Fact]
    public void WithoutTheStateOfTheLink_ATimeoutIsStillNoReply_AsBefore()
    {
        var timeout = new Elm327TimeoutException("Command '22DD01' failed after 3 attempt(s).");

        Assert.Equal(MileageReadCategory.NoReply, MileageReadDiagnosis.OfException(timeout).Category);
        Assert.Equal(MileageReadCategory.NoReply, MileageReadDiagnosis.OfException(timeout, linkDown: false).Category);
        Assert.Equal(MileageReadCategory.AdapterOrTransport, MileageReadDiagnosis.OfException(timeout, linkDown: true).Category);
    }

    [Fact]
    public void ALostLink_IsAnAdapterOrTransportFailure_InEveryFormItCanTake()
    {
        foreach (var exception in new Exception[] { new IOException("link"), new ObdConnectionLostException("lost"), new Elm327NotConnectedException("gone"), new AdapterNotConnectedException("not connected") })
        {
            var observation = MileageReadDiagnosis.OfException(exception);

            Assert.Equal(MileageReadCategory.AdapterOrTransport, observation.Category);
            Assert.Equal("the link to the adapter was lost", observation.Text);
        }
    }

    [Fact]
    public void AnUnclassifiedException_IsNamedByItsType_AndIsNotSaidToBeSilence()
    {
        var observation = MileageReadDiagnosis.OfException(new InvalidCastException("something"));

        Assert.Equal(MileageReadCategory.AdapterOrTransport, observation.Category);
        Assert.Equal("the request failed with InvalidCastException (not classified)", observation.Text);
    }

    // ------------------------------------------------------------------ a malformed reply

    [Theory]
    [InlineData("7E8 06 62 DD 02 03 B5 8F", "different identifier")]      // an answer, but for another identifier
    [InlineData("7E8 06 63 DD 01 03 B5 8F", "not the answer to this request")]   // not the positive code of this service
    [InlineData("hello world", "not the answer to this request")]       // text that is not an answer
    public async Task AReplyThatIsNotThisRequestsAnswer_IsMalformed_AndTheLogSaysHow(string raw, string expectedPhrase)
    {
        var seen = await ReadScriptedAsync(raw);

        Assert.Null(seen.Payload);
        Assert.Equal(MileageReadCategory.MalformedReply, seen.Observation!.Category);
        Assert.Contains(expectedPhrase, seen.Observation.Text);
    }

    [Fact]
    public async Task ATruncatedMultiFrameReply_IsTakenByTheUnchangedDecoderAsTheBytesThatArrived_AndTheLogShowsItAtTheDecode()
    {
        // The first frame of a 11-byte message and nothing after it. The decoder (not changed by Stage 2A) hands on the bytes that
        // arrived as the answer's data; the failure only appears when the 24-bit signal does not fit in them. The log then says so.
        const string raw = "7E8 10 0B 62 DD 01 00 00";
        var seen = await ReadScriptedAsync(raw);

        Assert.Equal(new byte[] { 0x00, 0x00 }, seen.Payload);
        Assert.Null(seen.Observation);                                           // a payload came back: nothing failed at this step
        var decoded = OdometerResponseDecoder.Decode(Spec(), seen.Payload!);
        Assert.Equal(OdometerDecodeFailure.TooShort, decoded.Failure);
        var undecoded = MileageReadDiagnosis.OfDecodeFailure(decoded.Failure);
        Assert.Equal($"{Method}: reply could not be decoded (TooShort) 0000 — [malformed reply] the reply is shorter than the signal definition needs",
            MileageReadDiagnosis.UndecodedLine(Method, decoded.Failure, "0000", undecoded));

        // Asked about the TEXT alone (a reply that gave no payload at all), the description says the message did not arrive completely.
        var text = MileageReadDiagnosis.OfReply(raw, 0x7E8, 0x22, 0xDD01);
        Assert.Equal(MileageReadCategory.MalformedReply, text.Category);
        Assert.Contains("did not arrive completely", text.Text);
    }

    [Theory]
    [InlineData(OdometerDecodeFailure.TooShort, MileageReadCategory.MalformedReply, "shorter than the signal definition needs")]
    [InlineData(OdometerDecodeFailure.BadScaling, MileageReadCategory.MalformedReply, "cannot be scaled")]
    [InlineData(OdometerDecodeFailure.NotAvailable, MileageReadCategory.Unverified, "not available")]
    [InlineData(OdometerDecodeFailure.Implausible, MileageReadCategory.Unverified, "never used")]
    public void AReplyThatCannotBeDecodedIntoAValue_IsDescribedByItsOwnCategory(OdometerDecodeFailure failure, MileageReadCategory category, string phrase)
    {
        var observation = MileageReadDiagnosis.OfDecodeFailure(failure);

        Assert.Equal(category, observation.Category);
        Assert.Contains(phrase, observation.Text);
    }

    [Fact]
    public void TheDecoderFailuresThemselves_AreProducedByTheDecoder_AsBefore()
    {
        // The categories above describe what the unchanged decoder reports for these inputs.
        Assert.Equal(OdometerDecodeFailure.TooShort, OdometerResponseDecoder.Decode(Spec(), new byte[] { 0x03, 0xB5 }).Failure);
        Assert.Equal(OdometerDecodeFailure.BadScaling, OdometerResponseDecoder.Decode(Spec(div: 0), new byte[] { 0x03, 0xB5, 0x8F }).Failure);
        Assert.Equal(OdometerDecodeFailure.NotAvailable, OdometerResponseDecoder.Decode(Spec(), new byte[] { 0xFF, 0xFF, 0xFF }).Failure);
        Assert.Equal(OdometerDecodeFailure.Implausible, OdometerResponseDecoder.Decode(Spec(), new byte[] { 0x00, 0x00, 0x00 }).Failure);
    }

    // ------------------------------------------------------------------ another ECU answers

    [Fact]
    public void WhenTheReplyIdIsKnown_FramesOnlyFromAnotherModule_AreOtherEcu_AndAreNeverTheValue()
    {
        const string fromEngine = FordDd01FromEngine;

        var payload = OdometerResponseDecoder.ExtractPayload(fromEngine, 0x22, 0xDD01, 0x7E9, out var failure, out var responder);
        var observation = MileageReadDiagnosis.OfReply(fromEngine, 0x7E9, 0x22, 0xDD01);

        Assert.Null(payload);
        Assert.Equal(OdometerDecodeFailure.ForeignReply, failure);               // unchanged: not used
        Assert.Equal(0x7E8, responder);
        Assert.Equal(MileageReadCategory.OtherEcu, observation.Category);
        Assert.Contains("only frames from 7E8 arrived, none from the asked module", observation.Text);
    }

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task WhenTheReplyIdIsUnknown_AValueFromTheEngine_IsStillDecoded_AndTheLogNamesTheOtherEcu(SimTransport transport)
    {
        var car = new SimulatedElm327();
        car.ModuleAnswers["7DF|22DD01"] = FordDd01FromEngine;                    // the adapter ignores ATSH: the engine answers

        var seen = await TransportRunner.RunAsync(transport, new HeaderIgnoringAdapter(car), rig => ReadAsObservedAsync(rig, Addr(0x7E1, null)));

        Assert.NotNull(seen.Payload);                                            // the value is decoded exactly as before...
        Assert.Equal(OdometerDecodeFailure.None, seen.Failure);
        Assert.Equal(0x7E8, seen.Responder);
        Assert.Null(seen.Observation);                                           // ...a decoded reply has no failure to describe

        var note = MileageReadDiagnosis.OfDecodedValue(definitionUnderTest: false, transmitCanId: 0x7E1, receiveCanId: null, responderCanId: seen.Responder);
        Assert.Equal(MileageReadCategory.OtherEcu, note!.Category);
        Assert.Contains("ECU 0x7E8", note.Text);
        Assert.Contains("0x7E1", note.Text);
        Assert.Contains("named but not verified", note.Text);                    // it does not make the value more trustworthy
        var line = MileageReadDiagnosis.ValueLine(Method, 243087, note);
        Assert.StartsWith($"{Method}: 243087 km — [other ECU] value decoded;", line);
    }

    // ------------------------------------------------------------------ a value that was decoded

    [Fact]
    public void AValueFromTheAskedModule_KeepsTheLineTheLogAlwaysHad_AndTheDetailStaysEmpty()
    {
        var note = MileageReadDiagnosis.OfDecodedValue(definitionUnderTest: false, transmitCanId: 0x7E0, receiveCanId: 0x7E8, responderCanId: 0x7E8);

        Assert.Null(note);
        Assert.Equal($"{Method}: 243087 km", MileageReadDiagnosis.ValueLine(Method, 243087, note));
        Assert.Equal(string.Empty, MileageReadDiagnosis.ValueDetail(note));
    }

    [Fact]
    public void AValueFromADefinitionUnderTest_IsMarkedUnverified_InTheLogOnly()
    {
        var note = MileageReadDiagnosis.OfDecodedValue(definitionUnderTest: true, transmitCanId: 0x7E0, receiveCanId: 0x7E8, responderCanId: 0x7E8);

        Assert.Equal(MileageReadCategory.Unverified, note!.Category);
        Assert.Contains("under test", note.Text);
        Assert.Equal($"{Method}: 243087 km — [unverified] value decoded; the signal definition is marked as under test, not yet verified on a car",
            MileageReadDiagnosis.ValueLine(Method, 243087, note));
    }

    [Fact]
    public void AnotherEcuAndAnUntestedDefinition_AreBothSaid_InOneNote()
    {
        var note = MileageReadDiagnosis.OfDecodedValue(true, 0x7E1, null, 0x7E8);

        Assert.Equal(MileageReadCategory.OtherEcu, note!.Category);
        Assert.Contains("ECU 0x7E8", note.Text);
        Assert.Contains("under test", note.Text);
    }

    // ------------------------------------------------------------------ the lines keep the text the log always had, then add

    [Fact]
    public void EveryLogLine_StartsWithWhatTheLineSaidBefore_SoAnythingReadingTheLogStillFindsIt()
    {
        var timeout = new Elm327TimeoutException("t");
        var negative = MileageReadDiagnosis.OfReply("NO DATA", 0x7E8, 0x22, 0xDD01);

        Assert.StartsWith($"{Method}: error (Elm327TimeoutException)", MileageReadDiagnosis.ErrorLine(Method, timeout, records: false));
        Assert.StartsWith($"{Method}: records error (Elm327TimeoutException)", MileageReadDiagnosis.ErrorLine(Method, timeout, records: true));
        Assert.StartsWith($"{Method}: Negative", MileageReadDiagnosis.FailureLine(Method, OdometerDecodeFailure.Negative, negative, records: false));
        Assert.StartsWith($"{Method}: records Negative", MileageReadDiagnosis.FailureLine(Method, OdometerDecodeFailure.Negative, negative, records: true));
        Assert.StartsWith($"{Method}: reply could not be decoded (TooShort) 03B5",
            MileageReadDiagnosis.UndecodedLine(Method, OdometerDecodeFailure.TooShort, "03B5", MileageReadDiagnosis.OfDecodeFailure(OdometerDecodeFailure.TooShort)));
        Assert.StartsWith("Reply came from 0x7E8, not from this module; not used.",
            MileageReadDiagnosis.FailureDetail("Reply came from 0x7E8, not from this module; not used.", MileageReadDiagnosis.OfReply(FordDd01FromEngine, 0x7E9, 0x22, 0xDD01)));
        Assert.StartsWith("Negative — [no reply]", MileageReadDiagnosis.FailureDetail("Negative", negative));
    }

    // ------------------------------------------------------------------ what did NOT change (pinned from the source)

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    [Fact]
    public void TheProvider_StillAsksTheSameModules_WithTheSameRequest_AndFilesTheSameOutcomes()
    {
        var provider = Source("Services", "Mileage", "CatalogOdometerProvider.cs");

        // Which module is asked, with which request, and how the answer is cut out of the reply: unchanged.
        Assert.Contains("? new byte[] { 0x21, (byte)signal.Identifier }", provider);
        Assert.Contains(": new byte[] { 0x22, (byte)(signal.Identifier >> 8), (byte)signal.Identifier };", provider);
        Assert.Contains("_obd.ReadPhysicalCanReadOnlyAsync(signal.Address, request, RequestTimeout, token)", provider);
        Assert.Contains("OdometerResponseDecoder.ExtractPayload(raw, signal.ServiceId, signal.Identifier, signal.Address.ReceiveCanId, out var failure, out var responder);", provider);
        Assert.Contains("private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1800);", provider);
        Assert.Contains("private const int MaximumRequests = 16;", provider);
        // The evidence and the outcomes of every branch: unchanged, line for line.
        Assert.Contains("evidence.Add(Build(signal, decoded, method, MileageReadStatus.Ok, reply.Responder, reply.Data));", provider);
        Assert.Contains("evidence.Add(Build(signal, decoded, method, MileageReadStatus.Implausible, reply.Responder, reply.Data));", provider);
        Assert.Contains("OdometerDecodeFailure.Negative => MileageAttemptOutcome.ReadingUnavailable,", provider);
        Assert.Contains("OdometerDecodeFailure.NotPositiveForRequest => MileageAttemptOutcome.InvalidReading,", provider);
        Assert.Contains("_ => MileageAttemptOutcome.ModuleNotResponding", provider);
        Assert.Contains("MileageAttemptOutcome.Read, MileageReadDiagnosis.ValueDetail(valueNote)", provider);
        Assert.Contains("MileageAttemptOutcome.InvalidReading,", provider);
        Assert.Contains("MileageAttemptOutcome.ModuleNotResponding, MileageReadDiagnosis.ErrorDetail(ex, linkDown: !_obd.IsAdapterConnected)", provider);
        // The confidence and status of the evidence did not become a function of the new text.
        Assert.Contains("return new MileageEvidence(Name, ecu, decoded.Km ?? double.NaN, signal.ExactModel ? 0.8 : 0.7, detail)", provider);
        Assert.Contains("var otherResponder = OdometerResponseDecoder.IsResponderForeign(", provider);
    }

    [Fact]
    public void TheDiagnosis_IsOnlyUsedToWriteTheLog_NothingThatDecidesAnythingReadsIt()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CarScanner.csproj")))
            root = root.Parent;
        var mileage = Path.Combine(root!.FullName, "Services", "Mileage");

        var users = Directory.EnumerateFiles(mileage, "*.cs")
            .Where(path => File.ReadAllText(path).Contains("MileageReadDiagnosis", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Only the provider and the scan service (which write the log) and the helper itself. Not the decoder, the analyzer, the
        // evidence, the result or any other provider.
        Assert.Equal(new[] { "ActualMileageService.cs", "CatalogOdometerProvider.cs", "MileageReadDiagnosis.cs" }, users);
        Assert.DoesNotContain("MileageReadDiagnosis", Source("Services", "Mileage", "OdometerResponseDecoder.cs"));
        Assert.DoesNotContain("MileageReadDiagnosis", Source("Services", "Mileage", "MileageAnalyzer.cs"));
        Assert.DoesNotContain("MileageReadDiagnosis", Source("Services", "Mileage", "MileageEvidence.cs"));
        Assert.DoesNotContain("MileageReadDiagnosis", Source("Services", "Mileage", "ActualMileageResult.cs"));

        // In the provider the evidence is built by a method that never sees it, and the one note about a decoded value is used
        // by exactly two log texts (the line and the attempt's detail): its declaration and those two uses.
        var provider = Source("Services", "Mileage", "CatalogOdometerProvider.cs");
        var build = provider[provider.IndexOf("private MileageEvidence Build(", StringComparison.Ordinal)..];
        build = build[..build.IndexOf("// Catalog", StringComparison.Ordinal)];
        Assert.DoesNotContain("MileageReadDiagnosis", build);
        Assert.DoesNotContain("Observed", build);
        Assert.Equal(3, provider.Split("valueNote").Length - 1);
        Assert.Contains("MileageTrace.Add(MileageReadDiagnosis.ValueLine(method, decoded.Km!.Value, valueNote));", provider);
        Assert.Contains("MileageAttemptOutcome.Read, MileageReadDiagnosis.ValueDetail(valueNote)", provider);
    }

    [Fact]
    public void ThePage_ShowsTheLogAsTheScanWroteIt_AndTheAttemptRowsDoNotShowTheDetail()
    {
        var page = Source("ViewModels", "MileageViewModel.cs");

        Assert.Contains("Log = result.Log;", page);                              // the copied technical log is the trace
        var row = page[page.IndexOf("private MileageAttemptRow ToAttemptRow(", StringComparison.Ordinal)..];
        row = row[..row.IndexOf("private async Task CopyLogAsync()", StringComparison.Ordinal)];
        Assert.DoesNotContain("attempt.Detail", row);                            // the visible rows are unchanged by the longer detail text
    }
}
