namespace CarScanner.Services.Energy;

public enum FuelSignal
{
    SpeedKph,
    Rpm,
    ThrottlePercent,
    LoadPercent,

    /// <summary>SAE J1979 PID 01 5E, litres per hour.</summary>
    DirectFuelRateLph,

    /// <summary>SAE J1979 PID 01 9D (engine fuel rate), grams per second of fuel. Needs a known liquid fuel to become litres.</summary>
    EngineFuelRateGramsPerSecond,

    /// <summary>SAE J1979 PID 01 10, grams per second.</summary>
    MafGramsPerSecond,

    /// <summary>Equivalence ratio λ from a wide-band sensor (PID 01 24–2B / 34–3B). Needed to estimate a diesel from air flow.</summary>
    EquivalenceRatio,

    FuelLevelPercent
}

/// <summary>How old a reading may be before it is no longer combined with others.</summary>
public static class FuelLimits
{
    public static readonly TimeSpan DriveSignalMaxAge = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FuelSignalMaxAge = TimeSpan.FromSeconds(6);

    /// <summary>Longest gap between two ticks that is still integrated; a longer gap is a data hole.</summary>
    public static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(8);

    public const double EngineOffRpm = 50;
    public const double EngineOnRpm = 300;
    public const double IdleSpeedKph = 5;
    public const double MovingSpeedKph = 1;
    public const double MaximumSpeedKph = 400;
    public const double MaximumRpm = 12000;

    /// <summary>A passenger car or van never burns this much; the PID's own limit (3 212 L/h) is far beyond it.</summary>
    public const double MaximumFuelRateLph = 400;
    public const double MaximumMafGramsPerSecond = 700;
}

/// <summary>Latest readings with the moment they arrived. Nothing is ever assumed for a signal that has not arrived.</summary>
public sealed class FuelSignalCache
{
    private readonly Dictionary<FuelSignal, (double Value, DateTime At)> _values = new();

    public void Set(FuelSignal signal, double value, DateTime at)
    {
        if (!double.IsFinite(value))
            return;

        // A reading older than the one we already have (clock step, out-of-order event) is ignored.
        if (_values.TryGetValue(signal, out var existing) && at < existing.At)
            return;

        _values[signal] = (value, at);
    }

    /// <summary>The value if it arrived within <paramref name="maxAge"/> before <paramref name="now"/>; otherwise null.</summary>
    public double? Get(FuelSignal signal, DateTime now, TimeSpan maxAge)
    {
        if (!_values.TryGetValue(signal, out var entry))
            return null;

        var age = now - entry.At;
        return age >= TimeSpan.Zero && age <= maxAge ? entry.Value : null;
    }

    /// <summary>true when the signal has ever arrived (used to tell "not supported" from "stale").</summary>
    public bool HasEverReceived(FuelSignal signal) => _values.ContainsKey(signal);

    public void Clear() => _values.Clear();
}

public enum FuelDataQuality
{
    /// <summary>No fuel flow could be determined.</summary>
    None,

    /// <summary>The car reported the fuel flow itself (PID 01 5E).</summary>
    Direct,

    /// <summary>Calculated from air flow and assumptions — an estimate, never a measurement.</summary>
    Estimated
}

public enum FuelRateIssue
{
    None,
    NoFuelData,
    Stale,
    Implausible,
    FuelTypeUnknown,
    FuelTypeUnsupported,
    DieselNeedsLambda,
    ElectricVehicle,
    EngineOff
}

/// <param name="LitersPerHour">Null when no fuel flow can be given. 0 is a real zero (engine off, or fuel cut).</param>
public sealed record FuelRate(
    double? LitersPerHour,
    FuelDataQuality Quality,
    FuelRateIssue Issue,
    bool FuelCutAssumed,
    string Source,
    string Assumption)
{
    public static FuelRate Unavailable(FuelRateIssue issue) => new(null, FuelDataQuality.None, issue, false, string.Empty, string.Empty);
}

/// <summary>
/// Chooses the fuel flow for one moment. Order: the car's own fuel-rate value (PID 5E) when fresh and plausible,
/// otherwise an air-flow (MAF) estimate where the fuel allows one, otherwise nothing. A direct 0 is a real 0 — it is never
/// replaced by an air-flow estimate. Old readings are not used.
/// </summary>
public static class FuelRateResolver
{
    private const double MinimumLambda = 0.5;
    private const double SaturatedLambda = 1.95; // the PID tops out at 2.0

    public static FuelRate Resolve(FuelSignalCache signals, DateTime now, FuelKind kind)
    {
        if (kind == FuelKind.Electric)
            return FuelRate.Unavailable(FuelRateIssue.ElectricVehicle);

        var rpm = signals.Get(FuelSignal.Rpm, now, FuelLimits.DriveSignalMaxAge);
        var engineOff = rpm is < FuelLimits.EngineOffRpm and >= 0;
        var direct = signals.Get(FuelSignal.DirectFuelRateLph, now, FuelLimits.FuelSignalMaxAge);

        // Engine stopped: no fuel is injected. This is a fact about the engine, not an invented value.
        if (engineOff)
        {
            return new FuelRate(0, direct is not null ? FuelDataQuality.Direct : FuelDataQuality.Estimated,
                FuelRateIssue.EngineOff, false, "engine stopped (rpm ≈ 0)", string.Empty);
        }

        // 1) The car's own fuel flow.
        var sawImplausibleDirect = false;
        if (direct is { } directValue)
        {
            if (directValue is >= 0 and <= FuelLimits.MaximumFuelRateLph)
                return new FuelRate(directValue, FuelDataQuality.Direct, FuelRateIssue.None, false, "PID 01 5E (fuel rate reported by the ECU)", string.Empty);
            sawImplausibleDirect = true;
        }

        // 1b) The car's own fuel MASS flow (PID 9D, g/s). It is a reading, converted to litres with the density of the selected fuel.
        //     Without a known liquid fuel there is no honest conversion, so the value is left unused.
        var massRate = signals.Get(FuelSignal.EngineFuelRateGramsPerSecond, now, FuelLimits.FuelSignalMaxAge);
        if (massRate is { } gramsPerSecond && FuelKinds.TryGetMafConstants(kind, out var massConstants))
        {
            var litres = gramsPerSecond / (massConstants.DensityKgPerLiter * 1000d) * 3600d;
            if (double.IsFinite(litres) && litres is >= 0 and <= FuelLimits.MaximumFuelRateLph)
                return new FuelRate(litres, FuelDataQuality.Direct, FuelRateIssue.None, false,
                    "PID 01 9D (fuel mass rate reported by the ECU) ÷ density",
                    $"density {massConstants.DensityKgPerLiter:0.###} kg/L assumed for the selected fuel");
            sawImplausibleDirect = true;
        }

        // 2) An estimate from air flow.
        var maf = signals.Get(FuelSignal.MafGramsPerSecond, now, FuelLimits.FuelSignalMaxAge);
        if (maf is null)
        {
            if (sawImplausibleDirect)
                return FuelRate.Unavailable(FuelRateIssue.Implausible);

            var everSeen = signals.HasEverReceived(FuelSignal.DirectFuelRateLph) || signals.HasEverReceived(FuelSignal.EngineFuelRateGramsPerSecond) || signals.HasEverReceived(FuelSignal.MafGramsPerSecond);
            return FuelRate.Unavailable(everSeen ? FuelRateIssue.Stale : FuelRateIssue.NoFuelData);
        }

        if (maf is < 0 or > FuelLimits.MaximumMafGramsPerSecond)
            return FuelRate.Unavailable(FuelRateIssue.Implausible);

        switch (kind)
        {
            case FuelKind.Unknown:
            case FuelKind.Mixed:
                return FuelRate.Unavailable(FuelRateIssue.FuelTypeUnknown);
            case FuelKind.Cng:
                return FuelRate.Unavailable(FuelRateIssue.FuelTypeUnsupported);
        }

        if (!FuelKinds.TryGetMafConstants(kind, out var constants))
            return FuelRate.Unavailable(FuelRateIssue.FuelTypeUnsupported);

        var airToFuel = constants.StoichiometricAfr;
        string assumption;
        if (kind == FuelKind.Diesel)
        {
            // A diesel burns lean: fuel = air / (stoichiometric ratio × λ). Without a measured λ there is no honest estimate.
            var lambda = signals.Get(FuelSignal.EquivalenceRatio, now, FuelLimits.FuelSignalMaxAge);
            if (lambda is not { } measuredLambda || measuredLambda < MinimumLambda || measuredLambda >= SaturatedLambda)
                return FuelRate.Unavailable(FuelRateIssue.DieselNeedsLambda);

            airToFuel *= measuredLambda;
            assumption = $"λ {measuredLambda:0.00} from the wide-band sensor; {constants.Range}";
        }
        else
        {
            assumption = constants.Range;
        }

        // Possible fuel cut while coasting (closed throttle, light load, above idle speed and revs): the air still flows but no fuel
        // is injected. Without a direct fuel rate this is an inference from the pedal and revs, and is labelled as such.
        if (IsLikelyFuelCut(signals, now, kind))
        {
            return new FuelRate(0, FuelDataQuality.Estimated, FuelRateIssue.None, true,
                "PID 01 10 (air flow) — fuel cut assumed while coasting", assumption + "; fuel cut inferred from closed throttle and light load");
        }

        // grams of air per second → litres of fuel per hour: g/s ÷ AFR = g/s of fuel; ÷ (kg/L × 1000) = L/s; × 3600 = L/h.
        var litersPerHour = maf.Value / airToFuel / (constants.DensityKgPerLiter * 1000d) * 3600d;
        if (!double.IsFinite(litersPerHour) || litersPerHour > FuelLimits.MaximumFuelRateLph)
            return FuelRate.Unavailable(FuelRateIssue.Implausible);

        return new FuelRate(litersPerHour, FuelDataQuality.Estimated, FuelRateIssue.None, false,
            kind == FuelKind.Diesel ? "PID 01 10 (air flow) ÷ (AFR × λ)" : "PID 01 10 (air flow) ÷ stoichiometric AFR", assumption);
    }

    /// <summary>
    /// Closed throttle (≤ 2 %), engine load ≤ 25 %, speed ≥ 20 km/h and revs ≥ 1 300 — a petrol-type engine coasting in gear
    /// normally cuts the fuel. All four must be fresh; a missing reading means "cannot tell" and the estimate is left alone.
    /// </summary>
    public static bool IsLikelyFuelCut(FuelSignalCache signals, DateTime now, FuelKind kind)
    {
        if (kind is not (FuelKind.Gasoline or FuelKind.Ethanol or FuelKind.Methanol or FuelKind.Lpg))
            return false;

        var throttle = signals.Get(FuelSignal.ThrottlePercent, now, FuelLimits.DriveSignalMaxAge);
        var load = signals.Get(FuelSignal.LoadPercent, now, FuelLimits.DriveSignalMaxAge);
        var speed = signals.Get(FuelSignal.SpeedKph, now, FuelLimits.DriveSignalMaxAge);
        var rpm = signals.Get(FuelSignal.Rpm, now, FuelLimits.DriveSignalMaxAge);
        return throttle is <= 2 and >= 0 && load is <= 25 and >= 0 && speed is >= 20 && rpm is >= 1300;
    }
}
