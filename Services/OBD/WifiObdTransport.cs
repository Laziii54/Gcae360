using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CarScanner.Services.OBD;

public sealed class WifiObdTransport : IObdTransport, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A timeout after which the adapter does not even answer the resync carriage return means the link is dead (adapter
    /// unplugged, phone left the adapter network, half-open TCP). This many in a row end the connection instead of leaving
    /// the app "connected" while every command times out. A slow command whose adapter still answers never counts.
    /// </summary>
    internal const int SilentTimeoutLimit = 3;

    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private int _silentTimeouts;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _connectionLifetime;
    private bool _disposed;

    public ObdTransportKind Kind => ObdTransportKind.Wifi;
    public bool IsConnected { get; private set; }
    public string ConnectedDeviceName { get; private set; } = string.Empty;

    public event EventHandler<bool>? ConnectionChanged;

    public async Task ConnectAsync(
        ObdTransportConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (options.Kind != ObdTransportKind.Wifi)
            throw new ArgumentException("Wi-Fi connection options are required.", nameof(options));

        if (!IPAddress.TryParse(options.Host?.Trim(), out var address))
            throw new ArgumentException("Invalid IP address.", nameof(options));

        if (options.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "TCP port must be between 1 and 65535.");

        await DisconnectAsync().ConfigureAwait(false);

        var client = new TcpClient(address.AddressFamily)
        {
            NoDelay = true
        };

        using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        AdapterDiagnosticCapture.TransportLabel = "Wi-Fi TCP";
        var connectStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        LinkEventLog.Add($"Wi-Fi: connecting to {DescribeEndpoint(address, options.Port)}");

        try
        {
            await client.ConnectAsync(address, options.Port, linkedCts.Token).ConfigureAwait(false);

            _client = client;
            _stream = client.GetStream();
            _connectionLifetime = new CancellationTokenSource();
            Volatile.Write(ref _silentTimeouts, 0);
            ConnectedDeviceName = $"Wi-Fi ELM327 ({address}:{options.Port})";

            try
            {
                var probe = await ExchangeCommandAsync("ATI", ProbeTimeout, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(RemovePromptAndEcho(probe, "ATI")))
                    throw new ObdAdapterNotRespondingException("The Wi-Fi adapter did not return an ELM327 response.");

                LinkEventLog.Add($"Wi-Fi: TCP connected, adapter answered ATI (\"{AdapterDiagnosticCapture.Brief(RemovePromptAndEcho(probe, "ATI"))}\") " +
                                 $"after {(long)System.Diagnostics.Stopwatch.GetElapsedTime(connectStarted).TotalMilliseconds} ms");
            }
            catch (TimeoutException ex)
            {
                LinkEventLog.Add($"Wi-Fi: TCP connected, but the adapter did not answer ATI within {AdapterDiagnosticCapture.Seconds(ProbeTimeout)} s");
                throw new ObdAdapterNotRespondingException("The Wi-Fi adapter is connected, but is not responding.", ex);
            }

            IsConnected = true;
            ConnectionChanged?.Invoke(this, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LinkEventLog.Add($"Wi-Fi: connect timed out after {AdapterDiagnosticCapture.Seconds(ConnectTimeout)} s");
            client.Dispose();
            CloseConnection(notify: false);
            throw new TimeoutException("Wi-Fi OBD connection timed out.");
        }
        catch (Exception connectError)
        {
            LinkEventLog.Add($"Wi-Fi: connect failed: {connectError.GetType().Name}");
            client.Dispose();
            CloseConnection(notify: false);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        _connectionLifetime?.Cancel();

        await _commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CloseConnection(notify: true);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<string> SendCommandAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected || _stream is null || _client?.Connected != true)
            throw new InvalidOperationException("OBD2 adapter is not connected.");

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be empty.", nameof(command));

        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExchangeCommandAsync(
                    command,
                    timeout ?? DefaultCommandTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            LinkEventLog.Add($"Wi-Fi: connection lost during {command.Trim()} ({ex.GetType().Name})");
            CloseConnection(notify: true);
            throw new ObdConnectionLostException("Wi-Fi OBD connection was lost.", ex);
        }
        catch (LinkSilentException ex)
        {
            LinkEventLog.Add($"Wi-Fi: adapter stopped answering ({SilentTimeoutLimit} timeouts in a row, none followed by an answer to the resync); closing the connection");
            CloseConnection(notify: true);
            throw new ObdConnectionLostException("The Wi-Fi OBD adapter stopped answering.", ex);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<string> ExchangeCommandAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("Wi-Fi OBD stream is unavailable.");
        var lifetimeToken = _connectionLifetime?.Token ?? CancellationToken.None;

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetimeToken,
            timeoutCts.Token);

        var token = linkedCts.Token;
        var isMonitor = ObdMonitorCommand.TryParse(command, out var monitorMilliseconds, out var monitorCommand);
        var payload = Encoding.ASCII.GetBytes((isMonitor ? monitorCommand : command).Trim() + "\r");

        try
        {
            // A late answer of an earlier (timed out / cancelled) command must
            // not be read as the reply to this one.
            await DrainPendingInputAsync(stream, token).ConfigureAwait(false);

            await stream.WriteAsync(payload, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);

            if (isMonitor)
            {
                var monitored = await MonitorAsync(stream, monitorMilliseconds, token).ConfigureAwait(false);
                Volatile.Write(ref _silentTimeouts, 0);
                return monitored;
            }

            var response = new StringBuilder();
            var buffer = new byte[1024];

            while (true)
            {
                var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                    throw new ObdConnectionLostException("The Wi-Fi OBD adapter closed the TCP connection.");

                response.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (response.ToString().Contains('>'))
                {
                    Volatile.Write(ref _silentTimeouts, 0);
                    return response.ToString();
                }
            }
        }
        catch (OperationCanceledException) when (!lifetimeToken.IsCancellationRequested)
        {
            // Timed out or cancelled while the adapter may still be busy:
            // stop it and throw away the rest of its answer.
            var adapterAnswered = await ResyncAsync(stream, lifetimeToken).ConfigureAwait(false);
            var timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

            if (adapterAnswered)
                Volatile.Write(ref _silentTimeouts, 0);
            else if (timedOut && Interlocked.Increment(ref _silentTimeouts) >= SilentTimeoutLimit)
                throw new LinkSilentException();

            LinkEventLog.Add(timedOut
                ? $"Wi-Fi: no reply to {command.Trim()} within {AdapterDiagnosticCapture.Seconds(timeout)} s; the resync {(adapterAnswered ? "was answered (adapter alive, just slow)" : "got no answer")}, " +
                  $"silent timeouts in a row {Volatile.Read(ref _silentTimeouts)}/{SilentTimeoutLimit}"
                : $"Wi-Fi: {command.Trim()} cancelled by the caller; the resync {(adapterAnswered ? "was answered" : "got no answer")}");

            if (timedOut)
                throw new TimeoutException("The Wi-Fi OBD adapter is not responding.");
            throw;
        }
    }

    /// <summary>Discards input that is already waiting on the socket (bounded to ~300 ms).</summary>
    private static async Task DrainPendingInputAsync(NetworkStream stream, CancellationToken token)
    {
        if (!stream.DataAvailable)
            return;

        var buffer = new byte[512];
        var deadline = DateTime.UtcNow.AddMilliseconds(300);
        while (stream.DataAvailable && DateTime.UtcNow < deadline)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
                throw new ObdConnectionLostException("The Wi-Fi OBD adapter closed the TCP connection.");

            // Let the rest of a stale reply arrive before checking again.
            await Task.Delay(20, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// After a timeout: sends a bare carriage return (stops a command the ELM327
    /// is still processing), reads until the '>' prompt for at most ~500 ms and
    /// discards everything. Never throws; a lost link is detected by the next command.
    /// </summary>
    private static async Task<bool> ResyncAsync(NetworkStream stream, CancellationToken lifetimeToken)
    {
        using var resyncCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        resyncCts.CancelAfter(TimeSpan.FromMilliseconds(500));
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\r"), resyncCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(resyncCts.Token).ConfigureAwait(false);

            var buffer = new byte[512];
            var seen = new StringBuilder();
            while (true)
            {
                var read = await stream.ReadAsync(buffer, resyncCts.Token).ConfigureAwait(false);
                if (read == 0)
                    return false;
                seen.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (seen.ToString().Contains('>'))
                    return true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        return false;
    }

    /// <summary>Internal signal: the adapter stayed silent through several timeouts and resyncs.</summary>
    private sealed class LinkSilentException : Exception
    {
        public LinkSilentException() : base("The Wi-Fi OBD adapter stopped answering.") { }
    }

    /// <summary>Collects monitored frames for a fixed time, then stops monitoring.</summary>
    private static async Task<string> MonitorAsync(NetworkStream stream, int milliseconds, CancellationToken token)
    {
        var response = new StringBuilder();
        var buffer = new byte[1024];

        async Task ReadUntilAsync(TimeSpan window, bool stopAtPrompt)
        {
            using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            windowCts.CancelAfter(window);
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, windowCts.Token).ConfigureAwait(false);
                    if (read == 0)
                        throw new ObdConnectionLostException("The Wi-Fi OBD adapter closed the TCP connection.");
                    response.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    if (stopAtPrompt && response.ToString().Contains('>'))
                        return;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Window over.
            }
        }

        await ReadUntilAsync(TimeSpan.FromMilliseconds(milliseconds), stopAtPrompt: true).ConfigureAwait(false);
        if (!response.ToString().Contains('>'))
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\r"), token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            await ReadUntilAsync(TimeSpan.FromMilliseconds(2500), stopAtPrompt: true).ConfigureAwait(false);
        }

        return response.ToString();
    }

    /// <summary>
    /// The adapter's address for the connection log: only when it is a local network address (the usual Wi-Fi adapter
    /// 192.168.x.x, 10.x.x.x …); anything else is not written down.
    /// </summary>
    internal static string DescribeEndpoint(IPAddress address, int port)
    {
        var local = IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        if (!local && address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            local = bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254);
        }

        return local ? $"{address}:{port}" : $"a non-local address (port {port})";
    }

    private static string RemovePromptAndEcho(string response, string command) =>
        response
            .Replace(">", string.Empty, StringComparison.Ordinal)
            .Replace(command, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

    private void CloseConnection(bool notify)
    {
        var wasConnected = IsConnected;

        if (wasConnected)
            LinkEventLog.Add("Wi-Fi: connection closed");

        IsConnected = false;
        ConnectedDeviceName = string.Empty;

        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        try { _connectionLifetime?.Dispose(); } catch { }

        _stream = null;
        _client = null;
        _connectionLifetime = null;

        if (notify && wasConnected)
            ConnectionChanged?.Invoke(this, false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connectionLifetime?.Cancel();
        CloseConnection(notify: false);
        _commandGate.Dispose();
    }
}
