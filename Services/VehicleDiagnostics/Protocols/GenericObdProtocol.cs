namespace CarScanner.Services.VehicleDiagnostics.Protocols;

public sealed class GenericObdProtocol : IDiagnosticProtocol
{
    public string Name => "Generic OBD-II";
    public bool IsVerified => true;
    public IReadOnlyCollection<string> Services { get; } = new[] { "DTC Read", "DTC Clear", "Live Data", "Readiness", "Freeze Frame" };
    public bool CanUse(string transportName) => transportName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) || transportName.Contains("USB", StringComparison.OrdinalIgnoreCase);
}
