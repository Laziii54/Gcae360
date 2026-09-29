namespace CarScanner.Models;

public sealed class ObdScanReport
{
    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; set; }
    public VehicleInfo Vehicle { get; init; } = new();
    public string Protocol { get; init; } = "Unknown";
    public string AdapterName { get; init; } = string.Empty;
    public string AdapterVoltage { get; init; } = string.Empty;
    public bool EcuResponded { get; init; }
    /// <summary>How many of the three standard DTC services (03/07/0A) completed.</summary>
    public int DtcServicesRead { get; init; }
    public int DtcServicesExpected { get; init; } = 3;
    public bool StoredDtcRead { get; init; }
    public bool PendingDtcRead { get; init; }
    public bool PermanentDtcRead { get; init; }
    public bool VehicleInfoRead { get; init; }
    public bool ReadinessRead { get; init; }
    public int SnapshotPidsRequested { get; init; }
    public IReadOnlyList<SupportedPid> SupportedPids { get; init; } = Array.Empty<SupportedPid>();
    public PidScanStats PidStats { get; init; } = new();
    public IReadOnlyList<DiagnosticCode> StoredCodes { get; init; } = Array.Empty<DiagnosticCode>();
    public IReadOnlyList<DiagnosticCode> PendingCodes { get; init; } = Array.Empty<DiagnosticCode>();
    public IReadOnlyList<DiagnosticCode> PermanentCodes { get; init; } = Array.Empty<DiagnosticCode>();
    public ReadinessInfo? Readiness { get; init; }
    public string Mode05Raw { get; init; } = string.Empty;
    public string Mode06Raw { get; init; } = string.Empty;
    public string Mode05Summary { get; init; } = "Unavailable";
    public string Mode06Summary { get; init; } = "Unavailable";
    public IReadOnlyList<ObdTestItem> Mode05Tests { get; init; } = Array.Empty<ObdTestItem>();
    public IReadOnlyList<ObdTestItem> Mode06Tests { get; init; } = Array.Empty<ObdTestItem>();
    public IReadOnlyList<SensorValue> Sensors { get; init; } = Array.Empty<SensorValue>();
    public IReadOnlyList<EcuIdentifier> Identifiers { get; init; } = Array.Empty<EcuIdentifier>();
    public string? OdometerKm { get; init; }
    public IReadOnlyList<FreezeFrame> FreezeFrames { get; init; } = Array.Empty<FreezeFrame>();
    public string Notes { get; init; } = string.Empty;

    /// <summary>Other vehicle modules (ABS, airbag, body …) that were asked for trouble codes.</summary>
    public List<ModuleDtcReport> Modules { get; init; } = new();

    /// <summary>How many module addresses were tried.</summary>
    public int ModulesChecked { get; init; }

    /// <summary>Why modules were not scanned (e.g. car moving, not CAN); empty when scanned.</summary>
    public string ModuleScanNote { get; init; } = string.Empty;

    public bool HasFaults => StoredCodes.Count > 0 || PendingCodes.Count > 0 || PermanentCodes.Count > 0 ||
        Modules.Any(module => module.Codes.Count > 0);
}
