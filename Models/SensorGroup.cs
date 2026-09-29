namespace CarScanner.Models;

public sealed class SensorGroup
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<SensorValue> Sensors { get; init; } = Array.Empty<SensorValue>();
    public string CountText => $"{Sensors.Count} სენსორი";
}
