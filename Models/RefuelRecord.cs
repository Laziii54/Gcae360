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

    /// <summary>
    /// Unit-aware summary (odometer in km or mi) built by FuelViewModel with
    /// UnitSystemService; the model itself cannot see the user's unit choice.
    /// </summary>
    [Ignore]
    public string? FormattedSummary { get; set; }

    [Ignore]
    public string DisplaySummary =>
        FormattedSummary ??
        $"{RefueledAt:dd.MM.yyyy} · {OdometerKm:0} km · {Liters:0.00} L · ₾{Liters * PricePerLiter:0.00}";

    [Ignore]
    public string DisplayDetails =>
        $"{(IsFullTank ? "სრული ავზი" : "ნაწილობრივი შევსება")} · {FuelType} · {Station}";
}
