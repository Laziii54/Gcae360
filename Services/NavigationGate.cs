namespace CarScanner.Services;

/// <summary>
/// Decides whether a navigation request may start. Pure (no MAUI): time comes from a delegate so it can be tested.
///
/// Why: the iOS crash report shows <c>ShellSection.SendPopping</c> failing with a "nav stack consistency" error that is
/// thrown on the run loop and closes the app. It happens when two pops overlap — a double tap on a Back button, a Back
/// button plus the swipe gesture, a handler that navigates while a navigation is still animating — or when ".." is asked
/// at the root. Rapid navigation between pages is also the reported route to the blank white screen. So requests are
/// serialised: one at a time, repeats of the same target inside a short window are ignored, and ".." is refused when
/// there is nothing to go back to.
/// </summary>
public sealed class NavigationGate
{
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();
    private bool _inFlight;
    private DateTime _startedAt;
    private string? _lastRoute;
    private DateTime _lastFinishedAt = DateTime.MinValue;

    /// <summary>The same target requested again inside this window after finishing is a double tap, not a new request.</summary>
    public TimeSpan DuplicateWindow { get; init; } = TimeSpan.FromMilliseconds(700);

    /// <summary>A navigation that never reports back must not block the app for ever.</summary>
    public TimeSpan MaximumInFlight { get; init; } = TimeSpan.FromSeconds(6);

    public NavigationGate(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    public bool InFlight
    {
        get { lock (_lock) return _inFlight; }
    }

    /// <summary>True when the request may start. Call <see cref="End"/> when it finished, failed or was cancelled.</summary>
    public bool TryBegin(string route)
    {
        lock (_lock)
        {
            var now = _now();
            if (_inFlight && now - _startedAt < MaximumInFlight)
                return false;

            if (string.Equals(route, _lastRoute, StringComparison.Ordinal) && now - _lastFinishedAt < DuplicateWindow)
                return false;

            _inFlight = true;
            _startedAt = now;
            _lastRoute = route;
            return true;
        }
    }

    public void End()
    {
        lock (_lock)
        {
            _inFlight = false;
            _lastFinishedAt = _now();
        }
    }

    /// <summary>Going back needs a page below the current one (the stack holds the root as its first entry).</summary>
    public static bool CanGoBack(int navigationStackCount, int modalStackCount) =>
        modalStackCount > 0 || navigationStackCount > 1;

    public static bool IsBack(string route) => string.Equals(route?.Trim(), "..", StringComparison.Ordinal);
}
