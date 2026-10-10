namespace CarScanner.Services.Mileage;

public sealed class ActualMileageResult
{
    public string VIN { get; init; } = string.Empty;

    /// <summary>Everything the car returned: odometer readings, flagged values and context counters.</summary>
    public List<MileageEvidence> Evidence { get; } = new();

    /// <summary>What was tried for each source and what came of it (including sources that were not tried and why).</summary>
    public List<MileageAttempt> Attempts { get; } = new();

    public MileageAnalysis Analysis { get; set; } = MileageAnalyzer.Analyze(Array.Empty<MileageEvidence>());

    /// <summary>
    /// The headline total-odometer value, set only when readings do not conflict. It is the value of one named source,
    /// never "the original mileage", and never the highest or lowest of conflicting readings.
    /// </summary>
    public double? FinalMileageKm { get; set; }

    /// <summary>true when comparable total-odometer readings differ by more than the tolerance.</summary>
    public bool HasMismatch { get; set; }

    public double? MaximumDifferenceKm { get; set; }
    public double? ConsistencyToleranceKm { get; set; }

    public string Status { get; set; } = "მიუწვდომელია";
    public string Details { get; set; } = string.Empty;

    /// <summary>Number of comparable total-odometer readings.</summary>
    public int SourceCount => Analysis.Odometer.Count;

    /// <summary>Requests sent to the car and their answers (for support).</summary>
    public string Log { get; set; } = string.Empty;

    /// <summary>Fills the summary fields from <see cref="Evidence"/>. Pure: no I/O.</summary>
    public void ApplyAnalysis()
    {
        var analysis = MileageAnalyzer.Analyze(Evidence);
        Analysis = analysis;
        FinalMileageKm = analysis.Primary?.ValueKm;
        HasMismatch = analysis.Outcome == MileageOutcome.Conflicting;
        MaximumDifferenceKm = analysis.SpreadKm;
        ConsistencyToleranceKm = analysis.Odometer.Count >= 2 ? analysis.ToleranceKm : null;

        Status = analysis.Outcome switch
        {
            MileageOutcome.NoReading => "ოდომეტრი მიუწვდომელია",
            MileageOutcome.SingleSource => "ერთი წყარო",
            MileageOutcome.Consistent => "მაჩვენებლები თანხვედრაშია",
            _ => "მაჩვენებლები განსხვავდება"
        };

        var lines = analysis.Odometer.Select(x => $"{x.Source} / {x.Ecu}: {x.ValueKm:0.0} km").ToList();
        lines.AddRange(analysis.Flagged.Select(x => $"{x.Source} / {x.Ecu}: {x.ValueKm:0.0} km ({x.Status})"));
        if (analysis.Odometer.Count >= 2)
            lines.Add($"max difference {analysis.SpreadKm:0.0} km · tolerance {analysis.ToleranceKm:0.0} km");
        Details = string.Join(Environment.NewLine, lines);
    }
}
