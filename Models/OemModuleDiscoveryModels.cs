namespace CarScanner.Models;

public enum OemModuleProbeState
{
    Available,
    RespondedLimited,
    NoResponse,
    TransportUnsupported,
    MalformedResponse,
    Error
}

public sealed record OemDiscoveredModule(
    string DefinitionId,
    string EcuId,
    string Name,
    string EcuName,
    string CanBus,
    string Source,
    string Notes);

public sealed record OemModuleProbeResult(
    string DefinitionId,
    string EcuId,
    string Name,
    string EcuName,
    string CanBus,
    OemModuleProbeState State,
    string Detail,
    long ElapsedMilliseconds,
    string Source,
    byte ResponseService = 0,
    byte[]? ResponsePayload = null,
    byte? NegativeResponseCode = null);

public sealed record OemModuleDiscoveryResult(
    IReadOnlyList<OemDiscoveredModule> Modules,
    IReadOnlyList<OemModuleProbeResult> Probes,
    string Status,
    string Detail)
{
    public static OemModuleDiscoveryResult Found(
        IReadOnlyList<OemDiscoveredModule> modules,
        IReadOnlyList<OemModuleProbeResult> probes) =>
        new(modules, probes, "AVAILABLE", $"{modules.Count} OEM ECU-მ რეალურად დააბრუნა read-only პასუხი.");

    public static OemModuleDiscoveryResult Partial(
        IReadOnlyList<OemDiscoveredModule> modules,
        IReadOnlyList<OemModuleProbeResult> probes) =>
        new(modules, probes, "PARTIAL", $"{probes.Count} ECU მისამართი შემოწმდა; {modules.Count}-მა სრული დადებითი პასუხი დააბრუნა.");

    public static OemModuleDiscoveryResult NotReady(string detail) =>
        new(Array.Empty<OemDiscoveredModule>(), Array.Empty<OemModuleProbeResult>(), "NOT READY", detail);

    public static OemModuleDiscoveryResult NoDefinition(string detail) =>
        new(Array.Empty<OemDiscoveredModule>(), Array.Empty<OemModuleProbeResult>(), "OEM PROFILE REQUIRED", detail);

    public static OemModuleDiscoveryResult NoResponse(
        string detail,
        IReadOnlyList<OemModuleProbeResult>? probes = null) =>
        new(Array.Empty<OemDiscoveredModule>(), probes ?? Array.Empty<OemModuleProbeResult>(), "NO OEM RESPONSE", detail);
}
