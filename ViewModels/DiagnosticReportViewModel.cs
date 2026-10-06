using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarScanner.ViewModels;

/// <summary>
/// Presents reports that were persisted by a completed generic OBD scan.
/// It never reconstructs unavailable values: an older or incomplete report
/// is shown as such instead of being filled with demo data.
/// </summary>
public partial class DiagnosticReportViewModel : BaseViewModel
{
    private const int MaxSessions = 30;

    // FreezeFrame.Values is a read-only dictionary: it has to be filled in place.
    internal static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    private static readonly Color Neutral = Color.FromArgb("#94A3B8");
    private static readonly Color Good = Color.FromArgb("#16A34A");
    private static readonly Color Warning = Color.FromArgb("#F59E0B");
    private static readonly Color Bad = Color.FromArgb("#EF4444");

    private readonly AppDatabase _database;
    private readonly ObdReportService _reports;
    private readonly LocalizationService _localization;
    private readonly QrReportSession _qrSession;
    private ObdScanReport? _report;
    private int? _selectedSessionId;

    public ObservableCollection<ScanSession> Sessions { get; } = new();

    [ObservableProperty] private ScanSession? selectedSession;
    [ObservableProperty] private string reportState = "—";
    [ObservableProperty] private Color reportStateColor = Neutral;
    [ObservableProperty] private Brush reportStateBrush = new SolidColorBrush(Neutral);
    [ObservableProperty] private string reportStateDetail = "—";
    [ObservableProperty] private string vehicleDetails = "—";
    [ObservableProperty] private string scanSummary = "—";
    [ObservableProperty] private string ecuSummary = "—";
    [ObservableProperty] private string dtcSummary = "—";
    [ObservableProperty] private string emissionsSummary = "—";
    [ObservableProperty] private string freezeFrameSummary = "—";
    [ObservableProperty] private string comparisonSummary = "—";
    [ObservableProperty] private bool hasReport;
    [ObservableProperty] private bool isExporting;

    public DiagnosticReportViewModel(AppDatabase database, ObdReportService reports, LocalizationService localization, QrReportSession qrSession)
    {
        _database = database;
        _reports = reports;
        _localization = localization;
        _qrSession = qrSession;
        Title = "Diagnostic Report";
        ShowNoScan();
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    [RelayCommand]
    private async Task Load()
    {
        try
        {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var saved = await _database.GetScanSessionsAsync();
            Sessions.Clear();
            foreach (var session in saved.OrderByDescending(item => item.StartedAt).Take(MaxSessions))
                Sessions.Add(session);

            if (Sessions.Count == 0)
            {
                ShowNoScan();
                return;
            }

            // Keep the report the user opened (e.g. after sharing), otherwise the newest one.
            var keep = _selectedSessionId is int id ? Sessions.FirstOrDefault(item => item.Id == id) : null;
            SelectSession(keep ?? Sessions[0]);
        }
        catch (Exception ex)
        {
            _report = null;
            HasReport = false;
            SetState(L("შეცდომა", "Error"), Bad, L("შენახული ანგარიშების წაკითხვა ვერ შესრულდა. სცადე თავიდან.", "Saved reports could not be read. Try again."));
            ClearDetails();
            System.Diagnostics.Debug.WriteLine($"Diagnostic report load failed: {ex.Message}");
        }
        finally { IsBusy = false; }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "DiagnosticReportViewModel.Load");
        }
    }

    [RelayCommand]
    private void SelectSession(ScanSession? session)
    {
        if (session is null) return;
        SelectedSession = session;
        _selectedSessionId = session.Id;
        _report = TryDeserialize(session.ReportJson);
        HasReport = _report is not null;

        if (_report is null)
        {
            SetState(
                L("ძველი სკანი", "Older scan"),
                Warning,
                L("ეს სკანი სრულ ანგარიშს არ შეიცავს. გაუშვი ახალი სრული სკანი.", "This scan has no full report. Run a new full scan."));
            VehicleDetails = Known(session.VehicleSummary);
            ScanSummary = $"{L("თარიღი", "Date")}: {session.StartedAt:dd.MM.yyyy HH:mm}";
            EcuSummary = string.Empty;
            DtcSummary = $"{L("შენახული", "Stored")}: {session.StoredDtcCount} · {L("მოლოდინში", "Pending")}: {session.PendingDtcCount} · {L("მუდმივი", "Permanent")}: {session.PermanentDtcCount}";
            EmissionsSummary = session.MilOn ? L("Check Engine: ჩართულია", "Check Engine: ON") : L("Check Engine: გამორთულია ან უცნობია", "Check Engine: OFF or unknown");
            FreezeFrameSummary = "—";
            ComparisonSummary = L("ამ სკანზე შედარება შეუძლებელია.", "This scan can't be compared.");
            return;
        }

        PopulateReport(_report);
    }

    [RelayCommand]
    private void CompareWithPrevious()
    {
        if (_report is null || SelectedSession is null)
        {
            ComparisonSummary = L("ჯერ აირჩიე სრული შენახული ანგარიში.", "Select a full saved report first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSession.VIN))
        {
            ComparisonSummary = L("VIN უცნობია, ამიტომ სკანები ვერ შედარდება.", "The VIN is unknown, so scans can't be compared.");
            return;
        }

        var previous = Sessions
            .Where(item => item.Id != SelectedSession.Id &&
                           string.Equals(item.VIN, SelectedSession.VIN, StringComparison.OrdinalIgnoreCase) &&
                           item.StartedAt < SelectedSession.StartedAt)
            .OrderByDescending(item => item.StartedAt)
            .Select(item => (Session: item, Report: TryDeserialize(item.ReportJson)))
            .FirstOrDefault(item => item.Report is not null);

        if (previous.Report is null)
        {
            ComparisonSummary = L("ამ მანქანის წინა სრული სკანი ვერ მოიძებნა.", "No earlier full scan of this car was found.");
            return;
        }

        var before = previous.Report;
        var beforeCodes = AllCodes(before).Select(code => code.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterCodes = AllCodes(_report).Select(code => code.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        beforeCodes.UnionWith(before.Modules.SelectMany(module => module.Codes).Select(code => code.Code));
        afterCodes.UnionWith(_report.Modules.SelectMany(module => module.Codes).Select(code => code.Code));
        var fixedCodes = beforeCodes.Except(afterCodes).ToArray();
        var newCodes = afterCodes.Except(beforeCodes).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine($"{L("წინა სკანი", "Previous scan")}: {previous.Session.StartedAt:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"{L("ეს სკანი", "This scan")}: {SelectedSession.StartedAt:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"{L("შეცდომები", "Codes")}: {beforeCodes.Count} → {afterCodes.Count}");
        sb.AppendLine($"Check Engine: {FormatMil(before)} → {FormatMil(_report)}");
        if (fixedCodes.Length > 0)
            sb.AppendLine($"{L("გაქრა", "Cleared")}: {string.Join(", ", fixedCodes)}");
        if (newCodes.Length > 0)
            sb.AppendLine($"{L("ახალი", "New")}: {string.Join(", ", newCodes)}");
        if (fixedCodes.Length == 0 && newCodes.Length == 0)
            sb.AppendLine(L("შეცდომების სია არ შეცვლილა.", "The list of codes did not change."));
        ComparisonSummary = sb.ToString().TrimEnd();
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        try
        {
        if (_report is null || IsExporting) return;
        try
        {
            IsExporting = true;
            var path = await _reports.ExportPdfAsync(_report);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "GCar360 — " + L("დიაგნოსტიკური ანგარიში", "Diagnostic report"),
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            ReportStateDetail = L("PDF-ის შექმნა ვერ მოხერხდა. სცადე თავიდან.", "The PDF could not be created. Try again.");
            System.Diagnostics.Debug.WriteLine($"PDF export failed: {ex}");
        }
        finally
        {
            IsExporting = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "DiagnosticReportViewModel.ExportPdf");
        }
    }

    [RelayCommand]
    private async Task ShareText()
    {
        try
        {
        if (_report is null) return;
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "GCar360 — " + L("დიაგნოსტიკური ანგარიში", "Diagnostic report"),
                Text = _reports.BuildText(_report)
            });
        }
        catch (Exception ex)
        {
            ReportStateDetail = L("გაზიარება ვერ მოხერხდა.", "Sharing failed.");
            System.Diagnostics.Debug.WriteLine($"Share failed: {ex}");
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "DiagnosticReportViewModel.ShareText");
        }
    }

    [RelayCommand]
    private async Task CreateQrReport()
    {
        try
        {
        if (_report is null || SelectedSession is null)
            return;

        var vehicle = _report.Vehicle;
        var display = new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
            .Where(value => !IsUnknown(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        _qrSession.Set(new QrReportDraft
        {
            Vehicle = string.Join(" ", display),
            Vin = vehicle.VIN ?? string.Empty,
            CheckedAt = _report.FinishedAt,
            Status = ReportState,
            Summary = ScanSummary + (string.IsNullOrWhiteSpace(EcuSummary) ? string.Empty : "\n" + EcuSummary),
            DtcSummary = DtcSummary,
            ReadinessSummary = EmissionsSummary,
            ServiceHistory = BuildServiceHistoryText(vehicle)
        });
        await Shell.Current.GoToAsync(nameof(Views.QrReportPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "DiagnosticReportViewModel.CreateQrReport");
        }
    }

    [RelayCommand]
    private Task StartNewScan() => Shell.Current.GoToAsync(nameof(Views.ModuleScanPage));

    /// <summary>Owner-entered service history for the QR report (one line per item).</summary>
    private string BuildServiceHistoryText(VehicleInfo vehicle)
    {
        var english = _localization.IsEnglish;
        var lines = CarScanner.Services.Maintenance.MaintenanceTrackerService.HistoryFor(vehicle)
            .Select(item =>
            {
                var last = item.Records[0];
                var when = string.Join(" · ", new[]
                {
                    last.DateUtc?.ToLocalTime().ToString("MM.yyyy"),
                    last.Km is { } km ? MaintenanceText.Km(km) : null
                }.Where(text => !string.IsNullOrWhiteSpace(text)));
                var state = item.Status.State switch
                {
                    CarScanner.Services.Maintenance.PartState.Overdue => L(" (ვადა გავიდა)", " (overdue)"),
                    CarScanner.Services.Maintenance.PartState.Soon => L(" (მალე)", " (due soon)"),
                    _ => string.Empty
                };
                return $"{(english ? item.Part.NameEn : item.Part.NameKa)}: {when}{state}";
            });
        return string.Join("\n", lines);
    }

    private void PopulateReport(ObdScanReport report)
    {
        var codes = AllCodes(report).ToArray();
        var moduleCodeCount = ModuleCodes(report).Count();
        if (!report.EcuResponded)
        {
            SetState(L("მანქანამ არ უპასუხა", "The car did not answer"), Warning,
                L("სკანი შენახულია, მაგრამ მანქანის კომპიუტერმა (ECU) არ უპასუხა. ჩართე ანთება და გაიმეორე სკანი.",
                  "The scan was saved, but the car's computer (ECU) did not answer. Turn the ignition on and scan again."));
        }
        else if (codes.Length + moduleCodeCount > 0)
        {
            var total = codes.Length + moduleCodeCount;
            SetState(L($"ნაპოვნია {total} შეცდომა", $"{total} fault code(s) found"), Bad,
                $"{L("სკანი", "Scan")}: {report.FinishedAt:dd.MM.yyyy HH:mm}");
        }
        else if (report.DtcServicesRead == 0)
        {
            SetState(L("შეცდომები ვერ წაიკითხა", "Codes could not be read"), Warning,
                $"{L("სკანი", "Scan")}: {report.FinishedAt:dd.MM.yyyy HH:mm}");
        }
        else
        {
            SetState(L("შეცდომები არ არის", "No fault codes"), Good,
                $"{L("სკანი", "Scan")}: {report.FinishedAt:dd.MM.yyyy HH:mm}");
        }

        var vehicle = report.Vehicle;
        var vb = new StringBuilder();
        vb.AppendLine($"{Known(vehicle.Manufacturer)} {Known(vehicle.Model)} · {Known(vehicle.Year)}");
        vb.AppendLine($"VIN: {Known(vehicle.VIN)}");
        if (!IsUnknown(vehicle.Engine) || !IsUnknown(vehicle.FuelType))
            vb.AppendLine($"{L("ძრავი", "Engine")}: {Known(vehicle.Engine)} · {L("საწვავი", "Fuel")}: {Known(vehicle.FuelType)}");
        if (!string.IsNullOrWhiteSpace(report.OdometerKm))
            vb.AppendLine($"{L("გარბენი", "Mileage")}: {report.OdometerKm} km");
        VehicleDetails = vb.ToString().TrimEnd();

        var sb = new StringBuilder();
        sb.AppendLine($"{L("თარიღი", "Date")}: {report.StartedAt:dd.MM.yyyy HH:mm} – {report.FinishedAt:HH:mm}");
        if (!IsUnknown(report.AdapterVoltage))
            sb.AppendLine($"{L("აკუმულატორი", "Battery")}: {report.AdapterVoltage}");
        if (!IsUnknown(report.Protocol))
            sb.AppendLine($"{L("პროტოკოლი", "Protocol")}: {report.Protocol}");
        ScanSummary = sb.ToString().TrimEnd();

        EcuSummary = report.EcuResponded
            ? L($"წაკითხულია {report.Sensors.Count} სენსორი", $"{report.Sensors.Count} sensors read")
            : L("მანქანის კომპიუტერმა არ უპასუხა.", "The car's computer did not answer.");

        DtcSummary = BuildDtcSummary(report, codes);
        EmissionsSummary = BuildReadinessSummary(report);
        FreezeFrameSummary = BuildFreezeFrameSummary(report);
        ComparisonSummary = L("დააჭირე „წინასთან შედარება“, რომ ნახო რა შეიცვალა წინა სკანის შემდეგ.",
                              "Press “Compare with previous” to see what changed since the last scan.");
    }

    private IEnumerable<(ModuleDtcReport Module, ModuleDtc Code)> ModuleCodes(ObdScanReport report)
    {
        var generic = AllCodes(report).Select(code => code.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return report.Modules
            .Where(module => module.Responded)
            .SelectMany(module => module.Codes.Select(code => (module, code)))
            .Where(item => !(item.module.ModuleId == "ECM" && generic.Contains(item.code.Code)));
    }

    private string BuildModuleSummary(ObdScanReport report)
    {
        var responded = report.Modules.Where(module => module.Responded).ToList();
        if (responded.Count == 0)
            return string.IsNullOrWhiteSpace(report.ModuleScanNote) ? string.Empty : report.ModuleScanNote;

        var sb = new StringBuilder();
        sb.AppendLine(L("მოდულები:", "Modules:"));
        foreach (var module in responded)
        {
            var name = _localization.IsEnglish ? module.NameEn : module.NameKa;
            sb.AppendLine(module.Codes.Count == 0
                ? $"{name}"
                : $"{name} — {module.Codes.Count}");
        }

        foreach (var (module, code) in ModuleCodes(report).Take(40))
        {
            var name = _localization.IsEnglish ? module.NameEn : module.NameKa;
            var title = _localization.IsEnglish
                ? (string.IsNullOrWhiteSpace(code.TitleEn) ? code.TitleKa : code.TitleEn)
                : (string.IsNullOrWhiteSpace(code.TitleKa) ? code.TitleEn : code.TitleKa);
            sb.AppendLine($"• {code.DisplayCode} ({name}){(string.IsNullOrWhiteSpace(title) ? string.Empty : " — " + title)}");
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildDtcSummary(ObdScanReport report, DiagnosticCode[] codes)
    {
        var generic = BuildGenericDtcSummary(report, codes);
        var modules = BuildModuleSummary(report);
        return string.IsNullOrWhiteSpace(modules) ? generic : generic + "\n\n" + modules;
    }

    private string BuildGenericDtcSummary(ObdScanReport report, DiagnosticCode[] codes)
    {
        if (report.DtcServicesRead == 0 && codes.Length == 0)
            return L("შეცდომის კოდები ვერ წაიკითხა. ეს არ ნიშნავს, რომ შეცდომა არ არის.",
                     "Fault codes could not be read. This does not mean there are none.");

        if (codes.Length == 0)
            return L("მანქანის კომპიუტერში შეცდომის კოდი არ არის.", "No fault codes stored in the car's computer.");

        var sb = new StringBuilder();
        foreach (var code in codes.Take(40))
        {
            var description = string.IsNullOrWhiteSpace(code.Description) ? string.Empty : $" — {code.Description}";
            sb.AppendLine($"• {code.Code} ({StatusName(code.Status)}){description}");
        }

        if (codes.Length > 40)
            sb.AppendLine(L($"… და კიდევ {codes.Length - 40}", $"… and {codes.Length - 40} more"));
        return sb.ToString().TrimEnd();
    }

    private string BuildReadinessSummary(ObdScanReport report)
    {
        if (report.Readiness is null)
            return L("მონაცემი მიუწვდომელია.", "Data unavailable.");

        return $"Check Engine: {FormatMil(report)}\n" +
               $"{L("ECU-ში შენახული კოდები", "Codes stored in the ECU")}: {report.Readiness.StoredDtcCount}";
    }

    private string BuildFreezeFrameSummary(ObdScanReport report)
    {
        if (report.FreezeFrames.Count == 0)
            return L("შენახული Freeze Frame არ არის.", "No stored Freeze Frame.");

        var sb = new StringBuilder();
        foreach (var frame in report.FreezeFrames)
        {
            sb.AppendLine($"{L("კოდი", "Code")}: {Known(frame.TriggerCode)}");
            foreach (var value in frame.Values.Take(15))
                sb.AppendLine($"  {value.Key}: {value.Value}");
        }

        return sb.ToString().TrimEnd();
    }

    private void ShowNoScan()
    {
        SelectedSession = null;
        _report = null;
        HasReport = false;
        SetState(
            L("ანგარიში ჯერ არ არის", "No report yet"),
            Neutral,
            L("დააჭირე „ახალი სრული სკანი“ — სკანის დასრულების შემდეგ ანგარიში აქ ავტომატურად გამოჩნდება.",
              "Press “New full scan” — the report appears here automatically when the scan finishes."));
        ClearDetails();
    }

    private void SetState(string state, Color color, string detail)
    {
        ReportState = state;
        ReportStateColor = color;
        ReportStateBrush = new SolidColorBrush(color);
        ReportStateDetail = detail;
    }

    private void ClearDetails()
    {
        VehicleDetails = "—";
        ScanSummary = "—";
        EcuSummary = string.Empty;
        DtcSummary = "—";
        EmissionsSummary = "—";
        FreezeFrameSummary = "—";
        ComparisonSummary = "—";
    }

    private string StatusName(DiagnosticCodeStatus status) => status switch
    {
        DiagnosticCodeStatus.Pending => L("მოლოდინში", "pending"),
        DiagnosticCodeStatus.Permanent => L("მუდმივი", "permanent"),
        _ => L("შენახული", "stored")
    };

    internal static ObdScanReport? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ObdScanReport>(json, ReportJsonOptions); }
        catch { return null; }
    }

    private static IEnumerable<DiagnosticCode> AllCodes(ObdScanReport report) =>
        report.StoredCodes.Concat(report.PendingCodes).Concat(report.PermanentCodes);

    private string FormatMil(ObdScanReport report) => report.Readiness is null
        ? L("უცნობია", "unknown")
        : report.Readiness.MilOn ? L("ჩართულია", "ON") : L("გამორთულია", "OFF");

    private static bool IsUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);

    private string Known(string? value) => IsUnknown(value) ? L("უცნობია", "unknown") : value!;
}
