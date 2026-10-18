using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>Less traffic for a cheap adapter: rotation only where needed, and a clean adapter after a long scan (simulation).</summary>
public sealed class PollingLoadAndHygieneTests
{
    private static async Task<(LiveDataEngine Engine, SimulatedElm327 Sim)> CreateAsync()
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        return (new LiveDataEngine(obd, sim, NullLogger<LiveDataEngine>.Instance), sim);
    }

    private static async Task<int> DistinctPidsAfterAsync(LiveDataEngine engine, SimulatedElm327 sim)
    {
        await engine.StartAsync();
        sim.Sent.Clear();
        await Task.Delay(TimeSpan.FromMilliseconds(2500));
        await engine.StopAsync(force: true);
        return sim.Sent.ToArray().Where(c => c.Length == 4 && c.StartsWith("01", StringComparison.Ordinal)).Distinct().Count();
    }

    [Fact]
    public async Task ByDefault_OnlyTheCorePidsArePolled()
    {
        var (engine, sim) = await CreateAsync();
        Assert.True(await DistinctPidsAfterAsync(engine, sim) <= 6);
    }

    [Fact]
    public async Task TheLiveDataList_CanAskForTheRotationOverEveryAdvertisedPid()
    {
        var (engine, sim) = await CreateAsync();
        engine.SetFullRotation(true);
        Assert.True(await DistinctPidsAfterAsync(engine, sim) > 6);
    }

    [Fact]
    public async Task ALongModuleSession_ResetsTheAdapterAfterwards_AShortOneDoesNot()
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();

        async Task RunAsync(int requests)
        {
            sim.Sent.Clear();
            await elm.RunDtcModuleSessionAsync(async session =>
            {
                for (var i = 0; i < requests; i++)
                    await session.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x19, 0x02, 0xFF }, false, CancellationToken.None);
            });
        }

        await RunAsync(5);
        Assert.DoesNotContain(sim.Sent, c => c.StartsWith("ATZ", StringComparison.Ordinal));

        await RunAsync(31);
        Assert.Contains(sim.Sent, c => c.StartsWith("ATZ", StringComparison.Ordinal));
        Assert.True(obd.IsInitialized);
    }
}
