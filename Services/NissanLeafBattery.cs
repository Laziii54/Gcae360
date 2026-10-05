using static CarScanner.Services.HvPackProtocol;

namespace CarScanner.Services;

/// <summary>
/// Nissan Leaf (and e-NV200) lithium battery controller (LBC, 79B → 7BB), read-only.
///
/// From OBDb Nissan-Leaf (CC BY-SA 4.0), checked against its recorded car answers:
///  • 21 61 — state of health (%), bytes 2–3 ÷ 100 (2012–2024);
///  • 21 01 — pack voltage (bytes 18–19 ÷ 100), capacity in Ah
///    (≤2017 bytes 33–35, 2018+ bytes 35–37, ÷ 10 000), SOC ≤2017 (bytes 29–31 ÷ 10 000);
///  • 21 03 — highest / lowest cell voltage (bytes 10–13, mV).
/// 21 02 (96 cell voltages, 16-bit mV) is the request every Leaf tool uses but
/// OBDb has no recorded answer for it yet, so the cells are accepted only when
/// they are plausible and, where 21 03 answers, agree with its highest / lowest cell.
/// </summary>
public static class NissanLeafBattery
{
    public static readonly HvRequest Health = new(0x79B, 0x7BB, new byte[] { 0x21, 0x61 });
    public static readonly HvRequest Status = new(0x79B, 0x7BB, new byte[] { 0x21, 0x01 });
    public static readonly HvRequest Cells = new(0x79B, 0x7BB, new byte[] { 0x21, 0x02 });
    public static readonly HvRequest CellRange = new(0x79B, 0x7BB, new byte[] { 0x21, 0x03 });

    public static bool IsLeaf(string? manufacturer, string? model) =>
        (manufacturer ?? string.Empty).Contains("Nissan", StringComparison.OrdinalIgnoreCase) &&
        ((model ?? string.Empty).Contains("Leaf", StringComparison.OrdinalIgnoreCase) ||
         (model ?? string.Empty).Replace(" ", string.Empty).Contains("e-NV200", StringComparison.OrdinalIgnoreCase));

    private static int U24(byte[] data, int index) => (data[index] << 16) | (data[index + 1] << 8) | data[index + 2];

    /// <summary>State of health (%).</summary>
    public static double? DecodeSoh(byte[]? data)
    {
        if (data is null || data.Length < 4)
            return null;
        var soh = Math.Round(U16(data, 2) / 100d, 1);
        return soh is > 0 and <= 110 ? Math.Min(100, soh) : null;
    }

    public sealed record StatusValues(double? PackVoltage, double? SocPercent, double? CapacityAh);

    public static StatusValues? DecodeStatus(byte[]? data, int? modelYear)
    {
        if (data is null || data.Length < 20)
            return null;

        var voltage = Math.Round(U16(data, 18) / 100d, 1);
        var newer = modelYear is >= 2018;
        double? capacity = null, soc = null;
        var capacityIndex = newer ? 35 : 33;
        if (data.Length >= capacityIndex + 3)
        {
            var ah = Math.Round(U24(data, capacityIndex) / 10_000d, 1);
            capacity = ah is > 5 and < 300 ? ah : null;
        }
        if (!newer && data.Length >= 32)
        {
            var percent = Math.Round(U24(data, 29) / 10_000d, 1);
            soc = percent is > 0 and <= 100 ? percent : null;
        }

        return new StatusValues(voltage is > 50 and < 1000 ? voltage : null, soc, capacity);
    }

    /// <summary>Highest and lowest cell (V) as the battery controller reports them.</summary>
    public static (double Max, double Min)? DecodeCellRange(byte[]? data)
    {
        if (data is null || data.Length < 14)
            return null;
        var max = U16(data, 10) / 1000d;
        var min = U16(data, 12) / 1000d;
        return max is >= 2.0 and <= 4.5 && min is >= 2.0 and <= 4.5 && max >= min ? (max, min) : null;
    }

    /// <summary>96 cell voltages (16-bit mV). Null unless all 96 are plausible.</summary>
    public static double[]? DecodeCells(byte[]? data)
    {
        if (data is null || data.Length < 192)
            return null;
        var cells = new double[96];
        for (var i = 0; i < 96; i++)
        {
            var volts = U16(data, i * 2) / 1000d;
            if (volts is < 2.0 or > 4.5)
                return null;
            cells[i] = volts;
        }
        return cells;
    }

    /// <summary>The cells agree with the controller's own highest / lowest cell (±15 mV).</summary>
    public static bool AgreesWithRange(IReadOnlyList<double> cells, (double Max, double Min) range) =>
        Math.Abs(cells.Max() - range.Max) <= 0.015 && Math.Abs(cells.Min() - range.Min) <= 0.015;
}
