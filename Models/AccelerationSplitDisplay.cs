namespace CarScanner.Models;

/// <summary>
/// One live checkpoint row on the Performance Lab page (e.g. "0–20" → "2.34s").
/// Populated fresh on every acceleration state update so each speed
/// checkpoint fills in with its own elapsed time as soon as the ECU
/// reports the vehicle crossing it.
/// </summary>
public sealed class AccelerationSplitDisplay
{
    public string Label { get; set; } = string.Empty;
    public string TimeText { get; set; } = "—";
}
