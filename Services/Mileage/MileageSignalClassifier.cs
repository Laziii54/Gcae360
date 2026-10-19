using System.Text.RegularExpressions;

namespace CarScanner.Services.Mileage;

/// <summary>What kind of distance a catalog signal name stands for. Only <see cref="TotalOdometer"/> is compared as an odometer.</summary>
public enum MileageSignalClass
{
    /// <summary>Not a distance signal at all.</summary>
    NotMileage,

    /// <summary>A module's copy of the vehicle's total distance ("Odometer, instrument panel", "Total distance", ...).</summary>
    TotalOdometer,

    /// <summary>The odometer value the module stored when something happened ("Odometer at last DPF regeneration", "Tire pressure monitor deflation odometer history 1"). A record of the past, never the current distance.</summary>
    OdometerAtEvent,

    /// <summary>A value tied to the module's installation ("Mileage / Odometer with ECU installed"). Whether it is the odometer at installation or the distance since is not stated by the definition, so only what holds under both readings is concluded from it.</summary>
    ModuleInstallRecord,

    /// <summary>A component's own distance counter (a catalytic converter, a traction battery): it restarts when the component is replaced.</summary>
    ComponentDistance,

    /// <summary>A distance-like value that is a difference, a remaining distance, an average or otherwise not a snapshot of the odometer. Not used.</summary>
    Excluded
}

/// <summary>What happened at the moment an <see cref="MileageSignalClass.OdometerAtEvent"/> value was stored.</summary>
public enum MileageEventKind
{
    None,
    Regeneration,
    Replacement,
    Adaptation,
    Warning,
    Service,
    TirePressure,
    Other
}

/// <summary>
/// Sorts the names of public OBDb signals into the kinds of distance they hold. Pure: no I/O.
///
/// The OBDb signal sets contain, besides the odometer copies kept by many modules, a large number of history records
/// ("odometer at last adaptation", "Sensor 1 odometer event 2", TPMS history lists), installation records ("Mileage with
/// ECU installed"), component counters ("Mileage of SCR catalytic converter") and remaining / delta values ("Mileage before
/// servicing"). Treating any of those as "the odometer" would make a normal car look inconsistent, and a real inconsistency
/// look normal, so each is kept apart and compared only with what it can honestly be compared with.
/// The decision uses the signal's name only; the names are the public definition's, not a verified fact about any vehicle.
/// </summary>
public static class MileageSignalClassifier
{
    private static readonly Regex DistanceName = new(@"odometer|total distance|mileage", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A difference, a remaining distance, an average or a request: never a snapshot of the odometer.
    private static readonly Regex NotASnapshot = new(@"since|remaining|before|after|average|until|to empty|request|raw count", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The pre-existing exclusions for the total odometer (kept exactly: they only ever removed names from the total group).
    private static readonly Regex NotTotal = new(
        @"since|after|adaptation|kiss|injector|fault|trip|range|service|reset|last|learn|regen|oil|filter|remaining|until|to empty",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MileageSignalClass Classify(string? name, out MileageEventKind eventKind)
    {
        eventKind = MileageEventKind.None;
        if (string.IsNullOrWhiteSpace(name) || !DistanceName.IsMatch(name))
            return MileageSignalClass.NotMileage;

        var text = name.ToLowerInvariant();

        if (NotASnapshot.IsMatch(text))
            return MileageSignalClass.Excluded;

        // Installation record of a module (OBDb: "Mileage with ECU installed", "Odometer with ECU installed [, variant 2]").
        if (text.Contains("ecu installed"))
            return MileageSignalClass.ModuleInstallRecord;

        // Snapshots of the odometer at an event.
        if (TryEvent(text, out eventKind))
            return MileageSignalClass.OdometerAtEvent;
        eventKind = MileageEventKind.None;

        // Component counters: they start again when the component is replaced.
        if ((text.Contains("catalytic converter") && text.Contains("mileage of")) || text.Contains("traction battery"))
            return MileageSignalClass.ComponentDistance;

        // "Counter steer support, odometer": whose odometer is not stated. Not guessed.
        if (text.Contains("counter steer"))
            return MileageSignalClass.Excluded;

        if (NotTotal.IsMatch(text))
            return MileageSignalClass.Excluded;

        return MileageSignalClass.TotalOdometer;
    }

    private static bool TryEvent(string text, out MileageEventKind kind)
    {
        kind = MileageEventKind.None;

        // Tire-pressure-monitor records come first: their "warning / adaptation / set / deflation" words are TPMS events.
        if (text.Contains("tire pressure") && (text.Contains("odometer") || text.Contains("mileage")))
        {
            kind = MileageEventKind.TirePressure;
            return true;
        }

        if (text.Contains("regeneration") || text.Contains("regen"))
        {
            kind = MileageEventKind.Regeneration;
            return text.Contains("last") || text.Contains("odometer");
        }

        if (text.Contains("replacement") || text.Contains("replace") || text.Contains("changing dpf"))
        {
            kind = MileageEventKind.Replacement;
            return true;
        }

        if (text.Contains("adaptation"))
        {
            kind = MileageEventKind.Adaptation;
            return true;
        }

        if (text.Contains("warning") || text.Contains("fault led") || text.Contains("fault warning light") ||
            text.Contains("excess speed") || text.Contains("overspeed") || text.Contains("over-speed"))
        {
            kind = MileageEventKind.Warning;
            return true;
        }

        if (text.Contains("at last service"))
        {
            kind = MileageEventKind.Service;
            return true;
        }

        if (text.Contains("odometer event") || text.Contains("last activat"))
        {
            kind = MileageEventKind.Other;
            return true;
        }

        return false;
    }
}
