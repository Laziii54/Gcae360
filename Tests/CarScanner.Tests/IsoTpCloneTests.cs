using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>The ISO-TP module path (hybrid battery, OEM live data) on an older-microcode clone (simulation only).</summary>
public sealed class IsoTpCloneTests
{
    private static async Task<(SimulatedElm327 Sim, Elm327Protocol Elm, OBDService Obd)> CreateAsync(bool clone)
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E4|22431F"] = "7EC 05 62 43 1F 12 34";
        if (clone)
            foreach (var c in new[] { "ATS1", "ATS0", "ATD0", "ATCFC1", "ATCRA", "ATAR" })
                sim.RejectedAt.Add(c);
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        sim.Sent.Clear();
        return (sim, elm, obd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheIsoTpRead_GivesTheSameAnswer_OnAFullAdapterAndOnAClone(bool clone)
    {
        var (sim, elm, obd) = await CreateAsync(clone);

        var raw = await elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0x43, 0x1F }, 0x7E4, 0x7EC, false, TimeSpan.FromSeconds(2));

        Assert.Contains("62 43 1F 12 34", raw);
        Assert.True(obd.IsInitialized);
        Assert.Equal("7DF", sim.CurrentHeader);
        Assert.False(sim.HeadersOn);
    }

    [Fact]
    public async Task OnAClone_AnotherModulesFramesAreDropped()
    {
        var (sim, elm, _) = await CreateAsync(true);
        sim.ModuleAnswers["7E4|22431F"] = "7E9 05 62 43 1F 99 99 7EC 05 62 43 1F 12 34";

        var raw = await elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0x43, 0x1F }, 0x7E4, 0x7EC, false, TimeSpan.FromSeconds(2));

        Assert.DoesNotContain("7E9", raw);
        Assert.Contains("12 34", raw);
    }
}
