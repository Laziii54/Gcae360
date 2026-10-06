using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>Pre-purchase check page (PRO): one button, full read-only check, score and AI consultation.</summary>
public partial class PrePurchaseViewModel : BaseViewModel
{
    private readonly PrePurchaseCheckService _check;
    private readonly ProService _pro;
    private readonly VehicleInfoService _vehicles;
    private readonly OBDService _obd;
    private readonly LocalizationService _localization;
    private readonly PdfReportService _pdf = new();
    private CancellationTokenSource? _cts;
    private PrePurchaseResult? _result;

    // (key, Georgian, English, start %, end %)
    private static readonly (string Key, string Ka, string En, int Start, int End)[] StepPlan =
    {
        ("conn", "კავშირი და VIN", "Connection and VIN", 0, 24),
        ("codes", "ძრავის შეცდომები", "Engine fault codes", 24, 48),
        ("ready", "მზადყოფნა (Readiness)", "Readiness", 48, 58),
        ("modules", "მოდულების სკანირება", "Module scan", 76, 80),
        ("mileage", "გარბენის შედარება", "Mileage comparison", 80, 90),
        ("live", "ცოცხალი მონაცემები", "Live data", 58, 76),
        ("battery", "ბატარეა", "Battery", 58, 76),
        ("vin", "VIN ისტორია", "VIN history", 90, 98)
    };

    public ObservableCollection<PrePurchaseStep> Steps { get; } = new();
    public ObservableCollection<PrePurchaseRow> Faults { get; } = new();
    public ObservableCollection<PrePurchaseRow> MileageRows { get; } = new();
    public ObservableCollection<PrePurchaseRow> ConditionRows { get; } = new();

    [ObservableProperty] private bool isIdle = true;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private int percent;
    [ObservableProperty] private double percentFraction;
    [ObservableProperty] private string progressDetail = string.Empty;
    [ObservableProperty] private string errorText = string.Empty;
    [ObservableProperty] private bool hasError;

    [ObservableProperty] private string vehicleName = "—";
    [ObservableProperty] private string vinText = "—";
    [ObservableProperty] private string adapterText = string.Empty;
    [ObservableProperty] private string adapterColor = "#94A3B8";

    [ObservableProperty] private int score;
    [ObservableProperty] private string scoreColor = "#4ADE80";
    [ObservableProperty] private string verdict = string.Empty;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string faultCountText = "0";
    [ObservableProperty] private string faultCountColor = "#4ADE80";
    [ObservableProperty] private string mileageBadge = string.Empty;
    [ObservableProperty] private string mileageBadgeColor = "#94A3B8";

    // Texts that depend on the language.
    [ObservableProperty] private string pageTitle = "ყიდვისწინა შემოწმება";
    [ObservableProperty] private string startButtonText = "▶ სრული შემოწმება";
    [ObservableProperty] private string stopButtonText = "■ გაჩერება";
    [ObservableProperty] private string runningTitle = "მიმდინარეობს შემოწმება";
    [ObservableProperty] private string aiButtonText = "AI კონსულტაცია";
    [ObservableProperty] private string pdfButtonText = "PDF ანგარიში";
    [ObservableProperty] private string accelerationButtonText = "აჩქარების ტესტი (არასავალდებულო)";
    [ObservableProperty] private string againButtonText = "↻ თავიდან შემოწმება";

    public PrePurchaseViewModel(
        PrePurchaseCheckService check,
        ProService pro,
        VehicleInfoService vehicles,
        OBDService obd,
        LocalizationService localization)
    {
        _check = check;
        _pro = pro;
        _vehicles = vehicles;
        _obd = obd;
        _localization = localization;
        Title = "Pre-purchase check";

        foreach (var (key, ka, en, _, _) in StepPlan)
            Steps.Add(new PrePurchaseStep { Key = key, TextKa = ka, TextEn = en });

        ApplyLanguage();
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyLanguage);
    }

    private bool English => _localization.IsEnglish;

    private void ApplyLanguage()
    {
        PageTitle = English ? "Pre-purchase check" : "ყიდვისწინა შემოწმება";
        StartButtonText = English ? "▶ Full check" : "▶ სრული შემოწმება";
        StopButtonText = English ? "■ Stop" : "■ გაჩერება";
        RunningTitle = English ? "Checking the car" : "მიმდინარეობს შემოწმება";
        AiButtonText = English ? "AI consultation" : "AI კონსულტაცია";
        PdfButtonText = English ? "PDF report" : "PDF ანგარიში";
        AccelerationButtonText = English ? "Acceleration test (optional)" : "აჩქარების ტესტი (არასავალდებულო)";
        AgainButtonText = English ? "↻ Check again" : "↻ თავიდან შემოწმება";
        UpdateSteps(Percent);
        if (_result is not null)
            ShowResult(_result);
        _ = RefreshHeaderAsync();
    }

    /// <summary>Car, VIN and adapter state shown before the check starts.</summary>
    public async Task RefreshHeaderAsync()
    {
        var connected = _obd.IsInitialized;
        AdapterText = connected
            ? (English ? "connected" : "დაკავშირებულია")
            : (English ? "not connected" : "არ არის დაკავშირებული");
        AdapterColor = connected ? "#4ADE80" : "#F87171";

        var vehicle = _vehicles.CurrentVehicle;
        if (vehicle is null && connected)
        {
            try { vehicle = await _vehicles.IdentifyConnectedVehicleAsync(); }
            catch { }
        }

        var name = vehicle is null
            ? string.Empty
            : string.Join(" ", new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
                .Where(part => !string.IsNullOrWhiteSpace(part) && part != "Unknown"));
        VehicleName = string.IsNullOrWhiteSpace(name) ? "—" : name;
        var vin = vehicle?.VIN;
        VinText = string.IsNullOrWhiteSpace(vin) || vin == "Unknown" ? "—" : vin;
    }

    [RelayCommand]
    private async Task Start()
    {
        try
        {
        if (IsRunning)
            return;

        if (!await ProGate.EnsureAsync(_pro, "ყიდვისწინა შემოწმება", "Pre-purchase check"))
            return;

        if (!_obd.IsInitialized)
        {
            try { await _obd.InitializeAsync(); }
            catch
            {
                ShowError(English
                    ? "The OBD adapter is not connected. Connect it on the home page and try again."
                    : "OBD ადაპტერი არ არის დაკავშირებული. დააკავშირე მთავარ გვერდზე და სცადე თავიდან.");
                return;
            }
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        HasError = false;
        HasResult = false;
        _result = null;
        IsIdle = false;
        IsRunning = true;
        IsBusy = true;
        SetPercent(0, string.Empty);

        try
        {
            var progress = new Progress<(int Percent, string Detail)>(step => SetPercent(step.Percent, step.Detail));
            var result = await _check.RunAsync(progress, token);
            _result = result;
            ShowResult(result);
            AddPrePurchaseToReport(result);
            HasResult = true;
        }
        catch (OperationCanceledException)
        {
            IsIdle = true;
        }
        catch (InvalidOperationException ex) when (ex.Message == "NO_ECU")
        {
            ShowError(English
                ? "The car did not answer. Turn the ignition (or engine) on and try again."
                : "მანქანამ არ უპასუხა. ჩართე კონტაქტი (ან ძრავი) და სცადე თავიდან.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Pre-purchase check failed: {ex}");
            ShowError(English
                ? "The check could not be completed. Check the adapter and try again."
                : "შემოწმება ვერ დასრულდა. შეამოწმე ადაპტერი და სცადე თავიდან.");
        }
        finally
        {
            IsRunning = false;
            IsBusy = false;
            await RefreshHeaderAsync();
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "PrePurchaseViewModel.Start");
        }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private Task Again()
    {
        HasResult = false;
        IsIdle = true;
        _result = null;
        SetPercent(0, string.Empty);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenAi()
    {
        try
        {
        if (_check.LastResult is null)
            return;
        if (!await ProGate.EnsureAsync(_pro, "AI კონსულტაცია", "AI consultation"))
            return;
        await Shell.Current.GoToAsync(nameof(Views.PrePurchaseAiPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "PrePurchaseViewModel.OpenAi");
        }
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        try
        {
        if (_result is null)
            return;

        try
        {
            var english = English;
            IEnumerable<(string Text, bool Bold, string? Color)> Lines(IEnumerable<PrePurchaseFinding> items) =>
                items.Select(item => ($"{(english ? item.LabelEn : item.LabelKa)}: {(english ? item.ValueEn : item.ValueKa)}",
                    false, (string?)PdfColor(item.Color)));

            var summary = new List<(string Title, IEnumerable<(string Text, bool Bold, string? Color)> Lines)>
            {
                (english ? "Result" : "შედეგი", new[]
                {
                    ($"{_result.Score}/100 — {(english ? _result.VerdictEn : _result.VerdictKa)}", true, (string?)PdfColor(_result.Color)),
                    (english ? _result.SummaryEn : _result.SummaryKa, false, (string?)null)
                }),
                (english ? "Fault codes" : "შეცდომები", Lines(_result.Faults)),
                ($"{(english ? "Mileage" : "გარბენი")} ({(english ? _result.MileageBadgeEn : _result.MileageBadgeKa)})", Lines(_result.Mileage)),
                (english ? "Condition" : "მდგომარეობა", Lines(_result.Condition.Concat(_result.History)))
            };

            var path = await _pdf.ExportPrePurchaseAsync(_result.Report, summary);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = english ? "GCar360 pre-purchase check" : "GCar360 ყიდვისწინა შემოწმება",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Pre-purchase PDF failed: {ex}");
            ShowError(English ? "The PDF could not be created." : "PDF ვერ შეიქმნა.", keepResult: true);
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "PrePurchaseViewModel.ExportPdf");
        }
    }

    [RelayCommand]
    private Task OpenAcceleration() => Shell.Current.GoToAsync(nameof(Views.AccelerationPage));

    // ------------------------------------------------------------------

    private void SetPercent(int value, string detail)
    {
        Percent = Math.Clamp(value, 0, 100);
        PercentFraction = Percent / 100d;
        var running = StepPlan.FirstOrDefault(step => Percent >= step.Start && Percent < step.End);
        ProgressDetail = !English && !string.IsNullOrWhiteSpace(detail) && Percent is >= 76 and < 80
            ? detail
            : running.Key is null
                ? string.Empty
                : (English ? running.En : running.Ka) + "…";
        UpdateSteps(Percent);
    }

    private void UpdateSteps(int value)
    {
        var english = English;
        foreach (var step in Steps)
        {
            var plan = StepPlan.First(item => item.Key == step.Key);
            var state = HasResult || value >= plan.End ? 2 : value >= plan.Start ? 1 : 0;
            if (step.Key == "vin" && (HasResult || value >= plan.Start) && !_check.VinHistoryAvailable)
                state = 3;
            step.Set(state, english);
        }
    }

    /// <summary>The pre-purchase check becomes its own part of the combined report (with its score).</summary>
    private void AddPrePurchaseToReport(PrePurchaseResult result)
    {
        var english = English;
        var section = new CarScanner.Services.Reports.ReportSectionItem
        {
            Kind = "prepurchase",
            Icon = "🔍",
            Title = english ? "Pre-purchase check" : "ყიდვისწინა შემოწმება",
            Score = result.Score,
            Status = result.Score >= 85 ? CarScanner.Services.Reports.ReportStatus.Ok
                : result.Score >= 60 ? CarScanner.Services.Reports.ReportStatus.Warning
                : CarScanner.Services.Reports.ReportStatus.Bad,
            Summary = $"{result.Score}/100 · {(english ? result.VerdictEn : result.VerdictKa)}"
        };

        if (!string.IsNullOrWhiteSpace(english ? result.SummaryEn : result.SummaryKa))
            section.Lines.Add(new() { Label = string.Empty, Value = english ? result.SummaryEn : result.SummaryKa });
        foreach (var finding in result.Faults.Concat(result.Mileage).Concat(result.Condition).Concat(result.History))
            section.Lines.Add(new() { Label = english ? finding.LabelEn : finding.LabelKa, Value = english ? finding.ValueEn : finding.ValueKa });

        CarScanner.Services.Reports.VehicleReportService.Instance?.AddOrReplace(section, result.Report);
    }

    private void ShowResult(PrePurchaseResult result)
    {
        var english = English;
        Score = result.Score;
        ScoreColor = result.Color;
        Verdict = english ? result.VerdictEn : result.VerdictKa;
        Summary = english ? result.SummaryEn : result.SummaryKa;

        var faults = result.Faults.Count(item => item.Color != "#4ADE80");
        FaultCountText = faults.ToString();
        FaultCountColor = faults == 0 ? "#4ADE80" : result.Faults.Any(item => item.Color == "#F87171") ? "#F87171" : "#FBBF24";
        MileageBadge = english ? result.MileageBadgeEn : result.MileageBadgeKa;
        MileageBadgeColor = result.MileageBadgeColor;

        Faults.Clear();
        foreach (var item in result.Faults)
            Faults.Add(item.ToRow(english));
        MileageRows.Clear();
        foreach (var item in result.Mileage)
            MileageRows.Add(item.ToRow(english));
        ConditionRows.Clear();
        foreach (var item in result.Condition.Concat(result.History))
            ConditionRows.Add(item.ToRow(english));

        UpdateSteps(100);
    }

    private void ShowError(string text, bool keepResult = false)
    {
        ErrorText = text;
        HasError = true;
        if (!keepResult)
        {
            IsIdle = true;
            HasResult = false;
        }
    }

    private static string PdfColor(string color) => color switch
    {
        "#F87171" => "#B91C1C",
        "#FBBF24" or "#FB923C" => "#B45309",
        "#4ADE80" => "#15803D",
        _ => "#475569"
    };
}
