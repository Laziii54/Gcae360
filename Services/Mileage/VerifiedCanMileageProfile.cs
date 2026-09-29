namespace CarScanner.Services.Mileage;

public sealed record VerifiedCanMileageProfile(
    string Brand, string VehicleProfile, string Ecu, uint CanId, bool Extended29Bit,
    int StartBit, int BitLength, bool LittleEndian, double Factor, double Offset,
    double MinKm, double MaxKm, double Confidence, string Source, string Notes);
