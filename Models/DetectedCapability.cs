namespace CarScanner.Models;

/// <summary>
/// Product layers shown to the driver. A layer is not a promise that every
/// function can run on every car: the detector records the evidence needed to
/// enable it for the current connection.
/// </summary>
public enum CapabilityTier
{
    UniversalObd,
    ProAnalytics,
    ManufacturerSpecific
}

public enum CapabilityState
{
    Available,
    Conditional,
    RequiresPro,
    RequiresVerifiedDefinition,
    NotAvailable,
    NotConnected
}

public sealed class DetectedCapability
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public CapabilityTier Tier { get; init; }
    public CapabilityState State { get; init; }

    public string StateText => State switch
    {
        CapabilityState.Available => "ხელმისაწვდომია",
        CapabilityState.Conditional => "ხელმისაწვდომია პირობებით",
        CapabilityState.RequiresPro => "საჭიროა PRO",
        CapabilityState.RequiresVerifiedDefinition => "საჭიროა დადასტურებული OEM პროფილი",
        CapabilityState.NotConnected => "OBD კავშირი არ არის",
        _ => "მიუწვდომელია"
    };

    public string StateColor => State switch
    {
        CapabilityState.Available => "#22C55E",
        CapabilityState.Conditional => "#F59E0B",
        CapabilityState.RequiresPro => "#A78BFA",
        CapabilityState.RequiresVerifiedDefinition => "#60A5FA",
        CapabilityState.NotConnected => "#94A3B8",
        _ => "#EF4444"
    };
}

public sealed class VehicleCapabilitySnapshot
{
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
    public string AdapterName { get; init; } = "OBD-II adapter";
    public string ConnectionSummary { get; init; } = "კავშირი არ შემოწმებულა";
    public string VehicleSummary { get; init; } = "ავტომობილი ჯერ არ არის ამოცნობილი";
    public string VehicleProfileStatus { get; init; } = "მანქანის პროფილი ჯერ არ არის დადასტურებული";
    public string Protocol { get; init; } = "—";
    public int SupportedPidCount { get; init; }
    public IReadOnlyList<DetectedCapability> Capabilities { get; init; } = Array.Empty<DetectedCapability>();
}

/// <summary>One real completed stage in a capability detection run.</summary>
public sealed record CapabilityDetectionProgress(int Percent, string Stage);
