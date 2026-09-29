using CarScanner.Models;
using CarScanner.Services.Bluetooth;

namespace CarScanner.Services.OBD;

public sealed class BluetoothObdTransport : IObdTransport
{
    private readonly BluetoothService _bluetooth;

    public BluetoothObdTransport(BluetoothService bluetooth)
    {
        _bluetooth = bluetooth;
        _bluetooth.ConnectionChanged += (_, connected) =>
            ConnectionChanged?.Invoke(this, connected);
    }

    public ObdTransportKind Kind => ObdTransportKind.Bluetooth;
    public bool IsConnected => _bluetooth.IsConnected;
    public string ConnectedDeviceName => _bluetooth.ConnectedDeviceName;

    public event EventHandler<bool>? ConnectionChanged;

    public Task StartScanAsync(
        Action<BleDeviceItem> onDeviceFound,
        CancellationToken cancellationToken = default) =>
        _bluetooth.StartScanAsync(onDeviceFound, cancellationToken);

    public Task StopScanAsync() => _bluetooth.StopScanAsync();

    public Task ConnectAsync(
        ObdTransportConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.Kind != ObdTransportKind.Bluetooth || options.BluetoothDevice is null)
            throw new ArgumentException("Bluetooth device is required.", nameof(options));

        return _bluetooth.ConnectAsync(options.BluetoothDevice, cancellationToken);
    }

    public Task DisconnectAsync() => _bluetooth.DisconnectAsync();

    public Task<string> SendCommandAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _bluetooth.SendCommandAsync(command, timeout, cancellationToken);
}
