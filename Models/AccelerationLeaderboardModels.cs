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
    /// <summary>Speed at the finish (trap speed on ¼ mile).</summary>
    public int MaxSpeedKph { get; set; }
    /// <summary>Approved, Pending (under review) or Verified.</summary>
    public string Status { get; set; } = "Approved";
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
    /// <summary>[[seconds, km/h], …] ECU speed.</summary>
    public List<double[]> SpeedTrace { get; set; } = new();
    /// <summary>[[seconds, km/h, lat, lon, accuracy], …] phone GPS.</summary>
    public List<double[]> GpsTrace { get; set; } = new();
}

public sealed class AccelerationLeaderboardPublishResponse
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool VideoRequired { get; set; }
}
