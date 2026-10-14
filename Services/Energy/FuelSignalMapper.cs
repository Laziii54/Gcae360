using CarScanner.Models;

namespace CarScanner.Services.Energy;

/// <summary>
/// The one place that decides which OBD readings feed the fuel calculation (used by the Fuel page and by the tests).
/// Every reading is stamped with the time it arrived, so old values expire instead of being used forever.
/// </summary>
public static class FuelSignalMapper
{
    /// <summary>PIDs read for fuel analytics: the direct fuel rate when the ECU has it, otherwise air flow (and λ for diesels).</summary>
    public static int[] AnalyticsPids(bool fuelRateSupported, bool lambdaSupported, bool wantLambda, bool fuelMassRateSupported = false)
    {
        if (fuelRateSupported)
            return new[] { EnergyCompatibility.PidFuelRate, EnergyCompatibility.PidFuelLevel, 0x51 };

        // PID 9D (fuel mass flow, g/s) is the car's own fuel reading too; it beats an air-flow estimate.
        if (fuelMassRateSupported)
            return new[] { EnergyCompatibility.PidEngineFuelRate, EnergyCompatibility.PidFuelLevel, 0x51 };

        return wantLambda && lambdaSupported
            ? new[] { EnergyCompatibility.PidMaf, 0x24, EnergyCompatibility.PidFuelLevel, 0x51 }
            : new[] { EnergyCompatibility.PidMaf, EnergyCompatibility.PidFuelLevel, 0x51 };
    }

    /// <summary>Standard Mode 01 PID → fuel signal. Returns false for PIDs that do not feed the calculation.</summary>
    public static bool TryApplyPid(FuelSignalCache cache, int pid, double value, DateTime at)
    {
        var signal = pid switch
        {
            0x0D => FuelSignal.SpeedKph,
            0x0C => FuelSignal.Rpm,
            0x11 => FuelSignal.ThrottlePercent,
            0x04 => FuelSignal.LoadPercent,
            0x5E => FuelSignal.DirectFuelRateLph,
            0x9D => FuelSignal.EngineFuelRateGramsPerSecond,
            0x10 => FuelSignal.MafGramsPerSecond,
            0x24 or 0x34 => FuelSignal.EquivalenceRatio,   // wide-band sensor 1 (bank 1)
            0x2F => FuelSignal.FuelLevelPercent,
            _ => (FuelSignal?)null
        };

        if (signal is null)
            return false;

        cache.Set(signal.Value, value, at);
        return true;
    }

    public static void Apply(FuelSignalCache cache, IEnumerable<SensorValue> values, DateTime at)
    {
        foreach (var value in values)
        {
            if (double.TryParse(value.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                TryApplyPid(cache, value.Pid, number, at);
        }
    }

    /// <summary>The base live-data snapshot (speed, rpm, throttle, load). Only fields the ECU really answered are copied.</summary>
    public static void Apply(FuelSignalCache cache, LiveDataModel data, DateTime at)
    {
        if (data.HasSpeed) cache.Set(FuelSignal.SpeedKph, data.Speed, at);
        if (data.HasRpm) cache.Set(FuelSignal.Rpm, data.Rpm, at);
        if (data.HasThrottlePosition) cache.Set(FuelSignal.ThrottlePercent, data.ThrottlePosition, at);
        if (data.HasEngineLoad) cache.Set(FuelSignal.LoadPercent, data.EngineLoad, at);
        if (data.HasMaf) cache.Set(FuelSignal.MafGramsPerSecond, data.MafGs, at);
        if (data.HasFuelLevel) cache.Set(FuelSignal.FuelLevelPercent, data.FuelLevel, at);
    }
}
