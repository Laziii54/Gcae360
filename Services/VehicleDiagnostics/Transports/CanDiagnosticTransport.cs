namespace CarScanner.Services.VehicleDiagnostics.Transports;

public sealed class CanDiagnosticTransport : IVehicleDiagnosticTransport
{
    public string Name => "CAN transport";
    public bool IsAvailable => false;
    public string ConnectionState => "Requires a verified CAN adapter profile";
    public Task InitializeAsync(CancellationToken token = default) => throw new NotSupportedException("CAN transport requires a verified adapter implementation.");
    public Task DisconnectAsync(CancellationToken token = default) => Task.CompletedTask;
    public Task<string> SendAsync(string payload, CancellationToken token = default) => throw new NotSupportedException("CAN transport requires a verified adapter implementation.");
}
