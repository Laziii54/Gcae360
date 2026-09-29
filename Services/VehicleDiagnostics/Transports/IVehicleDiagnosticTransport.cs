namespace CarScanner.Services.VehicleDiagnostics.Transports;

public interface IVehicleDiagnosticTransport
{
    string Name { get; }
    bool IsAvailable { get; }
    string ConnectionState { get; }
    Task InitializeAsync(CancellationToken token = default);
    Task DisconnectAsync(CancellationToken token = default);
    Task<string> SendAsync(string payload, CancellationToken token = default);
}
