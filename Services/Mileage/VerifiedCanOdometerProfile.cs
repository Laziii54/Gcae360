namespace CarScanner.Services.Mileage;

public sealed record VerifiedCanOdometerProfile(
    string Brand,
    string VehicleProfile,
    string Ecu,
    uint CanId,
    bool Extended29Bit,
    int OdometerStartBit,
    int OdometerBitLength,
    int UnitStartBit,
    int UnitBitLength,
    int KmUnitValue,
    int MilesUnitValue,
    double Confidence,
    string Source,
    string SourceUrl,
    string Notes);
