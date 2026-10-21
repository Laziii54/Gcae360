using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class ShellStatusViewModel : ObservableObject
{
    private readonly ProService _pro;
    private readonly IBluetoothService _bluetooth;
    private readonly VehicleContextService _vehicleContext;
    private readonly LocalizationService _localization;
    [ObservableProperty] private bool isPro;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string bluetoothText = "OBD2 • გათიშული";
    [ObservableProperty] private string vehicleIcon = "";
    [ObservableProperty] private string vehicleTypeText = "ავტომობილი";

    // ShellContent.Title (the implicit tab bar's caption) is navigation metadata, not part of the visual tree, so
    // it is never reached by LocalizationService.Apply()/ApplyNodeCore's page walk or by the Android/iOS display-
    // translation handler hook (both only touch Label/Button/Page and the Shell/page TitleView). It must be bound
    // to a ViewModel property instead, refreshed on LanguageChanged.
    [ObservableProperty] private string homeTabTitle = "მთავარი";

    public ShellStatusViewModel(ProService pro, IBluetoothService bluetooth, VehicleContextService vehicleContext, LocalizationService localization)
    {
        _pro = pro; _bluetooth = bluetooth; _vehicleContext = vehicleContext; _localization = localization;
        IsPro = pro.IsPro; IsConnected = bluetooth.IsConnected;
        UpdateVehicleContext(vehicleContext.Current);
        RefreshLocalizedTexts();
        bluetooth.ConnectionChanged += OnConnectionChanged;
        pro.ProChanged += OnProChanged;
        vehicleContext.Changed += OnVehicleChanged;
        _localization.LanguageChanged += (_, __) => MainThread.BeginInvokeOnMainThread(RefreshLocalizedTexts);
        _ = RefreshAsync();
    }

    private void RefreshLocalizedTexts() => HomeTabTitle = _localization.Translate("მთავარი");
    private void OnConnectionChanged(object? sender, bool value) => MainThread.BeginInvokeOnMainThread(() => { IsConnected=value; BluetoothText=value ? "OBD2 • დაკავშირებულია" : "OBD2 • გათიშული"; });
    private void OnProChanged(object? sender, bool value) => MainThread.BeginInvokeOnMainThread(() => IsPro=value);
    private void OnVehicleChanged(object? sender, VehicleKind value) => MainThread.BeginInvokeOnMainThread(() => UpdateVehicleContext(value));
    private void UpdateVehicleContext(VehicleKind kind){ VehicleIcon=_vehicleContext.GetIcon(); VehicleTypeText=_vehicleContext.GetDisplayName(); }
    private async Task RefreshAsync() { await _pro.LoadAsync(); IsPro=_pro.IsPro; IsConnected=_bluetooth.IsConnected; BluetoothText=IsConnected ? "OBD2 • დაკავშირებულია" : "OBD2 • გათიშული"; UpdateVehicleContext(_vehicleContext.Current); }
}
