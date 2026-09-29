namespace CarScanner.Models;

public enum TechnicalInspectionVerdict
{
    NotChecked,
    Pass,
    Fail,
    Dangerous,
    Unavailable
}

public sealed class TechnicalInspectionIssue
{
    public string Code { get; init; } = string.Empty;
    public string TitleKa { get; init; } = string.Empty;
    public string TitleEn { get; init; } = string.Empty;
    public string CategoryKa { get; init; } = string.Empty;
    public string CategoryEn { get; init; } = string.Empty;
    public string SeverityKa { get; init; } = string.Empty;
    public string SeverityEn { get; init; } = string.Empty;
    public string ImpactKa { get; init; } = string.Empty;
    public string ImpactEn { get; init; } = string.Empty;
    public string CheckKa { get; init; } = string.Empty;
    public string CheckEn { get; init; } = string.Empty;
    public string FixKa { get; init; } = string.Empty;
    public string FixEn { get; init; } = string.Empty;
    public string Color { get; init; } = "#F59E0B";
}

public sealed class TechnicalInspectionResult
{
    public TechnicalInspectionVerdict Verdict { get; init; }
    public IReadOnlyList<TechnicalInspectionIssue> Issues { get; init; } = Array.Empty<TechnicalInspectionIssue>();
    public int SupportedMonitorCount { get; init; }
    public int CompleteMonitorCount { get; init; }
    public bool? MilOn { get; init; }
    public int? StoredDtcCount { get; init; }
    public double? DistanceSinceClearKm { get; init; }
    public double? WarmUpsSinceClear { get; init; }
}
