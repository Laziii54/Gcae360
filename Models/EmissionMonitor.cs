namespace CarScanner.Models;
public sealed class EmissionMonitor
{
    public string Category { get; init; } = "Non-continuous";
    public string Name { get; init; } = string.Empty;
    public bool? Supported { get; init; }
    public bool? Complete { get; init; }
    public string Details { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string Raw { get; init; } = string.Empty;
    public string Status => Supported != true ? "Not Supported" : Complete == true ? "Complete" : "Incomplete";
    public string StatusColor => Supported != true ? "#94A3B8" : Complete == true ? "#22C55E" : "#F59E0B";
}
