namespace CarScanner.Services.Energy;

public enum EnergyQuality
{
    /// <summary>No electrical energy figure can be given.</summary>
    Unavailable,

    /// <summary>From the battery state of charge and a capacity the user entered: coarse, assumption-based.</summary>
    Estimated,

    /// <summary>Voltage × current integrated over time (the car's own battery readings).</summary>
    Measured
}

public enum SocEstimateIssue
{
    None,
    SocMissing,
    CapacityMissing,
    CapacityImplausible,
    SocImplausible,
    ChargeDidNotFall
}

/// <summary>
/// kWh from the drop of the state of charge: (start − now) / 100 × usable capacity. It ignores temperature, calibration,
/// the reserve the car never uses, and any energy regenerated or charged in between, so it is only ever shown as "≈".
/// </summary>
public static class SocEnergyEstimator
{
    public const double MinimumCapacityKWh = 5;
    public const double MaximumCapacityKWh = 250;

    /// <summary>A drop smaller than this is within the resolution of the percentage the car reports.</summary>
    public const double MinimumSocDrop = 1.5;

    public static double? KWh(double? socStart, double? socNow, double? capacityKWh, out SocEstimateIssue issue)
    {
        issue = SocEstimateIssue.None;
        if (socStart is not { } start || socNow is not { } now)
        {
            issue = SocEstimateIssue.SocMissing;
            return null;
        }

        if (capacityKWh is null)
        {
            issue = SocEstimateIssue.CapacityMissing;
            return null;
        }

        if (!double.IsFinite(capacityKWh.Value) || capacityKWh < MinimumCapacityKWh || capacityKWh > MaximumCapacityKWh)
        {
            issue = SocEstimateIssue.CapacityImplausible;
            return null;
        }

        if (!double.IsFinite(start) || !double.IsFinite(now) || start is < 0 or > 100 || now is < 0 or > 100)
        {
            issue = SocEstimateIssue.SocImplausible;
            return null;
        }

        if (start - now < MinimumSocDrop)
        {
            issue = SocEstimateIssue.ChargeDidNotFall;
            return null;
        }

        return (start - now) / 100d * capacityKWh.Value;
    }
}

public sealed record ElectricTrip(
    EnergyQuality Quality,
    double? NetKWh,
    double? ConsumedKWh,
    double? RegeneratedKWh,
    double? KWhPer100Km,
    double DistanceBasisKm,
    ElectricBasis Basis,
    double? SocChange,
    bool ChargeBalanceNotable);

public enum ElectricBasis
{
    None,

    /// <summary>Battery-electric: the whole trip distance.</summary>
    WholeTrip,

    /// <summary>Plug-in / hybrid: only the distance driven with the engine stopped (derived from rpm and speed).</summary>
    EngineOffDistance
}

/// <summary>
/// Electric energy for the trip, with its quality. A battery-electric car divides by the whole distance; a plug-in hybrid divides by
/// the engine-off distance only (so the engine's kilometres do not dilute the figure) and says so. Nothing is invented: without
/// battery current and without state of charge + capacity the energy is "unavailable".
/// </summary>
public static class ElectricTripCalculator
{
    public const double MinimumBasisKm = 0.5;
    public const double NotableSocChange = 3;

    public static ElectricTrip Compute(
        EnergyMode mode,
        FuelTripSnapshot trip,
        bool hasMeasured,
        double measuredConsumedKWh,
        double measuredRegeneratedKWh,
        double? socStart,
        double? socNow,
        double? capacityKWh)
    {
        double? socChange = socStart is { } s && socNow is { } n && double.IsFinite(s) && double.IsFinite(n) ? n - s : null;
        var notable = mode == EnergyMode.Hybrid && socChange is { } change && Math.Abs(change) >= NotableSocChange;

        if (mode == EnergyMode.Fuel)
            return new ElectricTrip(EnergyQuality.Unavailable, null, null, null, null, 0, ElectricBasis.None, socChange, false);

        EnergyQuality quality;
        double? net, consumed = null, regenerated = null;
        if (hasMeasured && double.IsFinite(measuredConsumedKWh) && double.IsFinite(measuredRegeneratedKWh))
        {
            quality = EnergyQuality.Measured;
            consumed = measuredConsumedKWh;
            regenerated = measuredRegeneratedKWh;
            net = measuredConsumedKWh - measuredRegeneratedKWh;
        }
        else if (SocEnergyEstimator.KWh(socStart, socNow, capacityKWh, out _) is { } estimated)
        {
            quality = EnergyQuality.Estimated;
            net = estimated;
        }
        else
        {
            return new ElectricTrip(EnergyQuality.Unavailable, null, null, null, null, 0, ElectricBasis.None, socChange, notable);
        }

        var basisKm = mode == EnergyMode.Electric ? trip.DistanceKm : trip.EngineOffKm;
        var basis = mode == EnergyMode.Electric ? ElectricBasis.WholeTrip : ElectricBasis.EngineOffDistance;
        double? per100 = basisKm >= MinimumBasisKm && net is { } value ? FuelUnits.KWhPer100Km(value, basisKm) : null;

        return new ElectricTrip(quality, net, consumed, regenerated, per100, basisKm, basis, socChange, notable);
    }

    /// <summary>Instant electric economy: power ÷ speed. Needs a real speed; at a standstill it is not defined.</summary>
    public static double? InstantKWhPer100Km(double powerKw, double? speedKph) =>
        speedKph is { } speed && speed >= FuelLimits.IdleSpeedKph && double.IsFinite(powerKw) ? powerKw / speed * 100d : null;
}
