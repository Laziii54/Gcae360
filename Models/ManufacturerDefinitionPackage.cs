namespace CarScanner.Models;

public enum ManufacturerDefinitionVerificationLevel
{
    MetadataOnly = 0,
    VerifiedReadOnly = 1,
    VerifiedService = 2,
    VerifiedCoding = 3
}

/// <summary>
/// A manufacturer identity package is not, by itself, proof of OEM access.
/// VehicleDefinitions is intentionally empty until an exact vehicle profile
/// has a reviewed source and one or more separately verified definition IDs.
/// </summary>
public sealed class ManufacturerDefinitionPackage
{
    public string Id { get; set; } = string.Empty;
    public string BrandId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public int Version { get; set; } = 1;
    public ManufacturerDefinitionVerificationLevel VerificationLevel { get; set; }
    public string CatalogNote { get; set; } = string.Empty;
    public List<VehicleDefinitionCoverage> VehicleDefinitions { get; set; } = new();
}

public sealed class VehicleDefinitionCoverage
{
    public string Id { get; set; } = string.Empty;
    public string VehicleProfile { get; set; } = string.Empty;
    public int? MinimumYear { get; set; }
    public int? MaximumYear { get; set; }
    public ManufacturerDefinitionVerificationLevel VerificationLevel { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public List<string> DefinitionIds { get; set; } = new();
}

public sealed class ManufacturerDefinitionCatalogFile
{
    public int SchemaVersion { get; set; } = 1;
    public List<ManufacturerDefinitionPackage> Manufacturers { get; set; } = new();
}

public enum ManufacturerDefinitionResolutionState
{
    VehicleNotVerified,
    ManufacturerUnknown,
    MetadataOnly,
    VerifiedReadOnly,
    VerifiedService,
    VerifiedCoding
}

public sealed record ManufacturerDefinitionResolution(
    ManufacturerDefinitionResolutionState State,
    ManufacturerDefinitionPackage? Package,
    IReadOnlyList<VehicleDefinitionCoverage> MatchingDefinitions,
    string Evidence)
{
    public bool HasVerifiedReadOnlyDefinition =>
        State >= ManufacturerDefinitionResolutionState.VerifiedReadOnly &&
        MatchingDefinitions.Count > 0;
}
