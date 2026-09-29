namespace CarScanner.Services.VehicleDiagnostics.Transports;

public sealed class BleDiagnosticTransport : IVehicleDiagnosticTransport
{
    private readonly IBluetoothService _bluetooth;
    public BleDiagnosticTransport(IBluetoothService bluetooth) => _bluetooth = bluetooth;
    public string Name => "Bluetooth Low Energy";
    public bool IsAvailable => _bluetooth.IsConnected;
    public string ConnectionState => _bluetooth.IsConnected ? "Connected" : "Disconnected";
    public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken token = default) => _bluetooth.DisconnectAsync();
    public Task<string> SendAsync(string payload, CancellationToken token = default) => _bluetooth.SendCommandAsync(payload, cancellationToken: token);
}
