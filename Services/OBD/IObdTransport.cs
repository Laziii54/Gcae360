using CarScanner.Models;

namespace CarScanner.Services.OBD;

public enum ObdTransportKind
{
    Bluetooth,
    Wifi
}

public sealed record ObdTransportConnectionOptions
{
    public required ObdTransportKind Kind { get; init; }
    public BleDeviceItem? BluetoothDevice { get; init; }
    public string? Host { get; init; }
    public int Port { get; init; }

    public static ObdTransportConnectionOptions ForBluetooth(BleDeviceItem device) =>
        new()
        {
            Kind = ObdTransportKind.Bluetooth,
            BluetoothDevice = device
        };

    public static ObdTransportConnectionOptions ForWifi(string host, int port) =>
        new()
        {
            Kind = ObdTransportKind.Wifi,
            Host = host,
            Port = port
        };
}

public interface IObdTransport
{
    ObdTransportKind Kind { get; }
    bool IsConnected { get; }
    string ConnectedDeviceName { get; }
    event EventHandler<bool>? ConnectionChanged;
    Task ConnectAsync(ObdTransportConnectionOptions options, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<string> SendCommandAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public sealed class ObdAdapterNotRespondingException : IOException
{
    public ObdAdapterNotRespondingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ObdConnectionLostException : IOException
{
    public ObdConnectionLostException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
