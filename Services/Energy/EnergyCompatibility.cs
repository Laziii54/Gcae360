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
    FuelMassRatePid,
    AirFlowEstimate,
    FuelTypeUnknown,
    DieselNeedsLambda,
    FuelTypeUnsupported,
    SpeedMissing,
    RpmMissing,
    NoBatterySource,
    /// <summary>The adapter refused or lost the commands needed to address the battery module: says nothing about the car.</summary>
    AdapterCouldNotRead,
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
    bool LambdaPidAdvertised = false,
    bool AdapterLimited = false);

/// <summary>
/// Decides, per calculation, whether the data to do it exists for this car. It looks at the PIDs the ECU advertises and at
/// the battery source that was found; nothing is "supported" because of a brand or a model.
/// </summary>
public static class EnergyCompatibility
{
    public const int PidFuelRate = 0x5E;
    public const int PidEngineFuelRate = 0x9D;
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
                facts.StateOfChargeSource || Has(PidBatteryCharge) == true ? EnergySupport.DirectlySupported : facts.AdapterLimited ? EnergySupport.InsufficientData : EnergySupport.Unsupported,
                facts.StateOfChargeSource || Has(PidBatteryCharge) == true ? EnergyReason.None : facts.AdapterLimited ? EnergyReason.AdapterCouldNotRead : EnergyReason.NoBatterySource);

            if (facts.BatteryCurrentSource)
            {
                Add(EnergyMetric.ElectricPower, EnergySupport.CalculatedFromAvailableData, EnergyReason.BatteryCurrentSource);
                Add(EnergyMetric.ElectricEnergy, EnergySupport.CalculatedFromAvailableData, EnergyReason.BatteryCurrentSource);
                Add(EnergyMetric.Regeneration, EnergySupport.CalculatedFromAvailableData, EnergyReason.BatteryCurrentSource);
            }
            else
            {
                // When the ADAPTER could not address the battery module, that is no finding about the car: say "not read",
                // never "not supported" (another adapter may well read it).
                var noSource = facts.AdapterLimited
                    ? (Support: EnergySupport.InsufficientData, Reason: EnergyReason.AdapterCouldNotRead)
                    : (Support: EnergySupport.Unsupported, Reason: EnergyReason.NoBatterySource);
                Add(EnergyMetric.ElectricPower, noSource.Support, noSource.Reason);
                Add(EnergyMetric.Regeneration, noSource.Support, noSource.Reason);
                var soc = facts.StateOfChargeSource || Has(PidBatteryCharge) == true;
                if (soc && facts.BatteryCapacityKnown)
                    Add(EnergyMetric.ElectricEnergy, EnergySupport.Estimated, EnergyReason.SocWithCapacity);
                else if (soc)
                    Add(EnergyMetric.ElectricEnergy, EnergySupport.InsufficientData, EnergyReason.SocWithoutCapacity);
                else
                    Add(EnergyMetric.ElectricEnergy, noSource.Support, noSource.Reason);
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

        // PID 9D is a mass flow (g/s): it needs the density of a known liquid fuel to become litres.
        if (pids.Contains(PidEngineFuelRate))
        {
            if (FuelKinds.TryGetMafConstants(facts.Kind, out _))
                return (EnergySupport.CalculatedFromAvailableData, EnergyReason.FuelMassRatePid);
            if (!pids.Contains(PidMaf))
                return (EnergySupport.InsufficientData, facts.Kind == FuelKind.Cng ? EnergyReason.FuelTypeUnsupported : EnergyReason.FuelTypeUnknown);
        }

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
