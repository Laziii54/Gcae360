using SQLite;

namespace CarScanner.Models;

/// <summary>
/// A completed trip based on actual OBD speed and direct/calculated fuel-rate
/// samples. Values are stored in canonical metric units for correct later
/// presentation in either unit system.
/// </summary>
[Table("FuelTripRecord")]
public sealed class FuelTripRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public string VIN { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public double DistanceKm { get; set; }
    public double FuelLiters { get; set; }
    public double MovingSeconds { get; set; }
    public double IdleSeconds { get; set; }
    public double AverageSpeedKph { get; set; }
    public double MaximumSpeedKph { get; set; }
    public double? AverageFuelLPer100Km { get; set; }
    public double? FuelPricePerLiter { get; set; }
    public string FuelDataSource { get; set; } = string.Empty;

    // Electric / hybrid trips: net battery energy (used − regenerated).
    public double? EnergyKWh { get; set; }
    public double? RegeneratedKWh { get; set; }
    public double? EnergyPricePerKWh { get; set; }
}
