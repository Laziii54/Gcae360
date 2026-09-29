using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// Produces a preliminary OBD-only technical-inspection assessment. It never
/// claims to evaluate the mechanical and visual checks performed by an official center.
/// </summary>
public sealed class TechnicalInspectionService
{
    private readonly EmissionService _emissions;
    private readonly DiagnosticService _diagnostics;

    public TechnicalInspectionService(EmissionService emissions, DiagnosticService diagnostics)
    {
        _emissions = emissions;
        _diagnostics = diagnostics;
    }

    public async Task<TechnicalInspectionResult> CheckAsync(CancellationToken token = default)
    {
        var report = await _emissions.ReadAsync(token).ConfigureAwait(false);
        var readiness = report.SinceClear;

        if (readiness is null)
        {
            return new TechnicalInspectionResult
            {
                Verdict = TechnicalInspectionVerdict.Unavailable,
                Issues = new[] { UnavailableIssue() }
            };
        }

        var issues = new List<TechnicalInspectionIssue>();
        var supported = report.SinceClearMonitors.Where(x => x.Supported == true).ToList();
        var incomplete = supported.Where(x => x.Complete == false).ToList();

        if (readiness.MilOn)
            issues.Add(MilIssue());

        foreach (var monitor in incomplete)
            issues.Add(IncompleteMonitorIssue(monitor.Name));

        List<ErrorCode> codes;
        try
        {
            codes = await _diagnostics.ScanForErrorsAsync(token).ConfigureAwait(false);
        }
        catch
        {
            codes = new List<ErrorCode>();
        }

        foreach (var code in codes)
            issues.Add(DtcIssue(code));

        // PID 01 can report stored codes even if a later Mode 03 request was not
        // available. Keep that real ECU signal visible instead of treating it as clean.
        if (readiness.StoredDtcCount > 0 && codes.Count == 0)
            issues.Add(StoredDtcCountIssue(readiness.StoredDtcCount));

        if (incomplete.Count > 0 &&
            (report.DistanceSinceClearKm is <= 50 || report.WarmUpsSinceClearValue is <= 2))
        {
            issues.Add(RecentlyClearedIssue());
        }

        var dangerous = codes.Any(x => IsDangerousCode(x.Code));
        var fails = readiness.MilOn || readiness.StoredDtcCount > 0 || incomplete.Count > 0 || codes.Count > 0;

        return new TechnicalInspectionResult
        {
            Verdict = dangerous
                ? TechnicalInspectionVerdict.Dangerous
                : fails ? TechnicalInspectionVerdict.Fail : TechnicalInspectionVerdict.Pass,
            Issues = issues,
            SupportedMonitorCount = supported.Count,
            CompleteMonitorCount = supported.Count(x => x.Complete == true),
            MilOn = readiness.MilOn,
            StoredDtcCount = readiness.StoredDtcCount,
            DistanceSinceClearKm = report.DistanceSinceClearKm,
            WarmUpsSinceClear = report.WarmUpsSinceClearValue
        };
    }

    private static bool IsDangerousCode(string code) =>
        code.Equals("P0217", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("P0524", StringComparison.OrdinalIgnoreCase);

    private static TechnicalInspectionIssue MilIssue() => new()
    {
        TitleKa = "Check Engine ნათურა ჩართულია",
        TitleEn = "Check Engine light is on",
        CategoryKa = "ემისია / ძრავი",
        CategoryEn = "Emissions / engine",
        SeverityKa = "მნიშვნელოვანი",
        SeverityEn = "Major",
        ImpactKa = "OBD ნაწილის გავლას სავარაუდოდ ხელს შეუშლის.",
        ImpactEn = "The vehicle is unlikely to pass the OBD portion.",
        CheckKa = "წაიკითხე შენახული, Pending და Permanent კოდები.",
        CheckEn = "Read stored, pending and permanent trouble codes.",
        FixKa = "ჯერ აღმოფხვერი მიზეზი; მხოლოდ კოდის წაშლა შეკეთება არ არის.",
        FixEn = "Repair the cause first; clearing the code alone is not a repair.",
        Color = "#EF4444"
    };

    private static TechnicalInspectionIssue IncompleteMonitorIssue(string monitor)
    {
        var kaName = MonitorNameKa(monitor);
        return new TechnicalInspectionIssue
        {
            TitleKa = $"{kaName} მონიტორი ჯერ არ დასრულებულა",
            TitleEn = $"{monitor} monitor is not complete",
            CategoryKa = "მზადყოფნის მონიტორი",
            CategoryEn = "Readiness monitor",
            SeverityKa = "მნიშვნელოვანი",
            SeverityEn = "Major",
            ImpactKa = "ამ ეტაპზე OBD ნაწილი მზად არ არის.",
            ImpactEn = "The OBD portion is not ready at this time.",
            CheckKa = "იმოძრავე 2–3 დღე სხვადასხვა რეჟიმში და ხელახლა შეამოწმე.",
            CheckEn = "Drive for 2–3 days in varied conditions and check again.",
            FixKa = "თუ მონიტორი კვლავ არ სრულდება, შეამოწმე შესაბამისი სისტემა ხელოსანთან.",
            FixEn = "If it remains incomplete, have the related system diagnosed.",
            Color = "#EF4444"
        };
    }

    private static TechnicalInspectionIssue DtcIssue(ErrorCode error)
    {
        var dangerous = IsDangerousCode(error.Code);
        var isMisfire = error.Code.StartsWith("P030", StringComparison.OrdinalIgnoreCase);
        return new TechnicalInspectionIssue
        {
            Code = error.Code,
            TitleKa = string.IsNullOrWhiteSpace(error.TitleKa) ? $"აღმოჩენილია კოდი {error.Code}" : error.TitleKa,
            TitleEn = string.IsNullOrWhiteSpace(error.TitleEn) ? $"Code {error.Code} detected" : error.TitleEn,
            CategoryKa = isMisfire ? "ძრავი / აალების გამოტოვება" : "დიაგნოსტიკური კოდი",
            CategoryEn = isMisfire ? "Engine / misfire" : "Diagnostic trouble code",
            SeverityKa = dangerous ? "სახიფათო" : "მნიშვნელოვანი",
            SeverityEn = dangerous ? "Dangerous" : "Major",
            ImpactKa = dangerous
                ? "შეაჩერე უსაფრთხო ადგილას — მართვამ შეიძლება მანქანა დააზიანოს ან საფრთხე შექმნას."
                : "აქტიური ან შენახული კოდი OBD ნაწილის გავლას სავარაუდოდ ხელს შეუშლის.",
            ImpactEn = dangerous
                ? "Stop safely — continued driving may cause damage or create a hazard."
                : "An active or stored code is likely to prevent passing the OBD portion.",
            CheckKa = isMisfire
                ? "შეამოწმე სანთელი, კოჭა, ინჟექტორი და კომპრესია."
                : "გახსენი კოდის სრული აღწერა და შეამოწმე მითითებული სისტემა.",
            CheckEn = isMisfire
                ? "Check the spark plug, coil, injector and compression."
                : "Open the full code description and inspect the indicated system.",
            FixKa = "აღმოფხვერი ძირითადი მიზეზი და შემდეგ გაიმეორე readiness ციკლი.",
            FixEn = "Repair the root cause, then complete the readiness drive cycle.",
            Color = "#EF4444"
        };
    }

    private static TechnicalInspectionIssue StoredDtcCountIssue(int count) => new()
    {
        TitleKa = $"ECU მიუთითებს {count} შენახულ შეცდომაზე",
        TitleEn = $"ECU reports {count} stored trouble code(s)",
        CategoryKa = "დიაგნოსტიკური კოდი",
        CategoryEn = "Diagnostic trouble code",
        SeverityKa = "მნიშვნელოვანი",
        SeverityEn = "Major",
        ImpactKa = "OBD ნაწილის გავლას სავარაუდოდ ხელს შეუშლის.",
        ImpactEn = "This is likely to prevent passing the OBD portion.",
        CheckKa = "გაიმეორე სრული DTC სკანირება სტაბილური კავშირისას.",
        CheckEn = "Repeat the full DTC scan with a stable connection.",
        FixKa = "დაადგინე და შეაკეთე თითოეული კოდის მიზეზი.",
        FixEn = "Diagnose and repair the cause of each code.",
        Color = "#EF4444"
    };

    private static TechnicalInspectionIssue RecentlyClearedIssue() => new()
    {
        TitleKa = "ჩანს კოდების ახლახან წაშლის ნიშანი",
        TitleEn = "Codes may have been cleared recently",
        CategoryKa = "ECU ისტორია",
        CategoryEn = "ECU history",
        SeverityKa = "საჭიროა გადამოწმება",
        SeverityEn = "Needs inspection",
        ImpactKa = "დაუსრულებელი მონიტორები ტექდათვალიერებამდე თავიდან უნდა შესრულდეს.",
        ImpactEn = "Incomplete monitors must run again before inspection.",
        CheckKa = "შეადარე წაშლის შემდეგ გავლილი მანძილი და გახურების ციკლები.",
        CheckEn = "Review distance and warm-up cycles since codes were cleared.",
        FixKa = "იმოძრავე რამდენიმე სრული ციკლი და ხელახლა შეამოწმე.",
        FixEn = "Complete several normal drive cycles and check again.",
        Color = "#F59E0B"
    };

    private static TechnicalInspectionIssue UnavailableIssue() => new()
    {
        TitleKa = "ECU-დან readiness მონაცემი ვერ წავიკითხე",
        TitleEn = "Could not read readiness data from the ECU",
        CategoryKa = "OBD კავშირი",
        CategoryEn = "OBD connection",
        SeverityKa = "საჭიროა გადამოწმება",
        SeverityEn = "Needs inspection",
        ImpactKa = "OBD ნაწილის შეფასება შეუძლებელია.",
        ImpactEn = "The OBD portion cannot be assessed.",
        CheckKa = "შეამოწმე ადაპტერი, ანთება და კავშირი, შემდეგ სცადე თავიდან.",
        CheckEn = "Check the adapter, ignition and connection, then retry.",
        FixKa = "აღადგინე სტაბილური OBD კავშირი.",
        FixEn = "Restore a stable OBD connection.",
        Color = "#F59E0B"
    };

    private static string MonitorNameKa(string name) => name switch
    {
        "Misfire" => "აალების გამოტოვების",
        "Fuel System" => "საწვავის სისტემის",
        "Comprehensive Components" => "კომპონენტების საერთო",
        "Catalyst" => "კატალიზატორის",
        "Heated Catalyst" => "გაცხელებადი კატალიზატორის",
        "Evaporative System" => "EVAP",
        "Secondary Air System" => "მეორადი ჰაერის სისტემის",
        "Oxygen Sensor" => "ჟანგბადის სენსორის",
        "Oxygen Sensor Heater" => "ჟანგბადის სენსორის გამათბობლის",
        "EGR / VVT" => "EGR / VVT",
        "NMHC Catalyst" => "NMHC კატალიზატორის",
        "NOx / SCR" => "NOx / SCR",
        "Boost Pressure" => "ბუსტის წნევის",
        "Exhaust Gas Sensor" => "გამონაბოლქვის სენსორის",
        "PM / DPF Filter" => "PM / DPF ფილტრის",
        _ => name
    };
}
