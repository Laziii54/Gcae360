using SQLite;

namespace CarScanner.Models;

[Table("ScanDtc")]
public sealed class ScanDtc
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int SessionId { get; set; }
    [Indexed] public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "უცნობი";
}
