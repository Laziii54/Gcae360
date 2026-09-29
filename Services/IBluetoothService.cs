using CarScanner.Models;

namespace CarScanner.Services;

public interface IBluetoothService
{
    bool IsConnected { get; }
    string ConnectedDeviceName { get; }
    event EventHandler<bool>? ConnectionChanged;
    Task StartScanAsync(Action<BleDeviceItem> onDeviceFound, CancellationToken cancellationToken = default);
    Task StopScanAsync();
    Task ConnectAsync(BleDeviceItem device, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
