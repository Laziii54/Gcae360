using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CarScanner.Services;
using CarScanner.Services.Bluetooth;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace CarScanner.Tests.Simulation;

/// <summary>
/// Implements a Plugin.BLE interface (<c>ICharacteristic</c>, <c>IDevice</c>) with only what a test needs. Everything else
/// returns a default value, so the big interfaces do not have to be written out. Uses <see cref="DispatchProxy"/> from the
/// framework: no mocking package.
/// </summary>
public class InterfaceProxy : DispatchProxy
{
    public Dictionary<string, Func<object?[]?, object?>> Members { get; } = new(StringComparer.Ordinal);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (Members.TryGetValue(targetMethod.Name, out var handler))
            return handler(args);

        var type = targetMethod.ReturnType;
        if (type == typeof(void))
            return null;
        if (type == typeof(Task))
            return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { value });
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    public static TInterface Create<TInterface>(out InterfaceProxy proxy) where TInterface : class
    {
        var instance = Create<TInterface, InterfaceProxy>();
        proxy = (InterfaceProxy)(object)instance;
        return instance;
    }
}

/// <summary>
/// A fake BLE UART adapter behind a GATT characteristic: it receives the app's writes (in the chunks the app wrote them),
/// asks a simulated ELM327 for the reply, and delivers it as notifications of a chosen size with a chosen pause between them.
/// </summary>
public sealed class FakeGatt
{
    private readonly IBluetoothService _adapter;
    private readonly InterfaceProxy _characteristicProxy;
    private readonly List<EventHandler<CharacteristicUpdatedEventArgs>> _subscribers = new();
    private readonly StringBuilder _incoming = new();
    private readonly SemaphoreSlim _order = new(1, 1);
    private byte[] _value = Array.Empty<byte>();

    public FakeGatt(IBluetoothService adapter)
    {
        _adapter = adapter;
        Characteristic = InterfaceProxy.Create<ICharacteristic>(out _characteristicProxy);
        var id = Guid.NewGuid();
        _characteristicProxy.Members["get_Id"] = _ => id;
        _characteristicProxy.Members["get_Uuid"] = _ => id.ToString();
        _characteristicProxy.Members["get_CanWrite"] = _ => true;
        _characteristicProxy.Members["get_CanUpdate"] = _ => true;
        _characteristicProxy.Members["get_Value"] = _ => _value;
        _characteristicProxy.Members["WriteAsync"] = args =>
        {
            OnWrite((byte[])args![0]!);
            return Task.FromResult(0);   // Plugin.BLE's WriteAsync returns Task<int> (0 = success)
        };
        _characteristicProxy.Members["add_ValueUpdated"] = args =>
        {
            lock (_subscribers)
                _subscribers.Add((EventHandler<CharacteristicUpdatedEventArgs>)args![0]!);
            return null;
        };
        _characteristicProxy.Members["remove_ValueUpdated"] = args =>
        {
            lock (_subscribers)
                _subscribers.Remove((EventHandler<CharacteristicUpdatedEventArgs>)args![0]!);
            return null;
        };

        var deviceId = Guid.NewGuid();
        Device = InterfaceProxy.Create<IDevice>(out var deviceProxy);
        deviceProxy.Members["get_Id"] = _ => deviceId;
        deviceProxy.Members["get_Name"] = _ => "SIMULATED BLE ELM327";
    }

    /// <summary>Used as both the write and the notify characteristic.</summary>
    public ICharacteristic Characteristic { get; }

    public IDevice Device { get; }

    /// <summary>Every write the app made, chunk by chunk, as the bytes went over the "air".</summary>
    public List<byte[]> Writes { get; } = new();

    /// <summary>Notification size in bytes (a reply is cut into pieces of this size). Whole reply in one when large.</summary>
    public int FragmentSize { get; set; } = int.MaxValue;

    public int FragmentDelayMilliseconds { get; set; }

    public string Terminator { get; set; } = "\r\r>";

    /// <summary>When true, nothing is ever sent back, not even the prompt after a bare carriage return.</summary>
    public bool Silent { get; set; }

    /// <summary>The commands (lines) the app sent, without the carriage return.</summary>
    public List<string> Commands { get; } = new();

    /// <summary>How many notifications were delivered.</summary>
    public int Notifications { get; private set; }

    /// <summary>Called with every complete command before the answer is produced (to inject scenario events).</summary>
    public Func<string, Task>? BeforeAnswer { get; set; }

    private void OnWrite(byte[] chunk)
    {
        lock (Writes)
            Writes.Add(chunk);

        List<string> lines = new();
        lock (_incoming)
        {
            foreach (var ch in Encoding.ASCII.GetString(chunk))
            {
                if (ch == '\r')
                {
                    lines.Add(_incoming.ToString().Trim());
                    _incoming.Clear();
                }
                else
                {
                    _incoming.Append(ch);
                }
            }
        }

        foreach (var line in lines)
        {
            lock (Commands)
                Commands.Add(line);
            _ = Task.Run(() => AnswerAsync(line));
        }
    }

    private async Task AnswerAsync(string line)
    {
        await _order.WaitAsync();
        try
        {
            if (Silent)
                return;

            if (BeforeAnswer is not null)
                await BeforeAnswer(line);

            string reply;
            if (line.Length == 0)
            {
                reply = ">";   // a bare carriage return stops the adapter and gets the prompt back
            }
            else
            {
                try
                {
                    reply = await _adapter.SendCommandAsync(line) + Terminator;
                }
                catch (Exception)
                {
                    return;   // the simulated adapter stays silent for this command
                }
            }

            await DeliverAsync(reply);
        }
        finally
        {
            _order.Release();
        }
    }

    /// <summary>Delivers text as notifications of <see cref="FragmentSize"/> bytes, pausing between them.</summary>
    public async Task DeliverAsync(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        for (var offset = 0; offset < bytes.Length; offset += FragmentSize)
        {
            var count = (int)Math.Min(FragmentSize, bytes.Length - offset);
            Notify(bytes.AsSpan(offset, count).ToArray());
            if (FragmentDelayMilliseconds > 0 && offset + count < bytes.Length)
                await Task.Delay(FragmentDelayMilliseconds);
        }
    }

    /// <summary>One notification, delivered now with exactly these bytes.</summary>
    public void Notify(byte[] bytes)
    {
        _value = bytes;
        Notifications++;
        EventHandler<CharacteristicUpdatedEventArgs>[] handlers;
        lock (_subscribers)
            handlers = _subscribers.ToArray();
        foreach (var handler in handlers)
            handler(Characteristic, new CharacteristicUpdatedEventArgs(Characteristic));
    }

    public void Notify(string text) => Notify(Encoding.ASCII.GetBytes(text));
}

/// <summary>
/// The REAL <see cref="BluetoothService"/> (BLE write/notify/resync code) attached to a <see cref="FakeGatt"/>. The service is
/// created without its constructor (which needs the platform's Bluetooth stack) and given only the fields its command path uses.
/// </summary>
public sealed class BleHarness
{
    private BleHarness(BluetoothService service, FakeGatt gatt)
    {
        Service = service;
        Gatt = gatt;
        service.ConnectionChanged += (_, connected) =>
        {
            lock (ConnectionEvents)
                ConnectionEvents.Add(connected);
        };
    }

    public BluetoothService Service { get; }

    /// <summary>The characteristic the service is attached to now (a new one after <see cref="ReconnectAsync"/>).</summary>
    public FakeGatt Gatt { get; private set; }

    public List<bool> ConnectionEvents { get; } = new();

    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task<BleHarness> CreateAsync(IBluetoothService adapter, Action<FakeGatt>? configure = null)
    {
        if (!adapter.IsConnected)
            await adapter.ConnectAsync(new CarScanner.Models.BleDeviceItem());

        var gatt = new FakeGatt(adapter);
        configure?.Invoke(gatt);

        var service = (BluetoothService)RuntimeHelpers.GetUninitializedObject(typeof(BluetoothService));
        SetField(service, "_logger", NullLogger<BluetoothService>.Instance);
        SetField(service, "_commandLock", new SemaphoreSlim(1, 1));
        SetField(service, "_connectGate", new SemaphoreSlim(1, 1));
        SetField(service, "_responseBuffer", new StringBuilder());
        SetField(service, "_connectedBleDevice", gatt.Device);
        typeof(BluetoothService).GetProperty(nameof(BluetoothService.IsConnected))!.SetValue(service, true);
        typeof(BluetoothService).GetProperty(nameof(BluetoothService.ConnectedDeviceName))!.SetValue(service, "SIMULATED BLE ELM327");

        // The service's own routine that subscribes to the notifications and starts them.
        var attach = (Task)typeof(BluetoothService).GetMethod("AttachCharacteristicsAsync", Private)!
            .Invoke(service, new object[] { gatt.Characteristic, gatt.Characteristic })!;
        await attach;

        return new BleHarness(service, gatt);
    }

    /// <summary>Makes the BLE stack report that the device went away (what a dropped link does), through the service's own handler.</summary>
    public void LoseTheLink()
    {
        typeof(BluetoothService).GetMethod("OnDeviceDisconnected", Private)!
            .Invoke(Service, new object?[] { null, new DeviceEventArgs { Device = Gatt.Device } });
    }

    /// <summary>
    /// A new link to the adapter after a loss: a NEW fake characteristic (a new GATT session) is attached to the same service,
    /// exactly as the service's own attach routine does it after a real reconnect. The old characteristic stays reachable
    /// through the object that was returned by <see cref="Gatt"/> before, so a test can still make it deliver late bytes.
    /// </summary>
    public async Task<FakeGatt> ReconnectAsync(IBluetoothService adapter, Action<FakeGatt>? configure = null)
    {
        var gatt = new FakeGatt(adapter);
        configure?.Invoke(gatt);

        SetField(Service, "_connectedBleDevice", gatt.Device);
        typeof(BluetoothService).GetProperty(nameof(BluetoothService.IsConnected))!.SetValue(Service, true);
        typeof(BluetoothService).GetProperty(nameof(BluetoothService.ConnectedDeviceName))!.SetValue(Service, "SIMULATED BLE ELM327");

        var attach = (Task)typeof(BluetoothService).GetMethod("AttachCharacteristicsAsync", Private)!
            .Invoke(Service, new object[] { gatt.Characteristic, gatt.Characteristic })!;
        await attach;

        Gatt = gatt;
        return gatt;
    }

    public bool PendingResponseExists =>
        typeof(BluetoothService).GetField("_pendingResponse", Private)!.GetValue(Service) is not null;

    public int BufferedCharacters
    {
        get
        {
            var buffer = (StringBuilder)typeof(BluetoothService).GetField("_responseBuffer", Private)!.GetValue(Service)!;
            lock (buffer)
                return buffer.Length;
        }
    }

    private static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, Private)!.SetValue(target, value);
}
