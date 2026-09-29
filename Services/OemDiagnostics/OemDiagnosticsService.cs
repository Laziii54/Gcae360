using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

public sealed class OemDiagnosticsService
{
    private readonly OemProfileResolver _resolver;
    private readonly VehicleProfileResolverService _vehicleProfiles;

    public OemDiagnosticsService(
        OemProfileResolver resolver,
        VehicleProfileResolverService vehicleProfiles)
    {
        _resolver = resolver;
        _vehicleProfiles = vehicleProfiles;
    }

    public IOemDiagnosticsProfile? ResolveProfile(VehicleInfo vehicle)
    {
        var resolved = _vehicleProfiles.Resolve(vehicle);
        return resolved.CanUseManufacturerSpecificDefinitions
            ? _resolver.Resolve(resolved.DiagnosticVehicle)
            : null;
    }

}
