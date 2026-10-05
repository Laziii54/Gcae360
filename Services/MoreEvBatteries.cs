using static CarScanner.Services.HvPackProtocol;

namespace CarScanner.Services;

/// <summary>
/// BMW i3 (2014–2022): battery management (SME) through BMW extended addressing
/// (6F1, target 07 → 607), read-only. From OBDb BMW-i3 (CC BY-SA 4.0), checked
/// against its recorded answers (2015–2021).
/// </summary>
public static class BmwI3Battery
{
    private static HvRequest Sme(int did) => new(0x6F1, 0x607, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) }, 0x07);

    public static readonly HvRequest Health = Sme(0x6335);
    public static readonly HvRequest Voltage = Sme(0xDD68);
    public static readonly HvRequest Current = Sme(0xDD69);
    public static readonly HvRequest Soc = Sme(0xDDBC);
    public static readonly HvRequest CellRange = Sme(0xDDBF);
    public static readonly HvRequest Temperatures = Sme(0xDDC0);
    public static readonly HvRequest CellStatistics = Sme(0xDFA0);

    public static bool IsI3(string? manufacturer, string? model) =>
        (manufacturer ?? string.Empty).Contains("BMW", StringComparison.OrdinalIgnoreCase) &&
        (model ?? string.Empty).Replace(" ", string.Empty).Contains("i3", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nominal cell capacity of a new pack by model year (BMW: 60 / 94 / 120 Ah).</summary>
    public static int? NominalAh(int? modelYear) => modelYear switch
    {
        null => null,
        <= 2016 => 60,
        <= 2018 => 94,
        _ => 120
    };

    public static double? DecodeSoh(byte[]? data) =>
        data is { Length: >= 4 } && data[3] is > 0 and <= 100 ? data[3] : null;

    public static double? DecodeVoltage(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 100d;
        return volts is > 50 and < 1000 ? volts : null;
    }

    public static double? DecodeCurrent(byte[]? data)
    {
        if (data is null || data.Length < 4) return null;
        var raw = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        var amps = raw / 100d;
        return amps is > -1000 and < 1000 ? Math.Round(amps, 1) : null;
    }

    public static double? DecodeSoc(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var soc = U16(data, 0) / 10d;
        return soc is >= 0 and <= 100 ? soc : null;
    }

    /// <summary>Lowest and highest cell (V) from one answer.</summary>
    public static (double Max, double Min)? DecodeCellRange(byte[]? data)
    {
        if (data is null || data.Length < 4) return null;
        var min = U16(data, 0) / 1000d;
        var max = U16(data, 2) / 1000d;
        return min is >= 2.0 and <= 4.5 && max is >= 2.0 and <= 4.5 && max >= min ? (max, min) : null;
    }

    public static double[] DecodeTemperatures(byte[]? data)
    {
        if (data is null || data.Length < 4) return Array.Empty<double>();
        return new[] { S16(data, 0) / 100d, S16(data, 2) / 100d }
            .Where(value => value is > -40 and < 90)
            .Select(value => Math.Round(value, 1))
            .ToArray();
    }

    /// <summary>Average cell capacity (Ah) as the battery management estimates it.</summary>
    public static double? DecodeAverageCapacity(byte[]? data)
    {
        if (data is null || data.Length < 6) return null;
        var ah = U16(data, 4) / 100d;
        return ah is > 5 and < 200 ? Math.Round(ah, 1) : null;
    }
}

/// <summary>
/// Ford Fusion / C-Max Hybrid and Energi (and Lincoln MKZ Hybrid), Mustang Mach-E: battery
/// energy control module (7E4 → 7EC), read-only. From OBDb Ford-Fusion-Hybrid
/// (CC BY-SA 4.0), checked against its recorded answers (2013–2020). There is
/// no list of single cells; the module reports the spread between its modules.
/// </summary>
public static class FordHybridBattery
{
    private static HvRequest Becm(int did) => new(0x7E4, 0x7EC, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });

    public static readonly HvRequest Temperature = Becm(0x4800);
    public static readonly HvRequest Soc = Becm(0x4801);
    public static readonly HvRequest TemperatureRange = Becm(0x4808);
    public static readonly HvRequest Current = Becm(0x480B);
    public static readonly HvRequest Voltage = Becm(0x480D);
    public static readonly HvRequest Age = Becm(0x4810);
    public static readonly HvRequest VoltageSpread = Becm(0x483F);
    public static readonly HvRequest MinimumModule = Becm(0x4840);
    public static readonly HvRequest AverageModule = Becm(0x4841);
    // Mustang Mach-E (confirmed 2021–2024 in OBDb); not confirmed on Fusion / C-Max.
    public static readonly HvRequest Health = Becm(0x490C);
    public static readonly HvRequest MachECurrent = Becm(0x48F9);

    public static bool IsMachE(string? manufacturer, string? model) =>
        (manufacturer ?? string.Empty).Contains("Ford", StringComparison.OrdinalIgnoreCase) &&
        (model ?? string.Empty).Contains("Mach", StringComparison.OrdinalIgnoreCase);

    public static double? DecodeSoh(byte[]? data) =>
        data is { Length: >= 1 } && data[0] / 2d is > 0 and <= 100 ? data[0] / 2d : null;

    public static double? DecodeMachECurrent(byte[]? data) =>
        data is { Length: >= 2 } ? Math.Round(S16(data, 0) / 10d, 1) : null;

    public static bool IsFordHybrid(string? manufacturer, string? model)
    {
        var make = manufacturer ?? string.Empty;
        var name = (model ?? string.Empty).Replace(" ", string.Empty);
        return (make.Contains("Ford", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Fusion", StringComparison.OrdinalIgnoreCase) || name.Contains("C-Max", StringComparison.OrdinalIgnoreCase) || name.Contains("CMax", StringComparison.OrdinalIgnoreCase))) ||
               (make.Contains("Lincoln", StringComparison.OrdinalIgnoreCase) && name.Contains("MKZ", StringComparison.OrdinalIgnoreCase));
    }

    public static double? DecodeTemperature(byte[]? data) =>
        data is { Length: >= 1 } && data[0] != 0xFF && data[0] - 50 is > -40 and < 90 ? data[0] - 50 : null;

    public static double[] DecodeTemperatureRange(byte[]? data)
    {
        if (data is null || data.Length < 2) return Array.Empty<double>();
        return new[] { data[0], data[1] }.Where(b => b != 0xFF).Select(b => b - 50d).Where(t => t is > -40 and < 90).ToArray();
    }

    public static double? DecodeSoc(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var soc = Math.Round(U16(data, 0) / 500d, 1);
        return soc is > 0 and <= 100 ? soc : null;
    }

    public static double? DecodeCurrent(byte[]? data) =>
        data is { Length: >= 2 } ? Math.Round(S16(data, 0) / 50d, 1) : null;

    public static double? DecodeVoltage(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 100d;
        return volts is > 50 and < 1000 ? Math.Round(volts, 1) : null;
    }

    /// <summary>Battery age (years) as the module counts it.</summary>
    public static double? DecodeAgeYears(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var hours = U16(data, 0) * 720d / 200d;
        var years = hours / 8766d;
        return years is > 0 and < 40 ? Math.Round(years, 1) : null;
    }

    /// <summary>Spread or module voltage (V, ÷ 1000).</summary>
    public static double? DecodeMillivolts(byte[]? data, double max)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 1000d;
        return volts >= 0 && volts <= max ? volts : null;
    }
}

/// <summary>
/// VW e-Golf: battery management (7E5 → 7ED), read-only. From OBDb
/// Volkswagen-e-Golf (CC BY-SA 4.0), checked against its recorded answers.
/// Only plausible values are used: OBDb's e-Golf cell, temperature, health and
/// capacity entries decode to impossible numbers on its own recordings, so
/// they are left out.
/// </summary>
public static class VwEGolfBattery
{
    private static HvRequest Bms(int did) => new(0x7E5, 0x7ED, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });

    public static readonly HvRequest Soc = Bms(0x028C);
    public static readonly HvRequest Voltage = Bms(0x1E3B);
    public static readonly HvRequest Current = Bms(0x1E3D);
    public static readonly HvRequest MaximumCell = Bms(0x1E33);
    public static readonly HvRequest MinimumCell = Bms(0x1E34);

    public static bool IsEGolf(string? manufacturer, string? model)
    {
        var name = (model ?? string.Empty).Replace(" ", string.Empty);
        return (manufacturer ?? string.Empty).Contains("Volkswagen", StringComparison.OrdinalIgnoreCase) || (manufacturer ?? string.Empty).Equals("VW", StringComparison.OrdinalIgnoreCase)
            ? name.Contains("e-Golf", StringComparison.OrdinalIgnoreCase) || name.Contains("eGolf", StringComparison.OrdinalIgnoreCase)
            : false;
    }

    public static double? DecodeSoc(byte[]? data) =>
        data is { Length: >= 1 } ? Math.Round(data[0] * 100d / 255d, 1) : null;

    public static double? DecodeVoltage(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 4d;
        return volts is > 50 and < 1000 ? volts : null;
    }

    public static double? DecodeCurrent(byte[]? data) =>
        data is { Length: >= 2 } ? Math.Round(U16(data, 0) / 4d - 511, 1) : null;

    /// <summary>Highest or lowest cell (V, ÷ 4096) and its number.</summary>
    public static (double Volts, int Cell)? DecodeCell(byte[]? data)
    {
        if (data is null || data.Length < 4) return null;
        var volts = Math.Round(U16(data, 0) / 4096d, 4);
        return volts is >= 2.0 and <= 4.5 ? (volts, data[3]) : null;
    }
}

/// <summary>
/// Audi e-tron (2019–2023, the first generation; not the Q4 / MEB cars):
/// battery management (7E5 → 7ED), read-only. From OBDb Audi-e-tron
/// (CC BY-SA 4.0), checked against its recorded answers (2019–2026).
/// </summary>
public static class AudiEtronBattery
{
    private static HvRequest Bms(int did) => new(0x7E5, 0x7ED, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });

    public static readonly HvRequest Soc = Bms(0x028C);
    public static readonly HvRequest Health = Bms(0x51E0);
    public static readonly HvRequest MaximumCell = Bms(0x1E33);
    public static readonly HvRequest MinimumCell = Bms(0x1E34);
    public static readonly HvRequest MaximumTemperature = Bms(0x1E0E);
    public static readonly HvRequest MinimumTemperature = Bms(0x1E0F);

    /// <summary>Module requests 22 1850 … 22 1870 (six cells each).</summary>
    public static HvRequest Module(int index) => Bms(0x1850 + index);
    public const int ModuleCount = 33;

    public static bool IsEtron(string? manufacturer, string? model)
    {
        var name = (model ?? string.Empty).Replace(" ", string.Empty);
        return (manufacturer ?? string.Empty).Contains("Audi", StringComparison.OrdinalIgnoreCase) &&
               name.Contains("e-tron", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("Q4", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("Q6", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("GT", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Porsche Taycan: the same BMS requests (7E5), 33 modules of six cells.</summary>
    public static bool IsTaycan(string? manufacturer, string? model) =>
        (manufacturer ?? string.Empty).Contains("Porsche", StringComparison.OrdinalIgnoreCase) &&
        (model ?? string.Empty).Contains("Taycan", StringComparison.OrdinalIgnoreCase);

    public static double? DecodeSoc(byte[]? data) =>
        data is { Length: >= 1 } && data[0] is > 0 and <= 100 ? data[0] : null;

    /// <summary>Health: raw × 0.127 − 1798.574 (%). FFFE ("not available") and out-of-range values are dropped.</summary>
    public static double? DecodeSoh(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var soh = Math.Round(U16(data, 0) * 0.127 - 1798.574, 1);
        return soh is > 30 and <= 105 ? Math.Min(100, soh) : null;
    }

    /// <summary>Highest / lowest cell (V). Newer software reports ÷ 10 000, older ÷ 1 000.</summary>
    public static double? DecodeCell(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var raw = U16(data, 0);
        foreach (var scale in new[] { 10_000d, 1_000d })
        {
            var volts = Math.Round(raw / scale, 4);
            if (volts is >= 2.0 and <= 4.5)
                return volts;
        }
        return null;
    }

    public static double? DecodeTemperature(byte[]? data) =>
        data is { Length: >= 1 } && data[0] - 100 is > -40 and < 90 ? data[0] - 100 : null;

    /// <summary>The cells of one module: voltage at byte 1 + 7·i (÷ 10 000). Stops at the first empty slot.</summary>
    public static double[] DecodeModule(byte[]? data)
    {
        if (data is null || data.Length < 3) return Array.Empty<double>();
        var cells = new List<double>();
        for (var i = 0; i < 6 && 2 + 7 * i < data.Length; i++)
        {
            var volts = Math.Round(U16(data, 1 + 7 * i) / 10_000d, 4);
            if (volts is < 2.0 or > 4.5)
                break;
            cells.Add(volts);
        }
        return cells.ToArray();
    }
}

/// <summary>
/// Haval Jolion HEV: battery management (78B → 7CB), read-only. From OBDb
/// Haval-Jolion-HEV (CC BY-SA 4.0), checked against its recorded answers (2024).
/// </summary>
public static class HavalHevBattery
{
    private static HvRequest Bms(int did) => new(0x78B, 0x7CB, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });

    public static readonly HvRequest Health = Bms(0xE0F5);
    public static readonly HvRequest Soc = Bms(0xE0F4);
    public static readonly HvRequest Voltage = Bms(0xE006);
    public static readonly HvRequest MaximumCell = Bms(0xE008);
    public static readonly HvRequest MinimumCell = Bms(0xE009);
    public static readonly HvRequest MaximumCellPosition = Bms(0xE00A);
    public static readonly HvRequest MinimumCellPosition = Bms(0xE00B);
    public static readonly HvRequest Current = Bms(0xE0A4);

    public static bool IsJolionHev(string? manufacturer, string? model) =>
        (manufacturer ?? string.Empty).Contains("Haval", StringComparison.OrdinalIgnoreCase) ||
        ((manufacturer ?? string.Empty).Contains("Great Wall", StringComparison.OrdinalIgnoreCase) &&
         (model ?? string.Empty).Contains("Jolion", StringComparison.OrdinalIgnoreCase));

    public static double? DecodeSoh(byte[]? data) =>
        data is { Length: >= 1 } && data[0] is > 0 and <= 100 ? data[0] : null;

    public static double? DecodeSoc(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var soc = U16(data, 0) / 10d;
        return soc is >= 0 and <= 100 ? soc : null;
    }

    public static double? DecodeVoltage(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 10d;
        return volts is > 50 and < 1000 ? volts : null;
    }

    public static double? DecodeCell(byte[]? data)
    {
        if (data is null || data.Length < 2) return null;
        var volts = U16(data, 0) / 1000d;
        return volts is >= 2.0 and <= 4.5 ? volts : null;
    }

    public static int? DecodePosition(byte[]? data) => data is { Length: >= 1 } ? data[0] + 1 : null;

    public static double? DecodeCurrent(byte[]? data) =>
        data is { Length: >= 2 } ? Math.Round(U16(data, 0) / 5d - 1200, 1) : null;
}
