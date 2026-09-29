using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class CapabilityCenterViewModel : BaseViewModel
{
    private readonly CapabilityDetectionService _capabilities;

    public ObservableCollection<DetectedCapability> Universal { get; } = new();
    public ObservableCollection<DetectedCapability> Pro { get; } = new();
    public ObservableCollection<DetectedCapability> Manufacturer { get; } = new();

    [ObservableProperty] private string connectionSummary = "ჯერ არ შემოწმებულა";
    [ObservableProperty] private string vehicleSummary = "—";
    [ObservableProperty] private string vehicleProfileStatus = "პროფილი ჯერ არ არის დადასტურებული";
    [ObservableProperty] private string adapterSummary = "—";
    [ObservableProperty] private string protocol = "—";
    [ObservableProperty] private string detectionStage = "დასაწყებად დააჭირე „შემოწმება“-ს.";
    [ObservableProperty] private int detectionPercent;
    [ObservableProperty] private string detectionColor = "#94A3B8";

    public double DetectionProgress => DetectionPercent / 100d;

    public CapabilityCenterViewModel(CapabilityDetectionService capabilities)
    {
        _capabilities = capabilities;
        Title = "Vehicle Capabilities";
    }

    partial void OnDetectionPercentChanged(int value) => OnPropertyChanged(nameof(DetectionProgress));

    [RelayCommand]
    public async Task Refresh()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            DetectionColor = "#60A5FA";
            var progress = new Progress<CapabilityDetectionProgress>(step =>
            {
                DetectionPercent = step.Percent;
                DetectionStage = step.Stage;
            });
            var snapshot = await _capabilities.DetectAsync(progress);
            Apply(snapshot);
            DetectionColor = snapshot.Capabilities.Any(item => item.State == CapabilityState.Available)
                ? "#22C55E"
                : "#F59E0B";
            StatusMessage = "Capability Detection განახლდა";
        }
        catch (Exception ex)
        {
            DetectionColor = "#EF4444";
            DetectionStage = "შემოწმება ვერ დასრულდა";
            StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Capability Detection ვერ შესრულდა."
                : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ApplyCurrentIfAny()
    {
        if (_capabilities.Current is not null)
            Apply(_capabilities.Current);
    }

    private void Apply(VehicleCapabilitySnapshot snapshot)
    {
        ConnectionSummary = snapshot.ConnectionSummary;
        VehicleSummary = snapshot.VehicleSummary;
        VehicleProfileStatus = snapshot.VehicleProfileStatus;
        AdapterSummary = $"{snapshot.AdapterName} · {snapshot.SupportedPidCount} PID";
        Protocol = snapshot.Protocol;
        DetectionPercent = 100;
        DetectionStage = $"ბოლო შემოწმება: {snapshot.DetectedAt.ToLocalTime():HH:mm:ss}";

        Replace(Universal, snapshot.Capabilities.Where(item => item.Tier == CapabilityTier.UniversalObd));
        Replace(Pro, snapshot.Capabilities.Where(item => item.Tier == CapabilityTier.ProAnalytics));
        Replace(Manufacturer, snapshot.Capabilities.Where(item => item.Tier == CapabilityTier.ManufacturerSpecific));
    }

    private static void Replace(ObservableCollection<DetectedCapability> target, IEnumerable<DetectedCapability> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
