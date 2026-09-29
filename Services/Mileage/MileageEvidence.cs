namespace CarScanner.Services.Mileage;

public sealed record MileageEvidence(
    string Source,
    string Ecu,
    double ValueKm,
    double Confidence,
    string Details);
