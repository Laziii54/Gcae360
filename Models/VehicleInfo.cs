namespace CarScanner.Models;

public enum VehicleDataOrigin
{
    Unknown,
    EcuMode09,
    VinDecoded,
    EcuPid,
    AdapterNegotiated,
    ExactVinCache,
    OnlineVinDecoder,
    VerifiedLocalDefinition,
    UserSelection
}

public sealed class VehicleInfo
{
    public string VIN { get; set; } = string.Empty;
    public string Trim { get; set; } = "Unknown";
    public string Manufacturer { get; set; } = "Unknown";
    public string Model { get; set; } = "Unknown";
    public string Year { get; set; } = "Unknown";
    public string Engine { get; set; } = "Unknown";
    public string FuelType { get; set; } = "Unknown";
    public string Protocol { get; set; } = "Unknown";
    public string ECUName { get; set; } = "Unknown";
    public string CalibrationId { get; set; } = "Unknown";
    public string CVN { get; set; } = "Unknown";

    public VehicleDataOrigin VinOrigin { get; set; }
    public VehicleDataOrigin ManufacturerOrigin { get; set; }
    public VehicleDataOrigin ModelOrigin { get; set; }
    public VehicleDataOrigin YearOrigin { get; set; }
    public VehicleDataOrigin EngineOrigin { get; set; }
    public VehicleDataOrigin FuelTypeOrigin { get; set; }
    public VehicleDataOrigin ProtocolOrigin { get; set; }
}
