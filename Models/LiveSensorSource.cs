using CommunityToolkit.Mvvm.ComponentModel;

namespace CarScanner.Models;

/// <summary>
/// A live reading that a screen already computes for its own display (battery SoC, fuel level …) and that is
/// not a <see cref="LivePidItem"/> or <see cref="ModuleLiveSignal"/> row. The owning view model writes
/// <see cref="Value"/> from the same refresh that updates its label; the sensor graph only listens, so
/// opening a graph never adds a second read from the ECU. <c>null</c> means no reading yet.
/// </summary>
public sealed partial class LiveSensorSource : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    [ObservableProperty] private double? value;
}
