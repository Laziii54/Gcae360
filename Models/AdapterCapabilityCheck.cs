namespace CarScanner.Models;

public enum AdapterCapabilityCheckState
{
    Passed,
    Failed,
    NotSupported,
    NotVerified
}

/// <summary>One capability that was actually tested, or explicitly left
/// unverified because the active vehicle protocol cannot prove it.</summary>
public sealed record AdapterCapabilityCheck(
    string Id,
    string Name,
    AdapterCapabilityCheckState State,
    string Evidence);

public sealed class AdapterCapabilitySnapshot
{
    public DateTime TestedAtUtc { get; init; } = DateTime.UtcNow;
    public string DeviceDescription { get; init; } = "Unavailable";
    public string ProtocolNumber { get; init; } = "Unknown";
    public string ActiveBus { get; init; } = "Unknown";
    public string Addressing { get; init; } = "Not verified";
    public int SafeCommandsAttempted { get; init; }
    public int SafeCommandsPassed { get; init; }
    public long MinimumLatencyMs { get; init; }
    public long AverageLatencyMs { get; init; }
    public long MaximumLatencyMs { get; init; }
    public double CommandThroughputPerSecond { get; init; }
    public IReadOnlyList<AdapterCapabilityCheck> Checks { get; init; } =
        Array.Empty<AdapterCapabilityCheck>();

    public AdapterCapabilityCheck? Find(string id) =>
        Checks.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
