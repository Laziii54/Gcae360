namespace CarScanner.Models;

/// <summary>One completed request in the expanded, support-filtered sensor scan.</summary>
public sealed record SensorScanProgress(int Completed, int Total, string SensorName);
