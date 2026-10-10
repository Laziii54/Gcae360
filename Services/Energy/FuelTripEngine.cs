using System.Text.Json;

namespace CarScanner.Services.Energy;

public enum InstantFuelState
{
    /// <summary>No consumption can be given (see <see cref="InstantFuel.Issue"/>).</summary>
    Unavailable,

    /// <summary>Engine stopped (rpm ≈ 0): nothing is being burned.</summary>
    EngineOff,

    /// <summary>Engine running, car (almost) standing: only a flow (L/h) is meaningful, L/100 km would tend to infinity.</summary>
    Idle,

    /// <summary>Fuel flow known but no usable speed: only a flow is shown.</summary>
    FlowOnly,

    Driving,

    /// <summary>Coasting with the fuel cut off (direct 0 from the ECU, or inferred from throttle/load for an air-flow estimate).</summary>
    FuelCut
}

public sealed record InstantFuel(
    InstantFuelState State,
    double? LitersPerHour,
    double? LitersPer100Km,
    double? SpeedKph,
    FuelDataQuality Quality,
    FuelRateIssue Issue,
    string Source,
    string Assumption)
{
    public static readonly InstantFuel None = new(InstantFuelState.Unavailable, null, null, null, FuelDataQuality.None, FuelRateIssue.NoFuelData, string.Empty, string.Empty);

    public bool IsEstimate => Quality == FuelDataQuality.Estimated;
}

public enum TripFuelQuality
{
    None,
    Direct,
    Estimated,
    Mixed
}

public sealed record FuelTripSnapshot(
    double DistanceKm,
    double FuelLiters,
    double DirectLiters,
    double EstimatedLiters,
    double MovingSeconds,
    double IdleSeconds,
    double EngineOffSeconds,
    double GapSeconds,
    double MissingFuelSeconds,
    double FuelCutSeconds,
    int InvalidTimestamps,
    double MaximumSpeedKph,
    double? AverageSpeedKph,
    double? AverageLPer100Km,
    double FuelCoverage,
    TripFuelQuality Quality,
    double CityKm,
    double CityLiters,
    double HighwayKm,
    double HighwayLiters,
    double EngineOnKm,
    double EngineOffKm,
    double UnknownEngineKm,
    double? EngineOnLPer100Km,
    bool IsInterrupted)
{
    public double TotalSeconds => MovingSeconds + IdleSeconds + EngineOffSeconds;

    /// <summary>Fuel for the trip is complete enough to give an average (distance ≥ 100 m and ≥ 70 % of the running time covered).</summary>
    public bool HasReliableAverage => AverageLPer100Km is not null && FuelCoverage >= FuelTripEngine.MinimumCoverage;
}

/// <summary>What is saved every few seconds so an interrupted trip can be recovered (never a fabricated trip).</summary>
public sealed record FuelTripCheckpoint(
    int Version,
    DateTime StartedAt,
    DateTime SavedAt,
    double DistanceKm,
    double DirectLiters,
    double EstimatedLiters,
    double MovingSeconds,
    double IdleSeconds,
    double EngineOffSeconds,
    double GapSeconds,
    double MissingFuelSeconds,
    double FuelCutSeconds,
    double RunningSeconds,
    double FuelCoveredSeconds,
    double MaximumSpeedKph,
    double CityKm,
    double CityLiters,
    double HighwayKm,
    double HighwayLiters,
    double EngineOnKm,
    double EngineOffKm,
    double UnknownEngineKm,
    string FuelKind,
    string Mode,
    double? EnergyNetKWh,
    double? EnergyRegeneratedKWh,
    bool EnergyIsEstimate,
    string? Vin = null)
{
    public const int CurrentVersion = 1;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);

    /// <summary>true when every number is finite and non-negative and the timestamps are consistent.</summary>
    public bool IsValid(DateTime now)
    {
        if (Version != CurrentVersion)
            return false;
        if (StartedAt == default || SavedAt < StartedAt || SavedAt > now.AddMinutes(1) || now - SavedAt > MaximumAge)
            return false;

        double[] numbers =
        {
            DistanceKm, DirectLiters, EstimatedLiters, MovingSeconds, IdleSeconds, EngineOffSeconds, GapSeconds, MissingFuelSeconds,
            FuelCutSeconds, RunningSeconds, FuelCoveredSeconds, MaximumSpeedKph, CityKm, CityLiters, HighwayKm, HighwayLiters,
            EngineOnKm, EngineOffKm, UnknownEngineKm
        };
        if (numbers.Any(n => !double.IsFinite(n) || n < 0))
            return false;

        // A trip cannot have covered more distance than the maximum speed allows in the time it ran.
        var seconds = MovingSeconds + IdleSeconds + EngineOffSeconds + GapSeconds;
        if (DistanceKm > 0 && seconds > 0 && DistanceKm > FuelLimits.MaximumSpeedKph * seconds / 3600d + 0.5)
            return false;

        return EnergyNetKWh is null || double.IsFinite(EnergyNetKWh.Value);
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static FuelTripCheckpoint? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<FuelTripCheckpoint>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Turns live readings into instantaneous consumption and trip statistics. Pure and clock-free: the caller passes the time
/// of every tick, so the same code runs on the phone and in tests.
///
///  • Distance: speed integrated with the trapezoid rule between ticks (speed is km/h, so km = km/h · s / 3600).
///  • Fuel: the fuel flow (L/h) integrated the same way; direct (ECU) and estimated (air flow) litres are kept apart.
///  • Gaps longer than <see cref="FuelLimits.MaximumGap"/> and non-increasing timestamps are never integrated.
///  • A value is only combined with others while fresh; a missing reading is "unknown", never replaced by an old one.
///  • Instantaneous L/100 km needs a speed ≥ 5 km/h; below that only the flow (L/h) is shown.
/// </summary>
public sealed class FuelTripEngine
{
    public const double MinimumCoverage = 0.7;
    public const double SmoothingSeconds = 3;
    public const double CityLimitKph = 60;

    private enum EngineState { Unknown, Off, On }

    private readonly record struct Sample(double? SpeedKph, double? LitersPerHour, FuelDataQuality Quality, EngineState Engine);

    private DateTime? _last;
    private Sample? _prev;
    private DateTime? _startedAt;

    private double _distanceKm;
    private double _directLiters;
    private double _estimatedLiters;
    private double _movingSeconds;
    private double _idleSeconds;
    private double _engineOffSeconds;
    private double _gapSeconds;
    private double _missingFuelSeconds;
    private double _fuelCutSeconds;
    private double _runningSeconds;
    private double _fuelCoveredSeconds;
    private double _maxSpeed;
    private double _cityKm, _cityLiters, _highwayKm, _highwayLiters;
    private double _engineOnKm, _engineOffKm, _unknownEngineKm;
    private int _invalidTimestamps;
    private bool _interrupted;

    private DateTime? _smoothedAt;
    private double _smoothedRate;
    private double _smoothedSpeed;

    public FuelSignalCache Signals { get; } = new();

    public FuelKind Kind { get; set; } = FuelKind.Unknown;

    public InstantFuel Instant { get; private set; } = InstantFuel.None;

    public DateTime? StartedAt => _startedAt;

    public void Reset()
    {
        _last = null;
        _prev = null;
        _startedAt = null;
        _distanceKm = _directLiters = _estimatedLiters = 0;
        _movingSeconds = _idleSeconds = _engineOffSeconds = _gapSeconds = _missingFuelSeconds = _fuelCutSeconds = 0;
        _runningSeconds = _fuelCoveredSeconds = _maxSpeed = 0;
        _cityKm = _cityLiters = _highwayKm = _highwayLiters = 0;
        _engineOnKm = _engineOffKm = _unknownEngineKm = 0;
        _invalidTimestamps = 0;
        _interrupted = false;
        _smoothedAt = null;
        Instant = InstantFuel.None;
        Signals.Clear();
    }

    /// <summary>Processes the readings received so far at time <paramref name="now"/>.</summary>
    public void Tick(DateTime now)
    {
        _startedAt ??= now;

        var speed = Valid(Signals.Get(FuelSignal.SpeedKph, now, FuelLimits.DriveSignalMaxAge), FuelLimits.MaximumSpeedKph);
        var rpm = Valid(Signals.Get(FuelSignal.Rpm, now, FuelLimits.DriveSignalMaxAge), FuelLimits.MaximumRpm);
        var rate = FuelRateResolver.Resolve(Signals, now, Kind);
        var engine = rpm is null ? EngineState.Unknown : rpm >= FuelLimits.EngineOnRpm ? EngineState.On : EngineState.Off;
        var sample = new Sample(speed, rate.LitersPerHour, rate.Quality, engine);

        UpdateInstant(now, speed, rate);

        if (_last is null)
        {
            _last = now;
            _prev = sample;
            if (speed is { } first)
                _maxSpeed = Math.Max(_maxSpeed, first);
            return;
        }

        var seconds = (now - _last.Value).TotalSeconds;
        if (seconds <= 0)
        {
            // Same or earlier timestamp: nothing to integrate, and the clock is not moved backwards.
            _invalidTimestamps++;
            return;
        }

        _last = now;
        var previous = _prev;
        _prev = sample;

        if (seconds > FuelLimits.MaximumGap.TotalSeconds || previous is null)
        {
            _gapSeconds += seconds;
            return;
        }

        Integrate(previous.Value, sample, seconds, rate);
    }

    private static double? Valid(double? value, double maximum) =>
        value is { } v && double.IsFinite(v) && v >= 0 && v <= maximum ? v : null;

    private void Integrate(Sample from, Sample to, double seconds, FuelRate rate)
    {
        if (to.SpeedKph is { } now)
            _maxSpeed = Math.Max(_maxSpeed, now);

        double? speed = from.SpeedKph is { } a && to.SpeedKph is { } b ? (a + b) / 2d : null;
        var km = speed is { } s ? s * seconds / 3600d : 0;
        _distanceKm += km;

        if (speed is { } meanSpeed)
        {
            if (meanSpeed >= FuelLimits.MovingSpeedKph)
                _movingSeconds += seconds;
            else if (to.Engine == EngineState.Off)
                _engineOffSeconds += seconds;
            else
                _idleSeconds += seconds;
        }

        var running = from.Engine == EngineState.On || to.Engine == EngineState.On || (from.Engine == EngineState.Unknown && to.Engine == EngineState.Unknown);
        if (running)
            _runningSeconds += seconds;

        double? liters = null;
        if (from.LitersPerHour is { } r0 && to.LitersPerHour is { } r1)
        {
            liters = (r0 + r1) / 2d * seconds / 3600d;
            if (from.Quality == FuelDataQuality.Direct && to.Quality == FuelDataQuality.Direct)
                _directLiters += liters.Value;
            else
                _estimatedLiters += liters.Value;

            if (running)
                _fuelCoveredSeconds += seconds;
            if (rate.FuelCutAssumed)
                _fuelCutSeconds += seconds;
        }
        else if (running)
        {
            _missingFuelSeconds += seconds;
        }

        if (speed is { } mean && km > 0)
        {
            if (mean <= CityLimitKph)
            {
                _cityKm += km;
                _cityLiters += liters ?? 0;
            }
            else
            {
                _highwayKm += km;
                _highwayLiters += liters ?? 0;
            }

            switch (to.Engine)
            {
                case EngineState.On: _engineOnKm += km; break;
                case EngineState.Off: _engineOffKm += km; break;
                default: _unknownEngineKm += km; break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Instantaneous consumption
    // ------------------------------------------------------------------

    private void UpdateInstant(DateTime now, double? speed, FuelRate rate)
    {
        if (rate.LitersPerHour is not { } flow)
        {
            _smoothedAt = null;
            Instant = new InstantFuel(InstantFuelState.Unavailable, null, null, speed, FuelDataQuality.None, rate.Issue, string.Empty, string.Empty);
            return;
        }

        if (rate.Issue == FuelRateIssue.EngineOff)
        {
            _smoothedAt = null;
            Instant = new InstantFuel(InstantFuelState.EngineOff, 0, null, speed, rate.Quality, rate.Issue, rate.Source, rate.Assumption);
            return;
        }

        // Exponential smoothing of the flow and of the speed separately (time constant 3 s); the ratio is taken afterwards.
        // The totals of the trip never use the smoothed values.
        var dt = _smoothedAt is { } at ? (now - at).TotalSeconds : double.PositiveInfinity;
        if (dt <= 0)
        {
            // Same moment again: keep what is shown.
            return;
        }

        var alpha = double.IsInfinity(dt) || dt > FuelLimits.MaximumGap.TotalSeconds ? 1d : 1d - Math.Exp(-dt / SmoothingSeconds);
        _smoothedRate = alpha >= 1d ? flow : _smoothedRate + alpha * (flow - _smoothedRate);
        if (speed is { } currentSpeed)
            _smoothedSpeed = alpha >= 1d || _smoothedAt is null ? currentSpeed : _smoothedSpeed + alpha * (currentSpeed - _smoothedSpeed);
        _smoothedAt = now;

        if (speed is null)
        {
            Instant = new InstantFuel(InstantFuelState.FlowOnly, _smoothedRate, null, null, rate.Quality, rate.Issue, rate.Source, rate.Assumption);
            return;
        }

        if (speed < FuelLimits.IdleSpeedKph)
        {
            Instant = new InstantFuel(InstantFuelState.Idle, _smoothedRate, null, speed, rate.Quality, rate.Issue, rate.Source, rate.Assumption);
            return;
        }

        var per100 = _smoothedSpeed >= FuelLimits.IdleSpeedKph ? _smoothedRate / _smoothedSpeed * 100d : (double?)null;
        if (per100 is > FuelUnits.MaximumPlausibleLPer100Km)
        {
            Instant = new InstantFuel(InstantFuelState.Unavailable, _smoothedRate, null, speed, FuelDataQuality.None, FuelRateIssue.Implausible, rate.Source, rate.Assumption);
            return;
        }

        Instant = new InstantFuel(
            rate.FuelCutAssumed || (rate.Quality == FuelDataQuality.Direct && flow == 0) ? InstantFuelState.FuelCut : InstantFuelState.Driving,
            _smoothedRate, per100, speed, rate.Quality, rate.Issue, rate.Source, rate.Assumption);
    }

    // ------------------------------------------------------------------
    // Totals
    // ------------------------------------------------------------------

    public FuelTripSnapshot Snapshot()
    {
        var liters = _directLiters + _estimatedLiters;
        var coverage = _runningSeconds > 0 ? Math.Min(1d, _fuelCoveredSeconds / _runningSeconds) : 0d;
        var quality = liters <= 0
            ? TripFuelQuality.None
            : _estimatedLiters <= 0 ? TripFuelQuality.Direct
            : _directLiters <= 0 ? TripFuelQuality.Estimated
            : TripFuelQuality.Mixed;

        double? average = _distanceKm >= 0.1 && liters > 0 ? liters / _distanceKm * 100d : null;
        double? averageSpeed = _movingSeconds > 0 && _distanceKm > 0 ? _distanceKm / (_movingSeconds / 3600d) : null;
        double? engineOn = _engineOnKm >= 0.1 && liters > 0 ? liters / _engineOnKm * 100d : null;

        return new FuelTripSnapshot(
            _distanceKm, liters, _directLiters, _estimatedLiters, _movingSeconds, _idleSeconds, _engineOffSeconds, _gapSeconds,
            _missingFuelSeconds, _fuelCutSeconds, _invalidTimestamps, _maxSpeed, averageSpeed, average, coverage, quality,
            _cityKm, _cityLiters, _highwayKm, _highwayLiters, _engineOnKm, _engineOffKm, _unknownEngineKm, engineOn, _interrupted);
    }

    public FuelTripCheckpoint ExportCheckpoint(DateTime now, string mode, double? energyNetKWh, double? energyRegeneratedKWh, bool energyIsEstimate, string? vin = null) =>
        new(FuelTripCheckpoint.CurrentVersion, _startedAt ?? now, now < (_startedAt ?? now) ? (_startedAt ?? now) : now,
            _distanceKm, _directLiters, _estimatedLiters, _movingSeconds, _idleSeconds, _engineOffSeconds, _gapSeconds, _missingFuelSeconds,
            _fuelCutSeconds, _runningSeconds, _fuelCoveredSeconds, _maxSpeed, _cityKm, _cityLiters, _highwayKm, _highwayLiters,
            _engineOnKm, _engineOffKm, _unknownEngineKm, Kind.ToString(), mode, energyNetKWh, energyRegeneratedKWh, energyIsEstimate, vin);

    /// <summary>Loads a saved trip. The result is marked interrupted; the live readings start again from nothing.</summary>
    public bool TryRestore(FuelTripCheckpoint checkpoint, DateTime now)
    {
        if (!checkpoint.IsValid(now))
            return false;

        Reset();
        _startedAt = checkpoint.StartedAt;
        _distanceKm = checkpoint.DistanceKm;
        _directLiters = checkpoint.DirectLiters;
        _estimatedLiters = checkpoint.EstimatedLiters;
        _movingSeconds = checkpoint.MovingSeconds;
        _idleSeconds = checkpoint.IdleSeconds;
        _engineOffSeconds = checkpoint.EngineOffSeconds;
        _gapSeconds = checkpoint.GapSeconds;
        _missingFuelSeconds = checkpoint.MissingFuelSeconds;
        _fuelCutSeconds = checkpoint.FuelCutSeconds;
        _runningSeconds = checkpoint.RunningSeconds;
        _fuelCoveredSeconds = checkpoint.FuelCoveredSeconds;
        _maxSpeed = checkpoint.MaximumSpeedKph;
        _cityKm = checkpoint.CityKm;
        _cityLiters = checkpoint.CityLiters;
        _highwayKm = checkpoint.HighwayKm;
        _highwayLiters = checkpoint.HighwayLiters;
        _engineOnKm = checkpoint.EngineOnKm;
        _engineOffKm = checkpoint.EngineOffKm;
        _unknownEngineKm = checkpoint.UnknownEngineKm;
        if (Enum.TryParse<FuelKind>(checkpoint.FuelKind, out var kind))
            Kind = kind;
        _interrupted = true;
        return true;
    }
}
