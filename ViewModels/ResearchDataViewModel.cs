using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.OemDiagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// Admin-only: reads the catalog's NOT-yet-reviewed signals (debug = 1) for the connected car, so the admin can
/// compare them against the real car (dashboard, a known gauge) while driving and judge whether a signal is
/// correct. Nothing read here is shown to, or usable by, a non-admin account — see HomeViewModel.IsAdmin, which
/// gates the Home tile that opens this page, and the IsAdminAsync re-check in Start() below.
/// </summary>
public partial class ResearchDataViewModel : BaseViewModel
{
    private readonly ModuleLiveDataService _liveData;
    private readonly VehicleInfoService _vehicles;
    private readonly AccountService _account;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _cts;
    private string _noteKa = string.Empty;
    private string _noteEn = string.Empty;

    public ObservableCollection<ModuleLiveGroup> Groups { get; } = new();

    [ObservableProperty] private string pageTitle = "კვლევა";
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private double searchProgress;
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private bool hasGroups;
    [ObservableProperty] private string vehicleName = string.Empty;
    [ObservableProperty] private string startButtonText = "კვლევა";
    [ObservableProperty] private string stopButtonText = "გაჩერება";
    [ObservableProperty] private bool isRunning;

    public bool IsNotRunning => !IsRunning;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsNotRunning));

    public ResearchDataViewModel(
        ModuleLiveDataService liveData,
        VehicleInfoService vehicles,
        AccountService account,
        LocalizationService localization)
    {
        _liveData = liveData;
        _vehicles = vehicles;
        _account = account;
        _localization = localization;
        Title = "Research";
        ApplyLanguage();
    }

    private bool English => _localization.IsEnglish;

    private void ApplyLanguage()
    {
        PageTitle = English ? "Research" : "კვლევა";
        StartButtonText = English ? "Research" : "კვლევა";
        StopButtonText = English ? "Stop" : "გაჩერება";
        if (!string.IsNullOrWhiteSpace(_noteKa) || !string.IsNullOrWhiteSpace(_noteEn))
            StatusText = English ? _noteEn : _noteKa;
        foreach (var group in Groups)
            group.ApplyLanguage(English);
    }

    [RelayCommand]
    public async Task Start()
    {
        try
        {
            if (IsRunning)
                return;

            // Server-confirmed only, re-checked here even though the Home tile that opens this page already
            // hides it from a non-admin: this command must refuse on its own if it is ever reached another way.
            if (!await _account.IsAdminAsync())
            {
                SetNote("ადმინის ანგარიში არ არის დადასტურებული.", "Admin account not confirmed.");
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Groups.Clear();
            HasGroups = false;
            IsRunning = true;
            IsSearching = true;
            SearchProgress = 0;
            SetNote("მოდულები მოწმდება — კვლევის სიგნალები...", "Checking the modules — research signals...");

            try
            {
                var vehicle = _liveData.LastVehicle;
                if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.Manufacturer) || vehicle.Manufacturer == "Unknown")
                {
                    try { vehicle = await _vehicles.IdentifyConnectedVehicleAsync(); }
                    catch { /* the scan below reports the problem */ }
                }

                vehicle ??= new VehicleInfo();
                VehicleName = string.Join(" ", new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
                    .Where(part => !string.IsNullOrWhiteSpace(part) && part != "Unknown"));

                var progress = new Progress<double>(value => SearchProgress = value);
                await _liveData.RunAsync(vehicle, result =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        IsSearching = false;
                        foreach (var group in result.Groups)
                            Groups.Add(group);
                        HasGroups = Groups.Count > 0;
                        if (HasGroups)
                            SetNote(
                                $"{Groups.Sum(group => group.Signals.Count)} კვლევის სიგნალი {Groups.Count} ჯგუფში · განახლება მიმდინარეობს",
                                $"{Groups.Sum(group => group.Signals.Count)} research signals in {Groups.Count} groups · updating");
                        else
                            SetNote(result.NoteKa, result.NoteEn);
                    });
                    return Task.CompletedTask;
                }, progress, token, debugOnly: true);
            }
            catch (OperationCanceledException)
            {
                // Stopped by the user or by leaving the page.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Research data: {ex}");
                SetNote("კვლევის მონაცემები ვერ წაიკითხა. შეამოწმე ადაპტერი და კონტაქტი, შემდეგ სცადე თავიდან.",
                    "Could not read the research data. Check the adapter and the ignition, then try again.");
            }
            finally
            {
                IsSearching = false;
                IsRunning = false;
                if (HasGroups)
                    SetNote(
                        $"{Groups.Sum(group => group.Signals.Count)} კვლევის სიგნალი · გაჩერებულია",
                        $"{Groups.Sum(group => group.Signals.Count)} research signals · stopped");
            }
        }
        catch (Exception commandException)
        {
            CommandErrors.Report(commandException, "ResearchDataViewModel.Start");
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _cts?.Cancel();
    }

    private void SetNote(string ka, string en)
    {
        _noteKa = ka;
        _noteEn = en;
        StatusText = English ? en : ka;
    }
}
