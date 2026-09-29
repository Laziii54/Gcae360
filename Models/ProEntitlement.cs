using SQLite;
namespace CarScanner.Models;
public sealed class ProEntitlement
{
    [PrimaryKey] public string Id { get; set; } = "local";
    public bool IsPro { get; set; }
    public bool VerifiedByServer { get; set; }
    public string Source { get; set; } = "none";
    public DateTime? ExpiresAt { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
