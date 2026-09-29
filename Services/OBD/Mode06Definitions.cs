using System.Globalization;
using CarScanner.Models;

namespace CarScanner.Services.OBD;

/// <summary>
/// SAE J1979 (ISO 15765-4 / CAN) Mode $06 definitions: monitor (MID) names,
/// standard test (TID) names and Unit-and-Scaling (UASID) conversions.
/// CAN response layout per test: MID, TID, UASID, value(2), min(2), max(2).
/// </summary>
public static class Mode06Definitions
{
    public static string MonitorName(byte mid)
    {
        static string O2(int index) => $"Bank {(index / 4) + 1} – Sensor {(index % 4) + 1}";
        return mid switch
        {
            >= 0x01 and <= 0x10 => $"Oxygen Sensor Monitor {O2(mid - 0x01)}",
            >= 0x21 and <= 0x24 => $"Catalyst Monitor Bank {mid - 0x20}",
            >= 0x31 and <= 0x34 => $"EGR Monitor Bank {mid - 0x30}",
            >= 0x35 and <= 0x38 => $"VVT Monitor Bank {mid - 0x34}",
            0x39 => "EVAP Monitor (Cap Off / 0.150\")",
            0x3A => "EVAP Monitor (0.090\")",
            0x3B => "EVAP Monitor (0.040\")",
            0x3C => "EVAP Monitor (0.020\")",
            0x3D => "Purge Flow Monitor",
            >= 0x41 and <= 0x50 => $"Oxygen Sensor Heater Monitor {O2(mid - 0x41)}",
            >= 0x61 and <= 0x64 => $"Heated Catalyst Monitor Bank {mid - 0x60}",
            >= 0x71 and <= 0x74 => $"Secondary Air Monitor {mid - 0x70}",
            >= 0x81 and <= 0x84 => $"Fuel System Monitor Bank {mid - 0x80}",
            0x85 => "Boost Pressure Control Monitor Bank 1",
            0x86 => "Boost Pressure Control Monitor Bank 2",
            0x90 => "NOx Adsorber Monitor Bank 1",
            0x91 => "NOx Adsorber Monitor Bank 2",
            0x98 => "NOx Catalyst Monitor Bank 1",
            0x99 => "NOx Catalyst Monitor Bank 2",
            0xA1 => "Misfire Monitor General Data",
            >= 0xA2 and <= 0xAD => $"Misfire Cylinder {mid - 0xA1}",
            0xB2 => "PM Filter Monitor Bank 1",
            0xB3 => "PM Filter Monitor Bank 2",
            _ => "Monitor"
        };
    }

    public static string TestName(byte tid) => tid switch
    {
        0x01 => "Rich to lean sensor threshold voltage (constant)",
        0x02 => "Lean to rich sensor threshold voltage (constant)",
        0x03 => "Low sensor voltage for switch time calculation (constant)",
        0x04 => "High sensor voltage for switch time calculation (constant)",
        0x05 => "Rich to lean sensor switch time (calculated)",
        0x06 => "Lean to rich sensor switch time (calculated)",
        0x07 => "Minimum sensor voltage for test cycle (calculated)",
        0x08 => "Maximum sensor voltage for test cycle (calculated)",
        0x09 => "Time between sensor transitions (calculated)",
        0x0A => "Sensor period (calculated)",
        0x0B => "Misfire counts, average of last 10 driving cycles",
        0x0C => "Misfire counts, last/current driving cycle",
        >= 0x80 => "Manufacturer defined",
        _ => "Test"
    };

    private readonly record struct Scaling(double Factor, double Offset, string Unit, bool Signed);

    private static Scaling Scale(byte uasid) => uasid switch
    {
        0x01 => new(1, 0, "", false),
        0x02 => new(0.1, 0, "", false),
        0x03 => new(0.01, 0, "", false),
        0x04 => new(0.001, 0, "", false),
        0x05 => new(0.0000305, 0, "", false),
        0x06 => new(0.000305, 0, "", false),
        0x07 => new(0.25, 0, "rpm", false),
        0x08 => new(0.01, 0, "km/h", false),
        0x09 => new(1, 0, "km/h", false),
        0x0A => new(0.000122, 0, "V", false),
        0x0B => new(0.001, 0, "V", false),
        0x0C => new(0.01, 0, "V", false),
        0x0D => new(0.00390625, 0, "mA", false),
        0x0E => new(0.001, 0, "A", false),
        0x0F => new(0.01, 0, "A", false),
        0x10 => new(1, 0, "ms", false),
        0x11 => new(100, 0, "ms", false),
        0x12 => new(1, 0, "s", false),
        0x13 => new(1, 0, "mΩ", false),
        0x14 => new(1, 0, "Ω", false),
        0x15 => new(1, 0, "kΩ", false),
        0x16 => new(0.1, -40, "°C", false),
        0x17 => new(0.01, 0, "kPa", false),
        0x18 => new(0.0117, 0, "kPa", false),
        0x19 => new(0.079, 0, "kPa", false),
        0x1A => new(1, 0, "kPa", false),
        0x1B => new(10, 0, "kPa", false),
        0x1C => new(0.01, 0, "°", false),
        0x1D => new(0.5, 0, "°", false),
        0x1E => new(0.0000305, 0, "", false),
        0x1F => new(0.05, 0, "A/F", false),
        0x20 => new(0.0039062, 0, "", false),
        0x21 => new(1, 0, "mHz", false),
        0x22 => new(1, 0, "Hz", false),
        0x23 => new(1, 0, "kHz", false),
        0x24 => new(1, 0, "", false),
        0x25 => new(1, 0, "km", false),
        0x26 => new(0.0001, 0, "V/ms", false),
        0x27 => new(0.01, 0, "g/s", false),
        0x28 => new(1, 0, "g/s", false),
        0x29 => new(0.25, 0, "Pa/s", false),
        0x2A => new(0.001, 0, "kg/h", false),
        0x2B => new(1, 0, "", false),
        0x2C => new(0.01, 0, "g/cyl", false),
        0x2D => new(0.01, 0, "mg/stroke", false),
        0x2E => new(1, 0, "", false),
        0x2F => new(0.01, 0, "%", false),
        0x30 => new(0.001526, 0, "%", false),
        0x31 => new(0.001, 0, "L", false),
        0x32 => new(0.0000305, 0, "in", false),
        0x33 => new(0.00024414, 0, "", false),
        0x34 => new(1, 0, "min", false),
        0x35 => new(10, 0, "ms", false),
        0x36 => new(0.01, 0, "g", false),
        0x37 => new(0.1, 0, "g", false),
        0x38 => new(1, 0, "g", false),
        0x39 => new(0.01, -327.68, "%", false),
        0x3A => new(0.001, 0, "g", false),
        0x3B => new(0.0001, 0, "g", false),
        0x3C => new(0.1, 0, "µs", false),
        0x3D => new(0.01, 0, "mA", false),
        0x3E => new(0.00006103516, 0, "mm²", false),
        0x3F => new(0.01, 0, "L", false),
        0x40 => new(1, 0, "ppm", false),
        0x41 => new(0.01, 0, "µA", false),
        0x81 => new(1, 0, "", true),
        0x82 => new(0.1, 0, "", true),
        0x83 => new(0.01, 0, "", true),
        0x84 => new(0.001, 0, "", true),
        0x85 => new(0.0000305, 0, "", true),
        0x86 => new(0.000305, 0, "", true),
        0x8A => new(0.000122, 0, "V", true),
        0x8B => new(0.001, 0, "V", true),
        0x8C => new(0.01, 0, "V", true),
        0x8D => new(0.00390625, 0, "mA", true),
        0x8E => new(0.001, 0, "A", true),
        0x90 => new(1, 0, "ms", true),
        0x96 => new(0.1, 0, "°C", true),
        0x9C => new(0.01, 0, "°", true),
        0x9D => new(0.5, 0, "°", true),
        0xA8 => new(1, 0, "g/s", true),
        0xA9 => new(0.25, 0, "Pa/s", true),
        0xAD => new(0.01, 0, "mg/stroke", true),
        0xAE => new(0.1, 0, "mg/stroke", true),
        0xAF => new(0.01, 0, "%", true),
        0xB0 => new(0.003052, 0, "%", true),
        0xB1 => new(2, 0, "mV/s", true),
        0xFC => new(0.01, 0, "kPa", true),
        0xFD => new(0.001, 0, "kPa", true),
        0xFE => new(0.25, 0, "Pa", true),
        _ => new(1, 0, "", uasid >= 0x80)
    };

    public static bool IsSigned(byte uasid) => Scale(uasid).Signed;

    public static int ToRaw(int word, byte uasid) =>
        IsSigned(uasid) && word > 0x7FFF ? word - 0x10000 : word;

    public static string Format(int raw, byte uasid)
    {
        var scaling = Scale(uasid);
        var value = raw * scaling.Factor + scaling.Offset;
        var text = value.ToString("0.######", CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(scaling.Unit) ? text : $"{text} {scaling.Unit}";
    }

    /// <summary>Bitmask of supported MIDs from a "46 xx A B C D" answer (xx = 00, 20, 40 …).</summary>
    public static IReadOnlyList<byte> ParseSupported(string raw, byte baseMid)
    {
        var bytes = ObdResponseParser.ParseHex(raw);
        var result = new SortedSet<byte>();
        for (var i = 0; i + 5 < bytes.Length; i++)
        {
            if (bytes[i] != 0x46 || bytes[i + 1] != baseMid)
                continue;
            for (var b = 0; b < 4; b++)
            {
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((bytes[i + 2 + b] & (0x80 >> bit)) != 0)
                        result.Add((byte)(baseMid + b * 8 + bit + 1));
                }
            }
        }
        return result.ToArray();
    }

    /// <summary>Parses all tests of one MID: 46 MID TID UASID V V MIN MIN MAX MAX [MID TID …].</summary>
    public static IReadOnlyList<Mode06MonitorResult> ParseMid(string raw, byte mid)
    {
        var bytes = ObdResponseParser.ParseHex(raw);
        var results = new List<Mode06MonitorResult>();
        var seen = new HashSet<(byte, byte)>();
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0x46 || i + 1 >= bytes.Length || bytes[i + 1] != mid)
                continue;

            var j = i + 1;
            while (j + 8 < bytes.Length && bytes[j] == mid)
            {
                var tid = bytes[j + 1];
                var uasid = bytes[j + 2];
                var value = ToRaw((bytes[j + 3] << 8) | bytes[j + 4], uasid);
                var min = ToRaw((bytes[j + 5] << 8) | bytes[j + 6], uasid);
                var max = ToRaw((bytes[j + 7] << 8) | bytes[j + 8], uasid);
                if (seen.Add((tid, uasid)))
                {
                    results.Add(new Mode06MonitorResult(tid, 0, min, max, value, 0)
                    {
                        MonitorId = mid,
                        UnitScalingId = uasid
                    });
                }
                j += 9;
            }
            i = j - 1;
        }
        return results;
    }
}
