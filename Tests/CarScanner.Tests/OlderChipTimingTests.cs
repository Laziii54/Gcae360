using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>Answer window and "response pending" on chips older than v1.2 / v2.1 (simulation only).</summary>
public sealed class OlderChipTimingTests
{
    private static readonly ServiceDiagnosticAddress Ecm = new(0x7E0, 0x7E8, false, null, "11-bit CAN", "ECM");

    private static async Task<(SimulatedElm327 Sim, Elm327Protocol Elm, OBDService Obd)> CreateAsync(bool noAdaptiveTiming)
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        if (noAdaptiveTiming)
            sim.RejectedAt.Add("ATAT");
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        sim.Sent.Clear();
        return (sim, elm, obd);
    }

    [Fact]
    public async Task WithoutAdaptiveTiming_TheAnswerWindowIsWidenedForTheRead_AndPutBack()
    {
        var (sim, _, obd) = await CreateAsync(noAdaptiveTiming: true);

        var raw = await obd.ReadPhysicalCanReadOnlyAsync(Ecm, new byte[] { 0x22, 0x11, 0x9F }, TimeSpan.FromSeconds(2));

        Assert.Contains("62 11 9F 80", raw);
        var widen = sim.Sent.IndexOf("ATST64");
        var request = sim.Sent.IndexOf("22119F");
        var restore = sim.Sent.LastIndexOf("ATST32");
        Assert.True(widen >= 0 && widen < request && request < restore);
    }

    [Fact]
    public async Task WithAdaptiveTiming_NothingAboutTheWindowIsChanged()
    {
        var (sim, _, obd) = await CreateAsync(noAdaptiveTiming: false);

        await obd.ReadPhysicalCanReadOnlyAsync(Ecm, new byte[] { 0x22, 0x11, 0x9F }, TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(sim.Sent, c => c.StartsWith("ATST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AResponsePendingOnlyReply_IsAskedOnceMore_AndTheFinalAnswerIsUsed()
    {
        var (sim, _, obd) = await CreateAsync(noAdaptiveTiming: false);
        sim.PendingBeforeAnswer = 1;

        var raw = await obd.ReadPhysicalCanReadOnlyAsync(Ecm, new byte[] { 0x22, 0x11, 0x9F }, TimeSpan.FromSeconds(2));

        Assert.Contains("62 11 9F 80", raw);
        Assert.Equal(2, sim.Sent.Count(c => c == "22119F"));   // exactly one repeat, nothing else is repeated
    }

    [Fact]
    public async Task APendingReplyThatNeverEnds_IsNotAskedForEver()
    {
        var (sim, _, obd) = await CreateAsync(noAdaptiveTiming: false);
        sim.PendingBeforeAnswer = 10;

        var raw = await obd.ReadPhysicalCanReadOnlyAsync(Ecm, new byte[] { 0x22, 0x11, 0x9F }, TimeSpan.FromSeconds(2));

        Assert.Contains("7F 22 78", raw);
        Assert.Equal(2, sim.Sent.Count(c => c == "22119F"));
    }
}
