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
    public string StatusText => IsProblem ? "ყურადღება" : "ნორმა";
    public string StatusColor => IsProblem ? "#EF4444" : "#4ADE80";
    private bool EvaluateProblem()
    {
        var valueForChecks = string.IsNullOrWhiteSpace(CanonicalValue) ? Value : CanonicalValue;
        if (!double.TryParse(valueForChecks, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
        return Pid switch
        {
            0x05 => v < -20 || v > 125,
            0x06 or 0x07 or 0x08 or 0x09 => Math.Abs(v) > 25,
            0x42 => v < 11.5 || v > 15.5,
            0x0A => v < 0,
            0x2F or 0x04 or 0x11 or 0x45 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x5A or 0x5B or 0x52 => v < 0 || v > 100,
            0x0B => v < 0 || v > 300,
            0x10 => v < 0,
            _ => false
        };
    }
}
