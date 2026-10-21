using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// Stage 1: the protocol layer sits on top of whichever link carries the text — the direct Bluetooth-style link (a whole
/// reply per call) or the Wi-Fi transport (a TCP stream that may deliver a reply in pieces). The same simulated adapter,
/// driven by the same protocol code, must give the same result over both. SIMULATION ONLY: the Bluetooth service itself
/// (Plugin.BLE, the classic socket) cannot run in these tests; its notification assembly is NOT covered here.
/// </summary>
[Collection("DiagnosticCapture")]
public sealed class SharedTransportBehaviorTests
{
    private sealed record Outcome(
        string Reply,
        string SetupNote,
        bool Initialized,
        ObdProtocol Protocol,
        IReadOnlyList<string> Sent,
        IReadOnlyList<string> Log);

    private static SimulatedElm327 VoltOilLife()
    {
        var sim = new SimulatedElm327();
        sim.ModuleAnswers["7E0|22119F"] = "7E8 04 62 11 9F 80";
        sim.ModuleAnswers["7DF|22DD01"] = "7E8 06 62 DD 01 03 B5 8F";
        return sim;
    }

    private static void OlderMicrocode(SimulatedElm327 sim)
    {
        foreach (var command in new[] { "ATS1", "ATS0", "ATD0", "ATD1", "ATCFC1", "ATCRA", "ATAR" })
            sim.RejectedAt.Add(command);
    }

    /// <summary>One personality: how to build the adapter, which request to read.</summary>
    private sealed record Personality(string Name, Func<SimulatedElm327, IBluetoothService> Wrap, Action<SimulatedElm327>? Configure, int Tx, byte[] Request);

    private static readonly Personality[] All =
    {
        new("full-featured", sim => sim, null, 0x7E0, new byte[] { 0x22, 0x11, 0x9F }),
        new("older microcode", sim => sim, OlderMicrocode, 0x7E0, new byte[] { 0x22, 0x11, 0x9F }),
        new("ignores ATSH", sim => new HeaderIgnoringAdapter(sim), null, 0x7E1, new byte[] { 0x22, 0xDD, 0x01 }),
        new("echoes commands", sim => new EchoingAdapter(sim), null, 0x7E0, new byte[] { 0x22, 0x11, 0x9F }),
    };

    public static IEnumerable<object[]> Names() => All.Select(item => new object[] { item.Name });

    private static async Task<Outcome> RunDirectAsync(Personality personality)
    {
        var sim = VoltOilLife();
        personality.Configure?.Invoke(sim);
        using var log = new LogScope();

        var rig = await AdapterRigs.CreateAsync(personality.Wrap(sim));
        sim.Sent.Clear();
        var reply = await rig.Obd.ReadPhysicalCanReadOnlyAsync(personality.Tx, personality.Request, TimeSpan.FromSeconds(2));

        return new Outcome(reply, rig.Elm.LastPhysicalReadSetupNote, rig.Obd.IsInitialized, rig.Elm.CurrentProtocol, sim.Sent.ToArray(), log.Lines);
    }

    private static async Task<Outcome> RunOverWifiAsync(Personality personality)
    {
        var sim = VoltOilLife();
        personality.Configure?.Invoke(sim);
        await using var server = await TcpSimulatedElm327.StartAsync(personality.Wrap(sim));
        server.SegmentSize = 4;                                      // every reply arrives in several pieces
        server.SegmentDelayMilliseconds = 1;
        using var log = new LogScope();
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));

        var rig = await AdapterRigs.CreateAsync(new WifiTransportBridge(transport), connect: false);
        sim.Sent.Clear();
        var reply = await rig.Obd.ReadPhysicalCanReadOnlyAsync(personality.Tx, personality.Request, TimeSpan.FromSeconds(2));
        var outcome = new Outcome(reply, rig.Elm.LastPhysicalReadSetupNote, rig.Obd.IsInitialized, rig.Elm.CurrentProtocol, sim.Sent.ToArray(), log.Lines);
        await transport.DisconnectAsync();
        return outcome;
    }

    private static async Task<Outcome> RunOverBleAsync(Personality personality)
    {
        var sim = VoltOilLife();
        personality.Configure?.Invoke(sim);
        using var log = new LogScope();
        var ble = await BleHarness.CreateAsync(personality.Wrap(sim), gatt =>
        {
            gatt.FragmentSize = 5;                                   // every reply arrives as several notifications
            gatt.FragmentDelayMilliseconds = 1;
        });

        var rig = await AdapterRigs.CreateAsync(ble.Service, connect: false);
        sim.Sent.Clear();
        var reply = await rig.Obd.ReadPhysicalCanReadOnlyAsync(personality.Tx, personality.Request, TimeSpan.FromSeconds(2));
        return new Outcome(reply, rig.Elm.LastPhysicalReadSetupNote, rig.Obd.IsInitialized, rig.Elm.CurrentProtocol, sim.Sent.ToArray(), log.Lines);
    }

    // The log lines that describe the adapter and the protocol, without time stamps, durations or the name of the link.
    private static IReadOnlyList<string> Comparable(IEnumerable<string> lines) => lines
        .Select(line => Regex.Replace(line, @"^\d\d:\d\d:\d\d\.\d{3}\s+", string.Empty))
        .Where(line => line.StartsWith("Init ", StringComparison.Ordinal) || line.StartsWith("Protocol", StringComparison.Ordinal) ||
                       line.StartsWith("Read", StringComparison.Ordinal) || line.StartsWith("Setup", StringComparison.Ordinal))
        .Select(line => Regex.Replace(line, @"( over [^,;]+)?[,;] \d+ ms$", string.Empty))
        .Select(line => Regex.Replace(line, @" over [A-Za-z0-9\- ]+$", string.Empty))
        .ToArray();

    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheSameAdapter_GivesTheSameResult_OverTheDirectLinkAndOverTcpInPieces(string name)
    {
        var personality = All.Single(item => item.Name == name);

        var direct = await RunDirectAsync(personality);
        var wifi = await RunOverWifiAsync(personality);

        Assert.Equal(direct.Reply, wifi.Reply);                          // the same text reaches the caller
        Assert.Equal(direct.SetupNote, wifi.SetupNote);                  // the adapter was set up the same way
        Assert.Equal(direct.Initialized, wifi.Initialized);
        Assert.Equal(direct.Protocol, wifi.Protocol);
        Assert.Equal(direct.Sent, wifi.Sent);                            // the same commands in the same order
        Assert.Equal(Comparable(direct.Log), Comparable(wifi.Log));      // and the log says the same about both
        Assert.NotEmpty(Comparable(direct.Log));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheSameAdapter_GivesTheSameResult_OverTheRealBleCode_AsOverTheDirectLink(string name)
    {
        var personality = All.Single(item => item.Name == name);

        var direct = await RunDirectAsync(personality);
        var ble = await RunOverBleAsync(personality);

        Assert.Equal(direct.Reply, ble.Reply);
        Assert.Equal(direct.SetupNote, ble.SetupNote);
        Assert.Equal(direct.Initialized, ble.Initialized);
        Assert.Equal(direct.Protocol, ble.Protocol);
        Assert.Equal(direct.Sent, ble.Sent);                             // the same commands in the same order
        Assert.Equal(Comparable(direct.Log), Comparable(ble.Log));       // and the same things in the connection log
    }

    [Fact]
    public async Task AReplyDeliveredWholeOrOneCharacterAtATime_IsTheSameTextAfterTheProtocolLayer()
    {
        // The text the protocol layer hands on does not depend on how the link chopped it up.
        var sim = VoltOilLife();
        var whole = await AdapterRigs.CreateAsync(sim);
        var viaWholeReply = await whole.Elm.SendCommandAsync("0100", TimeSpan.FromSeconds(1), 1, true);

        await using var server = await TcpSimulatedElm327.StartAsync(VoltOilLife());
        server.SegmentSize = 1;
        server.SegmentDelayMilliseconds = 1;
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        var split = await AdapterRigs.CreateAsync(new WifiTransportBridge(transport), connect: false);
        var viaPieces = await split.Elm.SendCommandAsync("0100", TimeSpan.FromSeconds(3), 1, true);
        await transport.DisconnectAsync();

        Assert.Equal("41 00 BE 3F A8 13", viaWholeReply);
        Assert.Equal(viaWholeReply, viaPieces);
    }

    [Fact]
    public async Task ATimeoutOnTheWifiLink_IsClassifiedExactlyLikeOneOnTheDirectLink()
    {
        // Direct link: the simulator times out. Wi-Fi link: the adapter stays silent for that command. Same classification.
        var direct = VoltOilLife();
        var directRig = await AdapterRigs.CreateAsync(direct);
        direct.TimeoutOnNextSend.Add("010C");

        var scripted = new ScriptedAdapter(VoltOilLife());
        scripted.Fail("010C", () => new TimeoutException("simulated: no reply"));
        await using var server = await TcpSimulatedElm327.StartAsync(scripted);
        var transport = new WifiObdTransport();
        await transport.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
        var wifiRig = await AdapterRigs.CreateAsync(new WifiTransportBridge(transport), connect: false);

        var onDirect = await Record.ExceptionAsync(() => directRig.Elm.SendCommandAsync("010C", TimeSpan.FromMilliseconds(300), 1, true));
        var onWifi = await Record.ExceptionAsync(() => wifiRig.Elm.SendCommandAsync("010C", TimeSpan.FromMilliseconds(300), 1, true));

        Assert.IsType<Elm327TimeoutException>(onDirect);
        Assert.IsType<Elm327TimeoutException>(onWifi);
        Assert.Equal(AdapterReadFailure.Classify(onDirect!).Kind, AdapterReadFailure.Classify(onWifi!).Kind);
        Assert.Equal(AdapterFailureKind.NoReplyInTime, AdapterReadFailure.Classify(onWifi!).Kind);
        Assert.Equal(ExchangeLayer.Transport, AdapterExchangeClassifier.Classify(onWifi!).Layer);
        await transport.DisconnectAsync();
    }
}
