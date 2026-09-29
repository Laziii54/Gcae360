using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>Connection-scoped source of truth for resolved vehicle identity.</summary>
public sealed class VehicleProfileResolverService
{
    private readonly VehicleBrandProfileService _brands;
    private VehicleInfo? _lastDetectedVehicle;

    public VehicleProfileResolverService(
        VehicleBrandProfileService brands,
        IBluetoothService bluetooth)
    {
        _brands = brands;
        _brands.Changed += OnBrandChanged;
        bluetooth.ConnectionChanged += OnConnectionChanged;
    }

    public ResolvedVehicleProfile? Current { get; private set; }
    public event EventHandler<ResolvedVehicleProfile>? ProfileResolved;

    public ResolvedVehicleProfile Resolve(VehicleInfo detectedVehicle)
    {
        _lastDetectedVehicle = detectedVehicle;
        Current = VehicleProfileResolutionEngine.Resolve(detectedVehicle, _brands.Current);
        ProfileResolved?.Invoke(this, Current);
        return Current;
    }

    public bool TryGetVerifiedOemVehicle(out VehicleInfo vehicle)
    {
        if (Current?.CanUseManufacturerSpecificDefinitions == true)
        {
            vehicle = Current.DiagnosticVehicle;
            return true;
        }

        vehicle = new VehicleInfo();
        return false;
    }

    private void OnBrandChanged(object? sender, VehicleBrandProfile profile)
    {
        if (_lastDetectedVehicle is not null)
            Resolve(_lastDetectedVehicle);
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        if (connected)
            return;

        _lastDetectedVehicle = null;
        Current = null;
    }
}
