namespace CarScanner.Models;

public sealed class SupportedPid
{
    public int Pid { get; init; }
    public string HexId => $"{Pid:X2}";
    public string Command => $"01{Pid:X2}";
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Category { get; init; } = "სხვა";
}
