using System.Net;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stages 0 and 1 for the Wi-Fi transport: replies that arrive in several partial reads or end differently, and the
/// connection events the transport now writes to the connection log. A software adapter on a loopback TCP socket; it proves
/// the transport logic only, never a real Wi-Fi adapter. (In the "DiagnosticCapture" collection only because the transport
/// label is a single shared setting.)
/// </summary>
[Collection("DiagnosticCapture")]
public sealed class WifiTransportScenarioTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    private static SimulatedElm327 Car()
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["010D"] = "41 0D 3C";
        return sim;
    }

    // ------------------------------------------------------------------ partial reads and terminators

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task AReplyDeliveredInSmallPieces_IsAssembledIntoOneCompleteReply(int segmentSize)
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        server.SegmentSize = segmentSize;
        server.SegmentDelayMilliseconds = 2;
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));

        var reply = await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(3));

        Assert.Contains("41 00 BE 3F A8 13", reply);
        Assert.EndsWith(">", reply.TrimEnd());
        Assert.True(server.Writes > 2, $"{server.Writes} writes: the reply was not actually delivered in pieces");
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ThePromptArrivingOnItsOwn_AfterAPause_StillEndsTheReply_NotEarlier()
    {
        // The data and its two carriage returns first, the prompt alone 250 ms later (wide margins: Windows timers can fire
        // ~15 ms early under load, which made a 60 ms delay checked against 50 ms fail once in a full-suite run).
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        server.SegmentSize = "41 00 BE 3F A8 13\r\r".Length;
        server.SegmentDelayMilliseconds = 250;
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var reply = await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(3));

        Assert.True(clock.ElapsedMilliseconds >= 150, "returned before the prompt arrived");
        Assert.Contains("41 00 BE 3F A8 13", reply);
        Assert.EndsWith(">", reply.TrimEnd());
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task AReplyOfSeveralLines_IsOnlyCompleteAtThePrompt_NotAtTheFirstLineEnd()
    {
        var scripted = new ScriptedAdapter(Car());
        const string first = "7E8 06 41 00 BE 3F A8 13\r";
        scripted.Reply("0100", first + "7E9 06 41 00 98 3A 80 11");
        await using var server = await TcpSimulatedElm327.StartAsync(scripted);
        server.SegmentSize = first.Length;                            // line one alone, then line two with the prompt
        server.SegmentDelayMilliseconds = 40;
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));

        var reply = await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(3));

        Assert.Contains("7E8 06 41 00 BE 3F A8 13", reply);
        Assert.Contains("7E9 06 41 00 98 3A 80 11", reply);           // both ECUs' frames in one response
        await transport.DisconnectAsync();
    }

    [Theory]
    [InlineData("\r\r>")]
    [InlineData("\r\n\r\n>")]
    [InlineData("\n>")]
    [InlineData(">")]
    [InlineData(" >")]
    [InlineData("\r>")]
    public async Task EveryCommonWayOfEndingAReply_IsAcceptedAsTheEnd(string terminator)
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        server.Terminator = terminator;
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));

        var reply = await transport.SendCommandAsync("0100", TimeSpan.FromSeconds(3));

        Assert.Contains("41 00 BE 3F A8 13", reply);
        Assert.EndsWith(">", reply.TrimEnd());
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ManyCommandsInARow_WithPieceByPieceReplies_EachGetItsOwnReply()
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        server.SegmentSize = 3;
        server.SegmentDelayMilliseconds = 1;
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        var asked = new[] { ("0100", "41 00 BE 3F A8 13"), ("010C", "41 0C 1A F8"), ("010D", "41 0D 3C") };

        for (var round = 0; round < 8; round++)
        {
            foreach (var (command, expected) in asked)
            {
                var reply = await transport.SendCommandAsync(command, TimeSpan.FromSeconds(3));
                Assert.Contains(expected, reply);
                foreach (var (otherCommand, otherReply) in asked.Where(item => item.Item1 != command))
                    Assert.DoesNotContain(otherReply, reply);              // nothing of an earlier or later reply is mixed into this one
            }
        }

        await transport.DisconnectAsync();
    }

    // ------------------------------------------------------------------ the connection events

    [Fact]
    public async Task Connecting_LogsTheAdapterAddress_TheProbeAnswer_AndHowTheAdapterIsReached()
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        using var log = new LogScope();
        var transport = new WifiObdTransport();

        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));

        log.AssertInOrder(
            $"Wi-Fi: connecting to 127.0.0.1:{server.Port}",
            "Wi-Fi: TCP connected, adapter answered ATI (\"ELM327 v1.5\") after");
        Assert.Equal("Wi-Fi TCP", AdapterDiagnosticCapture.TransportLabel);

        await transport.DisconnectAsync();
        Assert.NotNull(log.Find("Wi-Fi: connection closed"));
    }

    [Fact]
    public async Task ASlowButAliveAdapter_LogsTheTimeout_AndThatTheResyncWasAnswered()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("SLOW", () => new TimeoutException("simulated: this command is never answered"));
        await using var server = await TcpSimulatedElm327.StartAsync(scripted);
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        using var log = new LogScope();

        await Assert.ThrowsAsync<TimeoutException>(() => transport.SendCommandAsync("SLOW", Short));

        Assert.NotNull(log.Find("Wi-Fi: no reply to SLOW within 0.3 s; the resync was answered (adapter alive, just slow), silent timeouts in a row 0/3"));
        Assert.True(transport.IsConnected);
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ASilentAdapter_LogsEachTimeout_ThenTheLossAndTheClose()
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        using var log = new LogScope();

        server.Silent = true;
        for (var i = 0; i < WifiObdTransport.SilentTimeoutLimit - 1; i++)
            await Assert.ThrowsAsync<TimeoutException>(() => transport.SendCommandAsync("0100", Short));
        await Assert.ThrowsAsync<ObdConnectionLostException>(() => transport.SendCommandAsync("0100", Short));

        log.AssertInOrder(
            "Wi-Fi: no reply to 0100 within 0.3 s; the resync got no answer, silent timeouts in a row 1/3",
            "Wi-Fi: no reply to 0100 within 0.3 s; the resync got no answer, silent timeouts in a row 2/3",
            "Wi-Fi: adapter stopped answering (3 timeouts in a row",
            "Wi-Fi: connection closed");
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ASocketClosedByTheAdapter_IsLoggedAsALostConnection()
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        using var log = new LogScope();

        server.DropConnection();
        await Task.Delay(150);
        await Assert.ThrowsAsync<ObdConnectionLostException>(() => transport.SendCommandAsync("0100", TimeSpan.FromSeconds(1)));

        log.AssertInOrder("Wi-Fi: connection lost during 0100", "Wi-Fi: connection closed");
    }

    [Fact]
    public async Task AnAdapterThatVanishesInTheMiddleOfAReply_IsALostConnection_AndTheHalfReplyIsNotReturned()
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        using var log = new LogScope();
        server.SegmentSize = 6;                                       // "41 00 " first, then the socket is closed
        server.CloseAfterFirstPiece = true;

        var failure = await Record.ExceptionAsync(() => transport.SendCommandAsync("0100", TimeSpan.FromSeconds(3)));

        Assert.IsType<ObdConnectionLostException>(failure);
        Assert.False(transport.IsConnected);
        Assert.NotNull(log.Find("Wi-Fi: connection lost during 0100 (ObdConnectionLostException)"));
        Assert.Equal(AdapterFailureKind.AdapterLinkLost, AdapterReadFailure.Classify(failure!).Kind);
        // And the transport refuses further requests clearly.
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendCommandAsync("0100", Short));
    }

    [Fact]
    public async Task AnIncompleteReplyThatNeverGetsItsPrompt_TimesOut_AndTheNextCommandIsNotContaminated()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Reply("0100", "41 00 BE");                          // half a reply and no prompt, then silence
        await using var server = await TcpSimulatedElm327.StartAsync(scripted);
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        server.Terminator = string.Empty;                            // from now on a reply is sent without its prompt: the broken one

        await Assert.ThrowsAsync<TimeoutException>(() => transport.SendCommandAsync("0100", Short));
        server.Terminator = "\r\r>";
        var next = await transport.SendCommandAsync("010C", TimeSpan.FromSeconds(2));

        Assert.True(transport.IsConnected);                           // a broken reply is not a lost adapter
        Assert.Contains("41 0C 1A F8", next);
        Assert.DoesNotContain("41 00", next);                         // none of the half reply leaked into this one
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ACommandCancelledByTheCaller_IsLoggedAsACancellation_NotAsATimeout()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("SLOW", () => new TimeoutException("simulated: never answered"));
        await using var server = await TcpSimulatedElm327.StartAsync(scripted);
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        using var log = new LogScope();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendCommandAsync("SLOW", TimeSpan.FromSeconds(5), cancel.Token));

        Assert.NotNull(log.Find("Wi-Fi: SLOW cancelled by the caller; the resync was answered"));
        Assert.Null(log.Find("Wi-Fi: no reply to SLOW"));
        Assert.True(transport.IsConnected);
        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task ConnectingToAPortNobodyListensOn_IsLoggedAsAFailure()
    {
        var closed = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        using var log = new LogScope();
        var transport = new WifiObdTransport();

        await Assert.ThrowsAnyAsync<Exception>(() => transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", port)));

        log.AssertInOrder($"Wi-Fi: connecting to 127.0.0.1:{port}", "Wi-Fi: connect failed:");
        Assert.Null(log.Find("Wi-Fi: connection closed"));                // it never was connected
    }

    [Fact]
    public async Task AnAdapterThatConnectsButNeverAnswersTheProbe_IsLoggedAsSuch()
    {
        await using var server = await TcpSimulatedElm327.StartAsync(Car());
        server.Silent = true;
        using var log = new LogScope();
        var transport = new WifiObdTransport();

        await Assert.ThrowsAsync<ObdAdapterNotRespondingException>(() =>
            transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port)));

        Assert.NotNull(log.Find("Wi-Fi: TCP connected, but the adapter did not answer ATI within 4 s"));
        Assert.NotNull(log.Find("Wi-Fi: connect failed: ObdAdapterNotRespondingException"));
    }

    [Theory]
    [InlineData("192.168.0.10", "192.168.0.10:35000")]
    [InlineData("192.168.4.1", "192.168.4.1:35000")]
    [InlineData("10.1.2.3", "10.1.2.3:35000")]
    [InlineData("172.16.0.5", "172.16.0.5:35000")]
    [InlineData("172.31.255.1", "172.31.255.1:35000")]
    [InlineData("169.254.1.1", "169.254.1.1:35000")]
    [InlineData("127.0.0.1", "127.0.0.1:35000")]
    [InlineData("::1", "::1:35000")]
    [InlineData("fe80::1", "fe80::1:35000")]
    [InlineData("8.8.8.8", "a non-local address (port 35000)")]
    [InlineData("172.32.0.1", "a non-local address (port 35000)")]
    [InlineData("172.15.255.255", "a non-local address (port 35000)")]
    [InlineData("192.169.0.1", "a non-local address (port 35000)")]
    [InlineData("2001:4860:4860::8888", "a non-local address (port 35000)")]
    public void TheAdaptersAddress_IsWrittenDownOnlyWhenItIsALocalNetworkAddress(string address, string expected) =>
        Assert.Equal(expected, WifiObdTransport.DescribeEndpoint(IPAddress.Parse(address), 35000));
}
