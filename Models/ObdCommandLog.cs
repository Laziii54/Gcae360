namespace CarScanner.Models;

public sealed class ObdCommandLog
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Command { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public long DurationMs { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
}
