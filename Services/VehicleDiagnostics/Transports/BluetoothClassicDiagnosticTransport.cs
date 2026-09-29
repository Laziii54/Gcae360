namespace CarScanner.Services.VehicleDiagnostics.Transports;

public sealed class BluetoothClassicDiagnosticTransport : IVehicleDiagnosticTransport
{
    public string Name => "Bluetooth Classic / SPP";
    public bool IsAvailable => OperatingSystem.IsAndroid();
    public string ConnectionState => IsAvailable ? "Available on supported platform" : "Unsupported on this platform";
    public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken token = default) => Task.CompletedTask;
    public Task<string> SendAsync(string payload, CancellationToken token = default) => throw new NotSupportedException("Bluetooth Classic transport requires a platform-specific verified adapter implementation.");
}
