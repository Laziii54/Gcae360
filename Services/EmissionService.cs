using CarScanner.Models;
using CarScanner.Services.OBD;
namespace CarScanner.Services;

/// <summary>
/// Reads standard OBD-II emissions data only. Optional PIDs deliberately stay
/// unavailable when an ECU does not provide them; a missing response is never
/// changed into a zero or a positive readiness result.
/// </summary>
public sealed class EmissionService
{
    private readonly OBDService _obd;

    public EmissionService(OBDService obd) => _obd = obd;

    public async Task<EmissionReadinessReport> ReadAsync(CancellationToken token = default)
    {
        _obd.EnsureInitialized();

        var sinceClear = await TryReadReadinessAsync(0x01, token).ConfigureAwait(false);
        var thisDrive = await TryReadReadinessAsync(0x41, token).ConfigureAwait(false);
        var distanceSinceClear = await ReadU16Async(0x31, "km", token).ConfigureAwait(false);
        var warmUpsSinceClear = await ReadU8Async(0x30, "warm-ups", token).ConfigureAwait(false);
        var distanceWithMil = await ReadU16Async(0x21, "km", token).ConfigureAwait(false);
        var timeWithMil = await ReadU16Async(0x4D, "min", token).ConfigureAwait(false);
        var timeSinceClear = await ReadU16Async(0x4E, "min", token).ConfigureAwait(false);

        return new EmissionReadinessReport
        {
            SinceClear = sinceClear,
            CurrentDriveCycle = thisDrive,
            SinceClearMonitors = BuildMonitors(sinceClear, "Since DTC Clear"),
            DriveCycleMonitors = BuildMonitors(thisDrive, "Current Drive Cycle"),
            DistanceSinceClear = distanceSinceClear,
            DistanceSinceClearKm = NumericValue(distanceSinceClear),
            WarmUpsSinceClear = warmUpsSinceClear,
            WarmUpsSinceClearValue = NumericValue(warmUpsSinceClear),
            DistanceWithMil = distanceWithMil,
            DistanceWithMilKm = NumericValue(distanceWithMil),
            TimeWithMil = timeWithMil,
            TimeWithMilMinutes = NumericValue(timeWithMil),
            TimeSinceClear = timeSinceClear,
            TimeSinceClearMinutes = NumericValue(timeSinceClear)
        };
    }

    private async Task<ReadinessInfo?> TryReadReadinessAsync(int pid, CancellationToken token)
    {
        try
        {
            var data = await _obd.ReadMode01PidDataAsync(pid, token).ConfigureAwait(false);
            if (data is null || data.Length < 4)
                return null;

            return new ReadinessInfo
            {
                MilOn = (data[0] & 0x80) != 0,
                StoredDtcCount = data[0] & 0x7F,
                RawBytes = data.Take(4).ToArray()
            };
        }
        catch (Exception ex) when (ex is Elm327TimeoutException or Elm327CommandException or Elm327NotConnectedException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<string> ReadU8Async(int pid, string unit, CancellationToken token)
    {
        var data = await TryReadPidAsync(pid, token).ConfigureAwait(false);
        return data is { Length: >= 1 } ? $"{data[0]} {unit}" : "Not Supported";
    }

    private async Task<string> ReadU16Async(int pid, string unit, CancellationToken token)
    {
        var data = await TryReadPidAsync(pid, token).ConfigureAwait(false);
        return data is { Length: >= 2 } ? $"{(data[0] << 8) | data[1]} {unit}" : "Not Supported";
    }

    private async Task<byte[]?> TryReadPidAsync(int pid, CancellationToken token)
    {
        try
        {
            return await _obd.ReadMode01PidDataAsync(pid, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Elm327TimeoutException or Elm327CommandException or Elm327NotConnectedException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static double? NumericValue(string text)
    {
        var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(first, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Decodes the four bytes returned by PIDs 01 and 41: A=MIL/DTC count,
    /// B=continuous monitor support/incomplete bits, C=non-continuous support,
    /// D=non-continuous incomplete bits.
    /// </summary>
    private static IReadOnlyList<EmissionMonitor> BuildMonitors(ReadinessInfo? readiness, string source)
    {
        var raw = readiness?.RawBytes;
        if (raw is null || raw.Length < 4)
            return Array.Empty<EmissionMonitor>();

        var b = raw[1];
        var c = raw[2];
        var d = raw[3];
        var compressionIgnition = (b & 0x08) != 0;
        var result = new List<EmissionMonitor>
        {
            // B0-B2 say supported, B4-B6 say incomplete.
            Monitor("Misfire", "Continuous", (b & 0x01) != 0, (b & 0x10) == 0, source),
            Monitor("Fuel System", "Continuous", (b & 0x02) != 0, (b & 0x20) == 0, source),
            Monitor("Comprehensive Components", "Continuous", (b & 0x04) != 0, (b & 0x40) == 0, source)
        };

        var monitors = compressionIgnition
            ? new (string Name, int Bit)[]
            {
                ("NMHC Catalyst", 0), ("NOx / SCR", 1), ("Boost Pressure", 3),
                ("Exhaust Gas Sensor", 4), ("PM / DPF Filter", 5), ("EGR / VVT", 6)
            }
            : new (string Name, int Bit)[]
            {
                ("Catalyst", 0), ("Heated Catalyst", 1), ("Evaporative System", 2),
                ("Secondary Air System", 3), ("A/C Refrigerant", 4), ("Oxygen Sensor", 5),
                ("Oxygen Sensor Heater", 6), ("EGR / VVT", 7)
            };

        foreach (var (name, bit) in monitors)
        {
            var supported = (c & (1 << bit)) != 0;
            var complete = supported && (d & (1 << bit)) == 0;
            result.Add(Monitor(name, "Non-continuous", supported, complete, source));
        }

        return result;
    }

    private static EmissionMonitor Monitor(string name, string category, bool supported, bool complete, string source) => new()
    {
        Name = name,
        Category = category,
        Supported = supported,
        Complete = supported ? complete : null,
        Details = supported ? (complete ? "Complete" : "Incomplete") : "Not Supported",
        Explanation = source
    };
}
