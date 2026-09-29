namespace CarScanner.Models;

public sealed class ReadinessInfo
{
    public bool MilOn { get; init; }
    public int StoredDtcCount { get; init; }
    public byte[] RawBytes { get; init; } = Array.Empty<byte>();
    public string StatusText => MilOn ? "MIL ჩართულია" : "MIL გამორთულია";
    public string ConnectionMeaning => "ECU პასუხი მიღებულია და readiness data decode შესრულდა.";
    public string RawHex => string.Join(" ", RawBytes.Select(x=>x.ToString("X2")));
}
