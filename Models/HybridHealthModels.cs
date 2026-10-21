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
    string Evidence,
    bool IsBlockVoltage = false,
    string RawLog = "",
    // Pack read directly from the battery ECU (Toyota / Lexus, Hyundai / Kia; null otherwise).
    CarScanner.Services.HvPackKind? PackKind = null,
    IReadOnlyList<double>? Blocks = null,
    IReadOnlyList<int>? Resistances = null,
    double? SocPercent = null,
    double? CurrentA = null,
    double? PackVoltage = null,
    int? WeakestBlock = null,
    double WeakestDeviation = 0,
    double? SohPercent = null,
    // Cell numbers when some cells did not answer (null = 1…n).
    IReadOnlyList<int>? BlockNumbers = null,
    // Capacity estimated by the battery controller (GM), in Ah.
    double? CapacityAh = null,
    bool LoadTestSupported = true,
    string PackNote = "",
    // Why a read that returned no pack ended, when the evidence decides it (null: nothing was decided, the status text is the old wording).
    CarScanner.Services.HybridFailure? Failure = null);

/// <summary>Live state during the Toyota load test.</summary>
public sealed record HybridLoadProgress(
    int Seconds,
    int TotalSeconds,
    int Samples,
    double Spread,
    double MaxSpread,
    double? CurrentA,
    double? MaxDischargeA);

/// <summary>Outcome of the Toyota load test.</summary>
public sealed record HybridLoadOutcome(
    int Samples,
    double MaxSpread,
    double? MaxDischargeA,
    IReadOnlyList<double> WorstBlocks,
    int? WeakestBlock,
    double WeakestDeviation);
