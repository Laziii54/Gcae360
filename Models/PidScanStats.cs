namespace CarScanner.Models;

public sealed class PidScanStats
{
    public int Advertised { get; init; }
    public int Decoded { get; init; }
    public int Failed { get; init; }
    public int UnsupportedDecoder { get; init; }
}
