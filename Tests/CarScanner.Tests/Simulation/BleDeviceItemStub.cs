namespace CarScanner.Models;

/// <summary>Test stand-in for the app's Bluetooth device model (the real one depends on the BLE plugin).</summary>
public sealed class BleDeviceItem
{
    public string? NameOverride { get; init; }
}
