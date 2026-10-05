using CommunityToolkit.Mvvm.ComponentModel;
using CarScanner.Services;
using System.Globalization;

namespace CarScanner.Models;

/// <summary>
/// A live value that has actually been advertised by the connected ECU.
/// It holds only telemetry received from the adapter; unavailable PIDs never
/// get a fabricated zero value.
/// </summary>
public sealed partial class LivePidItem : ObservableObject
{
    private double _sum;

    public int Pid { get; init; }
    public bool IsOem { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string FieldKey { get; init; } = string.Empty;
    public string IdentifierText { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RawUnit { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Source { get; init; } = "Engine / ECM";

    [ObservableProperty] private string unit = string.Empty;
    [ObservableProperty] private string rangeText = string.Empty;
    [ObservableProperty] private string valueText = "—";
    [ObservableProperty] private string minimumText = "—";
    [ObservableProperty] private string averageText = "—";
    [ObservableProperty] private string maximumText = "—";
    [ObservableProperty] private string lastUpdateText = "ჯერ არ განახლებულა";
    [ObservableProperty] private string healthText = "მოლოდინში";
    [ObservableProperty] private string healthColor = "#94A3B8";
    [ObservableProperty] private bool isFavorite;
    [ObservableProperty] private bool isDashboard;
    [ObservableProperty] private bool isGraph;
    [ObservableProperty] private int sampleCount;
    [ObservableProperty] private double currentValue;
    [ObservableProperty] private double minimumValue = double.PositiveInfinity;
    [ObservableProperty] private double maximumValue = double.NegativeInfinity;

    public string DisplayName => IsOem
        ? $"{Name} · {IdentifierText}"
        : $"{Name} · PID 01{Pid:X2}";
    public string UnitText => string.IsNullOrWhiteSpace(Unit) ? string.Empty : Unit;
    public string StatisticsText => $"MIN {MinimumText}   |   AVG {AverageText}   |   MAX {MaximumText}";
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteColor => IsFavorite ? "#FBBF24" : "#94A3B8";

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteGlyph));
        OnPropertyChanged(nameof(FavoriteColor));
    }

    /// <summary>Accepts canonical OBD data and projects it with the current app units.</summary>
    public void Update(SensorValue value, DateTime timestamp, UnitSystemService units)
    {
        if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return;

        CurrentMetricValue = parsed;
        SampleCount++;
        _sum += parsed;
        MinimumValue = Math.Min(MinimumValue, parsed);
        MaximumValue = Math.Max(MaximumValue, parsed);
        LastUpdateText = timestamp.ToLocalTime().ToString("HH:mm:ss");
        RefreshPresentation(units);

        var state = new SensorValue
        {
            Pid = Pid,
            Value = value.Value
        };
        HealthText = state.StatusText;
        HealthColor = state.StatusColor;
    }

    public void UpdateOem(double value, DateTime timestamp, UnitSystemService units)
    {
        CurrentMetricValue = value;
        SampleCount++;
        _sum += value;
        MinimumValue = Math.Min(MinimumValue, value);
        MaximumValue = Math.Max(MaximumValue, value);
        LastUpdateText = timestamp.ToLocalTime().ToString("HH:mm:ss");
        HealthText = "ECU Live";
        HealthColor = "#4ADE80";
        RefreshPresentation(units);
    }

    public void RefreshPresentation(UnitSystemService units)
    {
        var displayUnit = units.Convert(0, RawUnit).Unit;
        Unit = displayUnit;
        RangeText = IsOem
            ? "Reviewed OEM DID · მნიშვნელობა პირდაპირ ECU-დან"
            : RangeFor(Pid, units);

        if (SampleCount == 0)
        {
            OnPropertyChanged(nameof(StatisticsText));
            return;
        }

        var current = units.Convert(CurrentMetricValue, RawUnit);
        CurrentValue = current.Value;
        ValueText = !IsOem && Pid == 0x51
            ? FuelTypeName((int)CurrentMetricValue)
            : $"{current.Value:0.##} {current.Unit}".Trim();
        MinimumText = units.Format(MinimumValue, RawUnit);
        AverageText = units.Format(_sum / SampleCount, RawUnit);
        MaximumText = units.Format(MaximumValue, RawUnit);
        OnPropertyChanged(nameof(StatisticsText));
    }

    private double CurrentMetricValue { get; set; }

    public void ResetStatistics()
    {
        _sum = 0;
        SampleCount = 0;
        MinimumValue = double.PositiveInfinity;
        MaximumValue = double.NegativeInfinity;
        ValueText = "—";
        MinimumText = "—";
        AverageText = "—";
        MaximumText = "—";
        LastUpdateText = "ჯერ არ განახლებულა";
        HealthText = "მოლოდინში";
        HealthColor = "#94A3B8";
        CurrentMetricValue = 0;
        OnPropertyChanged(nameof(StatisticsText));
    }

    private static string RangeFor(int pid, UnitSystemService units) => pid switch
    {
        0x05 => $"ნორმა: {units.FormatTemperature(105)}-მდე (გამთბარ ძრავაზე {units.FormatTemperature(80)}–{units.FormatTemperature(105)})",
        0x5C => $"ნორმა: {units.FormatTemperature(130)}-მდე",
        0x0F => $"ნორმა: {units.FormatTemperature(70)}-მდე",
        0x42 => "ნორმა: 11.8–15.0 V (ძრავა ჩართული: 13.5–14.8 V)",
        0x06 or 0x08 => "ნორმა: −20…+20 % (მოკლევადიანი კორექცია)",
        0x07 or 0x09 => "ნორმა: −10…+10 % (გრძელვადიანი კორექცია)",
        0x2F => "ნორმა: 10 %-ზე მეტი",
        0x3C or 0x3D or 0x3E or 0x3F => $"ნორმა: {units.FormatTemperature(900)}-მდე",
        _ => "ECU-ის რეალური მონაცემი · შეფასება კონტექსტით ხდება"
    };

    private static string FuelTypeName(int code) => code switch
    {
        1 => "ბენზინი", 2 => "მეთანოლი", 3 => "ეთანოლი", 4 => "დიზელი",
        5 => "LPG", 6 => "CNG", 7 => "პროპანი", 8 => "ელექტრო/ჰიბრიდი",
        _ => $"საწვავის კოდი {code}"
    };
}
