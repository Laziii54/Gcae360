namespace CarScanner.Models;

public enum HybridProfileKind
{
    Unsupported,
    ToyotaPrius,
    ChevroletVolt,
    HyundaiIoniq,
    Tesla,
    GenericElectric,
    GenericHybrid
}

public sealed record HybridHealthResult(
    HybridProfileKind Profile,
    string VehicleName,
    int? Score,
    string Rating,
    double? CellVoltageDelta,
    double? MinimumCellVoltage,
    double? MaximumCellVoltage,
    double? MinimumTemperatureC,
    double? MaximumTemperatureC,
    double? EstimatedRemainingLifePercent,
    bool RemainingLifeFromEcu,
    int CellReadingCount,
    int TemperatureReadingCount,
    string Status,
    string ValidationStatus,
    string Evidence);
