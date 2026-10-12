using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.OBD;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Net;

#if ANDROID
using Android.Bluetooth;
using Android.Content;
using Android.Provider;
#endif

namespace CarScanner.ViewModels;

public partial class BluetoothViewModel : BaseViewModel
{
    private readonly IBluetoothService _bluetooth;
    private readonly ObdConnectionService _connection;
    private readonly LocalizationService _localization;
    private readonly VehicleInfoService _vehicle;
    private readonly VehicleBrandProfileService _brand;

    private bool _scanAfterReturningFromSettings;
    private bool _manualWifiDisconnect;
    private WifiConnectionState _wifiState = WifiConnectionState.NotConnected;

    public ObservableCollection<BleDeviceItem> Devices { get; } = new();

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private bool isConnecting;

    [ObservableProperty]
    private bool isBluetoothTab = true;

    public bool IsWifiTab => !IsBluetoothTab;

    [ObservableProperty]
    private string wifiIpAddress = "192.168.0.10";

    [ObservableProperty]
    private string wifiPort = "35000";

    [ObservableProperty]
    private bool isWifiConnecting;

    [ObservableProperty]
    private bool isWifiConnected;

    [ObservableProperty]
    private string wifiConnectionStatus = string.Empty;

    [ObservableProperty]
    private string wifiStatusColor = "#F59E0B";

    [ObservableProperty]
    private string status = "დაკავშირება არ არის";

    [ObservableProperty]
    private string connectedDeviceName = "";

    [ObservableProperty]
    private string statusColor = "#F59E0B";

    [ObservableProperty]
    private string bluetoothInfoText =
        "ჩართე Bluetooth და შემდეგ მოძებნე OBD-II ადაპტერი.";

    [ObservableProperty]
    private string compatibilityMessage =
#if IOS
        "iPhone მხარს უჭერს BLE OBD-II ადაპტერებს. Classic ELM327 iPhone-თან არ მუშაობს.";
#else
        "Android მხარს უჭერს BLE და Bluetooth Classic OBD-II ადაპტერებს.";
#endif

    public BluetoothViewModel(
    IBluetoothService bluetooth,
    ObdConnectionService connection,
    LocalizationService localization,
    VehicleInfoService vehicle,
    VehicleBrandProfileService brand)
    {
        _bluetooth = bluetooth;
        _connection = connection;
        _localization = localization;
        _vehicle = vehicle;
        _brand = brand;

        Title = L("OBD Connection", "OBD კავშირი");

        UpdateConnectionInfo();
        SetWifiState(
            _connection.ActiveKind == ObdTransportKind.Wifi && _connection.IsConnected
                ? WifiConnectionState.Connected
                : WifiConnectionState.NotConnected);

        _bluetooth.ConnectionChanged +=
            (_, connected) =>
                MainThread.BeginInvokeOnMainThread(
                    () =>
                    {
                        UpdateConnectionInfo();

                        if (!connected &&
                            _connection.ActiveKind == ObdTransportKind.Bluetooth &&
                            !IsConnecting)
                        {
                            StatusMessage = L(
                                "Bluetooth OBD connection was lost. Reconnect the adapter.",
                                "Bluetooth OBD კავშირი დაიკარგა. ადაპტერი ხელახლა დააკავშირე.");
                        }

                        if (_connection.ActiveKind != ObdTransportKind.Wifi)
                            return;

                        if (_connection.IsConnected)
                            SetWifiState(WifiConnectionState.Connected);
                        else if (!_manualWifiDisconnect && !IsWifiConnecting)
                            SetWifiState(WifiConnectionState.ConnectionLost);
                    });

        _localization.LanguageChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(() => SetWifiState(_wifiState));
    }

    partial void OnIsBluetoothTabChanged(bool value) =>
        OnPropertyChanged(nameof(IsWifiTab));

    [RelayCommand]
    private void SelectBluetoothTab() => IsBluetoothTab = true;

    [RelayCommand]
    private void SelectWifiTab() => IsBluetoothTab = false;

    private void UpdateConnectionInfo()
    {
        if (_bluetooth.IsConnected)
        {
            ConnectedDeviceName =
                _bluetooth.ConnectedDeviceName;

            Status =
                "დაკავშირებულია";

            StatusColor =
                "#22C55E";

            BluetoothInfoText = _connection.ActiveKind == ObdTransportKind.Wifi
                ? L(
                    $"{_brand.Current.DisplayName} profile selected — Wi-Fi OBD adapter is ready.",
                    $"{_brand.Current.DisplayName} პროფილი არჩეულია — Wi-Fi OBD ადაპტერი მზად არის.")
                : $"{_brand.Current.DisplayName} პროფილი არჩეულია — OBD-II ადაპტერი მზად არის.";
        }
        else
        {
            ConnectedDeviceName =
                "";

            Status =
                "დაკავშირება არ არის";

            StatusColor =
                "#F59E0B";

            BluetoothInfoText =
                $"{_brand.Current.DisplayName} პროფილი არჩეულია. ჩართე Bluetooth, ჩართე მანქანის კონტაქტი და მოძებნე OBD-II ადაპტერი.";
        }
    }


    public async Task OnPageAppearingAsync()
    {
        UpdateConnectionInfo();

        if (_connection.ActiveKind == ObdTransportKind.Wifi && _connection.IsConnected)
            SetWifiState(WifiConnectionState.Connected);

        if (!_scanAfterReturningFromSettings)
        {
            // A scan result is a moment-in-time observation, not a saved
            // device list.  Start every visit with a clean screen.
            Devices.Clear();
            StatusMessage = string.Empty;
            return;
        }

        if (!IsBluetoothEnabled())
            return;

        _scanAfterReturningFromSettings =
            false;

        await Task.Delay(400);

        if (!IsBusy)
            await StartScan();
    }


    // =========================================================
    // WI-FI TCP
    // =========================================================

    [RelayCommand]
    private async Task ConnectWifi()
    {
        try
        {
        if (IsWifiConnecting)
            return;

        var host = WifiIpAddress.Trim();
        if (!IPAddress.TryParse(host, out _))
        {
            SetWifiState(WifiConnectionState.ConnectionFailed);
            StatusMessage = L("Enter a valid IP address.", "შეიყვანე სწორი IP მისამართი.");
            return;
        }

        if (!int.TryParse(WifiPort, out var port) || port is < 1 or > 65535)
        {
            SetWifiState(WifiConnectionState.ConnectionFailed);
            StatusMessage = L("Enter a valid TCP port (1–65535).", "შეიყვანე სწორი TCP პორტი (1–65535).");
            return;
        }

        try
        {
            IsWifiConnecting = true;
            SetWifiState(WifiConnectionState.Connecting);
            StatusMessage = L(
                $"Connecting to Wi-Fi OBD adapter at {host}:{port}…",
                $"ვუკავშირდები Wi-Fi OBD ადაპტერს {host}:{port}…");

            await _connection.ConnectWifiAsync(host, port);

            UpdateConnectionInfo();
            SetWifiState(WifiConnectionState.Connected);
            StatusMessage = L(
                "Wi-Fi OBD adapter connected. Reading vehicle information…",
                "Wi-Fi OBD ადაპტერი დაკავშირებულია. ვკითხულობ ავტომობილის ინფორმაციას…");

            using var identityTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try
            {
                await _vehicle.EnsureConnectedVehicleInfoAsync(
                    forceVinRefresh: true,
                    identityTimeout.Token);

                StatusMessage = L(
                    "Wi-Fi OBD connection is ready.",
                    "Wi-Fi OBD კავშირი მზადაა.");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = L(
                    "Wi-Fi OBD is connected. Vehicle information will refresh on Home.",
                    "Wi-Fi OBD დაკავშირებულია. ავტომობილის ინფორმაცია მთავარ გვერდზე განახლდება.");
            }
            catch
            {
                StatusMessage = L(
                    "Wi-Fi OBD is connected. Vehicle information will refresh on Home.",
                    "Wi-Fi OBD დაკავშირებულია. ავტომობილის ინფორმაცია მთავარ გვერდზე განახლდება.");
            }

            await GoHomeAfterConnectAsync();
        }
        catch (ObdAdapterNotRespondingException)
        {
            SetWifiState(WifiConnectionState.AdapterNotResponding);
            StatusMessage = L(
                "TCP connection opened, but the ELM327 adapter did not respond.",
                "TCP კავშირი გაიხსნა, მაგრამ ELM327 ადაპტერმა არ უპასუხა.");
        }
        catch (ObdConnectionLostException)
        {
            SetWifiState(WifiConnectionState.ConnectionLost);
            StatusMessage = L(
                "The Wi-Fi OBD connection was lost.",
                "Wi-Fi OBD კავშირი დაიკარგა.");
        }
        catch (TimeoutException)
        {
            SetWifiState(WifiConnectionState.ConnectionFailed);
            StatusMessage = L(
                "Wi-Fi OBD connection timed out.",
                "Wi-Fi OBD კავშირის დრო ამოიწურა.");
        }
        catch (Exception ex)
        {
            SetWifiState(WifiConnectionState.ConnectionFailed);
            StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? L("Could not connect to the Wi-Fi OBD adapter.", "Wi-Fi OBD ადაპტერთან დაკავშირება ვერ მოხერხდა.")
                : ex.Message;
        }
        finally
        {
            IsWifiConnecting = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "BluetoothViewModel.ConnectWifi");
        }
    }

    [RelayCommand]
    private async Task DisconnectWifi()
    {
        try
        {
            _manualWifiDisconnect = true;
            await _connection.DisconnectAsync();
            _vehicle.ClearConnectedVehicleState();
            UpdateConnectionInfo();
            SetWifiState(WifiConnectionState.NotConnected);
            StatusMessage = L("Wi-Fi OBD adapter disconnected.", "Wi-Fi OBD ადაპტერი გათიშულია.");
        }
        catch (Exception ex)
        {
            StatusMessage = CarScanner.Services.UserError.Describe(ex);
        }
        finally
        {
            _manualWifiDisconnect = false;
        }
    }

    private void SetWifiState(WifiConnectionState state)
    {
        _wifiState = state;
        IsWifiConnected = state == WifiConnectionState.Connected;

        (WifiConnectionStatus, WifiStatusColor) = state switch
        {
            WifiConnectionState.Connecting => (L("Connecting…", "დაკავშირება…"), "#F59E0B"),
            WifiConnectionState.Connected => (L("Connected", "დაკავშირებულია"), "#22C55E"),
            WifiConnectionState.ConnectionFailed => (L("Connection failed", "კავშირი ვერ დამყარდა"), "#EF4444"),
            WifiConnectionState.AdapterNotResponding => (L("Adapter not responding", "ადაპტერი არ პასუხობს"), "#EF4444"),
            WifiConnectionState.ConnectionLost => (L("Connection lost", "კავშირი დაიკარგა"), "#EF4444"),
            _ => (L("Not connected", "დაკავშირებული არ არის"), "#F59E0B")
        };
    }

    private string L(string english, string georgian) =>
        _localization.IsEnglish ? english : georgian;

    private enum WifiConnectionState
    {
        NotConnected,
        Connecting,
        Connected,
        ConnectionFailed,
        AdapterNotResponding,
        ConnectionLost
    }


    // =========================================================
    // START SCAN
    // =========================================================

    [RelayCommand]
    private async Task StartScan()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            IsScanning = true;

            Devices.Clear();

            if (!IsBluetoothEnabled())
            {
                _scanAfterReturningFromSettings = true;

                var openSettings =
                    await Shell.Current.DisplayAlertLocalizedAsync(
                        CarScanner.Services.Localization.LocalizationService.T("Bluetooth გამორთულია"),
                        CarScanner.Services.Localization.LocalizationService.T("Bluetooth ჩართული არ არის. გინდა Bluetooth-ის პარამეტრების გახსნა?"),
                        CarScanner.Services.Localization.LocalizationService.T("ჩართვა"),
                        CarScanner.Services.Localization.LocalizationService.T("გაუქმება"));

                if (openSettings)
                {
                    OpenBluetoothSettings();

                    StatusMessage =
                        "ჩართე Bluetooth და დაბრუნდი აპში. ძებნა ავტომატურად დაიწყება.";
                }
                else
                {
                    StatusMessage =
                        "Bluetooth ჩართული არ არის.";
                }

                return;
            }

            StatusMessage =
                "ვეძებ OBD-II ადაპტერებს…";

            var found =
                new Dictionary<string, BleDeviceItem>(
                    StringComparer.OrdinalIgnoreCase);

            await _bluetooth.StartScanAsync(
                device =>
                {
                    MainThread.BeginInvokeOnMainThread(
                        () =>
                        {
                            var key =
                                GetUniqueKey(device);

                            if (found.TryGetValue(
                                    key,
                                    out var existing))
                            {
                                // A Classic SPP entry is preferred over the
                                // same adapter's BLE advertisement because it
                                // is the transport used by most ELM327 units.
                                // Otherwise retain the stronger signal.
                                if (ShouldReplace(existing, device))
                                {
                                    found[key] =
                                        device;

                                    RefreshDevices(found);
                                }

                                return;
                            }

                            found[key] =
                                device;

                            RefreshDevices(found);
                        });
                });

            if (Devices.Count == 0)
            {
                StatusMessage =
                    "OBD-II ადაპტერი ვერ მოიძებნა. შეამოწმე ადაპტერი და მანქანის კონტაქტი.";
            }
            else
            {
                StatusMessage =
                    $"ნაპოვნია {Devices.Count} OBD-II ადაპტერი.";
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage =
                ex.Message;

            await Shell.Current.DisplayAlertLocalizedAsync(
                CarScanner.Services.Localization.LocalizationService.T("Bluetooth ნებართვა"),
                CarScanner.Services.Localization.LocalizationService.T(ex.Message),
                CarScanner.Services.Localization.LocalizationService.T("კარგი"));
        }
        catch (Exception ex)
        {
            StatusMessage =
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Bluetooth ძებნა ვერ შესრულდა."
                    : ex.Message;

            await Shell.Current.DisplayAlertLocalizedAsync(
                CarScanner.Services.Localization.LocalizationService.T("Bluetooth შეცდომა"),
                CarScanner.Services.Localization.LocalizationService.T(StatusMessage),
                CarScanner.Services.Localization.LocalizationService.T("კარგი"));
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "BluetoothViewModel.StartScan");
        }
    }


    private static string GetUniqueKey(
        BleDeviceItem device)
    {
        // One physical OBD adapter may advertise as both BLE and Classic.
        // A real advertised name is stable across those transports, unlike a
        // BLE identifier which may rotate while scanning.
        if (device.HasRecognizableName)
        {
            return
                "name:" +
                device.Name
                    .Trim()
                    .ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(
                device.ClassicAddress))
        {
            return
                "classic:" +
                device.ClassicAddress
                    .Trim()
                    .ToUpperInvariant();
        }

        // Unnamed BLE advertisements cannot be reliably identified by their
        // rotating address. Keep the nearest verified UART candidate instead
        // of flooding the screen with clones of the same adapter.
        return "ble:unnamed-obd-candidate";
    }


    private static bool ShouldReplace(
        BleDeviceItem existing,
        BleDeviceItem candidate)
    {
        if (candidate.IsClassicBluetooth &&
            !existing.IsClassicBluetooth)
        {
            return true;
        }

        if (existing.IsClassicBluetooth &&
            !candidate.IsClassicBluetooth)
        {
            return false;
        }

        return candidate.Rssi > existing.Rssi;
    }


    private void RefreshDevices(
        Dictionary<string, BleDeviceItem> found)
    {
        Devices.Clear();

        foreach (
            var device in
            found.Values.OrderByDescending(
                x => x.Rssi))
        {
            Devices.Add(device);
        }

        // Keep looking for a few seconds in the background, but stop the
        // visible spinner as soon as the user has a real adapter to choose.
        if (Devices.Count > 0)
            IsScanning = false;
    }


    // =========================================================
    // STOP SCAN
    // =========================================================

    [RelayCommand]
    private async Task StopScan()
    {
        try
        {
            await _bluetooth.StopScanAsync();

            IsScanning = false;

            StatusMessage =
                "ძებნა შეჩერდა.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                ex.Message;
        }
    }


    // =========================================================
    // CONNECT
    // =========================================================

    [RelayCommand]
    private async Task Connect(
        BleDeviceItem? item)
    {
        try
        {
        if (item == null ||
            IsConnecting)
            return;

        try
        {
            IsConnecting = true;
            await _bluetooth.StopScanAsync();
            IsScanning = false;

            StatusMessage =
                $"ვუკავშირდები {item.DisplayName}…";

            await _bluetooth.ConnectAsync(item);

            UpdateConnectionInfo();

            StatusMessage =
                $"დაკავშირებულია: {_bluetooth.ConnectedDeviceName}. მიმდინარეობს OBD ინიციალიზაცია…";

            // Keep the user on the connection page. Read only the connected
            // vehicle identity into shared state so Home can display it later
            // without depending on a one-time navigation lifecycle event.
            StatusMessage =
                "OBD-II დაკავშირებულია. ავტომობილის VIN, წელი და საწვავი იკითხება…";

            using var identityTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try
            {
                await _vehicle.EnsureConnectedVehicleInfoAsync(
                    forceVinRefresh: true,
                    identityTimeout.Token);

                StatusMessage =
                    "OBD-II დაკავშირებულია — ავტომობილის ინფორმაცია მზადაა.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage =
                    "OBD-II დაკავშირებულია. ავტომობილის ინფორმაცია Home-ზე ხელახლა განახლდება.";
            }
            catch
            {
                // A successful adapter connection must remain usable even if
                // this optional identity read is unavailable on the vehicle.
                StatusMessage =
                    "OBD-II დაკავშირებულია. ავტომობილის ინფორმაცია Home-ზე ხელახლა განახლდება.";
            }

            await GoHomeAfterConnectAsync();
        }
        catch (Exception ex)
        {
            StatusMessage =
                "OBD-II ადაპტერთან დაკავშირება ვერ მოხერხდა.";

            await Shell.Current.DisplayAlertLocalizedAsync(
                CarScanner.Services.Localization.LocalizationService.T("კავშირის შეცდომა"),
                CarScanner.Services.Localization.LocalizationService.T(string.IsNullOrWhiteSpace(ex.Message)
                    ? "შეამოწმე Bluetooth, OBD ადაპტერი და მანქანის კონტაქტი."
                    : ex.Message),
                CarScanner.Services.Localization.LocalizationService.T("კარგი"));
        }
        finally
        {
            IsConnecting = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "BluetoothViewModel.Connect");
        }
    }

    /// <summary>
    /// After the adapter is connected and the VIN / vehicle info was read,
    /// return to Home automatically (only if the user is still on this page).
    /// </summary>
    private static async Task GoHomeAfterConnectAsync()
    {
        try
        {
            // Let the "ready" message show for a moment.
            await Task.Delay(900);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current?.CurrentPage is Views.BluetoothPage)
                    await CarScanner.Services.SafeNavigation.GoToAsync("//HomePage");
            });
        }
        catch
        {
            // Staying on the connection page is fine.
        }
    }

    // =========================================================
    // DISCONNECT
    // =========================================================

    [RelayCommand]
    private async Task Disconnect()
    {
        try
        {
            await _bluetooth.DisconnectAsync();
            _vehicle.ClearConnectedVehicleState();

            UpdateConnectionInfo();

            StatusMessage =
                "OBD-II ადაპტერი გათიშულია.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                ex.Message;
        }
    }


    // =========================================================
    // ANDROID BLUETOOTH
    // =========================================================

    private static bool IsBluetoothEnabled()
    {
#if ANDROID

        try
        {
            return
                (Android.App.Application.Context.GetSystemService(
                    Context.BluetoothService) as BluetoothManager)?
                    .Adapter?
                    .IsEnabled == true;
        }
        catch
        {
            return false;
        }

#else

        return true;

#endif
    }


    private static void OpenBluetoothSettings()
    {
#if ANDROID

        try
        {
            var intent =
                new Intent(
                    Settings.ActionBluetoothSettings);

            intent.AddFlags(
                ActivityFlags.NewTask);

            Android.App.Application.Context
                .StartActivity(intent);
        }
        catch
        {
            try
            {
                var intent =
                    new Intent(
                        Settings.ActionSettings);

                intent.AddFlags(
                    ActivityFlags.NewTask);

                Android.App.Application.Context
                    .StartActivity(intent);
            }
            catch
            {
            }
        }

#endif
    }
}
