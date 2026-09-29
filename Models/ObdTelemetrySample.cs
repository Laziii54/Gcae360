namespace CarScanner.Models;

public sealed class ObdTelemetrySample
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double Value { get; init; }
}
