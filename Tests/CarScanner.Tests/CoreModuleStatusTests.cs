using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

/// <summary>
/// "ABS answered with no codes" and "ABS was never reached" must never look the same. These checks run the evaluator on
/// scan outcomes written by hand; they do not prove anything about a real car.
/// </summary>
public sealed class CoreModuleStatusTests
{
    private static ModuleDtcReport Report(string id, int codes, bool complete = true, string protocol = "UDS", int tx = 0x760) => new()
    {
        ModuleId = id,
        TransmitCanId = tx,
        ReceiveCanId = tx + 8,
        Responded = true,
        CodeListComplete = complete,
        Protocol = protocol,
        Codes = Enumerable.Range(0, codes).Select(i => new ModuleDtc { Code = "C" + (1000 + i) }).ToList()
    };

    private static ModuleProbe Probe(string id, ModuleProbeState state, int tx = 0x760, string? foreign = null) =>
        new(id, tx, tx + 8, false, null, state, string.Empty, foreign, string.Empty);

    private static CoreModuleStatus Of(CoreModule module, IReadOnlyList<ModuleDtcReport> reports, IReadOnlyList<ModuleProbe> probes, ModuleScanContext? context = null) =>
        CoreModuleStatusEvaluator.Evaluate(reports, probes, context ?? ModuleScanContext.Unknown).Single(item => item.Module == module);

    [Fact]
    public void ACompleteEmptyList_FromTheModuleItself_IsTheOnlyWayToSay_NoFaults()
    {
        var status = Of(CoreModule.Abs, new[] { Report("ABS", 0) }, new[] { Probe("ABS", ModuleProbeState.Answered) });
        Assert.Equal(CoreModuleReach.ReachedNoFaultCodes, status.Reach);
        Assert.True(status.ConfirmedNoFaults);
        Assert.True(status.Reached);
    }

    [Fact]
    public void AnAnsweringModuleWithCodes_ReportsTheCount()
    {
        var status = Of(CoreModule.Abs, new[] { Report("ABS", 3) }, new[] { Probe("ABS", ModuleProbeState.Answered) });
        Assert.Equal(CoreModuleReach.ReachedWithFaultCodes, status.Reach);
        Assert.Equal(3, status.FaultCount);
        Assert.False(status.ConfirmedNoFaults);
    }

    [Fact]
    public void Silence_IsNeverNoFaults_AndNeverAFaultyModule()
    {
        var status = Of(CoreModule.Abs, Array.Empty<ModuleDtcReport>(), new[] { Probe("ABS", ModuleProbeState.NoReply) });
        Assert.Equal(CoreModuleReach.NoReply, status.Reach);
        Assert.False(status.ConfirmedNoFaults);
        Assert.False(status.Reached);
        Assert.Contains("does not mean", status.TextEn);
    }

    [Fact]
    public void ACutOffReply_IsNotNoFaults()
    {
        var status = Of(CoreModule.Abs, new[] { Report("ABS", 0, complete: false) }, new[] { Probe("ABS", ModuleProbeState.AnsweredIncomplete) });
        Assert.Equal(CoreModuleReach.IncompleteReply, status.Reach);
        Assert.False(status.ConfirmedNoFaults);
    }

    [Fact]
    public void AnAnswerWithoutACodeList_IsReachedButNotNoFaults()
    {
        var status = Of(CoreModule.Abs, new[] { Report("ABS", 0, protocol: "—") }, new[] { Probe("ABS", ModuleProbeState.Answered) });
        Assert.Equal(CoreModuleReach.ReachedNoCodeList, status.Reach);
        Assert.False(status.ConfirmedNoFaults);
    }

    [Fact]
    public void ARefusalOnly_IsAnAnswer_ButNotACodeList()
    {
        var status = Of(CoreModule.Srs, Array.Empty<ModuleDtcReport>(), new[] { Probe("SRS", ModuleProbeState.AnsweredNegativeOnly, 0x737) });
        Assert.Equal(CoreModuleReach.RefusedCodeList, status.Reach);
        Assert.False(status.ConfirmedNoFaults);
    }

    [Fact]
    public void AForeignReplyOnly_IsNotCountedAsTheModule()
    {
        var status = Of(CoreModule.Abs, Array.Empty<ModuleDtcReport>(), new[] { Probe("ABS", ModuleProbeState.ForeignReplyOnly, foreign: "0x7E8") });
        Assert.Equal(CoreModuleReach.ForeignReplyOnly, status.Reach);
        Assert.False(status.Reached);
    }

    [Fact]
    public void ATechnicalFailure_IsNotSilence_AndNotNoFaults()
    {
        var status = Of(CoreModule.Abs, Array.Empty<ModuleDtcReport>(), new[] { Probe("ABS", ModuleProbeState.Error) });
        Assert.Equal(CoreModuleReach.RequestFailed, status.Reach);
    }

    [Fact]
    public void WhenOneAddressAnswers_AndAnotherIsSilent_TheAnswerWins()
    {
        // BMW lists two engine addresses; the silent one must not hide the one that answered.
        var reports = new[] { Report("ECM", 0, tx: 0x612) };
        var probes = new[] { Probe("ECM", ModuleProbeState.NoReply, 0x7E0), Probe("ECM", ModuleProbeState.Answered, 0x612) };
        Assert.Equal(CoreModuleReach.ReachedNoFaultCodes, Of(CoreModule.Engine, reports, probes).Reach);
    }

    [Fact]
    public void AModuleThatWasNeverAsked_SaysWhy_WhileTheCarWasMoving()
    {
        var moving = new ModuleScanContext(CarMoving: true, CanConnection: true);
        var statuses = CoreModuleStatusEvaluator.Evaluate(Array.Empty<ModuleDtcReport>(), new[] { Probe("ECM", ModuleProbeState.Answered, 0x7E0) }, moving);
        Assert.Equal(CoreModuleReach.NotAskedCarMoving, statuses.Single(item => item.Module == CoreModule.Abs).Reach);
        Assert.Equal(CoreModuleReach.NotAskedCarMoving, statuses.Single(item => item.Module == CoreModule.Srs).Reach);
    }

    [Fact]
    public void AModuleThatWasNeverAsked_SaysWhy_WhenNoVerifiedAddressIsKnown()
    {
        var status = Of(CoreModule.Abs, Array.Empty<ModuleDtcReport>(), new[] { Probe("ECM", ModuleProbeState.Answered, 0x7E0) });
        Assert.Equal(CoreModuleReach.NotAskedNoKnownAddress, status.Reach);
        Assert.Contains("not asked", status.TextEn);
    }

    [Fact]
    public void ANonCanConnection_IsReportedAsUnsupported_NotAsNoFaults()
    {
        var status = Of(CoreModule.Abs, Array.Empty<ModuleDtcReport>(), Array.Empty<ModuleProbe>(), new ModuleScanContext(false, false));
        Assert.Equal(CoreModuleReach.NotAskedUnsupportedConnection, status.Reach);
    }

    [Fact]
    public void TheHybridBattery_AcceptsEitherModuleId()
    {
        Assert.Equal(CoreModuleReach.ReachedWithFaultCodes, Of(CoreModule.HybridBattery, new[] { Report("BMS", 1, tx: 0x7E7) }, new[] { Probe("BMS", ModuleProbeState.Answered, 0x7E7) }).Reach);
        Assert.Equal(CoreModuleReach.ReachedNoFaultCodes, Of(CoreModule.HybridBattery, new[] { Report("HYB", 0, tx: 0x7E4) }, new[] { Probe("HYB", ModuleProbeState.Answered, 0x7E4) }).Reach);
    }

    [Fact]
    public void EveryCoreModule_GetsExactlyOneStatus_WithBothLanguages()
    {
        var all = CoreModuleStatusEvaluator.Evaluate(Array.Empty<ModuleDtcReport>(), Array.Empty<ModuleProbe>(), ModuleScanContext.Unknown);
        Assert.Equal(Enum.GetValues<CoreModule>().Length, all.Count);
        Assert.All(all, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.TextKa));
            Assert.False(string.IsNullOrWhiteSpace(item.TextEn));
            Assert.False(string.IsNullOrWhiteSpace(item.NameEn));
        });
    }
}
