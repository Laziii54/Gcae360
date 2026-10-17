using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Priority 3 of the live-data audit: long-running LiveDataEngine sessions against the real
/// OBDService/Elm327Protocol/SimulatedElm327 chain — not just the pure LiveDataHealthMonitor or the
/// transport layer alone. These are deliberately real-time (not a mocked clock), since the point is
/// proving the independent watchdog timer and the loop's own lifecycle actually behave this way over
/// elapsed wall-clock time, not just that the maths would work out.
/// </summary>
public sealed class LiveDataEngineLifecycleTests
{
    private static async Task<(LiveDataEngine Engine, SimulatedElm327 Sim)> CreateAsync()
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        var engine = new LiveDataEngine(obd, sim, NullLogger<LiveDataEngine>.Instance);
        return (engine, sim);
    }

    [Fact]
    public async Task ReopeningThePage_StartingWhileAlreadyRunning_NeverCreatesASecondPollingLoop()
    {
        var (engine, _) = await CreateAsync();
        await engine.StartAsync();
        await engine.StartAsync(); // e.g. the user leaves and reopens Live Data before it ever stopped
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        await engine.StopAsync(force: true);
        await Task.Delay(TimeSpan.FromMilliseconds(400)); // let any in-flight tick of an orphaned second loop settle

        var sawUpdateAfterStop = false;
        engine.LiveValuesUpdated += (_, __) => sawUpdateAfterStop = true;
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.False(sawUpdateAfterStop, "A value update fired after StopAsync returned — an orphaned second loop is still running.");
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task NavigatingAway_WhileATickIsHungForever_StopsPromptly_NoDeadlock()
    {
        var (engine, sim) = await CreateAsync();
        await engine.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        sim.Fault = SimFault.HangForever;
        await Task.Delay(TimeSpan.FromMilliseconds(500)); // let the loop get stuck inside the hung command

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await engine.StopAsync(force: true);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"StopAsync took {sw.Elapsed} — navigating away from the page would have frozen the UI.");
    }

    [Fact]
    public async Task AfterLinkDrop_AndReconnect_TheEngineResumesPollingOnItsOwn()
    {
        var (engine, sim) = await CreateAsync();
        await engine.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        Assert.True(engine.IsRunning);

        sim.Drop();
        var stopDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (engine.IsRunning && DateTime.UtcNow < stopDeadline)
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(engine.IsRunning);

        await sim.ConnectAsync(new BleDeviceItem());

        // The resume runs on a background Task.Run fired from the ConnectionChanged handler, so it is
        // not guaranteed to have been scheduled yet the instant ConnectAsync returns — poll instead of
        // a single fixed delay to avoid a flaky race against the thread pool under load.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!engine.IsRunning && DateTime.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.True(engine.IsRunning); // resumed by itself, no new StartAsync call from the test
        await engine.StopAsync(force: true);
    }

    [Fact]
    public async Task ARecoverableFailure_NeverPermanentlyBlocksTheQueue_PollingContinuesOnceItClears()
    {
        var (engine, sim) = await CreateAsync();
        var tickCount = 0;
        engine.LiveValuesUpdated += (_, __) => Interlocked.Increment(ref tickCount);

        await engine.StartAsync();
        sim.Fault = SimFault.Timeout;
        sim.FaultAfter = 0;
        await Task.Delay(TimeSpan.FromSeconds(3)); // several ticks' worth of failures, no restart

        sim.Fault = SimFault.None; // the adapter starts answering again, same as a real transient drop clearing
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.True(Volatile.Read(ref tickCount) > 0, "No successful tick after the fault cleared — the polling queue stayed blocked.");
        Assert.True(engine.IsRunning, "The loop stopped itself instead of continuing to retry — a recoverable failure should never need an app restart.");
        await engine.StopAsync(force: true);
    }

    [Fact]
    public async Task AHungTick_IsIdentifiedAsStale_ThenFrozen_ByTheIndependentWatchdog_OverRealElapsedTime()
    {
        var (engine, sim) = await CreateAsync();
        var healthEvents = new List<LiveDataHealth>();
        engine.HealthChanged += (_, h) => { lock (healthEvents) healthEvents.Add(h); };

        await engine.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        sim.Fault = SimFault.HangForever;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            lock (healthEvents)
                if (healthEvents.Contains(LiveDataHealth.Frozen))
                    break;
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        lock (healthEvents)
        {
            Assert.Contains(LiveDataHealth.Stale, healthEvents);
            Assert.Contains(LiveDataHealth.Frozen, healthEvents);
        }

        await engine.StopAsync(force: true);
    }
}
