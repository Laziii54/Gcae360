using System.Text.RegularExpressions;
using CarScanner.VinCheck.Models;

namespace CarScanner.VinCheck.Services;

/// <summary>
/// Turns the server data into what the report page shows, in the app
/// language: specification names and the "important history" summary
/// (auctions, damage, title, mileage, recalls).
/// </summary>
public static partial class VinReportPresenter
{
    [GeneratedRegex("salvage|rebuilt|rebuildable|junk|parts only|destruction|flood|fire|non-repairable|scrap|lemon",
        RegexOptions.IgnoreCase)]
    private static partial Regex BrandedTitle();

    [GeneratedRegex("not actual|exempt|tmu|exceeds|inoperable|broken|discrepancy|rollback", RegexOptions.IgnoreCase)]
    private static partial Regex BadOdometer();

    public static void Prepare(VinReport report)
    {
        foreach (var spec in report.Specifications)
        {
            if (string.IsNullOrWhiteSpace(spec.Key))
                spec.Key = spec.Label;
            spec.Label = VinText.Spec(spec.Key);
        }

        // Reports from the server (Access is set) get their summary here, in
        // the current language. Older saved reports keep what they have.
        if (report.Access is null)
            return;

        report.Alerts = BuildAlerts(report);
    }

    private static List<HistoryAlert> BuildAlerts(VinReport report)
    {
        var alerts = new List<HistoryAlert>();
        var lots = report.AuctionRecords;

        if (lots.Count > 0)
        {
            alerts.Add(new HistoryAlert
            {
                Label = VinText.T("AlertAuction"),
                Status = "issue",
                Detail = VinText.Format("AlertAuctionFound", lots.Count)
            });

            var damage = lots
                .Select(lot => lot.PrimaryDamage)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text) &&
                                        !text.Contains("normal wear", StringComparison.OrdinalIgnoreCase) &&
                                        !text.Equals("none", StringComparison.OrdinalIgnoreCase));
            alerts.Add(new HistoryAlert
            {
                Label = VinText.T("AlertDamage"),
                Status = damage is null ? "unknown" : "issue",
                Detail = damage ?? VinText.T("NoInformation")
            });
        }
        else if (report.Access is { HistoryState: "ready", HistoryLocked: false })
        {
            alerts.Add(new HistoryAlert
            {
                Label = VinText.T("AlertAuction"),
                Status = "unknown",
                Detail = VinText.T("AlertAuctionNone")
            });
        }

        var titles = lots.Select(lot => lot.TitleStatus)
            .Concat(report.TitleRecords.Select(title => title.Status))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (titles.Count > 0)
        {
            var branded = titles.FirstOrDefault(text => BrandedTitle().IsMatch(text!));
            alerts.Add(new HistoryAlert
            {
                Label = VinText.T("AlertTitle"),
                Status = branded is null ? "clear" : "issue",
                Detail = branded ?? VinText.T("AlertTitleClean")
            });
        }

        var readings = report.MileageRecords
            .Where(record => record.OdometerValue is > 0)
            .OrderBy(record => record.Date ?? DateTime.MinValue)
            .ToList();
        if (readings.Count > 0 || lots.Any(lot => !string.IsNullOrWhiteSpace(lot.OdometerStatus)))
        {
            var inconsistent = false;
            for (var i = 1; i < readings.Count; i++)
            {
                var previous = ToKm(readings[i - 1]);
                var current = ToKm(readings[i]);
                if (previous is not null && current is not null && current < previous * 0.98m)
                {
                    readings[i].IsPossibleInconsistency = true;
                    inconsistent = true;
                }
            }

            var badStatus = lots
                .Select(lot => lot.OdometerStatus)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text) && BadOdometer().IsMatch(text));

            alerts.Add(new HistoryAlert
            {
                Label = VinText.T("AlertMileage"),
                Status = inconsistent || badStatus is not null ? "issue" : "clear",
                Detail = inconsistent
                    ? VinText.T("PossibleMileage")
                    : badStatus ?? VinText.T("AlertMileageOk")
            });
        }

        if (report.RecallRecords.Count > 0)
        {
            alerts.Add(new HistoryAlert
            {
                Label = VinText.T("Recalls"),
                Status = "unknown",
                Detail = VinText.Format("AlertRecalls", report.RecallRecords.Count)
            });
        }

        return alerts;
    }

    private static decimal? ToKm(MileageRecord record) =>
        record.OdometerValue is null
            ? null
            : string.Equals(record.OdometerUnit, "km", StringComparison.OrdinalIgnoreCase)
                ? record.OdometerValue
                : record.OdometerValue * 1.609344m;
}
