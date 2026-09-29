using SQLite;

namespace CarScanner.Models;

public sealed class MaintenanceReminder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string VehicleKey { get; set; } = "default";

    [Indexed]
    public string ExternalKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "სხვა";
    public DateTime DueDate { get; set; } = DateTime.Today;
    public int IntervalMonths { get; set; }
    public int IntervalKm { get; set; }
    public double? OdometerAtCompletionKm { get; set; }
    public double? DueOdometerKm { get; set; }
    public bool IsAutomatic { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCompletedAtUtc { get; set; }
}
