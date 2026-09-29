using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class ShellStatusViewModel : ObservableObject
{
    private readonly ProService _pro;
    private readonly IBluetoothService _bluetooth;
    private readonly VehicleContextService _vehicleContext;
    [ObservableProperty] private bool isPro;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string bluetoothText = "OBD2 • გათიშული";
    [ObservableProperty] private string vehicleIcon = "";
    [ObservableProperty] private string vehicleTypeText = "ავტომობილი";

    public ShellStatusViewModel(ProService pro, IBluetoothService bluetooth, VehicleContextService vehicleContext)
    {
        _pro = pro; _bluetooth = bluetooth; _vehicleContext = vehicleContext;
        IsPro = pro.IsPro; IsConnected = bluetooth.IsConnected;
        UpdateVehicleContext(vehicleContext.Current);
        bluetooth.ConnectionChanged += OnConnectionChanged;
        pro.ProChanged += OnProChanged;
        vehicleContext.Changed += OnVehicleChanged;
        _ = RefreshAsync();
    }
    private void OnConnectionChanged(object? sender, bool value) => MainThread.BeginInvokeOnMainThread(() => { IsConnected=value; BluetoothText=value ? "OBD2 • დაკავშირებულია" : "OBD2 • გათიშული"; });
    private void OnProChanged(object? sender, bool value) => MainThread.BeginInvokeOnMainThread(() => IsPro=value);
    private void OnVehicleChanged(object? sender, VehicleKind value) => MainThread.BeginInvokeOnMainThread(() => UpdateVehicleContext(value));
    private void UpdateVehicleContext(VehicleKind kind){ VehicleIcon=_vehicleContext.GetIcon(); VehicleTypeText=_vehicleContext.GetDisplayName(); }
    [RelayCommand] private async Task SelectVehicleType() => await Shell.Current.GoToAsync(nameof(Views.VehicleTypeSelectionPage));
    private async Task RefreshAsync() { await _pro.LoadAsync(); IsPro=_pro.IsPro; IsConnected=_bluetooth.IsConnected; BluetoothText=IsConnected ? "OBD2 • დაკავშირებულია" : "OBD2 • გათიშული"; UpdateVehicleContext(_vehicleContext.Current); }
}
