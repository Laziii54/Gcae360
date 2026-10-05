using static CarScanner.Services.HvPackProtocol;

namespace CarScanner.Services;

/// <summary>
/// Chevrolet Volt (2011–2019, also Opel Ampera / Cadillac ELR) and Chevrolet
/// Bolt EV / EUV: battery energy control module (BECM, 7E7 → 7EF), read-only.
///
/// Each of the 96 cell groups is its own identifier (22 4181–419F, 22 4200–4240;
/// value × 5 / 65535 V), so a full reading takes 96 requests. Layouts follow
/// OBDb Chevrolet-Volt / Chevrolet-Bolt-EV / Chevrolet-Bolt-EUV (CC BY-SA 4.0)
/// and are checked against recorded car responses in the tests.
/// Only values OBDb marks as confirmed on real cars are used:
///  • Volt: cells (2011–2019), battery temperature 7E4 22434F;
///  • Bolt: cells, SOC 7E4 22432F, pack voltage 22432D, min/max temperature
///    224349 / 22434A, current 7E7 2240D4, capacity 2241A3 (≤2018) / 2245F9 (2019+).
/// </summary>
public static class GmVoltBoltBattery
{
    public const int CellCount = 96;

    /// <summary>Identifier of cell <paramref name="index"/> (0-based).</summary>
    public static int CellDid(int index) => index < 31 ? 0x4181 + index : 0x4200 + (index - 31);

    public static HvRequest CellRequest(int index)
    {
        var did = CellDid(index);
        return new HvRequest(0x7E7, 0x7EF, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });
    }

    private static HvRequest Ecm7E4(int did) => new(0x7E4, 0x7EC, new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });

    public static readonly HvRequest BatteryTemperature = Ecm7E4(0x434F);
    public static readonly HvRequest BoltSoc = Ecm7E4(0x432F);
    public static readonly HvRequest BoltPackVoltage = Ecm7E4(0x432D);
    public static readonly HvRequest BoltMaxTemperature = Ecm7E4(0x4349);
    public static readonly HvRequest BoltMinTemperature = Ecm7E4(0x434A);
    public static readonly HvRequest BoltCapacityTo2018 = Ecm7E4(0x41A3);
    public static readonly HvRequest BoltCapacityFrom2019 = Ecm7E4(0x45F9);
    public static readonly HvRequest BoltCurrent = new(0x7E7, 0x7EF, new byte[] { 0x22, 0x40, 0xD4 });

    /// <summary>One cell (V), or null for an empty / implausible answer (the BECM reports 0 while waking up).</summary>
    public static double? DecodeCell(byte[]? data)
    {
        if (data is null || data.Length < 2)
            return null;
        var volts = Math.Round(U16(data, 0) * 5d / 65535d, 4);
        return volts is >= 2.0 and <= 4.5 ? volts : null;
    }

    /// <summary>Temperature byte − 40 (°C).</summary>
    public static double? DecodeTemperature(byte[]? data)
    {
        if (data is null || data.Length < 1)
            return null;
        var celsius = data[0] - 40d;
        return celsius is > -40 and < 90 ? celsius : null;
    }

    /// <summary>State of charge (byte × 20 / 51).</summary>
    public static double? DecodeSoc(byte[]? data)
    {
        if (data is null || data.Length < 1)
            return null;
        var soc = Math.Round(data[0] * 20d / 51d, 1);
        return soc is > 0 and <= 100 ? soc : null;
    }

    /// <summary>Pack voltage (16-bit × 0.52 V).</summary>
    public static double? DecodePackVoltage(byte[]? data)
    {
        if (data is null || data.Length < 2)
            return null;
        var volts = Math.Round(U16(data, 0) * 0.52, 1);
        return volts is > 50 and < 1000 ? volts : null;
    }

    /// <summary>Pack current (signed 16-bit ÷ 20 A), as in OBDb's recorded Bolt EUV answer.</summary>
    public static double? DecodeCurrent(byte[]? data)
    {
        if (data is null || data.Length < 2)
            return null;
        var amps = Math.Round(S16(data, 0) / 20d, 1);
        return amps is > -1000 and < 1000 ? amps : null;
    }

    /// <summary>Capacity estimated by the BECM (Ah): ≤2018 ÷ 10, 2019+ ÷ 100.</summary>
    public static double? DecodeCapacity(byte[]? data, bool from2019)
    {
        if (data is null || data.Length < 2)
            return null;
        var ah = Math.Round(U16(data, 0) / (from2019 ? 100d : 10d), 1);
        return ah is > 10 and < 400 ? ah : null;
    }

    /// <summary>Which GM car this is, from make and model (null when not a Volt / Bolt family car).</summary>
    public static bool? IsBolt(string? manufacturer, string? model)
    {
        var make = manufacturer ?? string.Empty;
        var name = model ?? string.Empty;
        var gm = make.Contains("Chevrolet", StringComparison.OrdinalIgnoreCase) ||
                 make.Contains("Opel", StringComparison.OrdinalIgnoreCase) ||
                 make.Contains("Vauxhall", StringComparison.OrdinalIgnoreCase) ||
                 make.Contains("Holden", StringComparison.OrdinalIgnoreCase) ||
                 make.Contains("Cadillac", StringComparison.OrdinalIgnoreCase);
        if (!gm)
            return null;
        // Opel / Vauxhall Ampera-e is the European Bolt EV.
        if (name.Contains("Bolt", StringComparison.OrdinalIgnoreCase) ||
            name.Replace(" ", "-").Contains("Ampera-e", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("Volt", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ampera", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ELR", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }
}
