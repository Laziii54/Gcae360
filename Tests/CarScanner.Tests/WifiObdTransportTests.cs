using System.Net;
using System.Net.Sockets;
using System.Text;
using CarScanner.Services.OBD;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// The Wi-Fi OBD transport against a software ELM327 on a loopback TCP socket: framing, one command at a time, stale
/// replies, a slow adapter, a silent adapter and a closed link. It proves the transport logic only — not a real adapter.
/// </summary>
public sealed class WifiObdTransportTests
{
    /// <summary>A tiny ELM327 stand-in: reads carriage-return terminated lines, answers on its own task per line.</summary>
    private sealed class FakeElmServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private NetworkStream? _stream;
        private TcpClient? _client;
        private readonly Task _loop;

        public List<string> Received { get; } = new();

        /// <summary>Returns the full reply (including the "&gt;" prompt) or null to stay silent.</summary>
        public Func<string, Task<string?>> Responder { get; set; } = line => Task.FromResult<string?>(line == "" ? ">" : line + "\r\rOK\r\r>");

        public bool Silent { get; set; }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeElmServer()
        {
            _listener.Start();
            _loop = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _stream = _client.GetStream();
                var buffer = new byte[256];
                var pending = new StringBuilder();
                while (!_stop.IsCancellationRequested)
                {
                    var read = await _stream.ReadAsync(buffer, _stop.Token);
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
                        _ = Task.Run(() => AnswerAsync(line));
                    }
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested || true)
            {
                // The test ended or the client went away.
            }
        }

        private async Task AnswerAsync(string line)
        {
            try
            {
                if (Silent)
                    return;
                var reply = await Responder(line);
                if (reply is null || Silent || _stream is null)
                    return;
                await _writeLock.WaitAsync();
                try { await _stream.WriteAsync(Encoding.ASCII.GetBytes(reply)); }
                finally { _writeLock.Release(); }
            }
            catch (Exception)
            {
                // The client closed the link.
            }
        }

        public void DropConnection()
        {
            try { _client?.Close(); } catch { }
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

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    private static async Task<WifiObdTransport> ConnectedAsync(FakeElmServer server)
    {
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        return transport;
    }

    [Fact]
    public async Task ItConnects_AfterTheAdapterAnswersTheProbe_AndReturnsTheFullReply()
    {
        await using var server = new FakeElmServer();
        server.Responder = line => Task.FromResult<string?>(line switch
        {
            "ATI" => "ELM327 v1.5\r\r>",
            "0100" => "41 00 BE 3F A8 13\r\r>",
            _ => "?\r\r>"
        });
        var changes = new List<bool>();
        var transport = new WifiObdTransport();
        transport.ConnectionChanged += (_, connected) => changes.Add(connected);

        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        var reply = await transport.SendCommandAsync("0100", Short);

        Assert.True(transport.IsConnected);
        Assert.Contains("41 00 BE 3F A8 13", reply);
        Assert.EndsWith(">", reply.TrimEnd());
        Assert.Equal(new[] { true }, changes);
        await transport.DisconnectAsync();
        Assert.False(transport.IsConnected);
        Assert.Equal(new[] { true, false }, changes);
    }

    [Fact]
    public async Task ConcurrentCommands_AreSentOneAtATime_AndEachGetsItsOwnReply()
    {
        await using var server = new FakeElmServer();
        server.Responder = async line =>
        {
            if (line == "ATI") return "ELM327 v1.5\r\r>";
            await Task.Delay(30);
            return $"REPLY-{line}\r\r>";
        };
        var transport = await ConnectedAsync(server);

        var tasks = Enumerable.Range(1, 6).Select(i => transport.SendCommandAsync($"C{i}", TimeSpan.FromSeconds(2))).ToArray();
        var replies = await Task.WhenAll(tasks);

        for (var i = 0; i < replies.Length; i++)
            Assert.Contains($"REPLY-C{i + 1}", replies[i]);
        // The adapter never saw a second command before the first one was answered.
        lock (server.Received)
            Assert.Equal(7, server.Received.Count);
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ASlowButAliveAdapter_TimesOutTheCommand_ButTheLinkStaysUp_AndALateReplyIsNotTakenForTheNextOne()
    {
        await using var server = new FakeElmServer();
        server.Responder = async line =>
        {
            switch (line)
            {
                case "ATI": return "ELM327 v1.5\r\r>";
                case "": return ">";                                  // the resync carriage return is answered at once
                case "SLOW": await Task.Delay(700); return "LATE-ANSWER\r\r>";
                default: return $"FAST-{line}\r\r>";
            }
        };
        var transport = await ConnectedAsync(server);
        var lost = false;
        transport.ConnectionChanged += (_, connected) => lost |= !connected;

        // More timeouts than the silent-link limit: the adapter answers the resync every time, so none of them counts.
        for (var i = 0; i < WifiObdTransport.SilentTimeoutLimit + 2; i++)
        {
            await Assert.ThrowsAsync<TimeoutException>(() => transport.SendCommandAsync("SLOW", Short));
            await Task.Delay(900);                                       // let the stale reply arrive
        }

        var reply = await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(2));

        Assert.True(transport.IsConnected);
        Assert.False(lost);
        Assert.Contains("FAST-0100", reply);
        Assert.DoesNotContain("LATE-ANSWER", reply);
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ASilentAdapter_EndsTheConnection_AfterTheLimit_InsteadOfStayingConnected()
    {
        await using var server = new FakeElmServer();
        server.Responder = line => Task.FromResult<string?>(line == "ATI" ? "ELM327 v1.5\r\r>" : "OK\r\r>");
        var transport = await ConnectedAsync(server);
        var events = new List<bool>();
        transport.ConnectionChanged += (_, connected) => events.Add(connected);

        server.Silent = true;                                           // the adapter is unplugged / out of range
        for (var i = 0; i < WifiObdTransport.SilentTimeoutLimit - 1; i++)
            await Assert.ThrowsAsync<TimeoutException>(() => transport.SendCommandAsync("0100", Short));
        Assert.True(transport.IsConnected);

        await Assert.ThrowsAsync<ObdConnectionLostException>(() => transport.SendCommandAsync("0100", Short));

        Assert.False(transport.IsConnected);
        Assert.Equal(new[] { false }, events);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendCommandAsync("0100", Short));
    }

    [Fact]
    public async Task AClosedSocket_IsReportedAsALostConnection()
    {
        await using var server = new FakeElmServer();
        server.Responder = line => Task.FromResult<string?>(line == "ATI" ? "ELM327 v1.5\r\r>" : "OK\r\r>");
        var transport = await ConnectedAsync(server);
        var events = new List<bool>();
        transport.ConnectionChanged += (_, connected) => events.Add(connected);

        server.DropConnection();
        await Task.Delay(150);

        await Assert.ThrowsAsync<ObdConnectionLostException>(() => transport.SendCommandAsync("0100", TimeSpan.FromSeconds(1)));
        Assert.False(transport.IsConnected);
        Assert.Contains(false, events);
    }

    [Fact]
    public async Task ASuccessfulCommand_ResetsTheSilentCounter()
    {
        await using var server = new FakeElmServer();
        server.Responder = line => Task.FromResult<string?>(line == "ATI" ? "ELM327 v1.5\r\r>" : "OK\r\r>");
        var transport = await ConnectedAsync(server);

        for (var round = 0; round < 3; round++)
        {
            server.Silent = true;
            await Assert.ThrowsAsync<TimeoutException>(() => transport.SendCommandAsync("0100", Short));
            server.Silent = false;
            Assert.Contains("OK", await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(1)));
        }

        Assert.True(transport.IsConnected);                                // three isolated timeouts never add up
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ACancelledCommand_IsACancellation_NotALostLink_AndTheNextCommandStillWorks()
    {
        await using var server = new FakeElmServer();
        server.Responder = async line =>
        {
            switch (line)
            {
                case "ATI": return "ELM327 v1.5\r\r>";
                case "": return ">";
                case "SLOW": await Task.Delay(1500); return "LATE\r\r>";
                default: return $"FAST-{line}\r\r>";
            }
        };
        var transport = await ConnectedAsync(server);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendCommandAsync("SLOW", TimeSpan.FromSeconds(5), cts.Token));
        Assert.True(transport.IsConnected);                              // a cancellation by the caller never ends the link

        await Task.Delay(1700);                                          // the stale answer has arrived and is discarded
        var reply = await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(2));

        Assert.Contains("FAST-0100", reply);
        Assert.DoesNotContain("LATE", reply);
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task AfterALostLink_TheSameTransportCanConnectAgain()
    {
        await using var first = new FakeElmServer();
        first.Responder = line => Task.FromResult<string?>(line == "ATI" ? "ELM327 v1.5\r\r>" : "OK-1\r\r>");
        var transport = await ConnectedAsync(first);
        var events = new List<bool>();
        transport.ConnectionChanged += (_, connected) => events.Add(connected);

        first.Silent = true;
        for (var i = 0; i < WifiObdTransport.SilentTimeoutLimit - 1; i++)
            await Assert.ThrowsAsync<TimeoutException>(() => transport.SendCommandAsync("0100", Short));
        await Assert.ThrowsAsync<ObdConnectionLostException>(() => transport.SendCommandAsync("0100", Short));
        Assert.False(transport.IsConnected);

        await using var second = new FakeElmServer();
        second.Responder = line => Task.FromResult<string?>(line == "ATI" ? "ELM327 v1.5\r\r>" : "OK-2\r\r>");
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", second.Port));

        Assert.True(transport.IsConnected);
        Assert.Contains("OK-2", await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(1)));
        Assert.Equal(new[] { false, true }, events);                     // lost, then connected — never a stuck "connected"
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task Disposing_ClosesTheSocket_SoTheAdapterSeesTheLinkEnd()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = listener.AcceptTcpClientAsync();
        var transport = new WifiObdTransport();
        var connecting = transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", port));
        using var peer = await accept;
        var stream = peer.GetStream();
        var buffer = new byte[16];
        Assert.True(await stream.ReadAsync(buffer) > 0);                 // the ATI probe
        await stream.WriteAsync(Encoding.ASCII.GetBytes("ELM327 v1.5\r\r>"));
        await connecting;

        transport.Dispose();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await stream.ReadAsync(buffer, timeout.Token));  // EOF: the transport really closed its socket
        listener.Stop();
    }

    [Fact]
    public async Task ItRefusesBadAddresses_AndAnUnreachablePort_WithoutEverReportingConnected()
    {
        var transport = new WifiObdTransport();
        var events = new List<bool>();
        transport.ConnectionChanged += (_, connected) => events.Add(connected);

        await Assert.ThrowsAsync<ArgumentException>(() => transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("not-an-ip", 35000)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", 70000)));

        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();                                                    // nothing listens on this port now
        await Assert.ThrowsAnyAsync<Exception>(() => transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", port)));

        Assert.False(transport.IsConnected);
        Assert.DoesNotContain(true, events);
    }

    [Fact]
    public async Task AnAdapterThatConnectsButNeverAnswersTheProbe_IsRejected()
    {
        await using var server = new FakeElmServer { Silent = true };
        var transport = new WifiObdTransport();

        await Assert.ThrowsAsync<ObdAdapterNotRespondingException>(() =>
            transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port)));

        Assert.False(transport.IsConnected);
    }
}
