namespace CarScanner.Models;

public sealed class SensorValue
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Value { get; init; } = "—";
    /// <summary>Original OBD-II metric value used for thresholds after UI conversion.</summary>
    public string CanonicalValue { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool IsProblem => EvaluateProblem();
    /// <summary>
    /// True only for PIDs with a known normal range. Other values are real ECU
    /// data but are not labelled "normal", because there is nothing to compare.
    /// </summary>
    public bool HasNormalRange => Pid is 0x05 or 0x5C or 0x0F or 0x06 or 0x07 or 0x08 or 0x09 or 0x42
        or 0x2F or 0x3C or 0x3D or 0x3E or 0x3F
        or 0x0A or 0x04 or 0x11 or 0x45 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x5A or 0x5B or 0x52
        or 0x0B or 0x10;
    public string StatusText => !HasNormalRange ? "—" : IsProblem ? "ნორმაში არ არის" : "ნორმა";
    public string StatusColor => !HasNormalRange ? "#94A3B8" : IsProblem ? "#B3361F" : "#1F8A4C";
    private bool EvaluateProblem()
    {
        var valueForChecks = string.IsNullOrWhiteSpace(CanonicalValue) ? Value : CanonicalValue;
        if (!double.TryParse(valueForChecks, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
        return Pid switch
        {
            // Engine coolant / oil / intake air temperature (°C).
            0x05 => v > 105,
            0x5C => v > 130,
            0x0F => v > 70,
            // Short-term fuel trims move quickly; long-term trims should stay near 0.
            0x06 or 0x08 => Math.Abs(v) > 20,
            0x07 or 0x09 => Math.Abs(v) > 10,
            // Control module voltage.
            0x42 => v < 11.8 || v > 15.0,
            // Fuel level: almost empty.
            0x2F => v < 10,
            // Catalyst temperature (°C).
            0x3C or 0x3D or 0x3E or 0x3F => v > 900,
            0x0A => v < 0,
            0x04 or 0x11 or 0x45 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x5A or 0x5B or 0x52 => v < 0 || v > 100,
            0x0B => v < 0 || v > 300,
            0x10 => v < 0,
            _ => false
        };
    }
}
