namespace CarScanner.Services.VehicleDiagnostics.Protocols;

public interface IDiagnosticProtocol
{
    string Name { get; }
    bool IsVerified { get; }
    IReadOnlyCollection<string> Services { get; }
    bool CanUse(string transportName);
}

public sealed record DiagnosticProtocolDescriptor(string Name, bool IsVerified, IReadOnlyCollection<string> Services);
