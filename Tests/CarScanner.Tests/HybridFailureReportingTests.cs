using System.Text;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stage 2A, fix B: when the Volt / Bolt battery check ends without a pack, the causes the evidence can tell apart are told apart
/// (adapter setup command refused, module silent, another module answered, reply could not be read, too few cells, link lost) and
/// everything else keeps the sentence the page always had. The REAL reader (<c>ReadGmPackAsync</c>) and the real protocol layer run
/// against simulated adapters over the direct link, the real Wi-Fi transport and the real BLE code; the decision is the production
/// <see cref="HybridReadDiagnosis"/>. The scan service itself depends on the whole app and is not compiled here: its wiring is
/// pinned from the source (see the last tests). SIMULATION ONLY — nothing here ran against a car.
/// </summary>
public sealed class HybridFailureReportingTests
{
    private static readonly VehicleInfo Volt2012 = new() { Manufacturer = "Chevrolet", Model = "Volt", Year = "2012" };

    private static string CellFrame(int index)
    {
        var did = GmVoltBoltBattery.CellDid(index);
        var value = index == 0 ? 0xCED8 : index == 95 ? 0xCEA7 : 0xCE80 + (index % 9);
        return "7EF05" + "62" + did.ToString("X4") + value.ToString("X4");
    }

    private static string CellCommand(int index) => "22" + GmVoltBoltBattery.CellDid(index).ToString("X4");

    /// <summary>A battery module that answers the cells for which <paramref name="answers"/> is true (all by default).</summary>
    private static SimulatedElm327 Becm(Func<int, bool>? answers = null)
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            if (answers?.Invoke(index) ?? true)
                sim.ModuleAnswers[$"7E7|{CellCommand(index)}"] = CellFrame(index);
        sim.ModuleAnswers["7E4|22434F"] = "7EC0462434F3F";
        return sim;
    }

    private sealed record Diagnosis(HybridHealthService.HvPackReading? Reading, HybridFailure Failure, HybridReadEvidence Evidence);

    /// <summary>The same steps the scan service takes: read, classify a thrown exception, otherwise decide from the evidence.</summary>
    private static async Task<Diagnosis> DiagnoseAsync(AdapterRig rig, CancellationToken token = default)
    {
        var service = new HybridHealthService(rig.Obd);
        HybridHealthService.HvPackReading? pack = null;
        HybridFailure? failure = null;
        try
        {
            pack = await service.ReadGmPackAsync(Volt2012, false, new StringBuilder(), null, token);
        }
        catch (Exception ex) when (HybridReadDiagnosis.FromException(ex) is { } thrown)
        {
            failure = thrown;
        }

        failure ??= pack is null
            ? HybridReadDiagnosis.Decide(service.LastGmEvidence ?? new HybridReadEvidence(), HybridHealthService.MinimumGmCells, transportDown: !rig.Obd.IsAdapterConnected)
            : new HybridFailure(HybridFailureKind.None);
        return new Diagnosis(pack, failure, service.LastGmEvidence!);
    }

    private static Task<Diagnosis> DiagnoseOverAsync(SimTransport transport, IBluetoothService adapter) =>
        TransportRunner.RunAsync(transport, adapter, rig => DiagnoseAsync(rig));

    // ------------------------------------------------------------------ the adapter refuses a setup command

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task ARefusedSetupCommand_IsReportedAsThatCommand_OverEveryTransport(SimTransport transport)
    {
        var sim = Becm();
        sim.RejectedAt.Add("ATS0");

        var result = await DiagnoseOverAsync(transport, sim);

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.SetupNotAccepted, result.Failure.Kind);
        Assert.Equal("ATS0", result.Failure.Command);
        Assert.Contains("(ATS0)", HybridReadDiagnosis.StatusText(result.Failure));
        Assert.DoesNotContain("READY", HybridReadDiagnosis.StatusText(result.Failure));    // a limit of the adapter, nothing is said about the car
    }

    // ------------------------------------------------------------------ the module does not answer

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task AModuleThatAnswersNothing_IsReportedAsSilent_AfterTheThreeRequestsTheReaderAlwaysMakes(SimTransport transport)
    {
        var result = await DiagnoseOverAsync(transport, Becm(_ => false));       // NO DATA for every cell

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.ModuleSilent, result.Failure.Kind);
        Assert.Equal(3, result.Evidence.Requests);
        Assert.Equal(3, result.Evidence.Silent);
        Assert.Equal(0, result.Evidence.Answered + result.Evidence.OtherModule + result.Evidence.Unreadable + result.Evidence.Refused + result.Evidence.Other);
    }

    [Fact]
    public async Task ThreeRequestsThatTimeOut_AreSilence_NotALostLink_WhileTheLinkIsUp()
    {
        var scripted = new ScriptedAdapter(Becm());
        for (var index = 0; index < 3; index++)
            scripted.Fail(CellCommand(index), () => new TimeoutException("simulated: no answer"));

        var result = await DiagnoseOverAsync(SimTransport.Direct, scripted);

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.ModuleSilent, result.Failure.Kind);
        Assert.Equal(3, result.Evidence.Silent);
        Assert.Equal(0, result.Evidence.LinkLost);
    }

    // ------------------------------------------------------------------ another module answers

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task AnAdapterThatIgnoresTheHeader_IsReportedAsAnotherModuleAnswering_OverEveryTransport(SimTransport transport)
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            sim.ModuleAnswers[$"7DF|{CellCommand(index)}"] = "7E8 06 62 DD 01 03 B5 8F";   // whoever answers a functional request

        var result = await DiagnoseOverAsync(transport, new HeaderIgnoringAdapter(sim));

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.OtherModuleAnswered, result.Failure.Kind);
        Assert.Equal(3, result.Evidence.OtherModule);
        Assert.DoesNotContain("READY", HybridReadDiagnosis.StatusText(result.Failure));
    }

    // ------------------------------------------------------------------ something came back that could not be read

    [Fact]
    public async Task AnAdapterThatEchoesEveryCommand_IsReportedAsAnUnreadableReply_NotAsSilence()
    {
        var result = await DiagnoseOverAsync(SimTransport.Direct, new EchoingAdapter(Becm()));

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.UnreadableReply, result.Failure.Kind);
        Assert.Equal(0, result.Evidence.Silent);
        Assert.Equal(3, result.Evidence.Unreadable);
    }

    [Fact]
    public async Task ZeroVoltsForEveryCell_IsAnImplausibleReading_ReportedAsUnreadable_NotAsSilence()
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            sim.ModuleAnswers[$"7E7|{CellCommand(index)}"] = "7EF05" + "62" + GmVoltBoltBattery.CellDid(index).ToString("X4") + "0000";

        var result = await DiagnoseOverAsync(SimTransport.Direct, sim);

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.UnreadableReply, result.Failure.Kind);
        Assert.Equal(3, result.Evidence.ImplausibleValues);
        Assert.Equal(3, result.Evidence.Answered);                          // the text parsed; only the value is not a cell voltage
    }

    // ------------------------------------------------------------------ not decided: the old sentence stays

    [Fact]
    public async Task ANegativeResponseForEveryCell_IsNotDecided_AndKeepsTheSentenceThePageAlwaysHad()
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            sim.ModuleAnswers[$"7E7|{CellCommand(index)}"] = "7EF037F2231";      // request out of range

        var result = await DiagnoseOverAsync(SimTransport.Direct, sim);

        Assert.Null(result.Reading);
        Assert.Equal(3, result.Evidence.Refused);
        Assert.Equal(HybridFailureKind.None, result.Failure.Kind);              // a refusal is not "did not respond"
        Assert.Equal("ბატარეამ არ უპასუხა. ჩართე მანქანა (READY) და სცადე თავიდან.", HybridReadDiagnosis.StatusText(result.Failure));
    }

    [Fact]
    public void AMixtureOfSilenceAndARefusal_IsNotReportedAsSilence()
    {
        var evidence = new HybridReadEvidence();
        var request = GmVoltBoltBattery.CellRequest(0);
        evidence.RecordReply("NO DATA", request, null);
        evidence.RecordReply("7EF 03 7F 22 31", request, null);          // the module refuses: request out of range
        evidence.RecordReply("NO DATA", request, null);
        Assert.Equal(2, evidence.Silent);
        Assert.Equal(1, evidence.Refused);

        Assert.Equal(HybridFailureKind.None, HybridReadDiagnosis.Decide(evidence, 80, transportDown: false).Kind);
    }

    // ------------------------------------------------------------------ too few cells

    [Theory]
    [InlineData(70)]
    [InlineData(79)]
    public async Task SomeCellsAnswering_ButFewerThanTheMinimum_IsReportedWithTheNumberThatAnswered(int answering)
    {
        var result = await DiagnoseOverAsync(SimTransport.Direct, Becm(index => index < answering));

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.TooFewCells, result.Failure.Kind);
        Assert.Equal(answering, result.Failure.Count);
        var text = HybridReadDiagnosis.StatusText(result.Failure);
        Assert.Contains($"{answering}-მა", text);
        Assert.Contains("96", text);
        Assert.DoesNotContain("READY", text);                                // the module answered: nothing is said about the car
    }

    [Fact]
    public async Task ExactlyTheMinimumNumberOfCells_StillGivesAReading_AndNoFailure()
    {
        var result = await DiagnoseOverAsync(SimTransport.Direct, Becm(index => index < HybridHealthService.MinimumGmCells));

        Assert.NotNull(result.Reading);
        Assert.Equal(HybridHealthService.MinimumGmCells, result.Reading!.Blocks.Length);
        Assert.Equal(HybridFailureKind.None, result.Failure.Kind);
    }

    // ------------------------------------------------------------------ the link is lost, or the check is cancelled

    [Fact]
    public async Task ALinkLostBeforeTheSessionStarts_IsReportedAsALostLink()
    {
        var ble = await BleHarness.CreateAsync(Becm());
        var rig = await AdapterRigs.CreateAsync(ble.Service, connect: false);
        ble.LoseTheLink();

        var result = await DiagnoseAsync(rig);

        Assert.Null(result.Reading);
        Assert.Equal(HybridFailureKind.TransportLost, result.Failure.Kind);
    }

    [Fact]
    public async Task ALinkLostInTheMiddleOfTheCells_IsReportedAsALostLink_NotAsTooFewCellsOrSilence()
    {
        var ble = await BleHarness.CreateAsync(Becm(), gatt => gatt.FragmentSize = 7);
        var rig = await AdapterRigs.CreateAsync(ble.Service, connect: false);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line == CellCommand(9))
                ble.LoseTheLink();
            return Task.CompletedTask;
        };
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var result = await DiagnoseAsync(rig, guard.Token);

        Assert.Null(result.Reading);
        Assert.False(ble.Service.IsConnected);
        Assert.Equal(HybridFailureKind.TransportLost, result.Failure.Kind);
        Assert.DoesNotContain("READY", HybridReadDiagnosis.StatusText(result.Failure));
    }

    [Fact]
    public async Task ACancelledCheck_IsNeverClassified_AndStillCancels()
    {
        var rig = await AdapterRigs.CreateAsync(Becm());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiagnoseAsync(rig, cancelled.Token));

        Assert.Null(HybridReadDiagnosis.FromException(new OperationCanceledException()));
        Assert.Null(HybridReadDiagnosis.FromException(new TaskCanceledException()));
    }

    [Fact]
    public void ExceptionsThatSayNothingSpecific_AreNotClassified_SoThePageHandlesThemAsBefore()
    {
        Assert.Null(HybridReadDiagnosis.FromException(new InvalidOperationException("something else")));
        Assert.Null(HybridReadDiagnosis.FromException(new Elm327TimeoutException("a timeout")));
        Assert.Null(HybridReadDiagnosis.FromException(new Elm327CommandException("a refusal by the car", "7F2231")));
        Assert.Equal(HybridFailureKind.TransportLost, HybridReadDiagnosis.FromException(new IOException("link"))!.Kind);
        Assert.Equal(HybridFailureKind.TransportLost, HybridReadDiagnosis.FromException(new Elm327NotConnectedException("gone"))!.Kind);
        Assert.Equal(HybridFailureKind.SetupNotAccepted,
            HybridReadDiagnosis.FromException(new Elm327CommandException("x", "?") { UnsupportedCommand = "ATS0" })!.Kind);
    }

    // ------------------------------------------------------------------ the decision itself

    [Fact]
    public void TheOrderOfTheFindings_IsSetupThenLinkThenCellsThenWhatCameBack()
    {
        var request = GmVoltBoltBattery.CellRequest(0);

        HybridReadEvidence Evidence(Action<HybridReadEvidence> fill)
        {
            var evidence = new HybridReadEvidence();
            fill(evidence);
            return evidence;
        }

        var setupAndSilence = Evidence(e => { e.RecordException(new Elm327CommandException("x", "?") { UnsupportedCommand = "ATS0" }); e.RecordReply("NO DATA", request, null); });
        Assert.Equal(HybridFailureKind.SetupNotAccepted, HybridReadDiagnosis.Decide(setupAndSilence, 80, transportDown: true).Kind);

        var linkDownWithCells = Evidence(e => e.ValidCells = 70);
        Assert.Equal(HybridFailureKind.TransportLost, HybridReadDiagnosis.Decide(linkDownWithCells, 80, transportDown: true).Kind);

        var cellsAndForeign = Evidence(e => { e.ValidCells = 70; e.RecordReply("7E8 03 7F 22 31", request, null); });
        Assert.Equal(HybridFailureKind.TooFewCells, HybridReadDiagnosis.Decide(cellsAndForeign, 80, transportDown: false).Kind);

        var foreignAndSilence = Evidence(e => { e.RecordReply("7E8 06 62 DD 01 03 B5 8F", request, null); e.RecordReply("NO DATA", request, null); });
        Assert.Equal(HybridFailureKind.OtherModuleAnswered, HybridReadDiagnosis.Decide(foreignAndSilence, 80, transportDown: false).Kind);

        var enough = Evidence(e => e.ValidCells = 80);
        Assert.Equal(HybridFailureKind.None, HybridReadDiagnosis.Decide(enough, 80, transportDown: false).Kind);

        Assert.Equal(HybridFailureKind.None, HybridReadDiagnosis.Decide(new HybridReadEvidence(), 80, transportDown: false).Kind);   // no request made: nothing to say
    }

    [Fact]
    public void NoFinding_ClaimsThatTheCarIsNotReady_AndOnlyTheSilenceTextMentionsItAsOneOfSeveralPossibleCauses()
    {
        var kinds = Enum.GetValues<HybridFailureKind>();
        Assert.Equal(
            new[] { "None", "SetupNotAccepted", "ModuleSilent", "OtherModuleAnswered", "UnreadableReply", "TooFewCells", "TransportLost" },
            kinds.Select(kind => kind.ToString()));                              // there is no "not ready" finding to report

        foreach (var kind in kinds.Where(kind => kind is not (HybridFailureKind.None or HybridFailureKind.ModuleSilent)))
            Assert.DoesNotContain("READY", HybridReadDiagnosis.StatusText(new HybridFailure(kind, "ATS0", 70)));

        var silent = HybridReadDiagnosis.StatusText(new HybridFailure(HybridFailureKind.ModuleSilent));
        Assert.Contains("მაგალითად", silent);                                   // "for example": one possible cause among others
        Assert.Contains("ან ადაპტერს ამ მოდულთან წვდომა არ აქვს", silent);
    }

    [Fact]
    public void EveryFinding_HasItsOwnSentence_AndOnlyNoneKeepsTheOldOne()
    {
        var sentences = Enum.GetValues<HybridFailureKind>()
            .Select(kind => HybridReadDiagnosis.StatusText(new HybridFailure(kind, "ATS0", 70)))
            .ToList();

        Assert.Equal(sentences.Count, sentences.Distinct().Count());
        Assert.Equal(HybridReadDiagnosis.FallbackStatus, sentences[0]);
        Assert.Single(sentences, text => text == HybridReadDiagnosis.FallbackStatus);
    }

    // ------------------------------------------------------------------ what is sent does not change

    [Fact]
    public async Task TheRequestsOfAFullRead_AreStillOneCellRequestPerCell_InOrder_AndNothingElseAboutTheCells()
    {
        var sim = Becm();
        var rig = await AdapterRigs.CreateAsync(sim);
        sim.Sent.Clear();

        var result = await DiagnoseAsync(rig);

        Assert.NotNull(result.Reading);
        Assert.Equal(Enumerable.Range(0, 96).Select(CellCommand),
            sim.Sent.Where(command => command.StartsWith("22", StringComparison.Ordinal) && command != "22434F" && command != "2241A3"));
        Assert.Equal(96, result.Evidence.Answered);                              // every request was seen by the evidence, none was added
        Assert.Equal(96, result.Evidence.ValidCells);
    }

    // ------------------------------------------------------------------ how the scan service uses it (pinned from the source)

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    [Fact]
    public void TheScanService_AsksTheDiagnosisForTheSentence_AndKeepsTheOldSentenceOnlyAsTheFallback()
    {
        var service = Source("Services", "HybridHealthService.cs");
        var branch = service[service.IndexOf("pack = await ReadGmPackAsync(", StringComparison.Ordinal)..];
        branch = branch[..branch.IndexOf("if (pack is not null)", StringComparison.Ordinal)];

        // A thrown setup refusal or lost link is classified; cancellation and every other exception are not caught.
        Assert.Contains("catch (Exception ex) when (HybridReadDiagnosis.FromException(ex) is { } thrown)", branch);
        Assert.Equal(1, branch.Split("catch").Length - 1);
        // An empty result is decided from the evidence of this read and the state of the link when it ended.
        Assert.Contains("HybridReadDiagnosis.Decide(", branch);
        Assert.Contains("LastGmEvidence ?? new HybridReadEvidence(), MinimumGmCells, transportDown: !_obd.IsAdapterConnected", branch);
        Assert.Contains("HybridReadDiagnosis.StatusText(failure)", branch);
        Assert.Contains("if (pack is null && (GmKind(vehicle) is not null || profile == HybridProfileKind.ChevroletVolt))", branch);
        // The old sentence is no longer written into the service; it lives in one place, as the fallback.
        Assert.DoesNotContain("ბატარეამ არ უპასუხა", branch);
        Assert.Contains("\"ბატარეამ არ უპასუხა. ჩართე მანქანა (READY) და სცადე თავიდან.\"", Source("Services", "HybridReadDiagnosis.cs"));
    }

    [Fact]
    public void ThePage_StillHandlesCancellationAndAnyOtherExceptionExactlyAsBefore_AndNeedsNoChange()
    {
        var page = Source("ViewModels", "HybridHealthViewModel.cs");

        Assert.Contains("catch (OperationCanceledException)", page);
        Assert.Contains("ScanStage = L(\"შემოწმება შეჩერდა.\", \"Check stopped.\");", page);
        Assert.Contains("ScanStage = L($\"შემოწმება ვერ დასრულდა: {ex.Message}\", $\"Check failed: {ex.Message}\");", page);
        Assert.Contains("ScanStage = result.Status;", page);                    // the sentence of the finding is shown as the status
    }

    [Fact]
    public void TheReaderSendsTheSameRequests_TheEvidenceIsOnlyPassedAlong()
    {
        var gm = Source("Services", "HybridHealthService.Gm.cs");
        var pack = Source("Services", "HybridHealthService.Pack.cs");

        Assert.Contains("GM.CellRequest(index), index < 3 ? log : null, token, evidence", gm);
        Assert.Contains("evidence?.RecordException(ex);", pack);
        Assert.Contains("evidence?.RecordReply(raw, request, data);", pack);
        // The two session sends (the request and, after "response pending", the longer wait) are unchanged.
        Assert.Contains("session.SendAsync(request.Tx, request.Rx, request.Ext29, request.Payload, false, token, request.ExtendedAddress)", pack);
        Assert.Contains("session.SendAsync(request.Tx, request.Rx, request.Ext29, request.Payload, true, token, request.ExtendedAddress)", pack);
    }
}
