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

    // ---- added later (existing rows read as 0 / empty) -------------------------------------------------
    // Columns are added automatically by SQLite-net's CreateTable on the next start.

    /// <summary>"Fuel", "Hybrid" or "Electric" — the page mode the trip was recorded in.</summary>
    public string Powertrain { get; set; } = string.Empty;

    /// <summary>Combustion fuel the calculation assumed (e.g. "Gasoline"); empty for old trips.</summary>
    public string FuelKind { get; set; } = string.Empty;

    /// <summary>Share of the trip's litres that were calculated from air flow instead of reported by the ECU (0 = all direct, 1 = all estimated).</summary>
    public double EstimatedFuelShare { get; set; }

    /// <summary>Share of the running time that had a usable fuel reading (1 = complete).</summary>
    public double FuelCoverage { get; set; }

    /// <summary>true when the electric energy comes from state of charge × capacity (an estimate), not from battery current.</summary>
    public bool EnergyIsEstimate { get; set; }

    /// <summary>Distance driven with the engine stopped (derived from rpm and speed); hybrids only.</summary>
    public double? EngineOffKm { get; set; }

    /// <summary>Seconds with no usable data (disconnections, long gaps).</summary>
    public double GapSeconds { get; set; }

    /// <summary>true when the app was closed or crashed during the trip and this record was recovered from the last checkpoint.</summary>
    public bool IsInterrupted { get; set; }
}
