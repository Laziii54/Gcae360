using SQLite;
namespace CarScanner.Models;
public sealed class MileageRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public string VIN { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Status { get; set; } = "Unavailable";
    public string Details { get; set; } = string.Empty;
}
