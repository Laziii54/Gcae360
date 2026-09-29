namespace CarScanner.Services.VehicleDiagnostics.Transports;

public sealed class UsbDiagnosticTransport : IVehicleDiagnosticTransport
{
    public string Name => "USB diagnostic transport";
    public bool IsAvailable => false;
    public string ConnectionState => "Not available in the current build";
    public Task InitializeAsync(CancellationToken token = default) => throw new NotSupportedException("USB diagnostic transport requires a verified platform implementation.");
    public Task DisconnectAsync(CancellationToken token = default) => Task.CompletedTask;
    public Task<string> SendAsync(string payload, CancellationToken token = default) => throw new NotSupportedException("USB diagnostic transport is not implemented for this adapter profile.");
}
