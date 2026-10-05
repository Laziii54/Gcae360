using static CarScanner.Services.HvPackProtocol;

namespace CarScanner.Services;

/// <summary>
/// Hyundai / Kia / Genesis battery management system (BMS, 7E4 → 7EC):
/// read-only requests and their decoding. The same layout is used by the
/// hybrids, plug-ins and EVs (Ioniq, Niro, Kona Electric, Elantra / Sonata /
/// Tucson / Santa Fe / Sorento / Sportage hybrids …):
///  • older (≈2016–2019): 21 01 status, 21 02 / 03 / 04 cells, 21 05 SOH;
///  • newer (≈2020+):     22 0101 status, 22 0102 / 03 / 04 / 0A / 0B / 0C cells, 22 0105 SOH.
/// Cell voltage = byte / 50 (20 mV steps), 32 cells per request from byte 4.
/// Layouts follow OBDb (Kia-Niro, Hyundai-Kona-Electric, Hyundai-IONIQ-Electric …,
/// CC BY-SA 4.0) and are checked against recorded car responses in the tests.
/// </summary>
public static class HyundaiKiaBattery
{
    public enum Generation { Kwp21, Uds22 }

    public static HvRequest Status(Generation generation) => generation == Generation.Uds22
        ? new HvRequest(0x7E4, 0x7EC, new byte[] { 0x22, 0x01, 0x01 })
        : new HvRequest(0x7E4, 0x7EC, new byte[] { 0x21, 0x01 });

    public static HvRequest Health(Generation generation) => generation == Generation.Uds22
        ? new HvRequest(0x7E4, 0x7EC, new byte[] { 0x22, 0x01, 0x05 })
        : new HvRequest(0x7E4, 0x7EC, new byte[] { 0x21, 0x05 });

    /// <summary>Cell requests in order (32 cells each).</summary>
    public static IReadOnlyList<HvRequest> CellRequests(Generation generation) => generation == Generation.Uds22
        ? new byte[] { 0x02, 0x03, 0x04, 0x0A, 0x0B, 0x0C }.Select(id => new HvRequest(0x7E4, 0x7EC, new byte[] { 0x22, 0x01, id })).ToArray()
        : new byte[] { 0x02, 0x03, 0x04 }.Select(id => new HvRequest(0x7E4, 0x7EC, new byte[] { 0x21, id })).ToArray();

    /// <summary>Status block: SOC, current, pack voltage and temperatures.</summary>
    public sealed record StatusValues(double? SocPercent, double? CurrentA, double? PackVoltage, double[] Temperatures);

    public static StatusValues? DecodeStatus(byte[]? data)
    {
        if (data is null || data.Length < 16)
            return null;

        var soc = data[4] / 2d;
        var current = S16(data, 10) / 10d;
        var voltage = U16(data, 12) / 10d;
        var temperatures = new List<double>();
        // The BMS's own maximum / minimum (bytes 14, 15). Module sensor positions
        // differ between hybrids and EVs, so they are not used.
        foreach (var index in new[] { 14, 15 })
        {
            if (index >= data.Length)
                break;
            var celsius = S8(data, index);
            if (celsius is > -40 and < 90)
                temperatures.Add(celsius);
        }

        return new StatusValues(
            soc is > 0 and <= 100 ? soc : null,
            current is > -2000 and < 2000 ? current : null,
            voltage is > 50 and < 1000 ? voltage : null,
            temperatures.Distinct().ToArray());
    }

    /// <summary>32 cell voltages (from byte 4). Stops at the first unused slot.</summary>
    public static double[] DecodeCells(byte[]? data)
    {
        if (data is null || data.Length < 5)
            return Array.Empty<double>();
        var cells = new List<double>();
        for (var i = 4; i < data.Length && cells.Count < 32; i++)
        {
            var volts = data[i] / 50d;
            if (volts is < 2.0 or > 4.6)
                break;
            cells.Add(Math.Round(volts, 2));
        }
        return cells.ToArray();
    }

    /// <summary>State of health (bytes 25–26, ÷ 10). Zero means "not reported".</summary>
    public static double? DecodeSoh(byte[]? data)
    {
        if (data is null || data.Length < 27)
            return null;
        var soh = U16(data, 25) / 10d;
        return soh is > 0 and <= 110 ? Math.Min(100, soh) : null;
    }

    /// <summary>Cells 97 and 98 of 98-cell EV packs, carried in the health block (bytes 34–35).</summary>
    public static double[] DecodeExtraCells(byte[]? data)
    {
        if (data is null || data.Length < 36)
            return Array.Empty<double>();
        var cells = new List<double>();
        foreach (var index in new[] { 34, 35 })
        {
            var volts = data[index] / 50d;
            if (volts is < 2.0 or > 4.6)
                break;
            cells.Add(Math.Round(volts, 2));
        }
        return cells.ToArray();
    }

    public static bool IsHyundaiKia(string? manufacturer) =>
        manufacturer is { } make &&
        (make.Contains("Hyundai", StringComparison.OrdinalIgnoreCase) ||
         make.Contains("Kia", StringComparison.OrdinalIgnoreCase) ||
         make.Contains("Genesis", StringComparison.OrdinalIgnoreCase));
}
