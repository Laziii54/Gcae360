using SQLite;

namespace CarScanner.Models;

[Table("ScanFreezeFrame")]
public sealed class ScanFreezeFrame
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int SessionId { get; set; }
    public string TriggerCode { get; set; } = string.Empty;
    public string ValuesJson { get; set; } = string.Empty;
}
