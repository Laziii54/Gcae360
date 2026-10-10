namespace CarScanner.Models;

public enum ServiceProcedureExecutionType
{
    Manual,
    OemDiagnostic
}

public sealed class ServiceProcedureDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string Model { get; set; } = "*";

    public int MinimumYear { get; set; }

    public int MaximumYear { get; set; }

    public string Category { get; set; } = "Maintenance";

    public string NameKa { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string DescriptionKa { get; set; } = string.Empty;

    public string DescriptionEn { get; set; } = string.Empty;

    public ServiceProcedureExecutionType ExecutionType { get; set; }

    /// <summary>
    /// Corresponding operationId from oem_operations.json.
    /// Empty for manual procedures.
    /// </summary>
    public string OemOperationId { get; set; } = string.Empty;

    public List<ServiceProcedureStep> Steps { get; set; } = new();

    public string Source { get; set; } = string.Empty;

    // ------------------------------------------------------------------
    // Optional fields (schema stays at version 1; records without them keep
    // working and get sensible values from ServiceKindClassifier).
    // ------------------------------------------------------------------

    /// <summary>OilService, MaintenanceReset, Inspection, TirePressure, Battery, ParkingBrake or Other. Empty = inferred.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Name of the reminder system, e.g. "ASSYST PLUS", "CBS", "Maintenance Minder".</summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>Engine restriction (text that must appear in the detected engine). Empty = any engine.</summary>
    public List<string> Engines { get; set; } = new();

    /// <summary>Fuel restriction, e.g. "Diesel", "Hybrid", "Electric". Empty = any fuel.</summary>
    public List<string> FuelTypes { get; set; } = new();

    /// <summary>Control module the procedure talks to. Empty for dashboard/menu procedures.</summary>
    public string ControlModule { get; set; } = string.Empty;

    /// <summary>Adapter / protocol capabilities an automatic procedure needs (e.g. "can-uds").</summary>
    public List<string> RequiredCapabilities { get; set; } = new();

    public List<ServiceProcedureText> Preconditions { get; set; } = new();

    public string ExpectedResultKa { get; set; } = string.Empty;

    public string ExpectedResultEn { get; set; } = string.Empty;

    /// <summary>How the result is checked: "ReadBack", "Dashboard" or empty (cannot be verified by the app).</summary>
    public string VerificationMethod { get; set; } = string.Empty;

    /// <summary>VehicleVerified, SourceDocumented or Unverified. Empty = inferred from <see cref="Source"/>.</summary>
    public string VerificationStatus { get; set; } = string.Empty;

    /// <summary>true when the documented method needs a workshop/diagnostic tool the app cannot replace.</summary>
    public bool ToolRequired { get; set; }
}

public sealed class ServiceProcedureText
{
    public string TextKa { get; set; } = string.Empty;

    public string TextEn { get; set; } = string.Empty;
}

public sealed class ServiceProcedureStep
{
    public int Order { get; set; }

    public string TextKa { get; set; } = string.Empty;

    public string TextEn { get; set; } = string.Empty;
}