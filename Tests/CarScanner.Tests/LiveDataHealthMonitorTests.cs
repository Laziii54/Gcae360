using CarScanner.Models;
using CarScanner.Services;

namespace CarScanner.Tests;

public sealed class LiveDataHealthMonitorTests
{
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ARecentlyCompletedTick_IsHealthy()
    {
        var health = LiveDataHealthMonitor.Classify(
            now: Epoch.AddSeconds(1),
            tickStartedUtc: Epoch,
            lastTickCompletedUtc: Epoch);

        Assert.Equal(LiveDataHealth.Healthy, health);
    }

    [Fact]
    public void NoCompletedTickForAFewSeconds_IsStale_NotYetFrozen()
    {
        var health = LiveDataHealthMonitor.Classify(
            now: Epoch + LiveDataHealthMonitor.StaleAfter + TimeSpan.FromSeconds(1),
            tickStartedUtc: Epoch,
            lastTickCompletedUtc: Epoch);

        Assert.Equal(LiveDataHealth.Stale, health);
    }

    [Fact]
    public void NoCompletedTickForFarLongerThanAnySingleCommandTimeout_IsFrozen()
    {
        var health = LiveDataHealthMonitor.Classify(
            now: Epoch + LiveDataHealthMonitor.FrozenAfter + TimeSpan.FromSeconds(1),
            tickStartedUtc: Epoch,
            lastTickCompletedUtc: Epoch);

        Assert.Equal(LiveDataHealth.Frozen, health);
    }

    [Fact]
    public void ATickThatIsStillRunningTooLong_IsFrozen_EvenIfAnEarlierTickOnceSucceeded()
    {
        // The loop completed a tick 1 second ago (so "time since last success" alone looks fine),
        // but the CURRENT tick started 30s ago and has not returned — exactly the symptom of one
        // single awaited call that will never come back.
        var health = LiveDataHealthMonitor.Classify(
            now: Epoch.AddSeconds(31),
            tickStartedUtc: Epoch.AddSeconds(1),
            lastTickCompletedUtc: Epoch);

        Assert.Equal(LiveDataHealth.Frozen, health);
    }

    [Fact]
    public void ExactlyAtTheStaleThreshold_IsStillHealthy()
    {
        var health = LiveDataHealthMonitor.Classify(
            now: Epoch + LiveDataHealthMonitor.StaleAfter,
            tickStartedUtc: Epoch,
            lastTickCompletedUtc: Epoch);

        Assert.Equal(LiveDataHealth.Healthy, health);
    }

    [Fact]
    public void ARecoveredTick_GoesBackToHealthy()
    {
        // Simulates the monitor being asked again right after a tick finally completed following
        // a long stretch of staleness: only the most recent timestamps matter, not the history.
        var health = LiveDataHealthMonitor.Classify(
            now: Epoch.AddMinutes(5).AddMilliseconds(50),
            tickStartedUtc: Epoch.AddMinutes(5),
            lastTickCompletedUtc: Epoch.AddMinutes(5));

        Assert.Equal(LiveDataHealth.Healthy, health);
    }
}
