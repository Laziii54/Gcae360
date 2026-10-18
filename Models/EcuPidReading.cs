using CommunityToolkit.Mvvm.ComponentModel;
using CarScanner.Services;
using System.Globalization;

namespace CarScanner.Models;

/// <summary>
/// A standard PID advertised by the connected ECU together with its most
/// recent real response. The value is never populated from a demo/default.
/// </summary>
public sealed partial class EcuPidReading : ObservableObject
{
    public int Pid { get; init; }
    public string Command => $"01{Pid:X2}";
    public string Name { get; init; } = string.Empty;
    public string RawUnit { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    [ObservableProperty]
    private string unit = string.Empty;

    [ObservableProperty]
    private string currentValue = "—";

    [ObservableProperty]
    private string status = "Supported · ECU პასუხს ველოდები";

    [ObservableProperty]
    private string statusColor = "#94A3B8";

    [ObservableProperty]
    private string lastUpdated = "ჯერ არ განახლებულა";

    public void ApplyValue(string value, UnitSystemService units)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var metricValue))
        {
            CurrentMetricValue = metricValue;
            HasMetricValue = true;
            RefreshPresentation(units);
        }
        else
        {
            CurrentValue = value;
        }
        Status = "Supported · Live";
        StatusColor = "#4ADE80";
        LastUpdated = DateTime.Now.ToString("HH:mm:ss");
    }

    public void RefreshPresentation(UnitSystemService units)
    {
        var converted = units.Convert(CurrentMetricValue, RawUnit);
        Unit = converted.Unit;
        if (!HasMetricValue)
            return;
        CurrentValue = converted.Value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private double CurrentMetricValue { get; set; }
    private bool HasMetricValue { get; set; }

    /// <summary>True once the ECU has answered with a number, so tapping the row can open a live graph.</summary>
    public bool CanGraph => HasMetricValue;

    partial void OnCurrentValueChanged(string value) => OnPropertyChanged(nameof(CanGraph));

    public void MarkNoResponse()
    {
        // Keep the last confirmed ECU value visible, but never present it as
        // newly read data when this request did not receive a response.
        Status = "Supported · ECU პასუხი არ არის";
        StatusColor = "#F59E0B";
    }
}
