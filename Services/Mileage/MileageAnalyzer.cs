namespace CarScanner.Services.Mileage;

public enum MileageOutcome
{
    /// <summary>No usable total-odometer reading.</summary>
    NoReading,

    /// <summary>Exactly one usable total-odometer reading: nothing to compare it with.</summary>
    SingleSource,

    /// <summary>Two or more readings agree within the tolerance.</summary>
    Consistent,

    /// <summary>Two or more readings differ by more than the tolerance. None is chosen as "correct".</summary>
    Conflicting
}

public sealed record MileageConflict(MileageEvidence First, MileageEvidence Second, double DifferenceKm);

public sealed class MileageAnalysis
{
    public MileageOutcome Outcome { get; init; }

    /// <summary>Valid total-odometer readings (the only ones that are compared).</summary>
    public IReadOnlyList<MileageEvidence> Odometer { get; init; } = Array.Empty<MileageEvidence>();

    /// <summary>Readings returned by a module but rejected as implausible or malformed. Shown for review, never used.</summary>
    public IReadOnlyList<MileageEvidence> Flagged { get; init; } = Array.Empty<MileageEvidence>();

    /// <summary>Other distance / time values (since DTC clear, MIL on …). Shown separately, never compared with the odometer.</summary>
    public IReadOnlyList<MileageEvidence> Context { get; init; } = Array.Empty<MileageEvidence>();

    /// <summary>The reading to show as the headline: only when there is no conflict. Never the highest or lowest by rule.</summary>
    public MileageEvidence? Primary { get; init; }

    public IReadOnlyList<MileageConflict> Conflicts { get; init; } = Array.Empty<MileageConflict>();

    /// <summary>Largest difference between two comparable readings (km), when there are at least two.</summary>
    public double? SpreadKm { get; init; }

    public double ToleranceKm { get; init; }
}

/// <summary>Sanity limits for a total-odometer value.</summary>
public static class MileagePlausibility
{
    public const double MaximumKm = 2_000_000d;
    public const double KmPerMile = 1.609344d;

    public static bool IsPlausibleOdometerKm(double km) => double.IsFinite(km) && km > 0 && km <= MaximumKm;

    public static double MilesToKm(double miles) => miles * KmPerMile;
}

/// <summary>
/// Separates the different distance values and compares only values that mean the same thing. It makes no claim
/// about authenticity: agreement is "consistent", a difference is "conflicting", and neither says what the true
/// original mileage is.
/// </summary>
public static class MileageAnalyzer
{
    public static MileageAnalysis Analyze(IEnumerable<MileageEvidence> evidence)
    {
        var all = evidence.ToList();

        var odometerCandidates = all.Where(e => e.Measurement == MileageMeasurement.TotalOdometer).ToList();
        var context = all.Where(e => e.Measurement != MileageMeasurement.TotalOdometer).ToList();

        var flagged = new List<MileageEvidence>();
        var usable = new List<MileageEvidence>();
        foreach (var reading in odometerCandidates)
        {
            if (reading.Status != MileageReadStatus.Ok)
                flagged.Add(reading);
            else if (!reading.IsDistance || !MileagePlausibility.IsPlausibleOdometerKm(reading.ValueKm))
                flagged.Add(reading with { Status = MileageReadStatus.Implausible });
            else if (!usable.Any(u => u.Ecu == reading.Ecu && u.Source == reading.Source && u.Method == reading.Method))
                usable.Add(reading);
        }

        // Deterministic order: most direct source first, then by name.
        usable = usable.OrderBy(Rank).ThenBy(e => e.Ecu, StringComparer.Ordinal).ThenBy(e => e.Source, StringComparer.Ordinal).ToList();

        var tolerance = Tolerance(usable);
        var conflicts = new List<MileageConflict>();
        for (var i = 0; i < usable.Count; i++)
        {
            for (var j = i + 1; j < usable.Count; j++)
            {
                var difference = Math.Abs(usable[i].ValueKm - usable[j].ValueKm);
                if (difference > tolerance)
                    conflicts.Add(new MileageConflict(usable[i], usable[j], difference));
            }
        }

        double? spread = usable.Count >= 2 ? usable.Max(e => e.ValueKm) - usable.Min(e => e.ValueKm) : null;

        var outcome = usable.Count switch
        {
            0 => MileageOutcome.NoReading,
            1 => MileageOutcome.SingleSource,
            _ => conflicts.Count == 0 ? MileageOutcome.Consistent : MileageOutcome.Conflicting
        };

        return new MileageAnalysis
        {
            Outcome = outcome,
            Odometer = usable,
            Flagged = flagged,
            Context = context,
            Primary = outcome is MileageOutcome.SingleSource or MileageOutcome.Consistent ? usable[0] : null,
            Conflicts = conflicts,
            SpreadKm = spread,
            ToleranceKm = tolerance
        };
    }

    /// <summary>
    /// How far two readings may differ and still count as the same number: 0.1 % of the larger value (5–250 km),
    /// and at least 2 km when a reading was reported in whole miles.
    /// </summary>
    public static double Tolerance(IReadOnlyCollection<MileageEvidence> readings)
    {
        if (readings.Count == 0)
            return 5d;

        var tolerance = Math.Clamp(readings.Max(e => e.ValueKm) * 0.001d, 5d, 250d);
        if (readings.Any(e => e.Unit == "mi"))
            tolerance = Math.Max(tolerance, 2d);
        return tolerance;
    }

    /// <summary>Lower = more direct. Only used to pick a headline when readings agree.</summary>
    private static int Rank(MileageEvidence evidence)
    {
        var module = (evidence.Ecu + " " + evidence.Source).ToLowerInvariant();
        if (module.Contains("cluster") || module.Contains("dash") || module.Contains("kombi") || module.Contains("ipc"))
            return 0;
        if (evidence.SourceKind == MileageSourceKind.StandardObd)
            return 1;
        if (evidence.SourceKind == MileageSourceKind.LearnedOnPhone)
            return 3;
        return 2;
    }
}
