namespace CarScanner.Models;

public sealed class FreezeFrame
{
    public string TriggerCode { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
}
