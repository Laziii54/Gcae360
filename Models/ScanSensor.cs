using SQLite;

namespace CarScanner.Models;

[Table("ScanSensor")]
public sealed class ScanSensor
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int SessionId { get; set; }
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
