using SQLite;

namespace CarScanner.Models;

/// <summary>A completed, OBD-derived performance measurement (e.g. 0–100 km/h).</summary>
public sealed class AccelerationRun
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime StartedAt { get; set; }

    public DateTime FinishedAt { get; set; }

    [Indexed]
    public string VIN { get; set; } = string.Empty;

    public double DurationSeconds { get; set; }
    public string Source { get; set; } = "OBD";
    public string Confidence { get; set; } = "Estimated";
    public int SampleCount { get; set; }

    // Extra details of the run (empty for runs saved by older versions).
    public string TestId { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public double StartSpeedKph { get; set; }
    public double TargetSpeedKph { get; set; }
    public int MaxSpeedKph { get; set; }
    public int MaxRpm { get; set; }
    public double DistanceMeters { get; set; }
    public double PeakAcceleration { get; set; }

    /// <summary>Checkpoint times as JSON: {"20":1.95,"50":4.10,...} (km/h → seconds).</summary>
    public string SplitsJson { get; set; } = string.Empty;

    // Leaderboard evidence. Older rows keep the safe defaults and are not
    // silently promoted into the public ranking.
    public string UserName { get; set; } = string.Empty;
    public string VehicleDisplayName { get; set; } = string.Empty;
    public bool VinVerified { get; set; }
    public bool LeaderboardEligible { get; set; }
    public string LeaderboardRejectionReason { get; set; } = string.Empty;
    public bool HasVerifiedVideo { get; set; }
    public string LeaderboardSubmissionId { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }

    // Ranking evidence (empty for older runs).
    /// <summary>[[seconds from start, km/h], …] from ECU PID 01 0D.</summary>
    public string SpeedTraceJson { get; set; } = string.Empty;
    /// <summary>[[seconds, km/h, lat, lon, accuracy m], …] from the phone GPS.</summary>
    public string GpsTraceJson { get; set; } = string.Empty;
    /// <summary>passed / failed / missing.</summary>
    public string GpsStatus { get; set; } = string.Empty;
    public double GpsMedianDiffKph { get; set; }
    /// <summary>Server status after publishing: Approved, Pending (under review), Verified.</summary>
    public string LeaderboardStatus { get; set; } = string.Empty;
    /// <summary>Copy of the in-app video recorded with this run (for review uploads).</summary>
    public string VideoPath { get; set; } = string.Empty;
    public DateTime? VideoUploadedAt { get; set; }
}
