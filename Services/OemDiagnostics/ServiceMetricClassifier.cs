using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Decides what a service-related signal of the public OBDb catalog actually measures, from its name. Pure: no I/O.
///
/// Why this is its own class: oil "life remaining" and oil "degradation" are opposite scales. In the OBDb signal sets the
/// Stellantis / FCA family (Jeep, Chrysler, Dodge, Ram, Fiat, Maserati and the Peugeot / Citroën / DS / Opel-Vauxhall
/// repositories) publishes "Engine oil degradation" (22 3813, percent), a value that grows as the oil wears, while most
/// other makes publish "Oil life remaining" (percent), a value that shrinks. Showing a degradation figure as "oil life
/// remaining" would tell the owner the opposite of what the module reports, so the two are different kinds here.
/// </summary>
public static class ServiceMetricClassifier
{
    public static ServiceMetricKind Classify(string? name, string? parameterId)
    {
        var text = $"{name} {parameterId}".ToLowerInvariant();

        // "Remaining" life first: a name that says life remaining is a remaining-life figure whatever else it says.
        if (text.Contains("oil life"))
            return ServiceMetricKind.OilLifePercent;

        // The reverse scale (0 = new oil, higher = more worn). Never treated as remaining life.
        if (text.Contains("oil degradation"))
            return ServiceMetricKind.OilDegradationPercent;

        if (text.Contains("oil level") || text.Contains("oil fill level"))
            return ServiceMetricKind.OilLevel;
        if (text.Contains("distance to next service") || text.Contains("service distance") || text.Contains("distance until service"))
            return ServiceMetricKind.DistanceToNextService;
        if (text.Contains("days to next service") || text.Contains("service days") || text.Contains("days until service"))
            return ServiceMetricKind.DaysToNextService;

        // Distance still to go until the next oil change (OBDb: Mazda, Ford, Renault and others, kilometres).
        // "Max distance to oil change" is the interval and is handled below, so it is not matched here.
        if (text.Contains("distance to next oil change") ||
            text.Contains("distance before next oil change") ||
            text.Contains("estimated distance to oil change"))
            return ServiceMetricKind.DistanceToNextService;

        // Distance already driven since the last oil change. The "under severe operating conditions" counter is a
        // different counter and keeps its own, unclassified entry.
        if (text.Contains("distance since last oil change") ||
            text.Contains("distance since oil changed") ||
            text.Contains("distance since oil change") ||
            text.Contains("mileage since last oil change"))
            return ServiceMetricKind.DistanceSinceOilChange;

        if (text.Contains("max distance to oil change") || text.Contains("oil change interval"))
            return ServiceMetricKind.OilChangeInterval;
        if (text.Contains("inspection") && text.Contains("distance"))
            return ServiceMetricKind.InspectionDistance;
        if (text.Contains("inspection") && text.Contains("day"))
            return ServiceMetricKind.InspectionDays;
        if (IsServiceMaintenance(text))
            return ServiceMetricKind.MaintenanceCounter;
        if (text.Contains("service status") || text.Contains("maintenance status"))
            return ServiceMetricKind.ServiceStatus;
        return ServiceMetricKind.Other;
    }

    public static bool IsServiceRelevantName(string? name)
    {
        var text = (name ?? string.Empty).ToLowerInvariant();
        return text.Contains("oil life") ||
               text.Contains("oil degradation") ||
               text.Contains("oil level") ||
               text.Contains("oil change") ||
               text.Contains("next service") ||
               text.Contains("service interval") ||
               IsServiceMaintenance(text) ||
               (text.Contains("inspection") &&
                (text.Contains("distance") || text.Contains("day")));
    }

    // "maintenance" is also part of names that have nothing to do with a service schedule: the 12 V "voltage maintenance device"
    // (Peugeot, Jeep) and the electronic parking brake's maintenance state (Volvo). They are not service reminders.
    private static bool IsServiceMaintenance(string text) =>
        text.Contains("maintenance") &&
        !text.Contains("voltage maintenance") &&
        !text.Contains("parking brake");

    /// <summary>Plausible range of a decoded value of this kind (an implausible value is dropped, never shown).</summary>
    public static bool IsPlausible(ServiceMetricKind kind, string unit, double value)
    {
        if (!double.IsFinite(value))
            return false;

        return kind switch
        {
            ServiceMetricKind.OilLifePercent => value is >= 0 and <= 100,
            ServiceMetricKind.OilDegradationPercent => value is >= 0 and <= 100,
            ServiceMetricKind.DistanceToNextService => value is >= 0 and <= 300000,
            ServiceMetricKind.DaysToNextService => value is >= 0 and <= 5000,
            ServiceMetricKind.DistanceSinceOilChange => value is >= 0 and <= 1500000,
            ServiceMetricKind.OilChangeInterval => value is >= 0 and <= 1500000,
            ServiceMetricKind.MaintenanceCounter => value is >= 0 and <= 1000000,
            ServiceMetricKind.OilLevel when unit.Equals("percent", StringComparison.OrdinalIgnoreCase) => value is >= 0 and <= 100,
            _ => true
        };
    }
}
