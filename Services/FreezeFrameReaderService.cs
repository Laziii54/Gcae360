using CarScanner.Services.OBD;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services;

/// <summary>One decoded Freeze Frame (Mode 02) value.</summary>
public sealed class Mode02FreezeFrameValue
{
    public int Pid { get; init; }
    public double? Number { get; init; }
    public string? Text { get; init; }
    public string MetricUnit { get; init; } = string.Empty;
}

/// <summary>Result of one Freeze Frame read.</summary>
public sealed class Mode02FreezeFrameResult
{
    public string TriggerCode { get; init; } = string.Empty;
    public IReadOnlyList<Mode02FreezeFrameValue> Values { get; init; } = Array.Empty<Mode02FreezeFrameValue>();
    public DateTime ReadAt { get; init; } = DateTime.Now;
    public bool HasFrame => !string.IsNullOrWhiteSpace(TriggerCode);
}

/// <summary>
/// Reads the ECU's stored Freeze Frame (SAE J1979 service 02, frame 00).
/// Requests carry the frame number ("02 PID 00") and the frame byte is
/// removed from the reply before decoding, so values are not shifted.
/// Read-only: nothing is written to or cleared on the vehicle.
/// </summary>
public sealed class FreezeFrameReaderService
{
    // Useful snapshot PIDs, in display order.
    private static readonly int[] SnapshotPids =
    {
        0x0C, 0x0D, 0x05, 0x04, 0x11, 0x03,
        0x06, 0x07, 0x08, 0x09,
        0x10, 0x0B, 0x0F, 0x0E, 0x0A,
        0x42, 0x2F, 0x33, 0x46, 0x5C, 0x1F
    };

    // Data length (bytes) of each supported PID's value.
    private static readonly Dictionary<int, int> DataLength = new()
    {
        [0x00] = 4, [0x20] = 4, [0x40] = 4,
        [0x02] = 2, [0x03] = 2, [0x04] = 1, [0x05] = 1,
        [0x06] = 1, [0x07] = 1, [0x08] = 1, [0x09] = 1,
        [0x0A] = 1, [0x0B] = 1, [0x0C] = 2, [0x0D] = 1,
        [0x0E] = 1, [0x0F] = 1, [0x10] = 2, [0x11] = 1,
        [0x1F] = 2, [0x2F] = 1, [0x33] = 1, [0x42] = 2,
        [0x46] = 1, [0x5C] = 1
    };

    private static readonly Dictionary<int, string> Units = new()
    {
        [0x04] = "%", [0x05] = "°C", [0x06] = "%", [0x07] = "%", [0x08] = "%", [0x09] = "%",
        [0x0A] = "kPa", [0x0B] = "kPa", [0x0C] = "rpm", [0x0D] = "km/h", [0x0E] = "°",
        [0x0F] = "°C", [0x10] = "g/s", [0x11] = "%", [0x1F] = "s", [0x2F] = "%",
        [0x33] = "kPa", [0x42] = "V", [0x46] = "°C", [0x5C] = "°C"
    };

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1200);

    private readonly OBDService _obd;
    private readonly PIDDecoder _decoder;
    private readonly ILogger<FreezeFrameReaderService> _logger;

    public FreezeFrameReaderService(OBDService obd, PIDDecoder decoder, ILogger<FreezeFrameReaderService> logger)
    {
        _obd = obd;
        _decoder = decoder;
        _logger = logger;
    }

    public async Task<Mode02FreezeFrameResult> ReadAsync(
        IProgress<int>? progress = null,
        CancellationToken token = default)
    {
        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(token).ConfigureAwait(false);

        progress?.Report(5);

        // 1) Which DTC stored the freeze frame (PID 02). No DTC → no frame.
        var triggerData = await ReadPidDataAsync(0x02, token).ConfigureAwait(false);
        var trigger = triggerData is { Length: >= 2 } ? DecodeDtc(triggerData[0], triggerData[1]) : null;
        if (trigger is null)
        {
            progress?.Report(100);
            return new Mode02FreezeFrameResult();
        }

        progress?.Report(12);

        // 2) Which snapshot PIDs this ECU stored (bitmaps 00/20/40).
        var supported = await ReadSupportedPidsAsync(token).ConfigureAwait(false);
        var pids = supported.Count > 0
            ? SnapshotPids.Where(supported.Contains).ToArray()
            : SnapshotPids;

        // 3) Values.
        var values = new List<Mode02FreezeFrameValue>();
        for (var i = 0; i < pids.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var pid = pids[i];
            var data = await ReadPidDataAsync(pid, token).ConfigureAwait(false);
            if (data is not null)
            {
                var reading = Decode(pid, data);
                if (reading is not null)
                    values.Add(reading);
            }

            progress?.Report(15 + (int)Math.Round((i + 1) * 85d / Math.Max(1, pids.Length)));
        }

        progress?.Report(100);
        return new Mode02FreezeFrameResult { TriggerCode = trigger, Values = values };
    }

    private async Task<HashSet<int>> ReadSupportedPidsAsync(CancellationToken token)
    {
        var result = new HashSet<int>();
        foreach (var basePid in new[] { 0x00, 0x20, 0x40 })
        {
            var bitmap = await ReadPidDataAsync(basePid, token).ConfigureAwait(false);
            if (bitmap is not { Length: >= 4 })
                break;

            for (var bit = 0; bit < 32; bit++)
            {
                if ((bitmap[bit / 8] & (1 << (7 - bit % 8))) != 0)
                    result.Add(basePid + bit + 1);
            }

            if ((bitmap[3] & 0x01) == 0)
                break;
        }

        return result;
    }

    /// <summary>
    /// Sends "02 PID 00" (fallback "02 PID") and returns the value bytes with
    /// the frame-number byte removed, or null when the ECU has no value.
    /// </summary>
    private async Task<byte[]?> ReadPidDataAsync(int pid, CancellationToken token)
    {
        foreach (var command in new[] { $"02{pid:X2}00", $"02{pid:X2}" })
        {
            try
            {
                var raw = await _obd.ReadFreezeFrameCommandAsync(command, RequestTimeout, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
                    continue;

                var data = ExtractValue(raw, pid, command.Length == 6);
                if (data is not null)
                    return data;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Freeze frame request {Command} failed", command);
            }
        }

        return null;
    }

    private static byte[]? ExtractValue(string raw, int pid, bool requestedFrame)
    {
        DataLength.TryGetValue(pid, out var length);
        byte[]? best = null;

        var lines = raw.Replace("\r", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var bytes = ObdResponseParser.ParseHex(line);
            for (var i = 0; i + 1 < bytes.Length; i++)
            {
                if (bytes[i] != 0x42 || bytes[i + 1] != pid)
                    continue;

                var data = bytes[(i + 2)..];

                // Remove the frame-number byte (always 00 for frame 00).
                if (length > 0)
                {
                    if (data.Length >= length + 1 && data[0] == 0x00 && (requestedFrame || data.Length == length + 1))
                        data = data[1..(length + 1)];
                    else if (data.Length >= length)
                        data = data[..length];
                    else
                        continue;
                }

                // Several ECUs may answer PID 02; prefer one with a real DTC.
                if (pid == 0x02 && data.Length >= 2 && data[0] == 0 && data[1] == 0)
                {
                    best ??= data;
                    continue;
                }

                return data;
            }
        }

        return best;
    }

    private Mode02FreezeFrameValue? Decode(int pid, byte[] data)
    {
        if (pid == 0x03)
        {
            var status = data.Length > 0 ? data[0] : (byte)0;
            return status == 0 ? null : new Mode02FreezeFrameValue { Pid = pid, Text = $"FUEL_STATUS_{status}" };
        }

        var normalized = "41 " + pid.ToString("X2") + " " + string.Join(" ", data.Select(b => b.ToString("X2")));
        if (!_decoder.TryDecode(pid, normalized, out var value))
            return null;

        return new Mode02FreezeFrameValue
        {
            Pid = pid,
            Number = value,
            MetricUnit = Units.TryGetValue(pid, out var unit) ? unit : string.Empty
        };
    }

    private static string? DecodeDtc(byte high, byte low)
    {
        if (high == 0 && low == 0)
            return null;

        var family = (high >> 6) switch { 0 => 'P', 1 => 'C', 2 => 'B', _ => 'U' };
        return $"{family}{(high >> 4) & 0x03}{high & 0x0F:X1}{low:X2}";
    }
}
