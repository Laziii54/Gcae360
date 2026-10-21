using System.Net;
using System.Net.Sockets;
using System.Text;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests.Simulation;

/// <summary>
/// Adapter behaviours that cheap or old ELM327 clones show, layered over <see cref="SimulatedElm327"/> without changing it
/// (many tests depend on it). Everything here is synthetic and lives only in the test project: it proves what the app does
/// when an adapter behaves like this, never that a particular real adapter does.
/// </summary>
public abstract class AdapterDecorator : IBluetoothService
{
    protected AdapterDecorator(SimulatedElm327 inner) => Inner = inner;

    public SimulatedElm327 Inner { get; }

    public bool IsConnected => Inner.IsConnected;
    public string ConnectedDeviceName => Inner.ConnectedDeviceName;

    public event EventHandler<bool>? ConnectionChanged
    {
        add => Inner.ConnectionChanged += value;
        remove => Inner.ConnectionChanged -= value;
    }

    public Task StartScanAsync(Action<BleDeviceItem> onDeviceFound, CancellationToken cancellationToken = default) =>
        Inner.StartScanAsync(onDeviceFound, cancellationToken);

    public Task StopScanAsync() => Inner.StopScanAsync();

    public Task ConnectAsync(BleDeviceItem device, CancellationToken cancellationToken = default) =>
        Inner.ConnectAsync(device, cancellationToken);

    public Task DisconnectAsync() => Inner.DisconnectAsync();

    public virtual Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        Inner.SendCommandAsync(command, timeout, cancellationToken);

    protected static string Compact(string command) => string.Concat(command.Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant();
}

/// <summary>
/// Answers "OK" to ATSH and then goes on transmitting on its default (functional) header, so a request meant for one module
/// is heard by whichever ECU answers functional requests. The audit's hypothesis for the Ford Escape / Chevrolet Volt
/// adapter differences: a model of that behaviour, not evidence that any real adapter does it.
/// </summary>
public sealed class HeaderIgnoringAdapter : AdapterDecorator
{
    public HeaderIgnoringAdapter(SimulatedElm327 inner) : base(inner) { }

    /// <summary>The headers the app asked for with ATSH (each one was answered "OK" and never applied).</summary>
    public List<string> RequestedHeaders { get; } = new();

    /// <summary>When true, ATCRA is honoured by hardware: frames from other ids never reach the app.</summary>
    public bool HonorReceiveFilter { get; set; }

    public override async Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var compact = Compact(command);
        if (compact.StartsWith("ATSH", StringComparison.Ordinal) && compact.Length > 4)
        {
            RequestedHeaders.Add(compact[4..]);
            return "OK";
        }

        var reply = await Inner.SendCommandAsync(command, timeout, cancellationToken).ConfigureAwait(false);
        if (HonorReceiveFilter && !compact.StartsWith("AT", StringComparison.Ordinal) && Inner.ReceiveFilter is { Length: 3 } id)
            return OnlyFrom(reply, id);
        return reply;
    }

    // What a hardware receive filter does: frames from any other id are dropped, and silence is "NO DATA".
    private static string OnlyFrom(string reply, string id)
    {
        var kept = new List<string>();
        var sawFrame = false;
        var current = false;
        foreach (var token in reply.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var upper = token.ToUpperInvariant();
            var hex = upper.All(Uri.IsHexDigit);
            if (hex && upper.Length == 3)
            {
                sawFrame = true;
                current = upper == id;
                if (current)
                    kept.Add(token);
            }
            else if (hex && upper.Length > 3 && (upper.Length - 3) % 2 == 0)
            {
                sawFrame = true;
                if (upper.StartsWith(id, StringComparison.Ordinal))
                    kept.Add(token);
                current = false;
            }
            else if (hex && upper.Length == 2)
            {
                if (current)
                    kept.Add(token);
            }
            else
            {
                kept.Add(token);
            }
        }

        return sawFrame && kept.Count == 0 ? "NO DATA" : string.Join(' ', kept);
    }
}

/// <summary>Keeps echoing every command although ATE0 was sent and answered "OK" (echo stays on).</summary>
public sealed class EchoingAdapter : AdapterDecorator
{
    public EchoingAdapter(SimulatedElm327 inner) : base(inner) { }

    public override async Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var reply = await Inner.SendCommandAsync(command, timeout, cancellationToken).ConfigureAwait(false);
        return command.Trim() + "\r" + reply;
    }
}

/// <summary>Adds a fixed delay before every reply (a slow adapter or a slow link).</summary>
public sealed class DelayedAdapter : AdapterDecorator
{
    public DelayedAdapter(SimulatedElm327 inner, TimeSpan delay) : base(inner) => Delay = delay;

    public TimeSpan Delay { get; set; }

    public override async Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        return await Inner.SendCommandAsync(command, timeout, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Returns exactly the raw text a test scripts for a command (carriage returns, line feeds and the "&gt;" prompt included),
/// one entry per call and the last one repeating; every other command goes to the simulator.
/// </summary>
public sealed class ScriptedAdapter : AdapterDecorator
{
    private readonly Dictionary<string, Queue<string>> _script = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<Exception>> _failures = new(StringComparer.OrdinalIgnoreCase);

    public ScriptedAdapter(SimulatedElm327 inner) : base(inner) { }

    public List<string> Sent { get; } = new();

    public void Reply(string command, params string[] replies) => _script[Compact(command)] = new Queue<string>(replies);

    /// <summary>Makes every send of <paramref name="command"/> fail at once with the given exception (a transport-level failure).</summary>
    public void Fail(string command, Func<Exception> exception) => _failures[Compact(command)] = exception;

    public override Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Sent.Add(command);
        if (_failures.TryGetValue(Compact(command), out var failure))
            return Task.FromException<string>(failure());
        if (_script.TryGetValue(Compact(command), out var queue) && queue.Count > 0)
            return Task.FromResult(queue.Count == 1 ? queue.Peek() : queue.Dequeue());

        return Inner.SendCommandAsync(command, timeout, cancellationToken);
    }
}

/// <summary>The protocol layer, the OBD service and the link under test.</summary>
public sealed record AdapterRig(IBluetoothService Link, Elm327Protocol Elm, OBDService Obd);

public static class AdapterRigs
{
    /// <summary>Connects <paramref name="link"/>, optionally lets the test configure the simulated adapter, then initialises the OBD service.</summary>
    public static async Task<AdapterRig> CreateAsync(IBluetoothService link, Action? beforeInitialize = null, bool connect = true)
    {
        var elm = new Elm327Protocol(link, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), link, NullLogger<OBDService>.Instance);
        if (connect)
            await link.ConnectAsync(new BleDeviceItem());
        beforeInitialize?.Invoke();
        await obd.InitializeAsync();
        return new AdapterRig(link, elm, obd);
    }
}

/// <summary>
/// A software ELM327 on a loopback TCP socket that serves any <see cref="IBluetoothService"/>-shaped adapter, so the same
/// simulated adapter can be reached over the Wi-Fi transport. It can deliver every reply in small pieces with gaps, the way a
/// TCP stream may split it.
/// </summary>
public sealed class TcpSimulatedElm327 : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IBluetoothService _adapter;
    private TcpClient? _client;
    private Task _loop = Task.CompletedTask;

    private TcpSimulatedElm327(IBluetoothService adapter) => _adapter = adapter;

    /// <summary>Bytes per write (the whole reply in one write by default).</summary>
    public int SegmentSize { get; set; } = int.MaxValue;

    /// <summary>Pause between two writes of one reply.</summary>
    public int SegmentDelayMilliseconds { get; set; }

    /// <summary>What follows every reply: the carriage returns and the prompt.</summary>
    public string Terminator { get; set; } = "\r\r>";

    /// <summary>How many separate writes the replies were delivered in.</summary>
    public int Writes { get; private set; }

    /// <summary>When true the adapter says nothing at all, not even the prompt after a bare carriage return (unplugged, out of range).</summary>
    public bool Silent { get; set; }

    /// <summary>When true, the socket is closed right after the first piece of the next reply: the adapter vanishes mid-reply.</summary>
    public bool CloseAfterFirstPiece { get; set; }

    /// <summary>Closes the socket from the adapter's side.</summary>
    public void DropConnection()
    {
        try { _client?.Close(); } catch { }
    }

    public List<string> Received { get; } = new();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static async Task<TcpSimulatedElm327> StartAsync(IBluetoothService adapter)
    {
        var server = new TcpSimulatedElm327(adapter);
        if (!adapter.IsConnected)
            await adapter.ConnectAsync(new BleDeviceItem());
        server._listener.Start();
        server._loop = Task.Run(server.RunAsync);
        return server;
    }

    private async Task RunAsync()
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _client.NoDelay = true;
            var stream = _client.GetStream();
            var buffer = new byte[256];
            var pending = new StringBuilder();
            while (!_stop.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, _stop.Token);
                if (read == 0)
                    return;
                foreach (var ch in Encoding.ASCII.GetString(buffer, 0, read))
                {
                    if (ch != '\r')
                    {
                        pending.Append(ch);
                        continue;
                    }

                    var line = pending.ToString().Trim();
                    pending.Clear();
                    lock (Received)
                        Received.Add(line);
                    await AnswerAsync(stream, line);
                }
            }
        }
        catch (Exception)
        {
            // The test ended or the client went away.
        }
    }

    private async Task AnswerAsync(NetworkStream stream, string line)
    {
        if (Silent)
            return;

        string reply;
        if (line.Length == 0)
        {
            reply = ">";   // a bare carriage return stops whatever the adapter is doing and gets the prompt back
        }
        else
        {
            try
            {
                reply = await _adapter.SendCommandAsync(line, null, _stop.Token) + Terminator;
            }
            catch (Exception)
            {
                return;   // the simulated adapter stays silent (a timeout, a dropped link)
            }
        }

        var bytes = Encoding.ASCII.GetBytes(reply);
        await _writeLock.WaitAsync(_stop.Token);
        try
        {
            for (var offset = 0; offset < bytes.Length; offset += SegmentSize)
            {
                var count = (int)Math.Min(SegmentSize, bytes.Length - offset);
                await stream.WriteAsync(bytes.AsMemory(offset, count), _stop.Token);
                await stream.FlushAsync(_stop.Token);
                Writes++;
                if (CloseAfterFirstPiece && offset + count < bytes.Length)
                {
                    CloseAfterFirstPiece = false;
                    DropConnection();
                    return;
                }

                if (SegmentDelayMilliseconds > 0 && offset + count < bytes.Length)
                    await Task.Delay(SegmentDelayMilliseconds, _stop.Token);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { _client?.Close(); } catch { }
        _listener.Stop();
        try { await _loop; } catch { }
        _stop.Dispose();
    }
}

/// <summary>Lets the protocol layer talk to the Wi-Fi transport the way it talks to the Bluetooth service.</summary>
public sealed class WifiTransportBridge : IBluetoothService
{
    private readonly WifiObdTransport _transport;

    public WifiTransportBridge(WifiObdTransport transport) => _transport = transport;

    public bool IsConnected => _transport.IsConnected;
    public string ConnectedDeviceName => _transport.ConnectedDeviceName;

    public event EventHandler<bool>? ConnectionChanged
    {
        add => _transport.ConnectionChanged += value;
        remove => _transport.ConnectionChanged -= value;
    }

    public Task StartScanAsync(Action<BleDeviceItem> onDeviceFound, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopScanAsync() => Task.CompletedTask;

    public Task ConnectAsync(BleDeviceItem device, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Connect the Wi-Fi transport directly.");

    public Task DisconnectAsync() => _transport.DisconnectAsync();

    public Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _transport.SendCommandAsync(command, timeout, cancellationToken);
}
