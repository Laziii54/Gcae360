namespace CarScanner.Services.VehicleDiagnostics.Transports;

public sealed class KLineDiagnosticTransport : IVehicleDiagnosticTransport
{
    public string Name => "K-Line / ISO 9141 / ISO 14230";
    public bool IsAvailable => false;
    public string ConnectionState => "Requires a verified K-Line adapter profile";
    public Task InitializeAsync(CancellationToken token = default) => throw new NotSupportedException("K-Line transport requires a verified adapter implementation.");
    public Task DisconnectAsync(CancellationToken token = default) => Task.CompletedTask;
    public Task<string> SendAsync(string payload, CancellationToken token = default) => throw new NotSupportedException("K-Line transport requires a verified adapter implementation.");
}
