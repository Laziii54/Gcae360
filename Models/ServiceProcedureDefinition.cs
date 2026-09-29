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
}

public sealed class ServiceProcedureStep
{
    public int Order { get; set; }

    public string TextKa { get; set; } = string.Empty;

    public string TextEn { get; set; } = string.Empty;
}