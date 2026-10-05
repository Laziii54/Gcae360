using static CarScanner.Services.HvPackProtocol;

namespace CarScanner.Services;

/// <summary>
/// Toyota / Lexus hybrid battery: read-only requests and their decoding.
///
/// Layouts follow OBDb (https://github.com/OBDb, CC BY-SA 4.0) and are checked
/// against its recorded car responses in the tests:
///  • 2004–2009 (Toyota-Prius): 7E3 21CE blocks, 21CF temperatures, 21D0 internal resistance.
///  • ≈2012–2018 (Toyota-RAV4-Hybrid 2017, Camry Hybrid): 7E2 2181 blocks,
///    2187 temperatures, 2195 internal resistance, 2198 current, 2101 SOC.
///    Prius 2010–2015 is community-documented with the same 21 81 request.
///  • 2017+ (Prius, RAV4, Camry, Corolla, C-HR): 747 22182E cells, 221814
///    temperatures; 7D2 221F5B SOC, 221F9A voltage / current.
/// Only 0x21 / 0x22 read requests exist here — nothing is written to the car.
/// </summary>
public static class ToyotaHybridBattery
{
    public static readonly HvRequest Gen2Blocks = new(0x7E3, 0x7EB, new byte[] { 0x21, 0xCE });
    public static readonly HvRequest Gen2Thermal = new(0x7E3, 0x7EB, new byte[] { 0x21, 0xCF });
    public static readonly HvRequest Gen2Resistance = new(0x7E3, 0x7EB, new byte[] { 0x21, 0xD0 });

    public static readonly HvRequest Gen3Blocks = new(0x7E2, 0x7EA, new byte[] { 0x21, 0x81 });
    public static readonly HvRequest Gen3BlocksAlternative = new(0x7E3, 0x7EB, new byte[] { 0x21, 0x81 });
    public static readonly HvRequest Gen3Thermal = new(0x7E2, 0x7EA, new byte[] { 0x21, 0x87 });
    public static readonly HvRequest Gen3Resistance = new(0x7E2, 0x7EA, new byte[] { 0x21, 0x95 });
    public static readonly HvRequest Gen3Current = new(0x7E2, 0x7EA, new byte[] { 0x21, 0x98 });
    public static readonly HvRequest Gen3Status = new(0x7E2, 0x7EA, new byte[] { 0x21, 0x01 });

    public static readonly HvRequest TngaCells = new(0x747, 0x74F, new byte[] { 0x22, 0x18, 0x2E });
    public static readonly HvRequest TngaTemperatures = new(0x747, 0x74F, new byte[] { 0x22, 0x18, 0x14 });
    public static readonly HvRequest TngaSoc = new(0x7D2, 0x7DA, new byte[] { 0x22, 0x1F, 0x5B });
    public static readonly HvRequest TngaPower = new(0x7D2, 0x7DA, new byte[] { 0x22, 0x1F, 0x9A });

    /// <summary>Order in which the layouts are tried, most likely first by model year.</summary>
    public static IReadOnlyList<HvPackKind> ProbeOrder(int? modelYear) => modelYear switch
    {
        <= 2009 => new[] { HvPackKind.ToyotaGen2NiMh, HvPackKind.ToyotaGen3NiMh, HvPackKind.ToyotaCells },
        <= 2016 => new[] { HvPackKind.ToyotaGen3NiMh, HvPackKind.ToyotaGen2NiMh, HvPackKind.ToyotaCells },
        > 2016 => new[] { HvPackKind.ToyotaCells, HvPackKind.ToyotaGen3NiMh, HvPackKind.ToyotaGen2NiMh },
        _ => new[] { HvPackKind.ToyotaGen2NiMh, HvPackKind.ToyotaGen3NiMh, HvPackKind.ToyotaCells }
    };

    private static double Offset327(int raw) => Math.Round(raw / 100d - 327.68, 2);

    // ------------------------------------------------------------ 2004–2009

    /// <summary>7E3 21CE: SOC, current and the 14 block voltages.</summary>
    public static HvPackSample? DecodeGen2Blocks(byte[]? data)
    {
        const int blocks = 14;
        if (data is null || data.Length < 3 + blocks * 2)
            return null;

        var values = new double[blocks];
        for (var i = 0; i < blocks; i++)
            values[i] = Offset327(U16(data, 3 + i * 2));

        if (!values.All(value => value is >= 6 and <= 20))
            return null;

        var soc = data[0] / 2d;
        var current = Offset327(U16(data, 1));
        return new HvPackSample(values, soc is >= 0 and <= 100 ? soc : null, current is > -300 and < 300 ? current : null);
    }

    /// <summary>7E3 21CF: battery temperatures 1–3 (°C).</summary>
    public static double[] DecodeGen2Temperatures(byte[]? data)
    {
        if (data is null || data.Length < 16)
            return Array.Empty<double>();
        return new[] { 10, 12, 14 }
            .Select(index => Offset327(U16(data, index)))
            .Where(value => value is > -40 and < 90)
            .ToArray();
    }

    /// <summary>7E3 21D0: internal resistance of each block (mΩ).</summary>
    public static int[] DecodeGen2Resistances(byte[]? data)
    {
        if (data is null || data.Length < 16)
            return Array.Empty<int>();
        var count = data[0];
        if (count is < 6 or > 20 || data.Length < 15 + count)
            return Array.Empty<int>();
        var values = data.Skip(15).Take(count).Select(b => (int)b).ToArray();
        return values.All(value => value > 0) ? values : Array.Empty<int>();
    }

    // ------------------------------------------------------------ ≈2010–2018

    /// <summary>
    /// 21 81: consecutive 16-bit block voltages × 79.99 / 65535 (14 on Prius,
    /// 17 on RAV4 / Camry). Accepted as a run of at least 6 plausible NiMH
    /// blocks (6–20 V, within 3 V); the run ends at the first other value.
    /// </summary>
    public static HvPackSample? DecodeGen3Blocks(byte[]? data)
    {
        if (data is null || data.Length < 12)
            return null;

        double[] best = Array.Empty<double>();
        for (var offset = 0; offset < 2 && best.Length == 0; offset++)
        {
            var run = new List<double>();
            for (var i = offset; i + 1 < data.Length; i += 2)
            {
                var value = Math.Round(U16(data, i) * 79.99 / 65535d, 3);
                var fits = value is >= 6 and <= 20 &&
                           (run.Count == 0 || Math.Max(run.Max(), value) - Math.Min(run.Min(), value) <= 3);
                if (fits)
                {
                    run.Add(value);
                    if (run.Count > best.Length && run.Count >= 6)
                        best = run.ToArray();
                }
                else if (run.Count >= 6)
                {
                    break; // offset 0 is the documented layout: stop at the end of the blocks
                }
                else
                {
                    run.Clear();
                }
            }
        }

        return best.Length == 0 ? null : new HvPackSample(best.Take(20).ToArray(), null, null);
    }

    /// <summary>7E2 2187: battery temperatures TB1–TB3 (°C); the intake sensor is skipped.</summary>
    public static double[] DecodeGen3Temperatures(byte[]? data)
    {
        if (data is null || data.Length < 8)
            return Array.Empty<double>();
        return new[] { 2, 4, 6 }
            .Select(index => Math.Round(U16(data, index) * 0.0039047837 - 50, 1))
            .Where(value => value is > -40 and < 90)
            .ToArray();
    }

    /// <summary>7E2 2195: internal resistance R01… (one byte per block, mΩ).</summary>
    public static int[] DecodeGen3Resistances(byte[]? data, int blocks)
    {
        if (data is null || blocks < 6 || data.Length < blocks)
            return Array.Empty<int>();
        var values = data.Take(blocks).Select(b => (int)b).ToArray();
        return values.All(value => value > 0) ? values : Array.Empty<int>();
    }

    /// <summary>7E2 2198: pack current (A, positive = discharge).</summary>
    public static double? DecodeGen3Current(byte[]? data)
    {
        if (data is null || data.Length < 2)
            return null;
        var current = Offset327(U16(data, 0));
        return current is > -300 and < 300 ? current : null;
    }

    /// <summary>7E2 2101: state of charge (byte 21, × 20 / 51).</summary>
    public static double? DecodeGen3Soc(byte[]? data)
    {
        if (data is null || data.Length < 22)
            return null;
        var soc = Math.Round(data[21] * 20d / 51d, 1);
        return soc is > 0 and <= 100 ? soc : null;
    }

    // ------------------------------------------------------------ 2017+

    /// <summary>747 22182E: cell voltages (× 5 / 65535). Unused slots are zero.</summary>
    public static HvPackSample? DecodeTngaCells(byte[]? data)
    {
        if (data is null || data.Length < 20)
            return null;

        var cells = new List<double>();
        for (var i = 0; i + 1 < data.Length && cells.Count < 96; i += 2)
        {
            var value = Math.Round(U16(data, i) * 5d / 65535d, 4);
            if (value is < 2.0 or > 4.5)
                break;
            cells.Add(value);
        }

        return cells.Count >= 10 ? new HvPackSample(cells.ToArray(), null, null) : null;
    }

    /// <summary>747 221814: battery temperatures (°C). Zero slots are unused.</summary>
    public static double[] DecodeTngaTemperatures(byte[]? data)
    {
        if (data is null || data.Length < 2)
            return Array.Empty<double>();
        var values = new List<double>();
        for (var i = 0; i + 1 < data.Length && values.Count < 16; i += 2)
        {
            var raw = U16(data, i);
            if (raw == 0)
                continue;
            var celsius = Math.Round(raw * 19531d / 5_000_000d - 50, 1);
            if (celsius is > -40 and < 90)
                values.Add(celsius);
        }
        return values.ToArray();
    }

    /// <summary>7D2 221F5B: state of charge (%).</summary>
    public static double? DecodeTngaSoc(byte[]? data) =>
        data is { Length: >= 1 } ? Math.Round(data[0] * 100d / 255d, 1) : null;

    /// <summary>7D2 221F9A: pack voltage (V) and current (A).</summary>
    public static (double? Voltage, double? Current) DecodeTngaPower(byte[]? data)
    {
        if (data is null || data.Length < 6)
            return (null, null);
        var voltage = Math.Round(U16(data, 2) * 156.25 / 10_000d, 1);
        var current = Math.Round(S16(data, 4) / 10d, 1);
        return (voltage is > 50 and < 1000 ? voltage : null, current is > -500 and < 500 ? current : null);
    }
}
