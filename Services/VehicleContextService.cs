using CarScanner.Models;

namespace CarScanner.Services;

public sealed class VehicleContextService
{
    private const string Key = "selected_vehicle_kind";

    public event EventHandler<VehicleKind>? Changed;

    public VehicleContextService()
    {
        Preferences.Default.Set(Key, nameof(VehicleKind.Car));
    }

    public VehicleKind Current => VehicleKind.Car;

    public bool IsTwoWheeler => false;

    public void SetVehicleKind(VehicleKind kind)
    {
        Preferences.Default.Set(Key, nameof(VehicleKind.Car));
        Changed?.Invoke(this, VehicleKind.Car);
    }

    public string GetDisplayName() => "ავტომობილი";

    public string GetIcon() => "";
}