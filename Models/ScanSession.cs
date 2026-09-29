using SQLite;

namespace CarScanner.Models;

[Table("ScanSession")]
public sealed class ScanSession
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    [Indexed] public string VIN { get; set; } = string.Empty;
    public string VehicleSummary { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public int StoredDtcCount { get; set; }
    public int PendingDtcCount { get; set; }
    public int PermanentDtcCount { get; set; }
    public bool MilOn { get; set; }
    public string ReportJson { get; set; } = string.Empty;
}
