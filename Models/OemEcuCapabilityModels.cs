namespace CarScanner.Models;

public enum OemCapabilityAvailability
{
    Available,
    Conditional,
    NotSupported,
    NoResponse,
    TransportUnsupported,
    InvalidResponse,
    NotChecked,
    Error
}

public sealed record OemCapabilityProbeResult(
    string CapabilityCode,
    string CommandId,
    string Name,
    OemCapabilityAvailability Availability,
    string Evidence,
    long ElapsedMilliseconds,
    byte ResponseService = 0,
    byte[]? ResponsePayload = null,
    byte? NegativeResponseCode = null)
{
    public string StatusText => Availability switch
    {
        OemCapabilityAvailability.Available => "Available",
        OemCapabilityAvailability.Conditional => "Conditional",
        OemCapabilityAvailability.NotSupported => "Not Supported",
        OemCapabilityAvailability.NoResponse => "No Response",
        OemCapabilityAvailability.TransportUnsupported => "Transport Unsupported",
        OemCapabilityAvailability.InvalidResponse => "Invalid Response",
        OemCapabilityAvailability.NotChecked => "Not Checked",
        _ => "Error"
    };
}

public sealed record OemEcuCapabilityProfile(
    string DefinitionId,
    string EcuId,
    string EcuName,
    IReadOnlyList<OemCapabilityProbeResult> Capabilities)
{
    public string Summary => Capabilities.Count == 0
        ? "Capability probe არ შესრულებულა."
        : string.Join("\n", Capabilities.Select(capability =>
            $"{capability.CapabilityCode}: {capability.StatusText} — {capability.Evidence}"));

    public string StatusFor(string capabilityCode) =>
        Capabilities.FirstOrDefault(item =>
            item.CapabilityCode.Equals(capabilityCode, StringComparison.OrdinalIgnoreCase)) is { } capability
            ? $"{capability.CapabilityCode}: {capability.StatusText} — {capability.Evidence}"
            : $"{capabilityCode}: Not Checked";
}

public sealed record OemCapabilityDetectionResult(
    IReadOnlyList<OemEcuCapabilityProfile> Ecus,
    string Status,
    string Detail)
{
    public static OemCapabilityDetectionResult Empty(string detail) =>
        new(Array.Empty<OemEcuCapabilityProfile>(), "NOT CHECKED", detail);
}
