using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>Pure deterministic score calculation, independent of transport and UI.</summary>
public static class VehicleHealthScoreCalculator
{
    public static int Calculate(
        IEnumerable<VehicleHealthIssue> issues,
        int incompleteReadinessMonitors)
    {
        var materialized = issues.ToArray();
        var dtcIssues = materialized.Where(IsDtcIssue).ToArray();
        var sensorIssues = materialized.Where(IsSensorIssue).ToArray();

        var criticalDtcPenalty = Math.Min(45, dtcIssues
            .Where(issue => issue.Severity == VehicleHealthSeverity.Critical)
            .Sum(issue => issue.Penalty));
        var majorDtcPenalty = Math.Min(32, dtcIssues
            .Where(issue => issue.Severity == VehicleHealthSeverity.Major &&
                            issue.Category != VehicleHealthCategory.Communication)
            .Sum(issue => issue.Penalty));
        var minorDtcPenalty = Math.Min(15, dtcIssues
            .Where(issue => issue.Severity is VehicleHealthSeverity.Minor or VehicleHealthSeverity.Warning)
            .Sum(issue => issue.Penalty));
        var nonCriticalDtcPenalty = Math.Min(35, majorDtcPenalty + minorDtcPenalty);
        var communicationPenalty = Math.Min(10, dtcIssues
            .Where(issue => issue.Category == VehicleHealthCategory.Communication)
            .Sum(issue => issue.Penalty));
        var sensorPenalty = Math.Min(30, sensorIssues.Sum(issue => issue.Penalty));
        var readinessPenalty = Math.Min(8, Math.Max(0, incompleteReadinessMonitors));

        return Math.Clamp(
            100 - criticalDtcPenalty - nonCriticalDtcPenalty - communicationPenalty - sensorPenalty - readinessPenalty,
            0,
            100);
    }

    private static bool IsSensorIssue(VehicleHealthIssue issue) =>
        issue.Code.StartsWith("PID ", StringComparison.OrdinalIgnoreCase) ||
        issue.Code.StartsWith("FUEL-TRIM", StringComparison.OrdinalIgnoreCase);

    private static bool IsDtcIssue(VehicleHealthIssue issue) =>
        issue.Code != "READINESS" && !IsSensorIssue(issue);
}
