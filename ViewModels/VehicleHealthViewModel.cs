using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class VehicleHealthViewModel : BaseViewModel
{
    private readonly VehicleHealthService _service;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _scanCts;
    private long _scanGeneration;

    public ObservableCollection<VehicleHealthIssue> Issues { get; } = new();

    [ObservableProperty] private string scoreText = "— / 100";
    [ObservableProperty] private string interpretation = string.Empty;
    [ObservableProperty] private string issueSummary = "Critical Issues: —    Warnings: —";
    [ObservableProperty] private string coverageText = string.Empty;
    [ObservableProperty] private string scanSummary = string.Empty;
    [ObservableProperty] private string progressText = "0%";
    [ObservableProperty] private string scanStage = string.Empty;
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool hasIssues;
    [ObservableProperty] private bool canScan = true;

    public VehicleHealthViewModel(
        VehicleHealthService service,
        LocalizationService localization)
    {
        _service = service;
        _localization = localization;
        Title = "Vehicle Health";
        ScanStage = L("Connect the vehicle and start the scan.", "დააკავშირე ავტომობილი და დაიწყე სკანირება.");
    }

    [RelayCommand]
    private async Task Scan()
    {
        try
        {
        var generation = Interlocked.Increment(ref _scanGeneration);
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        ResetForScan();
        IsBusy = true;
        CanScan = false;
        var progress = new Progress<VehicleHealthProgress>(update =>
        {
            if (generation != Volatile.Read(ref _scanGeneration) || token.IsCancellationRequested) return;
            var percent = Math.Clamp(update.Percent, 0, 100);
            ProgressValue = percent / 100d;
            ProgressText = $"{percent}%";
            ScanStage = update.Stage;
        });

        try
        {
            var result = await _service.ScanAsync(progress, token);
            token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _scanGeneration)) return;

            Issues.Clear();
            foreach (var issue in result.Issues)
                Issues.Add(issue);

            ScoreText = $"{result.Score} / 100";
            Interpretation = result.Interpretation;
            IssueSummary = L(
                $"Critical Issues: {result.CriticalIssueCount}    Warnings: {result.WarningCount}",
                $"კრიტიკული: {result.CriticalIssueCount}    გაფრთხილება: {result.WarningCount}");
            CoverageText = L(
                $"Coverage: {result.ScanCoverage}% · Sensors: {result.CheckedSensorCount} · ECU modules: {result.CheckedModuleCount}",
                $"დაფარვა: {result.ScanCoverage}% · სენსორი: {result.CheckedSensorCount} · ECU მოდული: {result.CheckedModuleCount}");
            ScanSummary = result.Summary;
            HasIssues = Issues.Count > 0;
            HasResult = true;
            ProgressValue = 1;
            ProgressText = "100%";
            ScanStage = L("Health analysis complete", "მდგომარეობის შეფასება დასრულდა");
        }
        catch (OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _scanGeneration))
                ScanStage = L("Scan cancelled or vehicle disconnected.", "სკანირება გაუქმდა ან ავტომობილი გაითიშა.");
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _scanGeneration)) return;
            HasResult = false;
            HasIssues = false;
            ScanStage = string.IsNullOrWhiteSpace(ex.Message)
                ? L("Vehicle communication failed.", "ავტომობილთან კავშირი ვერ შესრულდა.")
                : ex.Message;
        }
        finally
        {
            if (generation == Volatile.Read(ref _scanGeneration))
            {
                IsBusy = false;
                CanScan = true;
            }
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VehicleHealthViewModel.Scan");
        }
    }

    private void ResetForScan()
    {
        Issues.Clear();
        HasIssues = false;
        HasResult = false;
        ScoreText = "— / 100";
        Interpretation = string.Empty;
        IssueSummary = L("Critical Issues: —    Warnings: —", "კრიტიკული: —    გაფრთხილება: —");
        CoverageText = string.Empty;
        ScanSummary = string.Empty;
        ProgressValue = 0;
        ProgressText = "0%";
        ScanStage = L("Starting full health scan…", "სრული მდგომარეობის სკანირება იწყება…");
    }

    private string L(string english, string georgian) => _localization.IsEnglish ? english : georgian;
}
