namespace CarScanner.Models;

public enum VehicleHealthSeverity
{
    Info,
    Minor,
    Warning,
    Major,
    Critical
}

public enum VehicleHealthCategory
{
    Dtc,
    Engine,
    Transmission,
    Electrical,
    Emissions,
    Temperature,
    FuelSystem,
    Sensor,
    HybridEv,
    Abs,
    Srs,
    Communication,
    Other
}

/// <summary>A real, scored finding from one completed health scan.</summary>
public sealed class VehicleHealthIssue
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public VehicleHealthSeverity Severity { get; init; }
    public VehicleHealthCategory Category { get; init; }
    public int Penalty { get; init; }
    public string Source { get; init; } = string.Empty;
    public string SeverityText { get; init; } = string.Empty;
    public string PenaltyText => Penalty <= 0 ? string.Empty : $"−{Penalty}";
    public string Color => Severity switch
    {
        VehicleHealthSeverity.Critical => "#F87171",
        VehicleHealthSeverity.Major => "#FB7185",
        VehicleHealthSeverity.Warning => "#FBBF24",
        VehicleHealthSeverity.Minor => "#F59E0B",
        _ => "#60A5FA"
    };
}

public sealed record VehicleHealthProgress(int Percent, string Stage);

public sealed class VehicleHealthReport
{
    public int Score { get; init; }
    public int CriticalIssueCount { get; init; }
    public int WarningCount { get; init; }
    public IReadOnlyList<VehicleHealthIssue> Issues { get; init; } = Array.Empty<VehicleHealthIssue>();
    public int CheckedSensorCount { get; init; }
    public int CheckedModuleCount { get; init; }
    public int ScanCoverage { get; init; }
    public int CheckedChecks { get; init; }
    public int AvailableChecks { get; init; }
    public string Interpretation { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}
