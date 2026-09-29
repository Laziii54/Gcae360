namespace CarScanner.Models;

/// <summary>
/// User interface choices for a specific VIN. This contains only PID ids the
/// user selected; it never creates telemetry values or claims PID support.
/// </summary>
public sealed class LiveDataLayout
{
    public List<int> FavoritePids { get; set; } = new();
    public List<int> DashboardPids { get; set; } = new();
    public List<int> GraphPids { get; set; } = new();
}
