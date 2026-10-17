using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// Pure freshness classifier for <see cref="LiveDataEngine"/>'s polling loop. Takes no dependency
/// on anything platform-specific so it can be driven by a real clock (a <see cref="System.Threading.Timer"/>
/// outside the loop's own await chain) or by fixed timestamps in a test — the two call sites that
/// actually matter: a wedged single await can never update "now" itself, so whatever decides
/// "frozen" must live outside it.
/// </summary>
public static class LiveDataHealthMonitor
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan FrozenAfter = TimeSpan.FromSeconds(20);

    /// <param name="now">The current time.</param>
    /// <param name="tickStartedUtc">When the in-flight (or most recently started) tick began.</param>
    /// <param name="lastTickCompletedUtc">When a tick last ran to completion, successful or not.</param>
    public static LiveDataHealth Classify(DateTime now, DateTime tickStartedUtc, DateTime lastTickCompletedUtc)
    {
        var sinceTickStarted = now - tickStartedUtc;
        var sinceLastCompleted = now - lastTickCompletedUtc;
        var longest = sinceTickStarted > sinceLastCompleted ? sinceTickStarted : sinceLastCompleted;

        return longest > FrozenAfter
            ? LiveDataHealth.Frozen
            : longest > StaleAfter
                ? LiveDataHealth.Stale
                : LiveDataHealth.Healthy;
    }
}
