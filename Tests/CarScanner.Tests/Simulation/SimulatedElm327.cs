using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;

namespace CarScanner.Tests.Simulation;

/// <summary>What the simulated adapter does wrong (or right) after the configured number of data commands.</summary>
public enum SimFault
{
    None,
    NoData,          // "NO DATA" for every data command
    UnableToConnect, // "UNABLE TO CONNECT"
    Garbage,         // random bytes / unprintable text instead of an answer
    Truncated,       // answer cut off in the middle of a byte
    Noise,           // valid answer with junk lines mixed in
    Timeout,         // adapter never answers (but a Task.Delay(timeout, token) still unblocks the caller)
    Disconnect,      // link drops: IsConnected=false, send throws
    BufferFull,      // "BUFFER FULL" in monitor mode
    HangForever      // simulates a native Android InputStream.Read() that a CancellationToken cannot
                      // interrupt: the returned Task never completes and ignores the token entirely,
                      // unlike Timeout above. Only an independent watchdog racing this from the outside
                      // (not the token passed into this call) can ever move on.
}

/// <summary>
/// A software ELM327 + car. It stands in for the Bluetooth link (<see cref="IBluetoothService"/>), so the real
/// <see cref="Elm327Protocol"/> runs unchanged against it. Everything it "knows" is synthetic test data that
/// lives only in the test project; it is never part of the app.
/// </summary>
public sealed class SimulatedElm327 : IBluetoothService
{
    private static readonly Regex HexOnly = new("^[0-9A-F]+$", RegexOptions.Compiled);

    private bool _connected;
    private bool _headers;
    private bool _spaces = true;
    private string _header = "7DF";
    private int _dataCommands;
    private readonly Random _random = new(1234);

    public List<string> Sent { get; } = new();

    /// <summary>Applies <see cref="Fault"/> from the Nth data command on (0 = from the start).</summary>
    public int FaultAfter { get; set; }
    public SimFault Fault { get; set; } = SimFault.None;

    /// <summary>
    /// Commands (e.g. "0100") that time out on their very next send, then answer normally afterwards — a real
    /// ECU was observed doing exactly this on one admin capture, answering a PID "valid" on one read and with
    /// no data at all moments later. Lets a test prove a retry recovers from one transient miss, as opposed to
    /// <see cref="Fault"/>/<see cref="FaultAfter"/>, which fail every attempt from a point on, retries included.
    /// </summary>
    public HashSet<string> TimeoutOnNextSend { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>OBD mode/PID answers as an ATH0/ATS1 adapter prints them, e.g. "010C" → "41 0C 1A F8".</summary>
    public Dictionary<string, string> ObdAnswers { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0100"] = "41 00 BE 3F A8 13",
        ["0105"] = "41 05 7B",
        ["010C"] = "41 0C 1A F8",
        ["010D"] = "41 0D 3C",
        ["03"] = "43 01 33 00 00 00 00",
        ["0A"] = "4A 00"
    };

    /// <summary>Module answers keyed "HEADER|PAYLOADHEX" → frames as ATH1/ATS0 prints them (one per line).</summary>
    public Dictionary<string, string> ModuleAnswers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Frames the car broadcasts by itself (what ATMA prints), one "ID+DATA" token per line.</summary>
    public string MonitorTraffic { get; set; } = "";

    public string ProtocolNumber { get; set; } = "6";
    public string ProtocolText { get; set; } = "ISO 15765-4 (CAN 11/500)";

    public bool IsConnected => _connected;
    public string ConnectedDeviceName => _connected ? "SIMULATED ELM327" : string.Empty;
    public event EventHandler<bool>? ConnectionChanged;

    public Task StartScanAsync(Action<BleDeviceItem> onDeviceFound, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopScanAsync() => Task.CompletedTask;

    public Task ConnectAsync(BleDeviceItem device, CancellationToken cancellationToken = default)
    {
        _connected = true;
        ConnectionChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        Drop();
        return Task.CompletedTask;
    }

    /// <summary>Pretends the cable/Bluetooth link vanished.</summary>
    public void Drop()
    {
        if (!_connected)
            return;
        _connected = false;
        ConnectionChanged?.Invoke(this, false);
    }

    public async Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connected)
            throw new ObdConnectionLostException("Simulated link is down.");

        Sent.Add(command);
        var cmd = command.Trim();

        if (ObdMonitorCommand.TryParse(cmd, out _, out _))
            return MonitorResponse();

        var upper = cmd.Replace(" ", string.Empty).ToUpperInvariant();
        if (upper.StartsWith("AT", StringComparison.Ordinal))
            return AtResponse(upper);

        if (TimeoutOnNextSend.Remove(upper))
        {
            await Task.Delay(timeout ?? TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            throw new TimeoutException("Simulated adapter did not answer.");
        }

        // A data command from here on.
        var count = _dataCommands++;
        if (Fault != SimFault.None && count >= FaultAfter)
        {
            switch (Fault)
            {
                case SimFault.HangForever:
                    // Deliberately ignores cancellationToken: a caller must race this from the
                    // outside (e.g. Task.WhenAny with its own independent timer) to ever move on.
                    // Bounded at 30s (not literally forever) purely so a test that does NOT race
                    // this from the outside cannot wedge the rest of a shared test run — the real
                    // native Android read this models has no such ceiling.
                    await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
                    break;
                case SimFault.Disconnect:
                    Drop();
                    throw new ObdConnectionLostException("Simulated link dropped.");
                case SimFault.Timeout:
                    await Task.Delay(timeout ?? TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                    throw new TimeoutException("Simulated adapter did not answer.");
                case SimFault.NoData:
                    return "NO DATA";
                case SimFault.UnableToConnect:
                    return "UNABLE TO CONNECT";
                case SimFault.Garbage:
                    return Garbage();
                case SimFault.Truncated:
                    return Truncate(Answer(upper));
                case SimFault.Noise:
                    return "\u0001\u0002@@##\r" + Answer(upper) + "\r?!?\r";
            }
        }

        return Answer(upper);
    }

    /// <summary>
    /// AT commands (by prefix, e.g. "ATS1", "ATD0", "ATCRA", "ATCF") this adapter answers with "?" — an older-microcode
    /// clone. Elm Electronics' command table lists ATS and ATD as first appearing in firmware 1.3, ATCRA hhh in 1.3 and
    /// ATCRA (no argument) in 1.4b; cheap "v1.5" adapters are widely reported to run older code. Empty = a full-featured adapter.
    /// </summary>
    public HashSet<string> RejectedAt { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The header set by the last ATSH (7DF after a reset).</summary>
    public string CurrentHeader => _header;
    public bool HeadersOn => _headers;
    public bool SpacesOn => _spaces;

    /// <summary>The receive address / ID filter the adapter currently has, or null for its default.</summary>
    public string? ReceiveFilter { get; private set; }

    private string AtResponse(string upper)
    {
        foreach (var prefix in RejectedAt)
        {
            if (upper.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "?";
        }

        if (upper.StartsWith("ATZ", StringComparison.Ordinal))
        {
            // A reset puts every setting back to its default.
            _headers = false;
            _spaces = true;
            _header = "7DF";
            ReceiveFilter = null;
            return "ELM327 v1.5";
        }

        if (upper.StartsWith("ATCRA", StringComparison.Ordinal))
        {
            ReceiveFilter = upper.Length > 5 ? upper[5..] : null;
            return "OK";
        }

        if (upper == "ATAR")
        {
            ReceiveFilter = null;
            return "OK";
        }

        if (upper.StartsWith("ATCF", StringComparison.Ordinal) && !upper.StartsWith("ATCFC", StringComparison.Ordinal))
        {
            ReceiveFilter = upper[4..];
            return "OK";
        }

        if (upper == "ATI")
            return "ELM327 v1.5";
        if (upper == "ATDPN")
            return "A" + ProtocolNumber;
        if (upper == "ATDP")
            return "AUTO, " + ProtocolText;
        if (upper == "ATRV")
            return "12.6V";
        if (upper.StartsWith("ATH", StringComparison.Ordinal))
            _headers = upper.EndsWith('1');
        if (upper.StartsWith("ATS", StringComparison.Ordinal) && !upper.StartsWith("ATSP", StringComparison.Ordinal) &&
            !upper.StartsWith("ATSH", StringComparison.Ordinal) && !upper.StartsWith("ATST", StringComparison.Ordinal))
            _spaces = upper.EndsWith('1');
        if (upper.StartsWith("ATSH", StringComparison.Ordinal))
            _header = upper[4..];
        return "OK";
    }

    private string Answer(string upper)
    {
        if (ModuleAnswers.TryGetValue(_header + "|" + upper, out var module))
            return module;

        if (HexOnly.IsMatch(upper) && ObdAnswers.TryGetValue(upper, out var obd))
        {
            if (!_headers)
                return _spaces ? obd : obd.Replace(" ", string.Empty);

            var data = obd.Replace(" ", string.Empty);
            var length = (data.Length / 2).ToString("X2");
            return "7E8" + length + data;
        }

        return "NO DATA";
    }

    private string MonitorResponse()
    {
        if (Fault == SimFault.Disconnect && _dataCommands >= FaultAfter)
        {
            Drop();
            throw new ObdConnectionLostException("Simulated link dropped.");
        }

        return Fault switch
        {
            SimFault.Garbage => Garbage(),
            SimFault.Truncated => Truncate(MonitorTraffic),
            SimFault.Noise => "\u0001@@##\r" + MonitorTraffic + "\r?!?\r",
            SimFault.BufferFull => MonitorTraffic + "\rBUFFER FULL\r",
            SimFault.NoData => "",
            _ => MonitorTraffic
        };
    }

    private string Garbage()
    {
        var chars = new char[24];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = (char)_random.Next(1, 255);
        return new string(chars);
    }

    private static string Truncate(string text) => text.Length <= 3 ? text : text[..^3];
}
