using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Cheap ELM327 clones against a full-featured adapter, for the physically addressed read-only requests (the path the
/// Chevrolet Volt oil-life read, EV battery, odometer and service values all use).
///
/// SIMULATION ONLY. The software adapter answers "?" to the commands a test lists, the way the vendor documentation says a
/// chip with older microcode does (Elm Electronics' version history: ATS0/ATS1, ATD0/ATD1 and ATCRA hhh first appear in
/// v1.3, ATCRA without argument in v1.4b, response-pending handling in v2.1). It proves what the app does when an
/// adapter refuses those commands. It does not prove which commands the user's cheap adapter or a Konnwei really
/// refuses, and nothing here ran against a car.
/// </summary>
public sealed class AdapterCloneCompatibilityTests
{
    private static readonly ServiceDiagnosticAddress VoltEcm = new(0x7E0, 0x7E8, false, null, "11-bit CAN", "Chevrolet Volt ECM");
    private static readonly byte[] OilLifeRequest = { 0x22, 0x11, 0x9F };

    private sealed record Rig(SimulatedElm327 Sim, Elm327Protocol Elm, OBDService Obd);

    private static async Task<Rig> CreateAsync(Action<SimulatedElm327>? configure = null, bool configureBeforeInit = true)
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        if (configureBeforeInit)
            configure?.Invoke(sim);
        await obd.InitializeAsync();
        if (!configureBeforeInit)
            configure?.Invoke(sim);
        sim.Sent.Clear();
        return new Rig(sim, elm, obd);
    }

    private static Task<string> ReadAsync(Rig rig, ServiceDiagnosticAddress? address = null, byte[]? request = null) =>
        rig.Obd.ReadPhysicalCanReadOnlyAsync(address ?? VoltEcm, request ?? OilLifeRequest, TimeSpan.FromSeconds(2));

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    // A v1.0..v1.2 style chip: no ATS0/1, no ATD0/1, no ATCRA (either form), no ATAR, no flow-control switch.
    private static void OlderMicrocode(SimulatedElm327 sim)
    {
        foreach (var command in new[] { "ATS1", "ATS0", "ATD0", "ATD1", "ATCFC1", "ATCRA", "ATAR" })
            sim.RejectedAt.Add(command);
    }

    // ------------------------------------------------------------------ the Volt oil-life request

    [Fact]
    public async Task AFullFeaturedAdapter_ReadsTheVoltOilLifeIdentifier_AndLeavesTheAdapterOnGenericSettings()
    {
        var rig = await CreateAsync();

        var raw = await ReadAsync(rig);

        Assert.Contains("62 11 9F 80", raw);
        Assert.Equal("setup: all accepted; filter: ATCRA", rig.Elm.LastPhysicalReadSetupNote);
        Assert.Equal(rig.Elm.LastPhysicalReadSetupNote, rig.Obd.LastPhysicalReadSetupNote);

        // Back to generic OBD: functional header, headers off, no receive filter, still initialised.
        Assert.Equal("7DF", rig.Sim.CurrentHeader);
        Assert.False(rig.Sim.HeadersOn);
        Assert.Null(rig.Sim.ReceiveFilter);
        Assert.True(rig.Obd.IsInitialized);
    }

    [Fact]
    public async Task AnOlderMicrocodeClone_ThatAnswersQuestionMarkToS1D0Cfc1AndCra_StillGetsTheVoltAnswer()
    {
        var rig = await CreateAsync(OlderMicrocode);

        // Before: ATS1, ATD0, ATCFC1 and ATCRA7E8 were all REQUIRED, so the first "?" threw
        // "Adapter does not support required command" and the oil life was never read.
        var raw = await ReadAsync(rig);

        Assert.Contains("62 11 9F 80", raw);

        var note = rig.Elm.LastPhysicalReadSetupNote;
        Assert.Contains("ATS1", note);
        Assert.Contains("ATD0", note);
        Assert.Contains("ATCFC1", note);
        Assert.Contains("ATCRA7E8", note);
        Assert.Contains("automatic receive address", note);

        // Standard OBD pairing: nothing stateful was set, so nothing had to be cleared and no re-initialisation is forced.
        Assert.DoesNotContain(rig.Sim.Sent, sent => sent.StartsWith("ATCF7", StringComparison.OrdinalIgnoreCase));
        Assert.True(rig.Obd.IsInitialized);
        Assert.Equal("7DF", rig.Sim.CurrentHeader);
        Assert.False(rig.Sim.HeadersOn);
    }

    [Fact]
    public async Task TheSameRequestOnTwoAdapters_GivesTheSamePayload_AndOnlyTheSetupNoteDiffers()
    {
        var full = await CreateAsync();
        var clone = await CreateAsync(OlderMicrocode);

        var fromFull = await ReadAsync(full);
        var fromClone = await ReadAsync(clone);

        Assert.Equal(fromFull, fromClone);                                            // the ECU's reply reaches the app unchanged
        Assert.NotEqual(full.Elm.LastPhysicalReadSetupNote, clone.Elm.LastPhysicalReadSetupNote);   // the comparison evidence
        Assert.StartsWith("setup: all accepted", full.Elm.LastPhysicalReadSetupNote);
        Assert.StartsWith("setup: not accepted:", clone.Elm.LastPhysicalReadSetupNote);
    }

    [Fact]
    public async Task OneRefusal_IsNeverRemembered_TheNextReadTriesTheCommandAfresh()
    {
        var rig = await CreateAsync();
        rig.Sim.RejectedAt.Add("ATD0");

        await ReadAsync(rig);
        Assert.Contains("ATD0", rig.Elm.LastPhysicalReadSetupNote);

        rig.Sim.RejectedAt.Remove("ATD0");   // e.g. a transient '?' from line noise
        rig.Sim.Sent.Clear();
        await ReadAsync(rig);

        Assert.Equal("setup: all accepted; filter: ATCRA", rig.Elm.LastPhysicalReadSetupNote);
        Assert.Contains("ATD0", rig.Sim.Sent);   // it was sent again, not skipped
    }

    // ------------------------------------------------------------------ other modules, filters and cross-talk

    private static readonly ServiceDiagnosticAddress NonStandardModule = new(0x7B0, 0x7B8, false, null, "11-bit CAN", "module 7B0");

    [Fact]
    public async Task ANonStandardModule_OnAChipWithoutCra_UsesAnIdFilter_AndAChipThatCannotClearItIsReinitialised()
    {
        var rig = await CreateAsync(sim =>
        {
            OlderMicrocode(sim);
            sim.ModuleAnswers["7B0|22119F"] = "7B8 04 62 11 9F 80";
        });

        var raw = await ReadAsync(rig, NonStandardModule);

        Assert.Contains("62 11 9F 80", raw);
        Assert.Contains("ID filter via ATCF/ATCM", rig.Elm.LastPhysicalReadSetupNote);
        Assert.Contains("ATCF7B8", rig.Sim.Sent);
        Assert.Contains("ATCM7FF", rig.Sim.Sent);

        // The ID filter stays on such a chip (ATCRA and ATAR both refused): the only way back is a full reset.
        Assert.False(rig.Obd.IsInitialized);
        await rig.Obd.InitializeAsync();
        Assert.True(rig.Obd.IsInitialized);
        Assert.Null(rig.Sim.ReceiveFilter);
    }

    [Fact]
    public async Task AChipWithNoFilterCommandAtAll_StillAnswers_AndTheAppDropsFramesOfOtherModules()
    {
        var rig = await CreateAsync(sim =>
        {
            OlderMicrocode(sim);
            sim.RejectedAt.Add("ATCF7B8");
            sim.ModuleAnswers["7B0|22119F"] = "7B9 04 62 11 9F 11 7B8 04 62 11 9F 80";   // another module is on the bus too
        });

        var raw = await ReadAsync(rig, NonStandardModule);

        Assert.Contains("7B8 04 62 11 9F 80", raw);
        Assert.DoesNotContain("7B9", raw);
        Assert.Contains("no hardware receive filter", rig.Elm.LastPhysicalReadSetupNote);
        Assert.True(rig.Obd.IsInitialized);
    }

    [Fact]
    public async Task WhenOnlyAnotherModuleAnswers_AFilterlessAdapterReportsNoData_LikeAHardwareFilterWould()
    {
        var rig = await CreateAsync(sim =>
        {
            OlderMicrocode(sim);
            sim.ModuleAnswers["7E0|22119F"] = "7E9 04 62 11 9F 11";   // the transmission answered, not the engine
        });

        var raw = await ReadAsync(rig);

        Assert.Equal("NO DATA", raw);
    }

    [Theory]
    [InlineData("7E8 04 62 11 9F 80 7E9 04 62 11 9F 11", "7E8 04 62 11 9F 80", 0x7E8, false)]
    [InlineData("7E80462119F80 7E90462119F11", "7E80462119F80", 0x7E8, false)]
    [InlineData("NO DATA", "NO DATA", 0x7E8, false)]
    [InlineData("7E9 04 62 11 9F 11", "NO DATA", 0x7E8, false)]
    [InlineData("18DAF110 04 62 11 9F 80 18DAF118 04 62 11 9F 11", "18DAF110 04 62 11 9F 80", 0x18DAF110, true)]
    public void TheSoftwareFilter_KeepsOnlyTheAskedModulesFrames(string raw, string expected, int canId, bool ext29)
    {
        Assert.Equal(expected, Elm327Protocol.KeepOnlyFramesFrom(raw, canId, ext29));
    }

    [Fact]
    public async Task AGoodAnswer_IsNotThrownAway_WhenTheCleanupAfterwardsFails()
    {
        // The read itself works; only the final return to the functional header is refused (ATSH7DF).
        var rig = await CreateAsync(sim => sim.RejectedAt.Add("ATSH7DF"), configureBeforeInit: false);

        var raw = await ReadAsync(rig);   // used to throw "Adapter does not support required command 'ATSH7DF'" from the cleanup

        Assert.Contains("62 11 9F 80", raw);
        Assert.False(rig.Obd.IsInitialized);   // the adapter state is untrusted: the next operation re-initialises it
    }

    [Fact]
    public async Task ARequiredCommandThatFails_StillThrows_AndNamesTheAdapterCommand()
    {
        var rig = await CreateAsync(sim => sim.RejectedAt.Add("ATH1"), configureBeforeInit: false);

        var error = await Assert.ThrowsAsync<Elm327CommandException>(() => ReadAsync(rig));

        Assert.Equal("ATH1", error.UnsupportedCommand);
        var failure = AdapterReadFailure.Classify(error);
        Assert.Equal(AdapterFailureKind.AdapterCommandUnsupported, failure.Kind);
        Assert.True(failure.IsAdapterSide);
        Assert.Contains("ATH1", failure.Text);
        Assert.True(AdapterReadFailure.ShouldTryFunctionalRequest(error));   // the functional request needs none of the addressing commands
    }

    [Fact]
    public async Task ThePhysicalRead_SendsOnlyAdapterSetupCommandsAndTheOneReadRequest()
    {
        var rig = await CreateAsync(OlderMicrocode);

        await ReadAsync(rig);

        var allowed = new Regex(@"^(AT(L0|S1|H1|H0|CAF1|CFC1|D0|SH[0-9A-F]{3,8}|CRA([0-9A-F]{3,8})?|CF[0-9A-F]{3,8}|CM[0-9A-F]{3,8}|AR)|22119F)$",
            RegexOptions.IgnoreCase);
        Assert.All(rig.Sim.Sent, sent => Assert.Matches(allowed, sent));
        Assert.Single(rig.Sim.Sent, sent => sent == "22119F");   // one read request, nothing that writes, resets or changes the car
    }

    // ------------------------------------------------------------------ the failure categories

    [Theory]
    [InlineData("?", AdapterFailureKind.AdapterCommandUnsupported)]
    [InlineData("NO DATA", AdapterFailureKind.EcuDidNotAnswer)]
    [InlineData("", AdapterFailureKind.NoReplyInTime)]
    [InlineData("UNABLE TO CONNECT", AdapterFailureKind.BusOrProtocolError)]
    [InlineData("CAN ERROR", AdapterFailureKind.BusOrProtocolError)]
    [InlineData("BUFFER FULL", AdapterFailureKind.BusOrProtocolError)]
    [InlineData("7E8 03 7F 22 31", AdapterFailureKind.EcuRefused)]
    [InlineData("7E8 03 7F 22 11", AdapterFailureKind.EcuRefused)]
    [InlineData("7E8 03 7F 22 78", AdapterFailureKind.IncompleteReply)]
    [InlineData("7E8 10 08 62 11 9F 80", AdapterFailureKind.IncompleteReply)]
    [InlineData("7E9 04 62 11 9F 11", AdapterFailureKind.ReplyFromAnotherModule)]
    [InlineData("7E8 04 62 4D C0 80", AdapterFailureKind.UnexpectedReply)]
    [InlineData("hello", AdapterFailureKind.UnexpectedReply)]
    [InlineData("7E8 04 62 11 9F 80", AdapterFailureKind.None)]
    [InlineData("7E80462119F80", AdapterFailureKind.None)]
    public void EachReply_IsPutInTheCategoryItBelongsTo(string raw, AdapterFailureKind expected)
    {
        Assert.Equal(expected, AdapterReadFailure.ClassifyReply(raw, 0x7E8, 0x119F).Kind);
    }

    [Fact]
    public void ANegativeResponse_SaysWhichRefusalItWas()
    {
        var failure = AdapterReadFailure.ClassifyReply("7E8 03 7F 22 31", 0x7E8, 0x119F);
        Assert.Contains("Request out of range", failure.Text);
        Assert.Contains("0x31", failure.Text);
        Assert.False(failure.IsAdapterSide);   // the ECU got the request: another adapter would not change that
    }

    [Fact]
    public void AnEcuSilence_IsNotAnAdapterVerdict()
    {
        var failure = AdapterReadFailure.ClassifyReply("NO DATA", 0x7E8, 0x119F);
        Assert.False(failure.IsAdapterSide);
        Assert.Contains("does not prove", failure.Text);
    }

    [Fact]
    public void ExceptionsAreSortedByWhereTheyCameFrom()
    {
        Assert.Equal(AdapterFailureKind.NoReplyInTime, AdapterReadFailure.Classify(new Elm327TimeoutException("t")).Kind);
        Assert.Equal(AdapterFailureKind.NoReplyInTime, AdapterReadFailure.Classify(new TimeoutException()).Kind);
        Assert.Equal(AdapterFailureKind.AdapterLinkLost, AdapterReadFailure.Classify(new Elm327NotConnectedException("x")).Kind);
        Assert.Equal(AdapterFailureKind.AdapterLinkLost, AdapterReadFailure.Classify(new ObdConnectionLostException("x")).Kind);
        Assert.Equal(AdapterFailureKind.AdapterLinkLost, AdapterReadFailure.Classify(new AdapterNotConnectedException("x")).Kind);
        Assert.Equal(AdapterFailureKind.BusOrProtocolError, AdapterReadFailure.Classify(new NotSupportedException("not CAN")).Kind);
        Assert.Equal(AdapterFailureKind.Undetermined, AdapterReadFailure.Classify(new InvalidCastException()).Kind);   // not decided, not blamed
        Assert.Equal(AdapterFailureKind.AdapterCommandUnsupported,
            AdapterReadFailure.Classify(new Elm327CommandException("x", "?") { UnsupportedCommand = "ATCRA7E8" }).Kind);
    }

    [Fact]
    public void TheFunctionalFallback_IsTriedOnlyAfterTheAdapterRefusedItsAddressingCommands()
    {
        Assert.True(AdapterReadFailure.ShouldTryFunctionalRequest(new Elm327CommandException("x", "?") { UnsupportedCommand = "ATD0" }));
        Assert.False(AdapterReadFailure.ShouldTryFunctionalRequest(new Elm327TimeoutException("slow")));
        Assert.False(AdapterReadFailure.ShouldTryFunctionalRequest(new Elm327NotConnectedException("gone")));
        Assert.False(AdapterReadFailure.ShouldTryFunctionalRequest(new NotSupportedException("K-line car")));
    }

    // ------------------------------------------------------------------ the Service page wiring

    [Fact]
    public void TheVoltProbe_StillTriesTheFunctionalRequest_WhenThePhysicalOneThrew_AndSaysWhichSideFailed()
    {
        var source = Read("Services", "OemDiagnostics", "ServiceDataReaderService.cs");
        var probe = source[source.IndexOf("TryReadFirstGenerationVoltOilLifeAsync(", StringComparison.Ordinal)..];
        probe = probe[..probe.IndexOf("GM OIL-LIFE DISCOVERY", StringComparison.Ordinal)];

        var physical = probe.IndexOf("_obd.ReadPhysicalCanReadOnlyAsync(\n                        address,", StringComparison.Ordinal);
        var catchBlock = probe.IndexOf("AdapterReadFailure.ShouldTryFunctionalRequest(ex)", physical, StringComparison.Ordinal);
        var functional = probe.IndexOf("_obd.SendRawCommandAsync($\"22{did:X4}\"", StringComparison.Ordinal);
        Assert.True(physical > 0 && catchBlock > physical, "the physical read's failure must decide about the fallback");
        Assert.True(functional > catchBlock, "the functional request must come after (and not inside) the physical attempt's failure handling");

        // The old behaviour: any exception skipped the functional request entirely.
        Assert.DoesNotContain("notes.Add($\"22 {did:X4} → {ex.GetType().Name}: {ex.Message}\")", probe);

        Assert.Contains("LastPhysicalReadSetupNote", probe);
        Assert.Contains("LastOilLifeProbeAdapterSide", source);
        Assert.Contains("LastOilLifeProbeAdapterSide", Read("ViewModels", "AdvancedViewModels.cs"));
    }

    [Fact]
    public void ThePhysicalReadPath_NoLongerRequiresTheCommandsThatAreTheAdaptersDefaults()
    {
        var source = Read("Services", "OBD", "Elm327Protocol.cs");
        var method = source[source.IndexOf("public async Task<string> SendPhysicalCanReadOnlyPayloadAsync(\n            ReadOnlyMemory<byte> payload,\n            int transmitCanId,\n            int? receiveCanId,", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("public string LastPhysicalReadSetupNote", StringComparison.Ordinal)];

        foreach (var command in new[] { "ATS1", "ATCFC1", "ATD0" })
        {
            Assert.Contains($"TrySetupAsync(\"{command}\"", method);
            Assert.DoesNotContain($"RequireOkInternalAsync(\"{command}\"", method);
        }

        // What addressing and parsing really need stays required.
        foreach (var command in new[] { "ATL0", "ATH1", "ATCAF1" })
            Assert.Contains($"RequireOkInternalAsync(\"{command}\"", method);
        Assert.Contains("RequireOkInternalAsync(\n                        $\"ATSH", method);
    }
}
