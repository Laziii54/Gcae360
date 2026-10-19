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

/// <summary>What the evidence, taken together, shows. Every code describes an observation; none says "tampering" and none picks an "original" mileage.</summary>
public enum MileageFindingCode
{
    /// <summary>Two or more current total-odometer readings agree within the tolerance.</summary>
    SourcesAgree,

    /// <summary>Two or more current total-odometer readings differ by more than the tolerance.</summary>
    SourcesDiffer,

    /// <summary>A module's stored historical odometer record is higher than even the highest current reading.</summary>
    HistoricalExceedsCurrent,

    /// <summary>A module's installation record is higher than even the highest current reading.</summary>
    ModuleRecordExceedsCurrent,

    /// <summary>Historical / installation records exist and none is higher than the current reading.</summary>
    HistoryNotAboveCurrent,

    /// <summary>Records were read but no usable current odometer, so they cannot be compared with one.</summary>
    NoCurrentOdometer,

    /// <summary>One current reading and no record to compare it with.</summary>
    SingleSourceNoHistory
}

public enum MileageFindingLevel
{
    Info,
    Notice,
    Attention
}

public sealed record MileageFinding(
    MileageFindingCode Code,
    MileageFindingLevel Level,
    IReadOnlyList<MileageEvidence> Evidence,
    double? DifferenceKm = null);

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

    /// <summary>
    /// Odometer values the modules stored at past events (DPF regeneration, adaptation, warning, TPMS event ...). Each is a
    /// record of the past and is compared only with the current readings, never with another record.
    /// </summary>
    public IReadOnlyList<MileageEvidence> History { get; init; } = Array.Empty<MileageEvidence>();

    /// <summary>Values tied to a module's installation ("Mileage / Odometer with ECU installed").</summary>
    public IReadOnlyList<MileageEvidence> InstallRecords { get; init; } = Array.Empty<MileageEvidence>();

    /// <summary>The highest value among the usable historical and installation records: a lower bound for the distance the car had covered at that time. Not "the original mileage".</summary>
    public MileageEvidence? HighestRecorded { get; init; }

    /// <summary>What the evidence shows as a whole, most important first. Observations only.</summary>
    public IReadOnlyList<MileageFinding> Findings { get; init; } = Array.Empty<MileageFinding>();
}

/// <summary>Sanity limits for a total-odometer value.</summary>
public static class MileagePlausibility
{
    public const double MaximumKm = 2_000_000d;
    public const double KmPerMile = 1.609344d;

    public static bool IsPlausibleOdometerKm(double km) => double.IsFinite(km) && km > 0 && km <= MaximumKm;

    /// <summary>A stored record: zero means "nothing recorded" and is not used.</summary>
    public static bool IsPlausibleRecordKm(double km) => IsPlausibleOdometerKm(km);

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
        var historyCandidates = all.Where(e => e.Measurement == MileageMeasurement.OdometerAtEvent).ToList();
        var installCandidates = all.Where(e => e.Measurement == MileageMeasurement.ModuleInstallRecord).ToList();
        var context = all.Where(e => e.Measurement is not (MileageMeasurement.TotalOdometer or MileageMeasurement.OdometerAtEvent or MileageMeasurement.ModuleInstallRecord)).ToList();

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

        var history = UsableRecords(historyCandidates, flagged);
        var installs = UsableRecords(installCandidates, flagged);
        var highest = history.Concat(installs).OrderByDescending(e => e.ValueKm).FirstOrDefault();
        var findings = BuildFindings(usable, history, installs, outcome, spread);

        return new MileageAnalysis
        {
            History = history,
            InstallRecords = installs,
            HighestRecorded = highest,
            Findings = findings,
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

    // A record is usable when it decoded, is a real distance and is not zero (zero = nothing recorded). Others go to "to review".
    private static List<MileageEvidence> UsableRecords(List<MileageEvidence> candidates, List<MileageEvidence> flagged)
    {
        var usable = new List<MileageEvidence>();
        foreach (var record in candidates)
        {
            if (record.Status != MileageReadStatus.Ok)
                flagged.Add(record);
            else if (!record.IsDistance || !MileagePlausibility.IsPlausibleRecordKm(record.ValueKm))
                flagged.Add(record with { Status = MileageReadStatus.Implausible });
            else if (!usable.Any(u => u.Ecu == record.Ecu && u.Method == record.Method && u.SignalName == record.SignalName))
                usable.Add(record);
        }

        return usable
            .OrderBy(e => e.Measurement)
            .ThenBy(e => e.EventKind)
            .ThenBy(e => e.Ecu, StringComparer.Ordinal)
            .ThenBy(e => e.SignalName, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<MileageFinding> BuildFindings(
        IReadOnlyList<MileageEvidence> totals,
        IReadOnlyList<MileageEvidence> history,
        IReadOnlyList<MileageEvidence> installs,
        MileageOutcome outcome,
        double? spread)
    {
        var findings = new List<MileageFinding>();
        var records = history.Concat(installs).ToList();

        if (outcome == MileageOutcome.Conflicting)
            findings.Add(new MileageFinding(MileageFindingCode.SourcesDiffer, MileageFindingLevel.Attention, totals, spread));
        else if (outcome == MileageOutcome.Consistent)
            findings.Add(new MileageFinding(MileageFindingCode.SourcesAgree, MileageFindingLevel.Info, totals, spread));

        if (totals.Count == 0)
        {
            if (records.Count > 0)
                findings.Add(new MileageFinding(MileageFindingCode.NoCurrentOdometer, MileageFindingLevel.Notice, records));
            return findings;
        }

        // The reference is the HIGHEST current reading: a record is called higher than "the current distance" only when it
        // is higher than every module's current value, so a low-reading module can never create the finding by itself.
        var reference = totals.OrderByDescending(e => e.ValueKm).First();

        var exceedingHistory = new List<MileageEvidence>();
        var exceedingInstall = new List<MileageEvidence>();
        foreach (var record in records)
        {
            var allowed = Tolerance(totals.Append(record).ToList());
            if (record.ValueKm - reference.ValueKm > allowed)
                (record.Measurement == MileageMeasurement.ModuleInstallRecord ? exceedingInstall : exceedingHistory).Add(record);
        }

        if (exceedingHistory.Count > 0)
            findings.Add(new MileageFinding(
                MileageFindingCode.HistoricalExceedsCurrent, MileageFindingLevel.Attention,
                exceedingHistory.Append(reference).ToList(), exceedingHistory.Max(e => e.ValueKm) - reference.ValueKm));
        if (exceedingInstall.Count > 0)
            findings.Add(new MileageFinding(
                MileageFindingCode.ModuleRecordExceedsCurrent, MileageFindingLevel.Attention,
                exceedingInstall.Append(reference).ToList(), exceedingInstall.Max(e => e.ValueKm) - reference.ValueKm));

        if (records.Count > 0 && exceedingHistory.Count == 0 && exceedingInstall.Count == 0)
            findings.Add(new MileageFinding(MileageFindingCode.HistoryNotAboveCurrent, MileageFindingLevel.Info, records));

        if (totals.Count == 1 && records.Count == 0)
            findings.Add(new MileageFinding(MileageFindingCode.SingleSourceNoHistory, MileageFindingLevel.Notice, totals));

        return findings
            .OrderByDescending(f => f.Level)
            .ThenBy(f => f.Code)
            .ToList();
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
