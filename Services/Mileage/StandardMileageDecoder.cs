using System.Globalization;

namespace CarScanner.Services.Mileage;

public enum StandardReadKind
{
    Value,
    NoResponse,
    NoData,
    AdapterError,
    NegativeResponse,
    Malformed
}

/// <summary>Result of decoding one standard Mode 01 distance / time PID.</summary>
public sealed record StandardPidResult(
    StandardReadKind Kind,
    MileageMeasurement Measurement,
    double? Value,
    string Unit,
    bool Saturated,
    string Detail)
{
    public bool IsValue => Kind == StandardReadKind.Value && Value.HasValue;
}

/// <summary>
/// SAE J1979 Mode 01 PIDs that carry a distance or a time. Only these definitions are decoded:
///  • A6  odometer                         4 bytes, 0.1 km          → total odometer
///  • 21  distance with MIL on             2 bytes, 1 km            → NOT the odometer
///  • 31  distance since codes cleared     2 bytes, 1 km            → NOT the odometer
///  • 4D  run time with MIL on             2 bytes, 1 minute        → time, not distance
///  • 4E  time since codes cleared         2 bytes, 1 minute        → time, not distance
/// A reply that is truncated, damaged, a status word or a negative reply is reported as such and never decoded.
/// </summary>
public static class StandardMileageDecoder
{
    public const int OdometerPid = 0xA6;
    public const int DistanceWithMilOnPid = 0x21;
    public const int DistanceSinceClearPid = 0x31;
    public const int TimeWithMilOnPid = 0x4D;
    public const int TimeSinceClearPid = 0x4E;

    public static readonly int[] ContextPids =
    {
        DistanceWithMilOnPid, DistanceSinceClearPid, TimeWithMilOnPid, TimeSinceClearPid
    };

    public static MileageMeasurement MeasurementOf(int pid) => pid switch
    {
        OdometerPid => MileageMeasurement.TotalOdometer,
        DistanceWithMilOnPid => MileageMeasurement.DistanceWithMilOn,
        DistanceSinceClearPid => MileageMeasurement.DistanceSinceDtcClear,
        TimeWithMilOnPid or TimeSinceClearPid => MileageMeasurement.MinutesSinceDiagnosticEvent,
        _ => MileageMeasurement.OtherDistance
    };

    public static string UnitOf(int pid) => pid switch
    {
        OdometerPid or DistanceWithMilOnPid or DistanceSinceClearPid => "km",
        _ => "min"
    };

    public static bool IsSupportedPid(int pid) =>
        pid == OdometerPid || ContextPids.Contains(pid);

    public static StandardPidResult Decode(int pid, string? raw)
    {
        var measurement = MeasurementOf(pid);
        var unit = UnitOf(pid);

        StandardPidResult Fail(StandardReadKind kind, string detail) => new(kind, measurement, null, unit, false, detail);

        if (!IsSupportedPid(pid))
            return Fail(StandardReadKind.Malformed, "PID is not a documented distance / time PID.");

        var text = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (text.Length == 0)
            return Fail(StandardReadKind.NoResponse, "No reply.");
        if (text.Contains("NO DATA", StringComparison.Ordinal))
            return Fail(StandardReadKind.NoData, "The ECU answered NO DATA.");
        if (text.Contains("UNABLE TO CONNECT", StringComparison.Ordinal) || text.Contains("CAN ERROR", StringComparison.Ordinal) ||
            text.Contains("BUS", StringComparison.Ordinal) || text.Contains("STOPPED", StringComparison.Ordinal) ||
            text.Contains("BUFFER FULL", StringComparison.Ordinal) || text.Contains('?'))
            return Fail(StandardReadKind.AdapterError, "The adapter reported an error.");

        var bytes = Bytes(text);

        // Positive reply first (data bytes may happen to contain 7F).
        var need = pid == OdometerPid ? 4 : 2;
        var found = false;
        for (var i = 0; i + 1 < bytes.Count; i++)
        {
            if (bytes[i] != 0x41 || bytes[i + 1] != pid)
                continue;
            found = true;
            if (i + 2 + need > bytes.Count)
                continue; // truncated here; maybe another copy of the reply is complete

            long value = 0;
            for (var k = 0; k < need; k++)
                value = (value << 8) | (uint)bytes[i + 2 + k];

            if (pid == OdometerPid)
            {
                // All bits set is "not available" in the standard.
                if (value == 0xFFFFFFFFL)
                    return Fail(StandardReadKind.Malformed, "The ECU reported the odometer as not available (all bits set).");
                return new StandardPidResult(StandardReadKind.Value, measurement, value / 10.0, unit, false, "0.1 km per bit");
            }

            return new StandardPidResult(StandardReadKind.Value, measurement, value, unit, value == 0xFFFF,
                value == 0xFFFF ? "Counter at its maximum (65,535): the real value is at least this." : "1 per bit");
        }

        if (found)
            return Fail(StandardReadKind.Malformed, "The reply is shorter than the documented format.");

        for (var i = 0; i + 2 < bytes.Count; i++)
        {
            if (bytes[i] == 0x7F && bytes[i + 1] == 0x01)
                return Fail(StandardReadKind.NegativeResponse, $"Negative reply, code 0x{bytes[i + 2]:X2}.");
        }

        return Fail(StandardReadKind.Malformed, "The reply does not contain the expected positive response.");
    }

    /// <summary>Bytes of a reply: tokens of whole hex bytes (two digits, or longer even-length hex split into bytes).</summary>
    private static List<int> Bytes(string text)
    {
        var bytes = new List<int>();
        foreach (var token in text.Split(new[] { ' ', '\r', '\n', '\t', '>' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length % 2 != 0 || !token.All(Uri.IsHexDigit))
                continue;
            for (var i = 0; i < token.Length; i += 2)
                bytes.Add(int.Parse(token.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return bytes;
    }
}
