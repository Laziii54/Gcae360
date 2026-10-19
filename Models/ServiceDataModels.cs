namespace CarScanner.Models;

public enum ServiceMetricKind
{
    Other,
    OilLifePercent,
    OilLevel,
    DistanceToNextService,
    DaysToNextService,
    DistanceSinceOilChange,
    OilChangeInterval,
    InspectionDistance,
    InspectionDays,
    MaintenanceCounter,
    ServiceStatus,

    /// <summary>Oil wear on the reverse scale (higher = more worn), as some makes report it. NOT "oil life remaining": never shown or colored as such.</summary>
    OilDegradationPercent
}

public enum ServiceMetricEvidenceLevel
{
    ProductionCatalog,
    ResearchCatalog
}

/// <summary>
/// Physical diagnostic target resolved from an OBDb-style signal definition.
/// This model is deliberately read-only: it contains no write/routine/security
/// request and is only accepted by OBDService's 0x19/0x22 read path.
/// </summary>
public sealed record ServiceDiagnosticAddress(
    int TransmitCanId,
    int? ReceiveCanId,
    bool Extended29Bit,
    byte? ExtendedAddress,
    string AddressingKind,
    string Detail);

/// <summary>
/// One real value returned by the connected vehicle. A value is created only
/// after a positive, identifier-matching ECU response has been decoded.
/// </summary>
public sealed record ServiceMetricReading(
    string Id,
    ServiceMetricKind Kind,
    string SourceName,
    double NumericValue,
    string Unit,
    string Detail,
    string SourceCatalog,
    int TransmitCanId,
    int DataIdentifier,
    int? ReceiveCanId = null,
    bool Extended29Bit = false,
    byte? ExtendedAddress = null,
    ServiceMetricEvidenceLevel EvidenceLevel = ServiceMetricEvidenceLevel.ProductionCatalog);

/// <summary>UI-ready representation of one service value.</summary>
public sealed class ServiceMetricDisplayItem
{
    public string Id { get; init; } = string.Empty;
    public ServiceMetricKind Kind { get; init; }
    public string IconText { get; init; } = "SRV";
    public string Name { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string ValueColor { get; init; } = "#4ADE80";
    public string Detail { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
    public string EvidenceBadge { get; init; } = string.Empty;
    public string EvidenceColor { get; init; } = "#4ADE80";
}

public sealed record ServiceDataReadResult(
    IReadOnlyList<ServiceMetricReading> Values,
    string Status,
    string Detail)
{
    public bool HasValues => Values.Count > 0;
    public int ProductionCandidateCount { get; init; }
    public int ResearchCandidateCount { get; init; }
    public int AttemptedRequestCount { get; init; }
    public int UnsupportedAddressCount { get; init; }
}
