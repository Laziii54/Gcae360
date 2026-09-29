namespace CarScanner.Services.Mileage;

/// <summary>
/// A single verified, read-only UDS mileage definition.
/// No guessed IDs/DIDs should ever be added here.
/// </summary>
public sealed record VerifiedUdsMileageProfile(
    string Brand,
    string VehicleProfile,
    string Ecu,
    int TxCanId,
    int RxCanId,
    bool Extended29Bit,
    ushort Did,
    int DataOffset,
    int DataLength,
    string ByteOrder,
    string NumericEncoding,
    double Scale,
    double Offset,
    double MinKm,
    double MaxKm,
    double Confidence,
    string Source,
    string Notes);
