using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Mileage;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Original Mileage, from the adapter to the decoded number. The provider class itself (<c>CatalogOdometerProvider</c>) needs
/// the app's SQLite catalog and cannot run here, so each test performs the provider's own steps on the REAL pieces, in its
/// order: <c>OBDService.ReadPhysicalCanReadOnlyAsync</c> (the real protocol layer on a simulated adapter), then
/// <c>OdometerResponseDecoder.ExtractPayload</c>, <c>IsResponderForeign</c> and <c>Decode</c>. How the provider turns those
/// results into a mileage outcome is pinned from its source at the end.
/// SIMULATION ONLY. The Ford frame is the one in MileageAttributionTests (a real capture of 22 DD 01 answered by the engine ECU);
/// the module addresses used here are examples, not a claim about any car's real addressing.
/// </summary>
public sealed class OriginalMileagePipelineTests
{
    private const string FordDd01FromEngine = "7E8 06 62 DD 01 03 B5 8F";
    private static readonly byte[] Dd01 = { 0x22, 0xDD, 0x01 };
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromMilliseconds(1800);   // CatalogOdometerProvider.RequestTimeout

    private static OdometerSignalSpec Spec() => new(BitOffset: 0, BitLength: 24, Mul: 1, Div: 1, Add: 0, Miles: false, Meters: false);

    private static ServiceDiagnosticAddress Addr(int tx, int? rx) => new(tx, rx, false, null, "11-bit CAN", "module");

    private sealed record Step(string? Raw, Exception? Error, byte[]? Payload, OdometerDecodeFailure Failure, int? Responder, bool Foreign, double? Km);

    private static async Task<Step> ProviderStepsAsync(AdapterRig rig, ServiceDiagnosticAddress address)
    {
        string raw;
        try
        {
            raw = await rig.Obd.ReadPhysicalCanReadOnlyAsync(address, Dd01, ProviderTimeout);
        }
        catch (Exception ex)
        {
            return new Step(null, ex, null, OdometerDecodeFailure.None, null, false, null);
        }

        var payload = OdometerResponseDecoder.ExtractPayload(raw, 0x22, 0xDD01, address.ReceiveCanId, out var failure, out var responder);
        var foreign = payload is not null && OdometerResponseDecoder.IsResponderForeign(address.TransmitCanId, address.ReceiveCanId, responder);
        double? km = payload is null ? null : OdometerResponseDecoder.Decode(Spec(), payload).Km;
        return new Step(raw, null, payload, failure, responder, foreign, km);
    }

    private static SimulatedElm327 EngineAnswers()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22DD01"] = FordDd01FromEngine;
        return sim;
    }

    // ------------------------------------------------------------------ the expected module answers

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task AValidAnswerFromTheAskedModule_GivesTheSameNumber_OverEveryTransport(SimTransport transport)
    {
        var step = await TransportRunner.RunAsync(transport, EngineAnswers(), rig => ProviderStepsAsync(rig, Addr(0x7E0, 0x7E8)));

        Assert.Null(step.Error);
        Assert.Equal(OdometerDecodeFailure.None, step.Failure);
        Assert.Equal(new byte[] { 0x03, 0xB5, 0x8F }, step.Payload);
        Assert.Equal(0x7E8, step.Responder);
        Assert.False(step.Foreign);
        Assert.Equal(243_087, step.Km!.Value, 6);        // 0x03B58F; the unit is the catalog's business, not the decoder's
    }

    // ------------------------------------------------------------------ nothing usable came back

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task NoAnswer_IsNoNumber_AndIsFiledUnderNegative_OverEveryTransport(SimTransport transport)
    {
        var step = await TransportRunner.RunAsync(transport, new SimulatedElm327(), rig => ProviderStepsAsync(rig, Addr(0x7E0, 0x7E8)));

        Assert.Equal("NO DATA", step.Raw);
        Assert.Null(step.Payload);
        Assert.Null(step.Km);
        Assert.Equal(OdometerDecodeFailure.Negative, step.Failure);
    }

    [Theory]
    [InlineData("7E8 03 7F 22 31")]    // request out of range
    [InlineData("7E8 03 7F 22 11")]    // service not supported
    [InlineData("7E8 03 7F 22 78")]    // response pending, and nothing final ever follows
    public async Task AnExplicitRefusalFromTheModule_IsNoNumber_AndIsFiledUnderTheSameNegativeAsSilence(string refusal)
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        var rig = await AdapterRigs.CreateAsync(scripted);
        scripted.Reply("22DD01", refusal);

        var step = await ProviderStepsAsync(rig, Addr(0x7E0, 0x7E8));

        Assert.Null(step.Payload);
        Assert.Null(step.Km);
        // Silence and an ECU saying "no" are the same bucket at this layer (a Stage 2 candidate: they mean different things).
        Assert.Equal(OdometerDecodeFailure.Negative, step.Failure);
    }

    [Theory]
    [InlineData("7E8 06 62 DD 02 03 B5 8F", OdometerDecodeFailure.NotPositiveForRequest)]   // an answer, but for another identifier
    [InlineData("7E8 06 63 DD 01 03 B5 8F", OdometerDecodeFailure.NotPositiveForRequest)]   // not the positive code of this service
    [InlineData("7E8 06 62 DD", OdometerDecodeFailure.NotPositiveForRequest)]               // cut off before the data
    [InlineData("hello world", OdometerDecodeFailure.NotPositiveForRequest)]                 // text that is not an answer
    public async Task AMalformedOrWrongAnswer_IsNoNumber_AndIsFiledAsNotAPositiveAnswer(string raw, OdometerDecodeFailure expected)
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        var rig = await AdapterRigs.CreateAsync(scripted);
        scripted.Reply("22DD01", raw);

        var step = await ProviderStepsAsync(rig, Addr(0x7E0, 0x7E8));

        Assert.Null(step.Payload);
        Assert.Null(step.Km);
        Assert.Equal(expected, step.Failure);
    }

    [Theory]
    [InlineData("7E8 06 62 DD 01 FF FF FF")]   // every bit set: the module has nothing recorded
    [InlineData("7E8 06 62 DD 01 00 00 00")]   // zero
    public async Task AnAnswerThatIsPositiveButHoldsNoOdometer_IsNeverANumber(string raw)
    {
        var scripted = new ScriptedAdapter(new SimulatedElm327());
        var rig = await AdapterRigs.CreateAsync(scripted);
        scripted.Reply("22DD01", raw);

        var provider = await ProviderStepsAsync(rig, Addr(0x7E0, 0x7E8));
        var decoded = OdometerResponseDecoder.Decode(Spec(), provider.Payload!);

        Assert.NotNull(provider.Payload);                                  // the answer itself is a positive answer...
        Assert.False(decoded.IsValue);                                     // ...but not a reading
        Assert.True(decoded.Failure is OdometerDecodeFailure.NotAvailable or OdometerDecodeFailure.Implausible);
    }

    // ------------------------------------------------------------------ somebody else answered

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task WhenTheReplyIdIsUnknown_AndTheEngineAnswersARequestForAnotherModule_TheNumberIsNamedAsTheEngines_NotTheModules(SimTransport transport)
    {
        // The adapter ignores ATSH, so the request for module 7E1 goes out functionally and the engine ECU (7E8) answers.
        var car = new SimulatedElm327();
        car.ModuleAnswers["7DF|22DD01"] = FordDd01FromEngine;

        var step = await TransportRunner.RunAsync(transport, new HeaderIgnoringAdapter(car), rig => ProviderStepsAsync(rig, Addr(0x7E1, null)));

        Assert.Equal(OdometerDecodeFailure.None, step.Failure);            // with no reply id the value is not refused here...
        Assert.NotNull(step.Payload);
        Assert.Equal(0x7E8, step.Responder);                                // ...but the responder is named
        Assert.True(step.Foreign);                                          // ...and flagged: the provider writes "ECU 0x7E8 (request was addressed to 7E1)"
    }

    [Fact]
    public async Task WhenTheReplyIdIsKnown_TheEnginesAnswerNeverReachesTheDecoder_SoItLooksLikeSilence()
    {
        var car = new SimulatedElm327();
        car.ModuleAnswers["7DF|22DD01"] = FordDd01FromEngine;
        var rig = await AdapterRigs.CreateAsync(new HeaderIgnoringAdapter(car) { HonorReceiveFilter = true });

        var step = await ProviderStepsAsync(rig, Addr(0x7E1, 0x7E9));

        Assert.Equal("NO DATA", step.Raw);                                  // filtered by the adapter or by the app before the decoder sees it
        Assert.Null(step.Payload);
        Assert.Equal(OdometerDecodeFailure.Negative, step.Failure);
    }

    [Fact]
    public void ArbitraryFramesFromAnotherModule_AreNeverAMileage_WhateverTheyContain()
    {
        // With the asked module's reply id known, ANY frame that does not carry it is refused, even a perfect-looking answer.
        var payload = OdometerResponseDecoder.ExtractPayload("7E8 06 62 DD 01 03 B5 8F", 0x22, 0xDD01, 0x7E9, out var failure);

        Assert.Null(payload);
        Assert.Equal(OdometerDecodeFailure.ForeignReply, failure);
    }

    // ------------------------------------------------------------------ the adapter or the link failed

    [Fact]
    public async Task AnAdapterThatRefusesASetupCommand_ThrowsBeforeAnyRequest_AndTheErrorSaysWhichCommand()
    {
        var sim = EngineAnswers();
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.RejectedAt.Add("ATH1");
        sim.Sent.Clear();

        var step = await ProviderStepsAsync(rig, Addr(0x7E0, 0x7E8));

        var error = Assert.IsType<Elm327CommandException>(step.Error);
        Assert.Equal("ATH1", error.UnsupportedCommand);
        Assert.DoesNotContain("22DD01", sim.Sent);
        Assert.Equal(AdapterFailureKind.AdapterCommandUnsupported, AdapterReadFailure.Classify(error).Kind);
    }

    [Fact]
    public async Task AModuleThatNeverAnswers_AtTheAdapter_IsATimeout_NotNoData()
    {
        var sim = EngineAnswers();
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.Fault = SimFault.Timeout;

        var step = await ProviderStepsAsync(rig, Addr(0x7E0, 0x7E8));

        Assert.IsType<Elm327TimeoutException>(step.Error);
        Assert.Equal(AdapterFailureKind.NoReplyInTime, AdapterReadFailure.Classify(step.Error!).Kind);
    }

    // ------------------------------------------------------------------ what the provider does with those results

    [Fact]
    public void TheProvider_FilesEveryFailureOfTheReadIntoThreeOutcomes_AndEveryExceptionIntoModuleNotResponding()
    {
        // Pinned from the source (the provider cannot run in this project). If this changes, the strings below change on purpose.
        var source = File.ReadAllText(RepoFile("Services/Mileage/CatalogOdometerProvider.cs")).Replace("\r\n", "\n");

        // A failure of the decode: Negative -> unavailable, NotPositive -> invalid, everything else -> not responding.
        Assert.Contains("OdometerDecodeFailure.Negative => MileageAttemptOutcome.ReadingUnavailable,", source);
        Assert.Contains("OdometerDecodeFailure.NotPositiveForRequest => MileageAttemptOutcome.InvalidReading,", source);
        Assert.Contains("_ => MileageAttemptOutcome.ModuleNotResponding", source);
        // A foreign reply gets its own sentence, naming the other module.
        Assert.Contains("Reply came from 0x{reply.Responder:X3}, not from this module; not used.", source);

        // An exception of the read (a refused setup command, a timeout, a lost link …) is still ONE outcome. Since Stage 2A the
        // log line and the attempt's detail also say what kind of failure it was (MileageTraceDiagnosisTests); the outcome did not change.
        var catchBlock = source[source.IndexOf("catch (Exception ex)", StringComparison.Ordinal)..];
        catchBlock = catchBlock[..catchBlock.IndexOf("continue;", StringComparison.Ordinal)];
        Assert.Contains("MileageTrace.Add(MileageReadDiagnosis.ErrorLine(method, ex, records: false, linkDown: !_obd.IsAdapterConnected));", catchBlock);
        Assert.Contains("MileageAttemptOutcome.ModuleNotResponding, MileageReadDiagnosis.ErrorDetail(ex, linkDown: !_obd.IsAdapterConnected)", catchBlock);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }
}
