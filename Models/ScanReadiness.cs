using SQLite;

namespace CarScanner.Models;

[Table("ScanReadiness")]
public sealed class ScanReadiness
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int SessionId { get; set; }
    public bool MilOn { get; set; }
    public int StoredDtcCount { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string RawHex { get; set; } = string.Empty;
}
