using CarScanner.Services.OBD;

namespace CarScanner.Models;

/// <summary>How a value compares with its normal range.</summary>
public enum SensorLevel
{
    /// <summary>No known normal range: shown without a colour.</summary>
    None,
    Normal,
    Attention,
    Critical
}

public sealed class SensorValue
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Value { get; init; } = "—";
    /// <summary>Original OBD-II metric value used for thresholds after UI conversion.</summary>
    public string CanonicalValue { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    /// <summary>Green = normal, orange = needs attention, red = alarming.</summary>
    public SensorLevel Level => Evaluate();

    /// <summary>Attention or critical (kept for older callers).</summary>
    public bool IsProblem => Level is SensorLevel.Attention or SensorLevel.Critical;

    /// <summary>
    /// True only for PIDs with a known normal range. Other values are real ECU
    /// data but are not labelled "normal", because there is nothing to compare.
    /// </summary>
    public bool HasNormalRange => Level != SensorLevel.None;

    public string LevelKey => Level switch
    {
        SensorLevel.Normal => "ok",
        SensorLevel.Attention => "warn",
        SensorLevel.Critical => "crit",
        _ => "none"
    };

    public string StatusText => Level switch
    {
        SensorLevel.Normal => ObdLang.L("ნორმა", "Normal"),
        SensorLevel.Attention => ObdLang.L("საყურადღებო", "Attention"),
        SensorLevel.Critical => ObdLang.L("საგანგაშო", "Alarm"),
        _ => "—"
    };

    public string StatusColor => Level switch
    {
        SensorLevel.Normal => "#1F8A4C",
        SensorLevel.Attention => "#D0701F",
        SensorLevel.Critical => "#B3361F",
        _ => "#94A3B8"
    };

    public bool HasStatus => Level != SensorLevel.None;

    /// <summary>True when the value is a number, so tapping it can open a live graph.</summary>
    public bool CanGraph => double.TryParse(Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

    private SensorLevel Evaluate()
    {
        var valueForChecks = string.IsNullOrWhiteSpace(CanonicalValue) ? Value : CanonicalValue;
        if (!double.TryParse(valueForChecks, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return SensorLevel.None;

        // (attention, critical) thresholds — everything else is normal.
        static SensorLevel Above(double v, double attention, double critical) =>
            v > critical ? SensorLevel.Critical : v > attention ? SensorLevel.Attention : SensorLevel.Normal;
        static SensorLevel Below(double v, double attention, double critical) =>
            v < critical ? SensorLevel.Critical : v < attention ? SensorLevel.Attention : SensorLevel.Normal;
        static SensorLevel Percent(double v) =>
            v < 0 || v > 100.5 ? SensorLevel.Attention : SensorLevel.Normal;

        return Pid switch
        {
            // Engine coolant / oil / intake air temperature (°C).
            0x05 => Above(v, 105, 115),
            0x5C => Above(v, 130, 145),
            0x0F => Above(v, 60, 75),
            // Short-term fuel trims move quickly; long-term trims should stay near 0.
            0x06 or 0x08 => Above(Math.Abs(v), 15, 25),
            0x07 or 0x09 => Above(Math.Abs(v), 10, 20),
            // Control module (12 V) voltage.
            0x42 => v > 15.5 ? SensorLevel.Critical
                : v > 15.0 ? SensorLevel.Attention
                : Below(v, 12.0, 11.5),
            // Fuel level: almost empty.
            0x2F => Below(v, 15, 7),
            // Catalyst temperature (°C).
            0x3C or 0x3D or 0x3E or 0x3F => Above(v, 850, 950),
            // Hybrid battery remaining life (%).
            0x5B => Below(v, 50, 30),
            0x0A => v < 0 ? SensorLevel.Attention : SensorLevel.Normal,
            0x04 or 0x11 or 0x45 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x5A or 0x52 => Percent(v),
            0x0B => v < 0 || v > 300 ? SensorLevel.Attention : SensorLevel.Normal,
            0x10 => v < 0 ? SensorLevel.Attention : SensorLevel.Normal,
            _ => SensorLevel.None
        };
    }
}
