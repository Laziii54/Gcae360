namespace CarScanner.Services.Energy;

public enum EnergyMetric
{
    InstantFuel,
    AverageFuel,
    TripFuel,
    TripDistance,
    FuelLevel,
    EngineState,
    ElectricDriveShare,
    StateOfCharge,
    ElectricPower,
    ElectricEnergy,
    Regeneration
}

public enum EnergySupport
{
    /// <summary>The car reports the value itself.</summary>
    DirectlySupported,

    /// <summary>Calculated from readings the car does report (speed × time, voltage × current × time …).</summary>
    CalculatedFromAvailableData,

    /// <summary>An estimate that rests on stated assumptions.</summary>
    Estimated,

    /// <summary>Only part of what the metric needs is available.</summary>
    PartiallySupported,

    Unsupported,

    /// <summary>It could be calculated, but something it needs is not known yet.</summary>
    InsufficientData
}

public enum EnergyReason
{
    None,
    NotApplicableToThisPowertrain,
    ListNotRead,
    NoFuelRatePid,
    FuelRatePid,
    AirFlowEstimate,
    FuelTypeUnknown,
    DieselNeedsLambda,
    FuelTypeUnsupported,
    SpeedMissing,
    RpmMissing,
    NoBatterySource,
    BatteryCurrentSource,
    SocWithoutCapacity,
    SocWithCapacity,
    DerivedFromRpm,
    NoFuelLevelPid
}

public sealed record EnergyCapability(EnergyMetric Metric, EnergySupport Support, EnergyReason Reason);

/// <summary>What is known about the connected car when deciding which calculations the page can honestly offer.</summary>
public sealed record EnergyFacts(
    EnergyMode Mode,
    FuelKind Kind,
    IReadOnlySet<int>? AdvertisedPids,
    bool BatteryCurrentSource,
    bool StateOfChargeSource,
    bool BatteryCapacityKnown,
    bool LambdaPidAdvertised = false);

/// <summary>
/// Decides, per calculation, whether the data to do it exists for this car. It looks at the PIDs the ECU advertises and at
/// the battery source that was found; nothing is "supported" because of a brand or a model.
/// </summary>
public static class EnergyCompatibility
{
    public const int PidFuelRate = 0x5E;
    public const int PidMaf = 0x10;
    public const int PidSpeed = 0x0D;
    public const int PidRpm = 0x0C;
    public const int PidFuelLevel = 0x2F;
    public const int PidBatteryCharge = 0x5B;

    public static IReadOnlyList<EnergyCapability> Evaluate(EnergyFacts facts)
    {
        var list = new List<EnergyCapability>();
        var pids = facts.AdvertisedPids;
        bool? Has(int pid) => pids is null ? null : pids.Contains(pid);

        void Add(EnergyMetric metric, EnergySupport support, EnergyReason reason = EnergyReason.None) =>
            list.Add(new EnergyCapability(metric, support, reason));

        var usesFuel = facts.Mode != EnergyMode.Electric;
        var usesBattery = facts.Mode != EnergyMode.Fuel;

        // ---- distance: speed integrated over time
        var speed = Has(PidSpeed);
        if (speed == true)
            Add(EnergyMetric.TripDistance, EnergySupport.CalculatedFromAvailableData);
        else
            Add(EnergyMetric.TripDistance, EnergySupport.InsufficientData, speed is null ? EnergyReason.ListNotRead : EnergyReason.SpeedMissing);

        // ---- liquid fuel
        if (!usesFuel)
        {
            foreach (var metric in new[] { EnergyMetric.InstantFuel, EnergyMetric.AverageFuel, EnergyMetric.TripFuel, EnergyMetric.FuelLevel })
                Add(metric, EnergySupport.Unsupported, EnergyReason.NotApplicableToThisPowertrain);
        }
        else
        {
            var (fuelSupport, fuelReason) = FuelRateSupport(facts, pids);
            Add(EnergyMetric.InstantFuel, fuelSupport, fuelReason);

            // An average and a total need a fuel flow AND a distance.
            if (fuelSupport is EnergySupport.Unsupported or EnergySupport.InsufficientData)
            {
                Add(EnergyMetric.AverageFuel, fuelSupport, fuelReason);
                Add(EnergyMetric.TripFuel, fuelSupport, fuelReason);
            }
            else if (speed != true)
            {
                Add(EnergyMetric.AverageFuel, EnergySupport.InsufficientData, speed is null ? EnergyReason.ListNotRead : EnergyReason.SpeedMissing);
                Add(EnergyMetric.TripFuel, fuelSupport == EnergySupport.DirectlySupported ? EnergySupport.CalculatedFromAvailableData : fuelSupport, fuelReason);
            }
            else
            {
                var derived = fuelSupport == EnergySupport.DirectlySupported ? EnergySupport.CalculatedFromAvailableData : fuelSupport;
                Add(EnergyMetric.AverageFuel, derived, fuelReason);
                Add(EnergyMetric.TripFuel, derived, fuelReason);
            }

            var level = Has(PidFuelLevel);
            Add(EnergyMetric.FuelLevel,
                level == true ? EnergySupport.DirectlySupported : level is null ? EnergySupport.InsufficientData : EnergySupport.Unsupported,
                level == true ? EnergyReason.None : level is null ? EnergyReason.ListNotRead : EnergyReason.NoFuelLevelPid);
        }

        // ---- engine state and electric share (hybrids)
        var rpm = Has(PidRpm);
        if (facts.Mode == EnergyMode.Hybrid)
        {
            Add(EnergyMetric.EngineState, rpm == true ? EnergySupport.CalculatedFromAvailableData : EnergySupport.InsufficientData,
                rpm == true ? EnergyReason.DerivedFromRpm : rpm is null ? EnergyReason.ListNotRead : EnergyReason.RpmMissing);
            // "Driven on electric" is the distance with the engine stopped: derived, not reported by the car.
            Add(EnergyMetric.ElectricDriveShare,
                rpm == true && speed == true ? EnergySupport.Estimated : EnergySupport.InsufficientData,
                rpm == true && speed == true ? EnergyReason.DerivedFromRpm : EnergyReason.RpmMissing);
        }

        // ---- battery
        if (!usesBattery)
        {
            foreach (var metric in new[] { EnergyMetric.StateOfCharge, EnergyMetric.ElectricPower, EnergyMetric.ElectricEnergy, EnergyMetric.Regeneration })
                Add(metric, EnergySupport.Unsupported, EnergyReason.NotApplicableToThisPowertrain);
        }
        else
        {
            Add(EnergyMetric.StateOfCharge,
                facts.StateOfChargeSource || Has(PidBatteryCharge) == true ? EnergySupport.DirectlySupported : EnergySupport.Unsupported,
                facts.StateOfChargeSource || Has(PidBatteryCharge) == true ? EnergyReason.None : EnergyReason.NoBatterySource);

            if (facts.BatteryCurrentSource)
            {
                Add(EnergyMetric.ElectricPower, EnergySupport.CalculatedFromAvailableData, EnergyReason.BatteryCurrentSource);
                Add(EnergyMetric.ElectricEnergy, EnergySupport.CalculatedFromAvailableData, EnergyReason.BatteryCurrentSource);
                Add(EnergyMetric.Regeneration, EnergySupport.CalculatedFromAvailableData, EnergyReason.BatteryCurrentSource);
            }
            else
            {
                Add(EnergyMetric.ElectricPower, EnergySupport.Unsupported, EnergyReason.NoBatterySource);
                Add(EnergyMetric.Regeneration, EnergySupport.Unsupported, EnergyReason.NoBatterySource);
                var soc = facts.StateOfChargeSource || Has(PidBatteryCharge) == true;
                if (soc && facts.BatteryCapacityKnown)
                    Add(EnergyMetric.ElectricEnergy, EnergySupport.Estimated, EnergyReason.SocWithCapacity);
                else if (soc)
                    Add(EnergyMetric.ElectricEnergy, EnergySupport.InsufficientData, EnergyReason.SocWithoutCapacity);
                else
                    Add(EnergyMetric.ElectricEnergy, EnergySupport.Unsupported, EnergyReason.NoBatterySource);
            }
        }

        return list;
    }

    private static (EnergySupport, EnergyReason) FuelRateSupport(EnergyFacts facts, IReadOnlySet<int>? pids)
    {
        if (pids is null)
            return (EnergySupport.InsufficientData, EnergyReason.ListNotRead);

        if (pids.Contains(PidFuelRate))
            return (EnergySupport.DirectlySupported, EnergyReason.FuelRatePid);

        if (!pids.Contains(PidMaf))
            return (EnergySupport.Unsupported, EnergyReason.NoFuelRatePid);

        switch (facts.Kind)
        {
            case FuelKind.Gasoline:
            case FuelKind.Ethanol:
            case FuelKind.Methanol:
            case FuelKind.Lpg:
                return (EnergySupport.Estimated, EnergyReason.AirFlowEstimate);
            case FuelKind.Diesel:
                // A diesel's air-fuel ratio is not fixed: without a wide-band λ there is nothing honest to calculate.
                return facts.LambdaPidAdvertised
                    ? (EnergySupport.Estimated, EnergyReason.AirFlowEstimate)
                    : (EnergySupport.InsufficientData, EnergyReason.DieselNeedsLambda);
            case FuelKind.Cng:
                return (EnergySupport.Unsupported, EnergyReason.FuelTypeUnsupported);
            default:
                return (EnergySupport.InsufficientData, EnergyReason.FuelTypeUnknown);
        }
    }
}
