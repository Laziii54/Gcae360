using SQLite;

namespace CarScanner.Models;

public sealed class VehicleProfile
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string VIN { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = "Unknown";
    public string Model { get; set; } = "Unknown";
    public string Year { get; set; } = "Unknown";
    public string Engine { get; set; } = "Unknown";
    public string FuelType { get; set; } = "Unknown";
    public string Protocol { get; set; } = "Unknown";
    public string ECUName { get; set; } = "Unknown";
    public string CalibrationId { get; set; } = "Unknown";
    public string CVN { get; set; } = "Unknown";
    public VehicleDataOrigin ManufacturerOrigin { get; set; }
    public VehicleDataOrigin ModelOrigin { get; set; }
    public VehicleDataOrigin YearOrigin { get; set; }
    public VehicleDataOrigin EngineOrigin { get; set; }
    public VehicleDataOrigin FuelTypeOrigin { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.Now;
}
