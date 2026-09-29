using SQLite;

namespace CarScanner.Models;

/// <summary>Manual refuel evidence for one identified vehicle.</summary>
[Table("RefuelRecord")]
public sealed class RefuelRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public string VIN { get; set; } = string.Empty;
    public DateTime RefueledAt { get; set; } = DateTime.Now;
    public double OdometerKm { get; set; }
    public double Liters { get; set; }
    public double PricePerLiter { get; set; }
    public bool IsFullTank { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public string Station { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    [Ignore]
    public string DisplaySummary =>
        $"{RefueledAt:dd.MM.yyyy} · {OdometerKm:0} km · {Liters:0.00} L · ₾{Liters * PricePerLiter:0.00}";

    [Ignore]
    public string DisplayDetails =>
        $"{(IsFullTank ? "სრული ავზი" : "ნაწილობრივი შევსება")} · {FuelType} · {Station}";
}
