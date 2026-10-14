using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>A check shown on the report page (done or still missing).</summary>
public sealed partial class ReportSectionRow : ObservableObject
{
    public string Kind { get; init; } = string.Empty;
    /// <summary>Icon kind (Controls/IconCatalog) that shows the status of the check.</summary>
    public string IconKind { get; init; } = "circle";
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public Color IconColor { get; init; } = Colors.Gray;
    public Color IconBackground { get; init; } = Colors.LightGray;
    public string DetailText { get; init; } = string.Empty;
    public bool HasDetail => !string.IsNullOrWhiteSpace(DetailText);
    [ObservableProperty] private bool isExpanded;
}

/// <summary>An earlier report in the history list.</summary>
public sealed record ReportHistoryRow(string Title, string Detail, VehicleReport Report);

/// <summary>
/// The combined report of the connected car: overall score, every check done
/// so far, the checks still missing (one tap opens them) and one export.
/// </summary>
public partial class VehicleReportViewModel : BaseViewModel
{
    private static Color Green => CarScanner.Services.Localization.ThemeService.Soft("#1E8A4C");
    private static Color GreenBg => CarScanner.Services.Localization.ThemeService.Soft("#E2F3E9");
    private static Color Orange => CarScanner.Services.Localization.ThemeService.Soft("#D0701F");
    private static Color OrangeBg => CarScanner.Services.Localization.ThemeService.Soft("#FCEBDC");
    private static Color Red => CarScanner.Services.Localization.ThemeService.Soft("#C2372B");
    private static Color RedBg => CarScanner.Services.Localization.ThemeService.Soft("#FBE6E3");
    private static Color Grey => CarScanner.Services.Localization.ThemeService.Soft("#8A9099");
    private static Color GreyBg => CarScanner.Services.Localization.ThemeService.Soft("#ECEFF3");

    private readonly VehicleReportService _reports;
    private readonly PdfReportService _pdf = new();
    private readonly VehicleInfoService _vehicle;
    private readonly QrReportSession _qrSession;
    private readonly LocalizationService _localization;

    public ObservableCollection<ReportSectionRow> DoneSections { get; } = new();
    public ObservableCollection<ReportSectionRow> MissingSections { get; } = new();
    public ObservableCollection<ReportHistoryRow> HistoryRows { get; } = new();

    [ObservableProperty] private string vehicleText = string.Empty;
    [ObservableProperty] private string scoreText = "—";
    [ObservableProperty] private Color scoreColor = Grey;
    [ObservableProperty] private Color scoreBackground = GreyBg;
    [ObservableProperty] private string verdictText = string.Empty;
    [ObservableProperty] private string overviewText = string.Empty;
    [ObservableProperty] private string doneCaption = string.Empty;
    [ObservableProperty] private bool hasSections;
    [ObservableProperty] private bool isEmpty = true;
    [ObservableProperty] private bool hasMissing;
    [ObservableProperty] private bool hasHistory;
    [ObservableProperty] private bool isHistoryOpen;

    public bool ShowNoHistory => IsHistoryOpen && !HasHistory;

    partial void OnIsHistoryOpenChanged(bool value) => OnPropertyChanged(nameof(ShowNoHistory));

    partial void OnHasHistoryChanged(bool value) => OnPropertyChanged(nameof(ShowNoHistory));

    public VehicleReportViewModel(
        VehicleReportService reports,
        VehicleInfoService vehicle,
        QrReportSession qrSession,
        LocalizationService localization)
    {
        _reports = reports;
        _vehicle = vehicle;
        _qrSession = qrSession;
        _localization = localization;
        Title = "Report";
        _reports.ReportChanged += (_, _) => MainThread.BeginInvokeOnMainThread(Refresh);
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(Refresh);
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    public void Refresh()
    {
        RefreshExportTexts();
        var report = _reports.Current;
        VehicleText = $"{report.Vehicle}{(report.Vin.Length == 17 ? " · VIN …" + report.Vin[^6..] : string.Empty)} · {report.StartedAt:dd.MM.yyyy HH:mm}";

        DoneSections.Clear();
        foreach (var section in report.Sections.OrderBy(s => s.At))
        {
            var (color, background) = StatusColors(section.Status);
            DoneSections.Add(new ReportSectionRow
            {
                Kind = section.Kind,
                IconKind = StatusIcon(section.Status),
                Title = section.Title,
                Summary = section.Summary,
                TimeText = section.At.ToString("HH:mm"),
                IconColor = color,
                IconBackground = background,
                DetailText = string.Join("\n", section.Lines.Select(line =>
                    string.IsNullOrWhiteSpace(line.Label) ? line.Value : $"{line.Label}: {line.Value}"))
            });
        }

        MissingSections.Clear();
        foreach (var (kind, ka, en) in AllChecks)
        {
            if (report.Sections.Any(s => s.Kind == kind))
                continue;
            MissingSections.Add(new ReportSectionRow { Kind = kind, IconKind = "circle", Title = L(ka, en), IconColor = Grey, IconBackground = GreyBg });
        }

        HasSections = report.Sections.Count > 0;
        IsEmpty = !HasSections;
        HasMissing = MissingSections.Count > 0;
        DoneCaption = L($"შესრულებული შემოწმებები ({report.Sections.Count})", $"Checks done ({report.Sections.Count})");

        if (HasSections)
        {
            var score = VehicleReportService.Score(report);
            ScoreText = score.ToString();
            (ScoreColor, ScoreBackground) = score >= 85 ? (Green, GreenBg) : score >= 60 ? (Orange, OrangeBg) : (Red, RedBg);
            VerdictText = _reports.Verdict(score);
            OverviewText = string.Join(" · ", report.Sections.OrderBy(s => s.At).Select(s => s.Summary.Split(" · ")[0]).Take(4));
        }
        else
        {
            ScoreText = "—";
            (ScoreColor, ScoreBackground) = (Grey, GreyBg);
            VerdictText = L("ჯერ არაფერი შემოწმებულა", "Nothing checked yet");
            OverviewText = L("ყოველი შემოწმება აქ ავტომატურად დაემატება.", "Every check is added here automatically.");
        }

        HistoryRows.Clear();
        foreach (var old in _reports.History().Where(r => r.Vin == report.Vin || string.IsNullOrEmpty(report.Vin)).Take(10))
            HistoryRows.Add(new ReportHistoryRow($"{old.StartedAt:dd.MM.yyyy HH:mm} · {old.Vehicle}", L($"{old.Sections.Count} შემოწმება · {VehicleReportService.Score(old)}/100", $"{old.Sections.Count} checks · {VehicleReportService.Score(old)}/100"), old));
        HasHistory = HistoryRows.Count > 0;
    }

    private static (Color, Color) StatusColors(ReportStatus status) => status switch
    {
        ReportStatus.Ok => (Green, GreenBg),
        ReportStatus.Warning => (Orange, OrangeBg),
        ReportStatus.Bad => (Red, RedBg),
        _ => (CarScanner.Services.Localization.ThemeService.Soft("#1D5BD6"), CarScanner.Services.Localization.ThemeService.Soft("#E4ECFB"))
    };

    private static string StatusIcon(ReportStatus status) => status switch
    {
        ReportStatus.Ok => "check",
        ReportStatus.Warning or ReportStatus.Bad => "warning",
        _ => "info"
    };

    private static readonly (string Kind, string Ka, string En)[] AllChecks =
    {
        ("scan", "სრული სკანი", "Full scan"),
        ("battery", "ჰიბრიდის / EV ბატარეა", "Hybrid / EV battery"),
        ("mileage", "რეალური გარბენი", "Real mileage"),
        ("inspection", "ტექდათვალიერება (OBD)", "Technical inspection (OBD)"),
        ("prepurchase", "ყიდვისწინა შემოწმება", "Pre-purchase check"),
        ("ai", "AI დასკვნა", "AI conclusion"),
        ("trip", "მოგზაურობა და ხარჯი", "Trip and consumption")
    };

    [RelayCommand]
    private void ToggleSection(ReportSectionRow? row)
    {
        if (row is not null && row.HasDetail)
            row.IsExpanded = !row.IsExpanded;
    }

    [RelayCommand]
    private async Task OpenCheck(ReportSectionRow? row)
    {
        if (row is null) return;
        var route = row.Kind switch
        {
            "scan" => nameof(Views.ModuleScanPage),
            "battery" => nameof(Views.HybridHealthPage),
            "mileage" => nameof(Views.MileagePage),
            "inspection" => nameof(Views.TechnicalInspectionPage),
            "prepurchase" => nameof(Views.PrePurchasePage),
            "ai" => nameof(Views.AiPage),
            "trip" => nameof(Views.FuelPage),
            _ => null
        };
        if (route is not null)
            await CarScanner.Services.SafeNavigation.GoToAsync(route);
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        var report = _reports.Current;
        if (report.Sections.Count == 0) return;
        if (!await ProGate.EnsureOrOpenAsync()) return;
        try
        {
            IsBusy = true;
            var path = await _pdf.ExportCombinedAsync(report, _reports.LastScanReport, _vehicle.CurrentVehicle);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = L("OBD360 ანგარიში", "OBD360 report"), File = new ShareFile(path) });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShareText()
    {
        try
        {
            var report = _reports.Current;
            if (report.Sections.Count == 0) return;
            if (!await ProGate.EnsureOrOpenAsync()) return;
            await Share.Default.RequestAsync(new ShareTextRequest { Title = L("OBD360 ანგარიში", "OBD360 report"), Text = _reports.BuildText(report) });
        }
        catch (Exception exception)
        {
            CommandErrors.Report(exception, "VehicleReport.ShareText");
        }
    }

    // Export buttons: the report is open to everyone, exporting is PRO.
    public string PdfButtonText => "PDF" + ProGate.Suffix;
    public string QrButtonText => "QR" + ProGate.Suffix;
    public string TextButtonText => L("ტექსტი", "Text") + ProGate.Suffix;

    private void RefreshExportTexts()
    {
        OnPropertyChanged(nameof(PdfButtonText));
        OnPropertyChanged(nameof(QrButtonText));
        OnPropertyChanged(nameof(TextButtonText));
    }

    [RelayCommand]
    private async Task CreateQr()
    {
        var report = _reports.Current;
        if (report.Sections.Count == 0) return;
        if (!await ProGate.EnsureOrOpenAsync()) return;

        static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
        static string Lines(IEnumerable<ReportSectionItem> sections) => string.Join("\n\n", sections.Select(section =>
            $"{section.Title} — {section.Summary}\n" + string.Join("\n", section.Lines.Select(line =>
                string.IsNullOrWhiteSpace(line.Label) ? line.Value : $"{line.Label}: {line.Value}"))));

        var score = VehicleReportService.Score(report);
        var ordered = report.Sections.OrderBy(s => s.At).ToList();
        _qrSession.Set(new QrReportDraft
        {
            Vehicle = Clip(string.IsNullOrWhiteSpace(report.Vehicle) ? "OBD360" : report.Vehicle, 160),
            Vin = report.Vin,
            CheckedAt = report.UpdatedAt,
            Status = Clip($"{score}/100 · {_reports.Verdict(score)}", 120),
            Summary = Clip(string.Join("\n", ordered.Select(s => $"{s.Title}: {s.Summary}")), 4000),
            DtcSummary = Clip(Lines(ordered.Where(s => s.Kind is "scan" or "repair")), 8000),
            ReadinessSummary = Clip(Lines(ordered.Where(s => s.Kind is not ("scan" or "repair" or "ai"))), 4000),
            ServiceHistory = Clip(Lines(ordered.Where(s => s.Kind == "ai")), 4000)
        });
        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.QrReportPage));
    }

    [RelayCommand]
    private async Task StartNew()
    {
        var ok = await Shell.Current.DisplayAlertLocalizedAsync(
            L("ახალი ანგარიში", "New report"),
            L("მიმდინარე ანგარიში ისტორიაში შეინახება და ახალი, ცარიელი დაიწყება.", "The current report is kept in history and a new, empty one starts."),
            L("დაწყება", "Start"),
            L("გაუქმება", "Cancel"));
        if (!ok) return;
        _reports.StartNew();
        Refresh();
    }

    [RelayCommand]
    private void ToggleHistory() => IsHistoryOpen = !IsHistoryOpen;

    [RelayCommand]
    private Task ShareHistory(ReportHistoryRow? row) =>
        row is null
            ? Task.CompletedTask
            : Share.Default.RequestAsync(new ShareTextRequest { Title = "OBD360", Text = _reports.BuildText(row.Report) });

    [RelayCommand]
    private Task OpenScanHistory() => CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.DiagnosticReportPage));
}
