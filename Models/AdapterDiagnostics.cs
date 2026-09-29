namespace CarScanner.Models;

public sealed class AdapterDiagnostics
{
    public string AdapterName { get; init; } = "OBD-II adapter";
    public string Firmware { get; init; } = "Unavailable";
    public string Voltage { get; init; } = "Unavailable";
    public string Protocol { get; init; } = "Unknown";
    public bool BluetoothConnected { get; init; }
    public bool AdapterInitialized { get; init; }
    public bool Elm327Responding { get; init; }
    public bool BasicCommandResponding { get; init; }
    public bool VehicleResponding { get; init; }
    public bool EcuProbeAttempted { get; init; }
    public long ProbeLatencyMs { get; init; }
    public AdapterCapabilitySnapshot? CapabilitySnapshot { get; init; }
    public string OverallStatus => !BluetoothConnected ? "Bluetooth disconnected" : !AdapterInitialized ? "Adapter not initialized" : !VehicleResponding ? "Vehicle ECU not responding" : "Ready";
}

/// <summary>One completed, real adapter diagnostic stage.</summary>
public sealed record AdapterTestProgress(int Percent, string Stage);
