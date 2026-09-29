namespace CarScanner.Models;

/// <summary>
/// A real completed scan milestone. Percent is reported only after the
/// corresponding OBD or local-processing stage has been reached.
/// </summary>
public sealed record DtcScanProgress(int Percent, string Stage);
