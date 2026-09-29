namespace CarScanner.Services.PerformanceVideo;

/// <summary>
/// Live 0–20 / 0–50 / 0–80 / 0–100 timing from OBD speed samples.
/// Targets are in the unit shown to the user (km/h or mph).
/// Timing starts at the last standstill sample before the car pulls away;
/// each split is interpolated between the two samples around the target.
/// </summary>
public sealed class PerformanceSplitTracker
{
    public static readonly int[] Targets = { 20, 50, 80, 100 };

    private readonly double?[] _splits = new double?[Targets.Length];
    private bool _armed;
    private DateTime? _lastStandstillAt;
    private DateTime? _runStartedAt;
    private DateTime? _previousAt;
    private double _previousSpeed;

    public IReadOnlyList<double?> Splits => _splits;
    public bool IsArmed => _armed;
    public bool IsRunning => _runStartedAt is not null && _splits.Any(split => split is null);

    /// <summary>Adds one sample. Returns true when the split results changed.</summary>
    public bool AddSample(DateTime at, double speedInDisplayUnit)
    {
        var changed = false;
        var speed = Math.Max(0, speedInDisplayUnit);

        if (speed < 0.5)
        {
            _armed = true;
            _lastStandstillAt = at;
        }
        else
        {
            if (_armed && _lastStandstillAt is not null)
            {
                // Pulling away: a new run starts, old results are cleared.
                _armed = false;
                _runStartedAt = _lastStandstillAt;
                _previousAt = _lastStandstillAt;
                _previousSpeed = 0;
                for (var i = 0; i < _splits.Length; i++)
                    _splits[i] = null;
                changed = true;
            }

            if (_runStartedAt is { } start && _previousAt is { } previousAt && at > previousAt)
            {
                for (var i = 0; i < Targets.Length; i++)
                {
                    var target = Targets[i];
                    if (_splits[i] is not null || speed < target || _previousSpeed >= target)
                        continue;

                    var fraction = (target - _previousSpeed) / Math.Max(0.001, speed - _previousSpeed);
                    var crossedAt = previousAt + TimeSpan.FromTicks((long)((at - previousAt).Ticks * Math.Clamp(fraction, 0, 1)));
                    _splits[i] = Math.Max(0, (crossedAt - start).TotalSeconds);
                    changed = true;
                }
            }
        }

        _previousAt = at;
        _previousSpeed = speed;
        return changed;
    }

    public double?[] Snapshot() => _splits.ToArray();
}

/// <summary>One overlay state during a recording (time from recording start).</summary>
public sealed record PerformanceOverlayFrame(double TimeMs, int? Speed, double?[] Splits);

/// <summary>Everything needed to draw the overlay into a saved video.</summary>
public sealed class PerformanceVideoTelemetry
{
    private readonly List<PerformanceOverlayFrame> _frames = new();
    private readonly object _gate = new();

    public string SpeedUnit { get; init; } = "km/h";
    public string SecondsUnit { get; init; } = "s";

    public void Add(PerformanceOverlayFrame frame)
    {
        lock (_gate)
            _frames.Add(frame);
    }

    /// <summary>The overlay state visible at <paramref name="timeMs"/> (last state at or before it).</summary>
    public PerformanceOverlayFrame StateAt(double timeMs)
    {
        lock (_gate)
        {
            PerformanceOverlayFrame? found = null;
            foreach (var frame in _frames)
            {
                if (frame.TimeMs > timeMs)
                    break;
                found = frame;
            }

            return found ?? _frames.FirstOrDefault() ?? new PerformanceOverlayFrame(0, null, new double?[PerformanceSplitTracker.Targets.Length]);
        }
    }

    /// <summary>A stable copy used by native video exporters.</summary>
    public PerformanceOverlayFrame[] Snapshot()
    {
        lock (_gate)
            return _frames.OrderBy(frame => frame.TimeMs).ToArray();
    }
}

/// <summary>Theme colors (ARGB hex) used for the overlay, so preview and saved video match.</summary>
public sealed record PerformanceOverlayStyle(
    string PanelColor,
    string TextColor,
    string SubTextColor,
    string AccentColor,
    string BorderColor);
