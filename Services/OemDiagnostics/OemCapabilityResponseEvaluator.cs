using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

public static class OemCapabilityResponseEvaluator
{
    public static OemCapabilityProbeResult Evaluate(
        OemReadOnlyCommandDefinition command,
        UdsResponse response,
        long elapsedMilliseconds) =>
        new(
            command.CapabilityCode,
            command.Id,
            command.Name,
            MapAvailability(response),
            BuildEvidence(response),
            elapsedMilliseconds,
            response.Service,
            response.Payload,
            response.NegativeResponseCode);

    public static OemCapabilityProbeResult FromDiscovery(
        OemReadOnlyCommandDefinition command,
        OemModuleProbeResult probe) =>
        new(
            command.CapabilityCode,
            command.Id,
            command.Name,
            probe.State switch
            {
                OemModuleProbeState.Available => OemCapabilityAvailability.Available,
                OemModuleProbeState.RespondedLimited => OemCapabilityAvailability.Conditional,
                OemModuleProbeState.NoResponse => OemCapabilityAvailability.NoResponse,
                OemModuleProbeState.TransportUnsupported => OemCapabilityAvailability.TransportUnsupported,
                OemModuleProbeState.MalformedResponse => OemCapabilityAvailability.InvalidResponse,
                _ => OemCapabilityAvailability.Error
            },
            probe.Detail,
            probe.ElapsedMilliseconds,
            probe.ResponseService,
            probe.ResponsePayload,
            probe.NegativeResponseCode);

    public static OemCapabilityProbeResult NotChecked(OemReadOnlyCommandDefinition command) =>
        new(command.CapabilityCode, command.Id, command.Name,
            OemCapabilityAvailability.NotChecked,
            "Definition-ში არსებობს, მაგრამ ავტომატურ capability probe-ად მონიშნული არ არის.", 0);

    private static OemCapabilityAvailability MapAvailability(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Positive => OemCapabilityAvailability.Available,
        UdsResponseKind.Negative when response.NegativeResponseCode is 0x11 or 0x12 or 0x31 =>
            OemCapabilityAvailability.NotSupported,
        UdsResponseKind.Negative => OemCapabilityAvailability.Conditional,
        UdsResponseKind.Timeout => OemCapabilityAvailability.NoResponse,
        UdsResponseKind.Unsupported => OemCapabilityAvailability.TransportUnsupported,
        UdsResponseKind.Malformed => OemCapabilityAvailability.InvalidResponse,
        _ => OemCapabilityAvailability.Error
    };

    private static string BuildEvidence(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Positive => $"ECU positive response 0x{response.Service:X2}",
        UdsResponseKind.Negative when response.NegativeResponseCode.HasValue =>
            $"ECU negative response NRC 0x{response.NegativeResponseCode.Value:X2}: {response.Message}",
        UdsResponseKind.Timeout => "ECU response timeout",
        UdsResponseKind.Unsupported => $"Adapter/transport: {response.Message}",
        UdsResponseKind.Malformed => $"Response validation failed: {response.Message}",
        _ => string.IsNullOrWhiteSpace(response.Message) ? "Capability check failed" : response.Message
    };
}
