using System.Text.Json;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>What the existing ELM327 path is known to be able to reach. Never "supported".</summary>
public enum CaptureAdapterPath
{
    /// <summary>Standard OBD-II port; generic OBD-II and the read-only module scan can be tried.</summary>
    Elm327ObdPort,

    /// <summary>Not established for an ELM327 on the OBD port; other diagnostic equipment is probably needed.</summary>
    NeedsOtherEquipment,

    /// <summary>Generic OBD-II can be tried, but module access has no documented basis here.</summary>
    Unconfirmed
}

/// <summary>How much evidence exists for a target, strongest last. None of these means "supported".</summary>
public enum CaptureEvidenceLevel
{
    /// <summary>Only generation or brand metadata.</summary>
    MetadataOnly,

    /// <summary>Third-party references (addresses, signal files, community identifiers) that this project has not seen on a car.</summary>
    ThirdPartyReference,

    /// <summary>App code exists and is exercised by tests or the software simulator.</summary>
    SimulationTested,

    /// <summary>A capture from a real vehicle exists but has not been reviewed.</summary>
    RealVehicleCaptured
}

public sealed record CaptureTargetCoverage(
    string GenericObd2,
    string ModuleScanFamily,
    bool BrandProbeTable,
    int CatalogEcuAddresses,
    int CatalogEnhancedPids,
    IReadOnlyList<string> CatalogCanSignalFiles,
    bool VerifiedOemDefinition,
    IReadOnlyList<string> ManualServiceProcedures,
    IReadOnlyList<string> SimulationTested,
    int RealVehicleCaptures,
    string Notes);

public sealed record CaptureTarget(
    string Id,
    int Priority,
    string Make,
    string Model,
    IReadOnlyList<string> GenerationsInCatalog,
    string Powertrain,
    CaptureTargetCoverage Coverage,
    CaptureAdapterPath AdapterPath,
    string AdapterNotes,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> VehicleConditions)
{
    public string DisplayName => $"{Make} {Model}";

    public CaptureEvidenceLevel Evidence =>
        Coverage.RealVehicleCaptures > 0 ? CaptureEvidenceLevel.RealVehicleCaptured
        : Coverage.SimulationTested.Count > 0 ? CaptureEvidenceLevel.SimulationTested
        : Coverage.CatalogEcuAddresses > 0 || Coverage.CatalogEnhancedPids > 0 || Coverage.CatalogCanSignalFiles.Count > 0
            ? CaptureEvidenceLevel.ThirdPartyReference
            : CaptureEvidenceLevel.MetadataOnly;
}

/// <summary>
/// The list of vehicles chosen for real-vehicle investigation, with what the app already has for each and
/// what is still missing. It is a research list: loading it never enables a feature.
/// </summary>
public sealed class OemCaptureTargetCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private sealed record Root(int SchemaVersion, string Rule, IReadOnlyList<CaptureTarget>? Targets);

    public OemCaptureTargetCatalog(IReadOnlyList<CaptureTarget> targets) => Targets = targets;

    public IReadOnlyList<CaptureTarget> Targets { get; }

    public CaptureTarget? Find(string? id) =>
        Targets.FirstOrDefault(t => t.Id.Equals(id ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses the catalog; a damaged file gives an empty catalog rather than an exception.</summary>
    public static OemCaptureTargetCatalog Parse(string? json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<Root>(json ?? string.Empty, Options);
            var targets = (root?.Targets ?? Array.Empty<CaptureTarget>())
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id) && t.Coverage is not null)
                .OrderBy(t => t.Priority)
                .ToArray();
            return new OemCaptureTargetCatalog(targets);
        }
        catch (JsonException)
        {
            return new OemCaptureTargetCatalog(Array.Empty<CaptureTarget>());
        }
    }

    /// <summary>
    /// Reasons the catalog would overstate what is known. Empty when it is consistent: nothing may be marked
    /// verified without a real-vehicle capture, equipment limits must be explained, and every target must list
    /// what is missing.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Targets)
        {
            if (!seen.Add(t.Id))
                problems.Add($"{t.Id}: duplicate id");
            if (t.Coverage.VerifiedOemDefinition && t.Coverage.RealVehicleCaptures <= 0)
                problems.Add($"{t.Id}: marked verified without any real-vehicle capture");
            if (t.Coverage.SimulationTested.Count > 0 && t.Coverage.RealVehicleCaptures <= 0 &&
                t.Evidence == CaptureEvidenceLevel.RealVehicleCaptured)
                problems.Add($"{t.Id}: simulation evidence counted as real-vehicle evidence");
            if (t.AdapterPath != CaptureAdapterPath.Elm327ObdPort && string.IsNullOrWhiteSpace(t.AdapterNotes))
                problems.Add($"{t.Id}: adapter limitation is not explained");
            if (t.Missing.Count == 0)
                problems.Add($"{t.Id}: lists nothing as missing");
            if (t.Coverage.RealVehicleCaptures < 0 || t.Coverage.CatalogEcuAddresses < 0 || t.Coverage.CatalogEnhancedPids < 0)
                problems.Add($"{t.Id}: negative count");
        }

        return problems;
    }
}
