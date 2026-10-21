using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>One value that answered during the service search.</summary>
public sealed record ServiceSearchHit(
    string SourceKey,
    string CatalogMake,
    string Name,
    ServiceMetricKind Kind,
    double Value,
    string Unit,
    string Request,
    string Module,
    bool OwnBrand)
{
    /// <summary>Set after the search when the value is close to what the dashboard shows.</summary>
    public bool MatchesDashboard { get; init; }
}

/// <summary>
/// "Enter manually" on the Service page: which answered values are CLOSE to what the person typed from the dashboard. A dashboard and
/// a module rarely show exactly the same number, so oil life counts within 2 points (84 also finds 82 to 86) and a service distance
/// within 2 % (at least 100 km). Pure: no I/O.
/// </summary>
public static class ServiceSearchMatching
{
    /// <summary>Oil life counts as the same when it is within this many percentage points of what the dashboard shows.</summary>
    public const double OilPercentTolerance = 2;

    public static IReadOnlyList<ServiceSearchHit> MatchDashboard(
        IReadOnlyList<ServiceSearchHit> hits, double? dashboardPercent, double? dashboardDistanceKm = null) =>
        hits.Select(hit => hit with { MatchesDashboard = MatchesTyped(hit, dashboardPercent, dashboardDistanceKm) }).ToArray();

    public static bool MatchesTyped(ServiceSearchHit hit, double? dashboardPercent, double? dashboardDistanceKm)
    {
        if (dashboardPercent is { } percent && hit.Kind == ServiceMetricKind.OilLifePercent)
            return Math.Abs(hit.Value - percent) <= OilPercentTolerance;

        if (dashboardDistanceKm is { } km && hit.Kind is ServiceMetricKind.DistanceToNextService or ServiceMetricKind.DistanceSinceOilChange
                or ServiceMetricKind.OilChangeInterval or ServiceMetricKind.InspectionDistance)
        {
            var unit = hit.Unit.Trim().ToLowerInvariant();
            var hitKm = unit is "km" ? hit.Value : unit is "mi" or "mile" or "miles" ? hit.Value * 1.609344 : double.NaN;
            return !double.IsNaN(hitKm) && Math.Abs(hitKm - km) <= Math.Max(100, km * 0.02);
        }

        return false;
    }
}
