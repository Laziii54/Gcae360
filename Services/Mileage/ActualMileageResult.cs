namespace CarScanner.Services.Mileage;

public sealed class ActualMileageResult
{
    public string VIN { get; init; } = string.Empty;
    public List<MileageEvidence> Evidence { get; } = new();

    public double? FinalMileageKm { get; set; }
    public double Confidence { get; set; }
    public bool HasMismatch { get; set; }
    public double? MaximumDifferenceKm { get; set; }
    public double? ConsistencyToleranceKm { get; set; }

    public string Status { get; set; } = "მიუწვდომელია";
    public string Details { get; set; } = string.Empty;

    public int SourceCount => Evidence.Count;

    /// <summary>Requests sent to the car and their answers (for support).</summary>
    public string Log { get; set; } = string.Empty;
}
