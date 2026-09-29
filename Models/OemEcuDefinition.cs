namespace CarScanner.Models;

/// <summary>
/// One reviewed, vehicle-specific OEM ECU definition. Only read-only UDS
/// commands are accepted by the v2 catalog. Service/coding payloads belong to
/// a separate, more restrictive system and cannot be placed in this model.
/// </summary>
public sealed class OemEcuDefinition
{
    public string DefinitionId { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; } = 1;
    public string PackageId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string VehicleProfile { get; set; } = string.Empty;
    public string Generation { get; set; } = string.Empty;
    public int MinimumYear { get; set; }
    public int MaximumYear { get; set; }
    public string Powertrain { get; set; } = string.Empty;
    public string EcuId { get; set; } = string.Empty;
    public string EcuName { get; set; } = string.Empty;
    public OemTransportDefinition Transport { get; set; } = new();
    public List<OemReadOnlyCommandDefinition> ReadOnlyCommands { get; set; } = new();
    /// <summary>
    /// Reviewed mapping from a three-byte UDS DTC to an existing SAE-style
    /// catalog code.  The title/description itself deliberately remains in
    /// the YAML DTC catalog so there is only one source of diagnostic text.
    /// </summary>
    public List<OemDtcMappingDefinition> DtcMappings { get; set; } = new();
    public OemDefinitionEvidence Evidence { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}

public sealed class OemDtcMappingDefinition
{
    /// <summary>Three-byte UDS DTC, exactly six hexadecimal characters.</summary>
    public string RawCodeHex { get; set; } = string.Empty;
    /// <summary>Existing P/B/C/U code whose text is loaded from DtcCatalog.sqlite.</summary>
    public string CatalogCode { get; set; } = string.Empty;
}

public sealed class OemTransportDefinition
{
    /// <summary>Currently only UDS_ISOTP_CAN is executable.</summary>
    public string Protocol { get; set; } = string.Empty;
    public string CanBus { get; set; } = string.Empty;
    public int BaudRateKbps { get; set; }
    public int TxCanId { get; set; }
    public int RxCanId { get; set; }
    public bool Extended29Bit { get; set; }
}

public sealed class OemReadOnlyCommandDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string RequestHex { get; set; } = string.Empty;
    public bool DiscoveryProbe { get; set; }
    public bool ProbeDuringCapabilityDetection { get; set; } = true;
    public int TimeoutMs { get; set; } = 1500;
    public List<OemResponseFieldDefinition> ResponseFields { get; set; } = new();
    /// <summary>DID layouts used only by a UDS 0x19/0x04 DTC snapshot template.</summary>
    public List<OemSnapshotDidDefinition> SnapshotDids { get; set; } = new();
    /// <summary>Limits automatic snapshot reads after a scan.</summary>
    public int MaxSnapshotDtcs { get; set; } = 3;
}

public sealed class OemSnapshotDidDefinition
{
    public int DataIdentifier { get; set; }
    public int DataLength { get; set; }
    public List<OemResponseFieldDefinition> Fields { get; set; } = new();
}

public sealed class OemResponseFieldDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>Live-data grouping; optional for identification fields.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Offset relative to bytes after the echoed two-byte DID.</summary>
    public int ByteOffset { get; set; }
    public int ByteLength { get; set; }
    public string Encoding { get; set; } = string.Empty;
    public double Scale { get; set; } = 1d;
    public double ValueOffset { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public sealed class OemDefinitionEvidence
{
    public string EvidenceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; set; }
    public string ObservedProtocol { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string ReviewedBy { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAtUtc { get; set; }
}

public sealed class OemDefinitionPackage
{
    public int SchemaVersion { get; set; } = 2;
    public int CatalogVersion { get; set; } = 1;
    public List<OemEcuDefinition> Definitions { get; set; } = new();
}
