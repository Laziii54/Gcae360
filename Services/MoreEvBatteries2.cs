using static CarScanner.Services.HvPackProtocol;

namespace CarScanner.Services;

/// <summary>
/// Honda CR-V / Civic / Accord hybrid 2023+: the battery ECU (CAN 29-bit
/// 18DA01F1 → 18DAF101) returns all cells in one answer, read-only. From OBDb
/// Honda-CR-V-Hybrid / Honda-Civic (CC BY-SA 4.0), checked against recorded answers (2023, 2027).
/// </summary>
public static class HondaHybridBattery
{
    private static HvRequest Bms(int did) => new(0x18DA01F1, 0x18DAF101, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) }, null, true);

    public static readonly HvRequest Cells = Bms(0x2028);
    public static readonly HvRequest Status = Bms(0x202A);
    public static readonly HvRequest Temperatures = Bms(0x202C);

    public static bool IsHondaHybrid(string? manufacturer, string? model, int? year)
    {
        var make = manufacturer ?? string.Empty;
        if (!make.Contains("Honda", StringComparison.OrdinalIgnoreCase))
            return false;
        // The recorded answers start with the 2023 model year. Older Honda hybrids
        // (Insight, Accord 2014–2017) use other ECUs and are not read here.
        return year is >= 2023 || (year is null && (model ?? string.Empty).Contains("Hybrid", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Cells from byte 27 on, 2 bytes each, raw ÷ 5 mV; stops at the first empty slot (84 slots, 60–72 used).</summary>
    public static double[] DecodeCells(byte[]? data)
    {
        const int start = 27;
        if (data is null || data.Length < start + 2)
            return Array.Empty<double>();
        var cells = new List<double>();
        for (var i = 0; i < 84 && start + 2 * i + 1 < data.Length; i++)
        {
            var volts = Math.Round(U16(data, start + 2 * i) / 5000d, 4);
            if (volts is < 2.0 or > 4.6)
                break;
            cells.Add(volts);
        }
        return cells.Count >= 10 ? cells.ToArray() : Array.Empty<double>();
    }

    public sealed record StatusBlock(double? SocPercent, double? CurrentA, double? PackVoltage, double? CapacityAh);

    /// <summary>SOC byte 50, current (signed ÷ 50) at 57, voltage (÷ 20) at 61, capacity (÷ 100 Ah) at 167.</summary>
    public static StatusBlock? DecodeStatus(byte[]? data)
    {
        if (data is null || data.Length < 63)
            return null;
        double? soc = data[50] <= 100 ? data[50] : null;
        double? current = Math.Round(S16(data, 57) / 50d, 2);
        double? volts = U16(data, 61) / 20d;
        if (volts is not (> 50 and < 500))
            volts = null;
        double? capacity = data.Length >= 169 && U16(data, 167) / 100d is > 5 and < 250 ? U16(data, 167) / 100d : null;
        return soc is null && volts is null ? null : new StatusBlock(soc, current, volts, capacity);
    }

    /// <summary>Module temperatures 1A / 3A (signed ÷ 10 °C); unused slots are 0 and dropped.</summary>
    public static double[] DecodeTemperatures(byte[]? data)
    {
        if (data is null || data.Length < 65)
            return Array.Empty<double>();
        // OBDb names modules 1A and 3A (bytes 59 and 63); the slot between them is not documented.
        var values = new List<double>();
        foreach (var index in new[] { 59, 63 })
        {
            var raw = S16(data, index);
            if (raw != 0 && raw / 10d is > -30 and < 90)
                values.Add(raw / 10d);
        }
        return values.ToArray();
    }
}

/// <summary>
/// Jaguar I-PACE: battery controller (7E4 → 7EC), read-only. From OBDb
/// Jaguar-I-PACE (CC BY-SA 4.0), checked against recorded answers (2019–2023).
/// </summary>
public static class JaguarIPaceBattery
{
    private static HvRequest Bms(int did) => new(0x7E4, 0x7EC, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });

    public static readonly HvRequest MaximumCell = Bms(0x4903);
    public static readonly HvRequest MinimumCell = Bms(0x4904);
    public static readonly HvRequest Temperature = Bms(0x4907);
    public static readonly HvRequest Voltage = Bms(0x490F);
    public static readonly HvRequest Soc = Bms(0x4910);
    public static readonly HvRequest Health = Bms(0x4918);

    public static bool IsIPace(string? manufacturer, string? model)
    {
        var name = (model ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
        return (manufacturer ?? string.Empty).Contains("Jaguar", StringComparison.OrdinalIgnoreCase) &&
               name.Contains("IPACE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Highest / lowest cell in millivolts.</summary>
    public static double? DecodeCell(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = Math.Round(U16(data, 0) / 1000d, 3);
        return volts is >= 2.0 and <= 4.5 ? volts : null;
    }

    public static double? DecodeTemperature(byte[]? data) =>
        data is { Length: >= 1 } && data[0] / 2d - 40 is > -40 and < 90 ? data[0] / 2d - 40 : null;

    public static double? DecodeVoltage(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 100d;
        return volts is > 100 and < 500 ? volts : null;
    }

    public static double? DecodeSoc(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var soc = U16(data, 0) / 100d;
        return soc is >= 0 and <= 100 ? soc : null;
    }

    public static double? DecodeSoh(byte[]? data)
    {
        if (data is null || data.Length < 1) return null;
        var soh = data[0] / 2d;
        return soh is > 30 and <= 100 ? soh : null;
    }
}

/// <summary>
/// MG ZS EV: pack values from the BMS (781 → 789, 2020) and per-module minimum /
/// maximum cell voltage (7E5 → 7ED, 2021+), read-only. From OBDb MG-ZS-EV
/// (CC BY-SA 4.0), checked against recorded answers (2020, 2022–2024).
/// </summary>
public static class MgZsEvBattery
{
    private static HvRequest Pack(int did) => new(0x781, 0x789, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });
    private static HvRequest Cmu(int did) => new(0x7E5, 0x7ED, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });

    public static readonly HvRequest Soc = Pack(0xB046);
    public static readonly HvRequest Voltage = Pack(0xB042);
    public static readonly HvRequest Health = Pack(0xB061);

    /// <summary>Module requests 22 B001, B009, B011 … B029 (six modules).</summary>
    public static readonly HvRequest[] Modules = new[] { 0xB001, 0xB009, 0xB011, 0xB019, 0xB021, 0xB029 }.Select(Cmu).ToArray();

    public static bool IsZsEv(string? manufacturer, string? model) =>
        ((manufacturer ?? string.Empty).Equals("MG", StringComparison.OrdinalIgnoreCase) ||
         (manufacturer ?? string.Empty).StartsWith("MG ", StringComparison.OrdinalIgnoreCase) ||
         (manufacturer ?? string.Empty).Contains("Morris", StringComparison.OrdinalIgnoreCase) ||
         (manufacturer ?? string.Empty).Contains("SAIC", StringComparison.OrdinalIgnoreCase)) &&
        (model ?? string.Empty).Replace(" ", string.Empty).Contains("ZS", StringComparison.OrdinalIgnoreCase);

    public static double? DecodeSoc(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var soc = U16(data, 0) / 10d;
        return soc is >= 0 and <= 100 ? soc : null;
    }

    public static double? DecodeVoltage(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 4d;
        return volts is > 100 and < 500 ? volts : null;
    }

    public static double? DecodeSoh(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var soh = U16(data, 0) / 100d;
        return soh is > 40 and <= 100 ? soh : null;
    }

    public sealed record ModuleReading(double MinVolts, double MaxVolts, double MinTemperature, double MaxTemperature);

    /// <summary>Module block: min / max cell = raw × 0.0005 + 1 V, min / max temperature = raw × 0.5 − 40 °C.</summary>
    public static ModuleReading? DecodeModule(byte[]? data)
    {
        if (data is null || data.Length < 6) return null;
        var min = Math.Round(U16(data, 0) * 0.0005 + 1, 4);
        var max = Math.Round(U16(data, 2) * 0.0005 + 1, 4);
        var tMin = data[4] * 0.5 - 40;
        var tMax = data[5] * 0.5 - 40;
        return min is >= 2.0 and <= 4.5 && max is >= 2.0 and <= 4.5 && max >= min && tMin is > -40 and < 90
            ? new ModuleReading(min, max, tMin, tMax)
            : null;
    }
}
