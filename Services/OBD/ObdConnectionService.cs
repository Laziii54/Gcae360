using CarScanner.Models;
using CarScanner.Services;

namespace CarScanner.Services.OBD;

public sealed class ObdConnectionService : IBluetoothService, IObdTransport
{
    private readonly BluetoothObdTransport _bluetooth;
    private readonly WifiObdTransport _wifi;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IObdTransport _active;

    public ObdConnectionService(
        BluetoothObdTransport bluetooth,
        WifiObdTransport wifi)
    {
        _bluetooth = bluetooth;
        _wifi = wifi;
        _active = bluetooth;

        _bluetooth.ConnectionChanged += OnTransportConnectionChanged;
        _wifi.ConnectionChanged += OnTransportConnectionChanged;
    }

    public ObdTransportKind Kind => _active.Kind;
    public ObdTransportKind ActiveKind => _active.Kind;
    public bool IsConnected => _active.IsConnected;
    public string ConnectedDeviceName => _active.ConnectedDeviceName;

    public event EventHandler<bool>? ConnectionChanged;

    public Task StartScanAsync(
        Action<BleDeviceItem> onDeviceFound,
        CancellationToken cancellationToken = default) =>
        _bluetooth.StartScanAsync(onDeviceFound, cancellationToken);

    public Task StopScanAsync() => _bluetooth.StopScanAsync();

    public Task ConnectAsync(
        BleDeviceItem device,
        CancellationToken cancellationToken = default) =>
        ConnectAsync(ObdTransportConnectionOptions.ForBluetooth(device), cancellationToken);

    public Task ConnectWifiAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default) =>
        ConnectAsync(ObdTransportConnectionOptions.ForWifi(host, port), cancellationToken);

    public async Task ConnectAsync(
        ObdTransportConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var target = options.Kind == ObdTransportKind.Wifi
            ? (IObdTransport)_wifi
            : _bluetooth;

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_active, target) && _active.IsConnected)
                await _active.DisconnectAsync().ConfigureAwait(false);

            _active = target;
            await target.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (target.IsConnected)
                await target.DisconnectAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _active.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public Task<string> SendCommandAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _active.SendCommandAsync(command, timeout, cancellationToken);

    private void OnTransportConnectionChanged(object? sender, bool connected)
    {
        if (ReferenceEquals(sender, _active))
            ConnectionChanged?.Invoke(this, connected);
    }
}
