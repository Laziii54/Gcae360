using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CarScanner.Models;

/// <summary>One live value read from a car module (ABS wheel speed, battery cell …).</summary>
public sealed partial class ModuleLiveSignal : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string GroupId { get; init; } = string.Empty;
    public string NameKa { get; init; } = string.Empty;
    public string NameEn { get; init; } = string.Empty;
    public string UnitKa { get; init; } = string.Empty;
    public string UnitEn { get; init; } = string.Empty;

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string valueText = "—";
    [ObservableProperty] private string unit = string.Empty;

    /// <summary>True once the value is a number, so tapping the row can open a live graph.</summary>
    public bool CanGraph => double.TryParse(ValueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

    partial void OnValueTextChanged(string value) => OnPropertyChanged(nameof(CanGraph));

    public void ApplyLanguage(bool english)
    {
        Name = english || string.IsNullOrWhiteSpace(NameKa) ? NameEn : NameKa;
        Unit = english ? UnitEn : UnitKa;
    }
}

/// <summary>Signals of one area of the car (engine, tires, battery …).</summary>
public sealed partial class ModuleLiveGroup : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string TitleKa { get; init; } = string.Empty;
    public string TitleEn { get; init; } = string.Empty;
    public int Order { get; init; }

    [ObservableProperty] private string title = string.Empty;

    public ObservableCollection<ModuleLiveSignal> Signals { get; } = new();

    public void ApplyLanguage(bool english)
    {
        Title = english ? TitleEn : TitleKa;
        foreach (var signal in Signals)
            signal.ApplyLanguage(english);
    }
}
