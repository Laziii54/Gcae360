namespace CarScanner.Models;

public enum VehicleModuleType
{
    Powertrain,
    AbsBrakes,
    SrsAirbag,
    BcmBody,
    HybridHv,
    Adas,
    Hvac,
    Cluster,
    Eps,
    Tcm,
    Tpms,
    Infotainment,
    Other
}

public sealed class VehicleDiagnosticModule
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public VehicleModuleType Type { get; init; }
    public string EcuName { get; init; } = string.Empty;
    public bool CommunicationAvailable { get; set; }
    public int DtcCount { get; set; }
    public string Status { get; set; } = "NotScanned";
    public string Severity { get; set; } = "Unknown";
    public DateTime? LastScanAt { get; set; }
    public IReadOnlyList<string> SupportedFunctions { get; init; } = Array.Empty<string>();
}

public sealed class VehicleCapability
{
    public string Code { get; init; } = string.Empty;
    public string Ecu { get; init; } = string.Empty;
    public string VehicleProfile { get; init; } = string.Empty;
    public string Transport { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public bool ReadSupported { get; init; }
    public bool ClearSupported { get; init; }
    public bool LiveDataSupported { get; init; }
    public bool RoutineSupported { get; init; }
    public bool WriteSupported { get; init; }
    public string SafetyLevel { get; init; } = "READ_ONLY";
    public bool RequiresVoltageCheck { get; init; }
    public bool RequiresSecurityAccess { get; init; }
    public bool EnabledByDefault { get; init; }
}
