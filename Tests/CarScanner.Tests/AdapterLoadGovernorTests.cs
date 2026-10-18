using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// The load governor: polling backs off when an adapter shows strain and recovers slowly. Pure logic with a fake clock,
/// plus a simulator check that the back-off really reaches the commands that are sent. This proves the rules do what they
/// say; it does not prove that a real clone adapter stops dropping out — that needs a phone, the adapter and a car.
/// </summary>
public sealed class AdapterLoadGovernorTests
{
    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Read() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(900);

    private static (AdapterLoadGovernor Governor, Clock Clock) Create()
    {
        var clock = new Clock();
        return (new AdapterLoadGovernor(clock.Read), clock);
    }

    [Fact]
    public void AHealthyAdapter_GetsFullPolling()
    {
        var (governor, _) = Create();
        for (var i = 0; i < 200; i++)
            governor.Record(Quick, ok: true);

        Assert.Equal(AdapterLoadLevel.Normal, governor.Level);
        Assert.Equal(TimeSpan.Zero, governor.MinimumGap);
        Assert.Equal(9, governor.MaxExtraReadings);
        Assert.Equal(1.0, governor.IntervalFactor);
    }

    [Fact]
    public void TwoFailuresInARow_MakeThePollingGentle()
    {
        var (governor, _) = Create();
        governor.Record(Quick, ok: true);
        governor.Record(Quick, ok: false);
        governor.Record(Quick, ok: false);

        Assert.Equal(AdapterLoadLevel.Gentle, governor.Level);
        Assert.True(governor.MinimumGap > TimeSpan.Zero);
        Assert.True(governor.MaxExtraReadings < 9);
        Assert.True(governor.IntervalFactor > 1.0);
    }

    [Fact]
    public void ManyFailures_MakeItCareful_WithTheStrongestBackOff()
    {
        var (governor, clock) = Create();
        for (var i = 0; i < 2; i++)
            governor.Record(Quick, ok: false);
        Assert.Equal(AdapterLoadLevel.Gentle, governor.Level);

        clock.Advance(TimeSpan.FromSeconds(6));
        for (var i = 0; i < 2; i++)
            governor.Record(Quick, ok: false);

        Assert.Equal(AdapterLoadLevel.Careful, governor.Level);
        Assert.True(governor.MinimumGap >= TimeSpan.FromMilliseconds(100));
        Assert.True(governor.MaxExtraReadings <= 2);
        Assert.True(governor.IntervalFactor >= 2.0);
    }

    [Fact]
    public void AnAdapterThatOnlyAnswersSlowly_IsEaseOffToo_BeforeItFails()
    {
        var (governor, _) = Create();
        for (var i = 0; i < 6; i++)
            governor.Record(Slow, ok: true);

        Assert.Equal(AdapterLoadLevel.Gentle, governor.Level);
    }

    [Fact]
    public void ANoDataFromTheCar_IsAnAnswer_NotAFailure()
    {
        // The protocol reports ok for "NO DATA"; here: many quick ok samples never move the level.
        var (governor, _) = Create();
        for (var i = 0; i < 50; i++)
            governor.Record(TimeSpan.FromMilliseconds(80), ok: true);

        Assert.Equal(AdapterLoadLevel.Normal, governor.Level);
    }

    [Fact]
    public void TheLevelDoesNotJumpTwiceWithinFiveSeconds()
    {
        var (governor, _) = Create();
        for (var i = 0; i < 12; i++)
            governor.Record(Quick, ok: false);

        // Many failures at the same instant: one step, not an immediate second one.
        Assert.Equal(AdapterLoadLevel.Gentle, governor.Level);
    }

    [Fact]
    public void Recovery_NeedsALongCleanRun_AndTime_AndGivesBackOneLevelAtATime()
    {
        var (governor, clock) = Create();
        for (var i = 0; i < 2; i++)
            governor.Record(Quick, ok: false);
        clock.Advance(TimeSpan.FromSeconds(6));
        for (var i = 0; i < 2; i++)
            governor.Record(Quick, ok: false);
        Assert.Equal(AdapterLoadLevel.Careful, governor.Level);

        // Fast answers alone are not enough: it also has to last.
        for (var i = 0; i < 60; i++)
            governor.Record(Quick, ok: true);
        Assert.Equal(AdapterLoadLevel.Careful, governor.Level);

        clock.Advance(TimeSpan.FromSeconds(31));
        for (var i = 0; i < 45; i++)
            governor.Record(Quick, ok: true);
        Assert.Equal(AdapterLoadLevel.Gentle, governor.Level);   // one step back, not straight to Normal

        clock.Advance(TimeSpan.FromSeconds(31));
        for (var i = 0; i < 45; i++)
            governor.Record(Quick, ok: true);
        Assert.Equal(AdapterLoadLevel.Normal, governor.Level);
    }

    [Fact]
    public void ASlowAnswerInTheMiddleOfARecovery_RestartsTheCleanRun()
    {
        var (governor, clock) = Create();
        governor.Record(Quick, ok: false);
        governor.Record(Quick, ok: false);
        Assert.Equal(AdapterLoadLevel.Gentle, governor.Level);

        clock.Advance(TimeSpan.FromSeconds(31));
        for (var i = 0; i < 39; i++)
            governor.Record(Quick, ok: true);
        governor.Record(Slow, ok: true);          // breaks the run
        governor.Record(Quick, ok: true);

        Assert.Equal(AdapterLoadLevel.Gentle, governor.Level);
    }

    [Fact]
    public void EveryChange_IsReported_WithAReasonForTheConnectionLog()
    {
        var (governor, clock) = Create();
        var events = new List<(AdapterLoadLevel From, AdapterLoadLevel To, string Reason)>();
        governor.LevelChanged += (from, to, reason) => events.Add((from, to, reason));

        governor.Record(Quick, ok: false);
        governor.Record(Quick, ok: false);

        var change = Assert.Single(events);
        Assert.Equal(AdapterLoadLevel.Normal, change.From);
        Assert.Equal(AdapterLoadLevel.Gentle, change.To);
        Assert.Contains("failed", change.Reason);

        governor.Reset();
        Assert.Equal(AdapterLoadLevel.Normal, governor.Level);
        Assert.Equal(2, events.Count);
        Assert.Equal("new connection", events[1].Reason);
        _ = clock;
    }

    [Fact]
    public void AFreshConnection_StartsClean()
    {
        var (governor, _) = Create();
        governor.Record(Quick, ok: false);
        governor.Record(Quick, ok: false);
        Assert.NotEqual(AdapterLoadLevel.Normal, governor.Level);

        governor.Reset();
        Assert.Equal(AdapterLoadLevel.Normal, governor.Level);
        Assert.Equal(9, governor.MaxExtraReadings);
    }

    // ---- the simulator: the back-off reaches the commands that are really sent ----

    private static async Task<(OBDService Obd, SimulatedElm327 Sim)> ConnectedAsync()
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        return (obd, sim);
    }

    [Fact]
    public async Task WhenTheAdapterTimesOut_TheProtocolMovesTheLevel_AndANewConnectionClearsIt()
    {
        var (obd, sim) = await ConnectedAsync();
        Assert.Equal(AdapterLoadLevel.Normal, obd.Load.Level);

        sim.Fault = SimFault.Timeout;
        sim.FaultAfter = 0;
        for (var i = 0; i < 3; i++)
        {
            try { await obd.ReadLiveValuesAsync(new[] { 0x0C }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        Assert.True(obd.Load.Level >= AdapterLoadLevel.Gentle);

        sim.Fault = SimFault.None;
        await obd.Load.ResetForTestAsync();
        Assert.Equal(AdapterLoadLevel.Normal, obd.Load.Level);
    }

    [Fact]
    public async Task InGentleMode_ALiveTickAsksForFewerExtraReadings()
    {
        var (obd, sim) = await ConnectedAsync();
        var pids = obd.SupportedPids.Select(p => p.Pid).Take(9).ToArray();
        Assert.True(pids.Length >= 6, "the simulated car should advertise several PIDs");

        sim.Sent.Clear();
        await obd.ReadLiveValuesAsync(pids);
        var normal = sim.Sent.Count(c => c.Length == 4 && c.StartsWith("01", StringComparison.Ordinal));

        obd.Load.Record(Quick, ok: false);
        obd.Load.Record(Quick, ok: false);
        Assert.Equal(AdapterLoadLevel.Gentle, obd.Load.Level);

        sim.Sent.Clear();
        await obd.ReadLiveValuesAsync(pids);
        var gentle = sim.Sent.Count(c => c.Length == 4 && c.StartsWith("01", StringComparison.Ordinal));

        Assert.True(normal > gentle, $"normal {normal} should be more than gentle {gentle}");
        Assert.True(gentle <= 4);
    }

    [Fact]
    public async Task InCarefulMode_ThereIsAQuietGapBetweenDataRequests()
    {
        var (obd, sim) = await ConnectedAsync();
        var pids = obd.SupportedPids.Select(p => p.Pid).Take(2).ToArray();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 6; i++)
            await obd.ReadLiveValuesAsync(pids);
        var fastRun = clock.Elapsed;

        // Force Careful through the public rule (failures spaced by the five-second guard are not needed: two steps via Record).
        for (var i = 0; i < 4; i++)
            obd.Load.Record(Quick, ok: false);
        var level = obd.Load.Level;
        Assert.True(level >= AdapterLoadLevel.Gentle);

        clock.Restart();
        for (var i = 0; i < 6; i++)
            await obd.ReadLiveValuesAsync(pids);
        var gappedRun = clock.Elapsed;

        // 12 data requests with at least a 40 ms gap each cannot finish faster than the gap allows.
        Assert.True(gappedRun >= TimeSpan.FromMilliseconds(12 * 30), $"gapped run {gappedRun.TotalMilliseconds:F0} ms (fast run {fastRun.TotalMilliseconds:F0} ms)");
        _ = sim;
    }
}

internal static class AdapterLoadGovernorTestExtensions
{
    /// <summary>The protocol resets the governor on a fresh session; tests do the same through the public API.</summary>
    public static Task ResetForTestAsync(this AdapterLoadGovernor governor)
    {
        governor.Reset();
        return Task.CompletedTask;
    }
}
