using System.Globalization;
using System.Text;
using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.Localization;

namespace CarScanner.Services.Reports;

/// <summary>ok / warn / bad / info — the colour of a report section.</summary>
public enum ReportStatus
{
    Info,
    Ok,
    Warning,
    Bad
}

public sealed class ReportLineItem
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>One check in the combined report (scan, battery, mileage …).</summary>
public sealed class ReportSectionItem
{
    /// <summary>scan, battery, mileage, inspection, prepurchase, ai, repair, trip.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = "•";
    public ReportStatus Status { get; set; } = ReportStatus.Info;
    public string Summary { get; set; } = string.Empty;
    public List<ReportLineItem> Lines { get; set; } = new();
    public DateTime At { get; set; } = DateTime.Now;
    /// <summary>0–100 when the check gives its own score (pre-purchase).</summary>
    public int? Score { get; set; }
}

/// <summary>All checks done on one car until "Start a new report".</summary>
public sealed class VehicleReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Vin { get; set; } = string.Empty;
    public string Vehicle { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public List<ReportSectionItem> Sections { get; set; } = new();
}

/// <summary>
/// Collects every check of the connected car into one report. A check that
/// is repeated replaces its earlier result; different checks add up. The
/// report is saved per VIN, so it survives closing the app, and is exported
/// as one PDF / text / QR.
/// </summary>
public sealed class VehicleReportService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly VehicleInfoService _vehicle;
    private readonly LocalizationService _localization;
    private readonly object _gate = new();
    private VehicleReport? _current;

    /// <summary>Full OBD report of the last scan in this report (PDF detail pages).</summary>
    public ObdScanReport? LastScanReport { get; private set; }

    public event EventHandler<ReportSectionItem>? SectionAdded;
    public event EventHandler? ReportChanged;

    public VehicleReportService(VehicleInfoService vehicle, LocalizationService localization)
    {
        _vehicle = vehicle;
        _localization = localization;
    }

    /// <summary>Resolved without constructor changes from any view model.</summary>
    public static VehicleReportService? Instance =>
        IPlatformApplication.Current?.Services.GetService<VehicleReportService>();

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    private static string Folder
    {
        get
        {
            var folder = Path.Combine(FileSystem.AppDataDirectory, "vehicle_reports");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private static string KeyFor(string? vin) =>
        string.IsNullOrWhiteSpace(vin) || vin.Length != 17 ? "unknown" : vin.ToUpperInvariant();

    private string CurrentKey() => KeyFor(_vehicle.CurrentVehicle?.VIN);

    private string CurrentVehicleName()
    {
        var car = _vehicle.CurrentVehicle;
        if (car is null) return L("უცნობი მანქანა", "Unknown vehicle");
        var name = string.Join(" ", new[] { car.Manufacturer, car.Model, car.Year }
            .Where(part => !string.IsNullOrWhiteSpace(part) && !part.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));
        return string.IsNullOrWhiteSpace(name) ? L("უცნობი მანქანა", "Unknown vehicle") : name;
    }

    /// <summary>The report of the connected car (loaded from disk, or a new one).</summary>
    public VehicleReport Current
    {
        get
        {
            lock (_gate)
            {
                var key = CurrentKey();
                if (_current is not null && KeyFor(_current.Vin) == key)
                    return _current;

                _current = Load(key) ?? new VehicleReport { Vin = key == "unknown" ? string.Empty : key, Vehicle = CurrentVehicleName() };
                LastScanReport = null;
                return _current;
            }
        }
    }

    public void AddOrReplace(ReportSectionItem section, ObdScanReport? scanReport = null)
    {
        try
        {
            VehicleReport report;
            lock (_gate)
            {
                report = Current;
                report.Sections.RemoveAll(existing => existing.Kind == section.Kind);
                report.Sections.Add(section);
                report.UpdatedAt = DateTime.Now;
                if (string.IsNullOrWhiteSpace(report.Vehicle) || report.Vehicle == L("უცნობი მანქანა", "Unknown vehicle"))
                    report.Vehicle = CurrentVehicleName();
                if (scanReport is not null)
                    LastScanReport = scanReport;
                Save(report);
            }

            SectionAdded?.Invoke(this, section);
            ReportChanged?.Invoke(this, EventArgs.Empty);
            ReportToast.Show(L($"✓ დაემატა ანგარიშს · {report.Sections.Count} შემოწმება", $"✓ Added to the report · {report.Sections.Count} checks"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Report section not saved: {ex.Message}");
        }
    }

    /// <summary>Keeps the current report in history and starts an empty one.</summary>
    public void StartNew()
    {
        lock (_gate)
        {
            var report = Current;
            if (report.Sections.Count > 0)
            {
                var archive = Path.Combine(Folder, $"archive_{KeyFor(report.Vin)}_{report.StartedAt:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(archive, JsonSerializer.Serialize(report, Json));
            }

            _current = new VehicleReport { Vin = report.Vin, Vehicle = CurrentVehicleName() };
            LastScanReport = null;
            Save(_current);
        }

        ReportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Earlier reports (newest first).</summary>
    public IReadOnlyList<VehicleReport> History()
    {
        var list = new List<VehicleReport>();
        try
        {
            foreach (var file in Directory.GetFiles(Folder, "archive_*.json").OrderByDescending(f => f).Take(30))
                if (JsonSerializer.Deserialize<VehicleReport>(File.ReadAllText(file), Json) is { } report)
                    list.Add(report);
        }
        catch
        {
            // Unreadable history: show what could be read.
        }

        return list;
    }

    private static VehicleReport? Load(string key)
    {
        try
        {
            var path = Path.Combine(Folder, $"current_{key}.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<VehicleReport>(File.ReadAllText(path), Json) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(VehicleReport report) =>
        File.WriteAllText(Path.Combine(Folder, $"current_{KeyFor(report.Vin)}.json"), JsonSerializer.Serialize(report, Json));

    // =========================================================
    // OVERALL VERDICT AND TEXT EXPORT
    // =========================================================

    /// <summary>0–100: the pre-purchase score if done, otherwise points off for problems.</summary>
    public static int Score(VehicleReport report)
    {
        if (report.Sections.FirstOrDefault(s => s.Kind == "prepurchase")?.Score is { } own)
            return Math.Clamp(own, 0, 100);

        var score = 100;
        foreach (var section in report.Sections)
            score -= section.Status switch
            {
                ReportStatus.Bad => 25,
                ReportStatus.Warning => 12,
                _ => 0
            };
        return Math.Clamp(score, 0, 100);
    }

    public string Verdict(int score) => score >= 85
        ? L("კარგ მდგომარეობაშია", "In good condition")
        : score >= 60
            ? L("საჭიროებს ყურადღებას", "Needs attention")
            : L("სერიოზული პრობლემებია", "Serious problems");

    public string BuildText(VehicleReport report)
    {
        var sb = new StringBuilder();
        var score = Score(report);
        sb.AppendLine(L("GCar360 — მანქანის ანგარიში", "GCar360 — vehicle report"));
        sb.AppendLine($"{report.Vehicle}{(string.IsNullOrWhiteSpace(report.Vin) ? string.Empty : " · VIN " + report.Vin)}");
        sb.AppendLine($"{report.StartedAt:dd.MM.yyyy HH:mm} – {report.UpdatedAt:HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"{L("შეფასება", "Score")}: {score}/100 · {Verdict(score)}");
        foreach (var (section, index) in report.Sections.OrderBy(s => s.At).Select((s, i) => (s, i + 1)))
        {
            sb.AppendLine();
            sb.AppendLine($"{index}. {section.Title} ({section.At:HH:mm})");
            if (!string.IsNullOrWhiteSpace(section.Summary))
                sb.AppendLine(section.Summary);
            foreach (var line in section.Lines)
                sb.AppendLine(string.IsNullOrWhiteSpace(line.Label) ? $"  {line.Value}" : $"  {line.Label}: {line.Value}");
        }

        return sb.ToString();
    }

    public static string Format(double value, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
