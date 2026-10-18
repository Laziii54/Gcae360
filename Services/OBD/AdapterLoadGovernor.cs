namespace CarScanner.Services.OBD;

/// <summary>How hard the app may push the adapter right now.</summary>
public enum AdapterLoadLevel
{
    /// <summary>The adapter keeps up: full polling.</summary>
    Normal = 0,
    /// <summary>Signs of strain (timeouts, error words or slow answers): fewer extra readings, slower ticks, a short gap between commands.</summary>
    Gentle = 1,
    /// <summary>Repeated trouble: only the essentials, long gaps. Gives a weak adapter room to recover instead of being pushed until it stops answering.</summary>
    Careful = 2
}

/// <summary>
/// Backs the polling off when an adapter shows strain and brings it back up slowly when it copes again.
///
/// Why: cheap ELM327 clones were reported to stop answering a few minutes into continuous polling and to come back only
/// after a reconnect. Whatever the exact trigger is inside such a chip, the app pushing ten or more back-to-back
/// commands every second leaves it no room, and an adapter that already answers slowly or fails now and then is the
/// one most likely to wedge. This class only reacts to what the adapter itself shows (slow answers, timeouts, error
/// words); it does not claim to know the cause and cannot prove that it prevents a stop on a real adapter.
///
/// Pure logic with an injectable clock, no I/O: it is unit-tested; the effect on a real adapter needs a phone and a car.
/// </summary>
public sealed class AdapterLoadGovernor
{
    private const int WindowSize = 12;
    private static readonly TimeSpan SlowAnswer = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan FastAnswer = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MinimumGapBetweenChanges = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecoveryTime = TimeSpan.FromSeconds(30);
    private const int CleanAnswersToStepUp = 40;

    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();
    private readonly Queue<(bool Ok, bool Slow)> _window = new();
    private int _cleanStreak;
    private DateTime _lastChangeAt;
    private AdapterLoadLevel _level = AdapterLoadLevel.Normal;

    public AdapterLoadGovernor(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _lastChangeAt = DateTime.MinValue;
    }

    /// <summary>Raised on every change with (old, new, short reason). The reason goes into the connection log.</summary>
    public event Action<AdapterLoadLevel, AdapterLoadLevel, string>? LevelChanged;

    public AdapterLoadLevel Level
    {
        get { lock (_gate) return _level; }
    }

    /// <summary>Minimum quiet time between two commands of the live polling.</summary>
    public TimeSpan MinimumGap => Level switch
    {
        AdapterLoadLevel.Gentle => TimeSpan.FromMilliseconds(40),
        AdapterLoadLevel.Careful => TimeSpan.FromMilliseconds(120),
        _ => TimeSpan.Zero
    };

    /// <summary>How many optional (non-core) readings one live tick may add.</summary>
    public int MaxExtraReadings => Level switch
    {
        AdapterLoadLevel.Gentle => 4,
        AdapterLoadLevel.Careful => 2,
        _ => 9
    };

    /// <summary>Multiplier for the pause between live ticks.</summary>
    public double IntervalFactor => Level switch
    {
        AdapterLoadLevel.Gentle => 1.6,
        AdapterLoadLevel.Careful => 2.5,
        _ => 1.0
    };

    /// <summary>
    /// Reports one data request. <paramref name="ok"/> is false for a timeout or an adapter-level error word
    /// ("?", BUFFER FULL, CAN ERROR…); a "NO DATA" from the car is an answer and counts as ok.
    /// </summary>
    public void Record(TimeSpan latency, bool ok)
    {
        AdapterLoadLevel from;
        AdapterLoadLevel to;
        string reason;
        lock (_gate)
        {
            var slow = ok && latency >= SlowAnswer;
            _window.Enqueue((ok, slow));
            while (_window.Count > WindowSize)
                _window.Dequeue();

            _cleanStreak = ok && latency < FastAnswer ? _cleanStreak + 1 : 0;

            from = _level;
            var now = _clock();
            var failures = _window.Count(sample => !sample.Ok);
            var slows = _window.Count(sample => sample.Slow);

            var target = _level;
            reason = string.Empty;
            if (failures >= 4 || (failures >= 2 && slows >= 4))
            {
                target = AdapterLoadLevel.Careful;
                reason = $"{failures} failed and {slows} slow of the last {_window.Count} requests";
            }
            else if (failures >= 2 || slows >= 6)
            {
                target = AdapterLoadLevel.Gentle;
                reason = $"{failures} failed and {slows} slow of the last {_window.Count} requests";
            }

            if (target > _level && now - _lastChangeAt >= MinimumGapBetweenChanges)
            {
                _level = target;
                _lastChangeAt = now;
                _cleanStreak = 0;
            }
            else if (_level > AdapterLoadLevel.Normal &&
                     _cleanStreak >= CleanAnswersToStepUp &&
                     now - _lastChangeAt >= RecoveryTime)
            {
                // The adapter has answered a long run quickly: give back one level, not all at once.
                _level = _level - 1;
                _lastChangeAt = now;
                _cleanStreak = 0;
                _window.Clear();
                reason = $"{CleanAnswersToStepUp} fast answers in a row";
            }

            to = _level;
        }

        if (to != from)
            LevelChanged?.Invoke(from, to, reason);
    }

    /// <summary>A new connection starts with a clean slate.</summary>
    public void Reset()
    {
        AdapterLoadLevel from;
        lock (_gate)
        {
            from = _level;
            _level = AdapterLoadLevel.Normal;
            _window.Clear();
            _cleanStreak = 0;
            _lastChangeAt = DateTime.MinValue;
        }

        if (from != AdapterLoadLevel.Normal)
            LevelChanged?.Invoke(from, AdapterLoadLevel.Normal, "new connection");
    }
}
