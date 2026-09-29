namespace CarScanner.Models;

public sealed class EmissionReadinessReport
{
    public ReadinessInfo? SinceClear { get; init; }
    public ReadinessInfo? CurrentDriveCycle { get; init; }
    public IReadOnlyList<EmissionMonitor> SinceClearMonitors { get; init; } = Array.Empty<EmissionMonitor>();
    public IReadOnlyList<EmissionMonitor> DriveCycleMonitors { get; init; } = Array.Empty<EmissionMonitor>();
    public string DistanceSinceClear { get; init; } = "მიუწვდომელია";
    public double? DistanceSinceClearKm { get; init; }
    public string WarmUpsSinceClear { get; init; } = "მიუწვდომელია";
    public double? WarmUpsSinceClearValue { get; init; }
    public string DistanceWithMil { get; init; } = "მიუწვდომელია";
    public double? DistanceWithMilKm { get; init; }
    public string TimeWithMil { get; init; } = "მიუწვდომელია";
    public double? TimeWithMilMinutes { get; init; }
    public string TimeSinceClear { get; init; } = "მიუწვდომელია";
    public double? TimeSinceClearMinutes { get; init; }
}
