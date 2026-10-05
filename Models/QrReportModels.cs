namespace CarScanner.Models;

public sealed class QrReportDraft
{
    public string Vehicle { get; init; } = string.Empty;
    public string Vin { get; init; } = string.Empty;
    public DateTime CheckedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string DtcSummary { get; init; } = string.Empty;
    public string ReadinessSummary { get; init; } = string.Empty;
    public string ServiceHistory { get; init; } = string.Empty;
}

public sealed class QrReportLink
{
    public bool Success { get; set; }
    public string Url { get; set; } = string.Empty;
    public string QrImageUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
