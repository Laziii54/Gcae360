namespace CarScanner.Models;

public sealed class AccelerationLeaderboardEntry
{
    public string SubmissionId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string VehicleModel { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public DateTime FinishedAt { get; set; }
    public bool HasVerifiedVideo { get; set; }
    public bool IsDemo { get; set; }
}

public sealed class AccelerationLeaderboardSubmission
{
    public string SubmissionId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string VehicleModel { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    public bool VinVerified { get; set; }
    public string SpeedSource { get; set; } = string.Empty;
    public string TestId { get; set; } = string.Empty;
    public double StartSpeedKph { get; set; }
    public double TargetSpeedKph { get; set; }
    public double DurationSeconds { get; set; }
    public int SampleCount { get; set; }
    public int MaxSpeedKph { get; set; }
    public double DistanceMeters { get; set; }
    public double PeakAcceleration { get; set; }
    public DateTime FinishedAt { get; set; }
    public bool HasVerifiedVideo { get; set; }
}
