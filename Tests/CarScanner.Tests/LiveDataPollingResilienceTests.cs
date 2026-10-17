using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Exercises the real OBDService/Elm327Protocol/SimulatedElm327 chain the way LiveDataEngine's
/// polling loop actually calls it — over many consecutive ticks and every fault the simulator can
/// inject — to answer the audit's question directly: does an extended live-data session ever stop
/// making progress? "Extended" here means wall-clock-bounded, repeated calls, not real duration.
/// </summary>
public sealed class LiveDataPollingResilienceTests
{
    /// <summary>
    /// Connects and initializes with a clean adapter first, then applies the requested fault —
    /// never during InitializeAsync itself. InitializeAsync has its own real, multi-attempt,
    /// multi-second retry/backoff (by design, for a real adapter that is still booting), so a
    /// fault configured before it runs makes a test "slow" for genuine, working-as-designed
    /// reasons that have nothing to do with the steady-state polling behaviour under test.
    /// </summary>
    private static async Task<(OBDService Obd, SimulatedElm327 Sim)> CreateAsync()
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        return (obd, sim);
    }

    [Fact]
    public async Task ContinuousPolling_OverManyTicks_NeverStopsMakingProgress_DespiteRecurringTransientMisses()
    {
        var (obd, sim) = await CreateAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ticks = 0;
        for (var i = 0; i < 40; i++)
        {
            // One PID genuinely misses its answer every 5th tick — a real adapter missing a single
            // beat now and then, not a dead link. 40 ticks of that must still make it to the end.
            if (i % 5 == 0)
                sim.TimeoutOnNextSend.Add("010C");

            var data = await obd.GetLiveDataAsync();
            Assert.NotNull(data);
            ticks++;
        }
        sw.Stop();

        Assert.Equal(40, ticks);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"40 ticks took {sw.Elapsed} — looks stuck, not just slow.");
    }

    [Fact]
    public async Task DelayedAndMissingResponses_DoNotCorruptLaterTicks()
    {
        var (obd, sim) = await CreateAsync();
        sim.Fault = SimFault.NoData;
        sim.FaultAfter = 0;

        for (var i = 0; i < 10; i++)
        {
            var data = await obd.GetLiveDataAsync();
            Assert.NotNull(data);
        }
    }

    [Fact]
    public async Task MalformedAndPartialResponses_AreSkipped_WithoutBreakingTheNextTick()
    {
        var (obd, sim) = await CreateAsync();
        sim.Fault = SimFault.Truncated;
        sim.FaultAfter = 0;

        for (var i = 0; i < 10; i++)
        {
            var data = await obd.GetLiveDataAsync();
            Assert.NotNull(data);
        }
    }

    [Fact]
    public async Task NoiseOnEveryCommand_NeverThrowsOrHangs_AcrossManyTicks()
    {
        // Noise (junk bytes and stray status-looking lines) wrapped around every single polling
        // answer once the session is already up — proving the loop tolerates noise on every tick,
        // not just an occasional one. Decoding through noise is covered elsewhere
        // (Obd2SimulatorTests); this test is only about the polling loop staying alive. The initial
        // handshake probe is deliberately left clean: ELM327 initialization validates that first
        // reply strictly and noise there is a different, already-covered failure path, not this one.
        var (obd, sim) = await CreateAsync();
        sim.Fault = SimFault.Noise;
        sim.FaultAfter = 0;

        for (var i = 0; i < 15; i++)
        {
            var data = await obd.GetLiveDataAsync();
            Assert.NotNull(data);
        }
    }

    [Fact]
    public async Task AdapterDisconnectMidPoll_ThrowsCleanly_RatherThanHanging()
    {
        var (obd, sim) = await CreateAsync();
        sim.Fault = SimFault.Disconnect;
        sim.FaultAfter = 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Exception? observed = null;
        for (var i = 0; i < 10 && observed is null; i++)
        {
            try
            {
                await obd.GetLiveDataAsync();
            }
            catch (Exception ex)
            {
                observed = ex;
            }
        }
        sw.Stop();

        Assert.NotNull(observed);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Disconnect took {sw.Elapsed} to surface across 10 ticks — should be prompt.");
    }

    [Fact]
    public async Task CancellingAnOutstandingCommand_ReturnsPromptly_InsteadOfWaitingOutTheFullTimeout()
    {
        var (obd, sim) = await CreateAsync();
        sim.Fault = SimFault.Timeout;
        sim.FaultAfter = 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obd.GetLiveDataAsync(cts.Token));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Cancellation took {sw.Elapsed} to take effect.");
    }

    /// <summary>
    /// The exact worst case this audit was asked to find: a single awaited call that ignores its
    /// CancellationToken entirely (modelling a native Android InputStream read some Bluetooth
    /// bridges never actually interrupt). Exercised directly against the transport double — not
    /// through OBDService/Elm327Protocol's own gates — because the point here is the fault
    /// primitive itself: nothing can make this call return early, cancellation included. The only
    /// thing that can ever regain control is a caller racing it from the outside with its own
    /// timer, exactly how BluetoothService's own SendCommandAsync (Task.WhenAny against a timeout
    /// task) and LiveDataEngine's health timer work in production. A cancellation token is passed
    /// here and deliberately expected to do nothing, proving that race — not the token — is what
    /// has to carry the load for this failure mode.
    /// </summary>
    [Fact]
    public async Task AHungCommandThatIgnoresCancellation_OnlyReturnsControl_WhenRacedFromOutside()
    {
        var sim = new SimulatedElm327 { Fault = SimFault.HangForever };
        await sim.ConnectAsync(new BleDeviceItem());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var callTask = sim.SendCommandAsync("010C", TimeSpan.FromSeconds(1), cts.Token);
        var winner = await Task.WhenAny(callTask, Task.Delay(TimeSpan.FromMilliseconds(300)));

        Assert.NotEqual(callTask, winner);
        Assert.False(callTask.IsCompleted, "The simulated hang resolved (possibly via the cancellation token) — the test double no longer models the bug it was built for.");
    }
}
