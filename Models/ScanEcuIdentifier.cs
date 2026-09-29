using SQLite;

namespace CarScanner.Models;

[Table("ScanEcuIdentifier")]
public sealed class ScanEcuIdentifier
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
