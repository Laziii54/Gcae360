namespace CarScanner.Models;

public enum OemOperationType
{
    ActiveTest,
    Service,
    ManualService,
    Coding
}

public enum OemOperationAvailabilityState
{
    Available,
    PreconditionsRequired,
    NotSupported,
    NoResponse,
    InvalidDefinition,
    Error
}

public sealed class OemOperationDefinition
{
    public string OperationId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string DefinitionId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string VehicleProfile { get; set; } = string.Empty;
    public int MinimumYear { get; set; }
    public int MaximumYear { get; set; }
    public string EcuId { get; set; } = string.Empty;
    public string EcuName { get; set; } = string.Empty;
    public OemOperationType Type { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public OemOperationSafetyDefinition Safety { get; set; } = new();
    public OemOperationValidationDefinition Validation { get; set; } = new();
    public string BackupReadHex { get; set; } = string.Empty;
    public List<OemOperationStepDefinition> Steps { get; set; } = new();
    public OemDefinitionEvidence Evidence { get; set; } = new();
}

/// <summary>
/// Two independent gates for an ECU-changing operation. Simulation proves the
/// package is internally coherent; only a real vehicle capture may authorize
/// production execution.
/// </summary>
public sealed class OemOperationValidationDefinition
{
    public bool SimulationValidated { get; set; }
    public bool VehicleValidated { get; set; }
    public string VehicleCaptureReference { get; set; } = string.Empty;
    public string ValidationNotes { get; set; } = string.Empty;
}

public sealed class OemOperationSafetyDefinition
{
    public bool RequiresUserConfirmation { get; set; } = true;
    public bool RequiresStationary { get; set; } = true;
    public bool RequiresIgnitionOn { get; set; } = true;
    public string RequiredEngineState { get; set; } = "ANY";
    public double MinimumVoltage { get; set; } = 12.0;
    public double MaximumVoltage { get; set; } = 15.5;
    public bool RequiresSecurityAccess { get; set; }
    public int MaximumRunSeconds { get; set; } = 5;
}

public sealed class OemOperationStepDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "EXECUTE";
    public string RequestHex { get; set; } = string.Empty;
    /// <summary>Required payload prefix for a VERIFY response.</summary>
    public string ExpectedResponsePayloadHex { get; set; } = string.Empty;
    /// <summary>Full simulated UDS response, including the response SID.</summary>
    public string SimulationResponseHex { get; set; } = string.Empty;
    public int DelayAfterMs { get; set; }
    public int TimeoutMs { get; set; } = 2000;
}

public sealed class OemOperationPackage
{
    public int SchemaVersion { get; set; } = 1;
    public int CatalogVersion { get; set; } = 1;
    public List<OemOperationDefinition> Operations { get; set; } = new();
}

public sealed record OemOperationAvailability(
    OemOperationDefinition Definition,
    OemOperationAvailabilityState State,
    string Detail)
{
    public bool CanExecute => State == OemOperationAvailabilityState.Available;
    public string StatusText => State switch
    {
        OemOperationAvailabilityState.Available => "AVAILABLE",
        OemOperationAvailabilityState.PreconditionsRequired => "PRECONDITIONS REQUIRED",
        OemOperationAvailabilityState.NotSupported => "NOT SUPPORTED",
        OemOperationAvailabilityState.NoResponse => "NO RESPONSE",
        OemOperationAvailabilityState.InvalidDefinition => "INVALID DEFINITION",
        _ => "ERROR"
    };
}

public sealed record OemOperationAvailabilityResult(
    IReadOnlyList<OemOperationAvailability> Operations,
    string Status,
    string Detail);

public sealed record OemOperationExecutionResult(
    string OperationId,
    DiagnosticOperationStatus Status,
    string Message,
    IReadOnlyList<string> CompletedSteps,
    string BackupPath = "")
{
    public bool IsSuccess => Status == DiagnosticOperationStatus.Success;
}

public sealed class OemCodingBackup
{
    public string OperationId { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    public string EcuId { get; set; } = string.Empty;
    public string ReadRequestHex { get; set; } = string.Empty;
    public string ResponsePayloadHex { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string EvidenceSource { get; set; } = string.Empty;
}
