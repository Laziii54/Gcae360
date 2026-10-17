namespace CarScanner.Models;

/// <summary>
/// How trustworthy the most recent live-data reading is, judged purely by how long it has been
/// since the polling loop last completed a tick / got a real answer — never by whether a value
/// looks plausible (a steady reading is not a failure; see <see cref="SensorLevel"/> for that).
/// </summary>
public enum LiveDataHealth
{
    /// <summary>A tick completed, or the current one is still within its normal time budget.</summary>
    Healthy,

    /// <summary>No successful read in a while, or the current tick is running long. Still polling.</summary>
    Stale,

    /// <summary>
    /// The polling loop has not completed a tick for far longer than any single command's own
    /// timeout could explain — almost certainly a single awaited call that will never return
    /// (e.g. a native Bluetooth read the OS does not actually interrupt on cancellation).
    /// </summary>
    Frozen
}
