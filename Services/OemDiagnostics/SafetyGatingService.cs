using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

public sealed class SafetyGatingService
{
    public DiagnosticOperationResult<bool> Validate(VehicleCapability capability, bool vehicleDetected, bool ecuIdentified, bool stableVoltage, bool sessionValid, bool securityValid, bool userConfirmed)
    {
        if (!vehicleDetected) return new(DiagnosticOperationStatus.VehicleNotConnected, false, "Vehicle not detected.");
        if (!ecuIdentified) return new(DiagnosticOperationStatus.PreconditionsFailed, false, "ECU identification is required before this operation.");
        if (!capability.EnabledByDefault && !userConfirmed) return new(DiagnosticOperationStatus.PreconditionsFailed, false, "Explicit user confirmation is required.");
        if (capability.RequiresVoltageCheck && !stableVoltage) return new(DiagnosticOperationStatus.PreconditionsFailed, false, "Vehicle/adapter voltage is not within a safe range.");
        if (!sessionValid) return new(DiagnosticOperationStatus.PreconditionsFailed, false, "Diagnostic session is not valid for this operation.");
        if (capability.RequiresSecurityAccess && !securityValid) return new(DiagnosticOperationStatus.SecurityDenied, false, "Security access is required and has not been granted.");
        return new(DiagnosticOperationStatus.Success, true, "Safety preconditions passed.");
    }
}
