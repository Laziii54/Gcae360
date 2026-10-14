using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.OemDiagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>Live values of the car's modules (ABS, steering, battery cells …). PRO.</summary>
public partial class ModuleLiveDataViewModel : BaseViewModel
{
    private readonly ModuleLiveDataService _liveData;
    private readonly VehicleInfoService _vehicles;
    private readonly ProService _pro;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _cts;
    private string _noteKa = string.Empty;
    private string _noteEn = string.Empty;

    public ObservableCollection<ModuleLiveGroup> Groups { get; } = new();

    [ObservableProperty] private string pageTitle = "მოდულების მონაცემები";
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private double searchProgress;
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private bool hasGroups;
    [ObservableProperty] private string vehicleName = string.Empty;
    [ObservableProperty] private string startButtonText = "დაწყება";
    [ObservableProperty] private string stopButtonText = "გაჩერება";
    [ObservableProperty] private bool isRunning;

    public bool IsNotRunning => !IsRunning;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsNotRunning));

    public ModuleLiveDataViewModel(
        ModuleLiveDataService liveData,
        VehicleInfoService vehicles,
        ProService pro,
        LocalizationService localization)
    {
        _liveData = liveData;
        _vehicles = vehicles;
        _pro = pro;
        _localization = localization;
        Title = "Module Live Data";
        ApplyLanguage();
        Attach();
    }

    // Detached by the page when it goes away (no leak through singleton services).
    private bool _attached;
    private bool _refreshOnAttach;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _pro.ProChanged += HandleProChanged;
        _localization.LanguageChanged += HandleLanguageChanged;

        // Texts may have changed while the page was hidden (e.g. Settings on top).
        if (_refreshOnAttach)
        {
            _refreshOnAttach = false;
            HandleLanguageChanged(this, EventArgs.Empty);
        }
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _pro.ProChanged -= HandleProChanged;
        _localization.LanguageChanged -= HandleLanguageChanged;
        _refreshOnAttach = true;
    }

    private void HandleProChanged(object? sender, bool isPro) =>
        MainThread.BeginInvokeOnMainThread(ApplyLanguage);

    private void HandleLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(ApplyLanguage);

    private bool English => _localization.IsEnglish;

    private void ApplyLanguage()
    {
        PageTitle = English ? "Module live data" : "მოდულების მონაცემები";
        StartButtonText = (English ? "Start" : "დაწყება") + ProGate.Suffix;
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

        if (!await ProGate.EnsureAsync(_pro, "მოდულების ცოცხალი მონაცემები", "Module live data"))
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Groups.Clear();
        HasGroups = false;
        IsRunning = true;
        IsSearching = true;
        SearchProgress = 0;
        SetNote("მოდულები მოწმდება — რომელი მონაცემი აქვს ამ მანქანას…",
            "Checking the modules — which values this car has…");

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

            var progress = new System.Progress<double>(value => SearchProgress = value);
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
                            $"{Groups.Sum(group => group.Signals.Count)} მნიშვნელობა {Groups.Count} ჯგუფში · განახლება მიმდინარეობს",
                            $"{Groups.Sum(group => group.Signals.Count)} values in {Groups.Count} groups · updating");
                    else
                        SetNote(result.NoteKa, result.NoteEn);
                });
                return Task.CompletedTask;
            }, progress, token);
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user or by leaving the page.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Module live data: {ex}");
            SetNote("მოდულების მონაცემები ვერ წაიკითხა. შეამოწმე ადაპტერი და კონტაქტი, შემდეგ სცადე თავიდან.",
                "Could not read the module data. Check the adapter and the ignition, then try again.");
        }
        finally
        {
            IsSearching = false;
            IsRunning = false;
            if (HasGroups)
                SetNote(
                    $"{Groups.Sum(group => group.Signals.Count)} მნიშვნელობა · გაჩერებულია",
                    $"{Groups.Sum(group => group.Signals.Count)} values · stopped");
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleLiveDataViewModel.Start");
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
