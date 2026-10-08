using System.Globalization;
using System.Text.RegularExpressions;

namespace CarScanner.Services.OBD;

public sealed class PIDDecoder
{
    private static readonly HashSet<int> DecodablePids =
    [
        0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D,
        0x0E, 0x0F, 0x10, 0x11, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
        0x1A, 0x1B, 0x1F, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B,
        0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32,
        0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B,
        0x3C, 0x3D, 0x3E, 0x3F, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A,
        0x4B, 0x4C, 0x4D, 0x4E, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x5B, 0x5C, 0x5D, 0x5E,
        0x61, 0x62, 0x63, 0x66, 0x67, 0x68, 0x73, 0x77, 0x78, 0x79, 0x7C, 0x7F,
        0x84, 0x8D, 0x8E, 0x9D, 0x9E, 0xA2, 0xA4, 0xA6
    ];

    public static bool CanDecode(int pid) => DecodablePids.Contains(pid);

    public bool TryDecodeFreezeFrame(int pid, string raw, out double value)
    {
        value = 0;
        try
        {
            var bytes = ParseHexBytes(raw);
            for (var index = 0; index + 2 < bytes.Length; index++)
            {
                if (bytes[index] != 0x42 || bytes[index + 1] != pid) continue;
                var normalized = "41" + pid.ToString("X2") + string.Concat(bytes[(index + 2)..].Select(item => item.ToString("X2")));
                return TryDecode(pid, normalized, out value);
            }
        }
        catch { }

        return false;
    }

    public bool TryDecode(int pid, string raw, out double value)
    {
        value = 0;
        try
        {
            var d = Read(raw, (byte)pid);
            value = pid switch
            {
                0x04 => U8(d) * 100.0 / 255.0,
                0x05 => U8(d) - 40.0,
                0x06 or 0x07 or 0x08 or 0x09 => (U8(d) - 128.0) * 100.0 / 128.0,
                0x0A => U8(d) * 3.0,
                0x0B => U8(d),
                0x0C => U16(d) / 4.0,
                0x0D => U8(d),
                0x0E => U8(d) / 2.0 - 64.0,
                0x0F => U8(d) - 40.0,
                0x10 => U16(d) / 100.0,
                0x11 => U8(d) * 100.0 / 255.0,
                >= 0x14 and <= 0x1B => U8(d) / 200.0,
                0x1F => U16(d),
                0x21 or 0x31 => U16(d),
                0x22 => U16(d) * 0.079,
                0x23 => U16(d) * 10.0,
                >= 0x24 and <= 0x2B => U16(d) * 2.0 / 65536.0,
                0x2C or 0x2E => U8(d) * 100.0 / 255.0,
                0x2D => (U8(d) - 128.0) * 100.0 / 128.0,
                0x2F => U8(d) * 100.0 / 255.0,
                0x30 => U8(d),
                0x32 => (short)U16(d) / 4.0,
                0x33 => U8(d),
                >= 0x34 and <= 0x3B => U16(d) * 2.0 / 65536.0,
                >= 0x3C and <= 0x3F => U16(d) / 10.0 - 40.0,
                0x42 => U16(d) / 1000.0,
                0x43 => U16(d) * 100.0 / 255.0,
                0x44 => U16(d) * 2.0 / 65536.0,
                0x45 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x5A => U8(d) * 100.0 / 255.0,
                0x46 => U8(d) - 40.0,
                0x4D or 0x4E => U16(d),
                0x50 => U8(d) * 10.0,
                0x51 => U8(d),
                0x52 => U8(d) * 100.0 / 255.0,
                0x53 => U16(d) / 200.0,
                0x54 => (short)U16(d),
                >= 0x55 and <= 0x58 => (U8(d) - 128.0) * 100.0 / 128.0,
                0x59 => U16(d) * 10.0,
                0x5B => U8(d) * 100.0 / 255.0,
                0x5C => U8(d) - 40.0,
                0x5D => U16(d) / 128.0 - 210.0,
                0x5E => U16(d) / 20.0,
                0x61 or 0x62 or 0x8E => U8(d) - 125.0,
                0x63 => U16(d),
                // Multi-value PIDs: byte A says which sensors exist; the first sensor is shown.
                0x66 => Supported(d, 0) ? At16(d, 1) / 32.0 : double.NaN,
                0x67 or 0x68 or 0x77 => Supported(d, 0) ? At8(d, 1) - 40.0 : double.NaN,
                0x73 => Supported(d, 0) ? At16(d, 1) * 0.01 : double.NaN,
                0x78 or 0x79 => Supported(d, 0) ? At16(d, 1) / 10.0 - 40.0 : double.NaN,
                0x7C => U16(d) / 10.0 - 40.0,
                0x7F => Supported(d, 0) ? At32(d, 1) / 3600.0 : double.NaN,
                0x84 => U8(d) - 40.0,
                0x8D => U8(d) * 100.0 / 255.0,
                0x9D => U16(d) * 0.02,
                0x9E => U16(d) / 5.0,
                0xA2 => U16(d) / 32.0,
                0xA4 => Supported(d, 1) && At8(d, 1) >> 4 is >= 1 and <= 10 ? At8(d, 1) >> 4 : double.NaN,
                0xA6 => U32(d) / 10.0,
                _ => double.NaN
            };
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
        catch { value = 0; return false; }
    }

    /// <summary>Status-type PIDs (no single number) that are shown as text.</summary>
    private static readonly HashSet<int> DescribablePids = [0x01, 0x03, 0x12, 0x13, 0x1C, 0x1D, 0x1E, 0x51];

    public static bool CanDescribe(int pid) => DescribablePids.Contains(pid);

    private static string L(string ka, string en) => ObdLang.L(ka, en);

    /// <summary>Plain-language text for status-type PIDs (MIL, fuel system, O2 sensors present …).</summary>
    public bool TryDescribe(int pid, string raw, out string text)
    {
        text = string.Empty;
        try
        {
            var d = Read(raw, (byte)pid);
            text = pid switch
            {
                0x01 => ((U8(d) & 0x80) != 0 ? L("შეცდომის ნათურა: ანთია", "Check-engine light: ON") : L("შეცდომის ნათურა: ჩამქრალია", "Check-engine light: OFF"))
                        + " · " + L($"კოდები: {U8(d) & 0x7F}", $"codes: {U8(d) & 0x7F}"),
                0x03 => FuelSystemStatus(U8(d)),
                0x12 => U8(d) switch
                {
                    1 => L("ზემოთ (კატალიზატორამდე)", "Upstream"),
                    2 => L("კატალიზატორის შემდეგ", "Downstream of catalyst"),
                    4 => L("გარე ჰაერი / გამორთული", "From outside / off"),
                    8 => L("დიაგნოსტიკისთვის ჩართული", "Pump on for diagnostics"),
                    _ => L("უცნობი", "Unknown")
                },
                0x13 => SensorList(U8(d), twoSensorBanks: false),
                0x1D => SensorList(U8(d), twoSensorBanks: true),
                0x1C => ObdStandard(U8(d)),
                0x1E => (U8(d) & 0x01) != 0 ? L("PTO ჩართულია", "PTO active") : L("PTO გამორთულია", "PTO off"),
                0x51 => FuelType(U8(d)),
                _ => string.Empty
            };
            return text.Length > 0;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private static string FuelSystemStatus(int value) => value switch
    {
        1 => L("ღია ციკლი — ძრავი ჯერ არ გამთბარა", "Open loop — engine not warm yet"),
        2 => L("დახურული ციკლი — ჟანგბადის სენსორით (ნორმალური)", "Closed loop — using O2 sensor (normal)"),
        4 => L("ღია ციკლი — დატვირთვა ან დამუხრუჭება", "Open loop — load or deceleration"),
        8 => L("ღია ციკლი — სისტემის ხარვეზი", "Open loop — system fault"),
        16 => L("დახურული ციკლი — ხარვეზით", "Closed loop — with a fault"),
        _ => L("უცნობი", "Unknown")
    };

    private static string SensorList(int mask, bool twoSensorBanks)
    {
        var names = new List<string>();
        for (var bit = 0; bit < 8; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            var bank = twoSensorBanks ? bit / 2 + 1 : bit / 4 + 1;
            var sensor = twoSensorBanks ? bit % 2 + 1 : bit % 4 + 1;
            names.Add($"B{bank}S{sensor}");
        }
        return names.Count == 0 ? L("არცერთი", "None") : string.Join(", ", names);
    }

    private static string ObdStandard(int value) => value switch
    {
        1 => "OBD-II (CARB)",
        2 => "OBD (EPA)",
        3 => "OBD + OBD-II",
        4 => "OBD-I",
        5 => L("არ არის OBD", "Not OBD compliant"),
        6 => "EOBD (Europe)",
        7 => "EOBD + OBD-II",
        8 => "EOBD + OBD",
        9 => "EOBD + OBD + OBD-II",
        10 => "JOBD (Japan)",
        11 => "JOBD + OBD-II",
        12 => "JOBD + EOBD",
        13 => "JOBD + EOBD + OBD-II",
        17 => "EMD",
        18 => "EMD+",
        19 => "HD OBD-C",
        20 => "HD OBD",
        21 => "WWH OBD",
        23 => "HD EOBD-I",
        24 => "HD EOBD-I N",
        25 => "HD EOBD-II",
        26 => "HD EOBD-II N",
        28 => "OBDBr-1",
        29 => "OBDBr-2",
        30 => "KOBD (Korea)",
        31 => "IOBD I (India)",
        32 => "IOBD II (India)",
        33 => "HD EOBD-IV",
        _ => L($"სტანდარტი {value}", $"Standard {value}")
    };

    private static string FuelType(int code) => code switch
    {
        1 => L("ბენზინი", "Gasoline"),
        2 => L("მეთანოლი", "Methanol"),
        3 => L("ეთანოლი", "Ethanol"),
        4 => L("დიზელი", "Diesel"),
        5 => "LPG",
        6 => "CNG",
        7 => L("პროპანი", "Propane"),
        8 => L("ელექტრო", "Electric"),
        9 => L("ორსაწვავიანი: ბენზინი", "Bifuel: gasoline"),
        10 => L("ორსაწვავიანი: მეთანოლი", "Bifuel: methanol"),
        11 => L("ორსაწვავიანი: ეთანოლი", "Bifuel: ethanol"),
        12 => L("ორსაწვავიანი: LPG", "Bifuel: LPG"),
        13 => L("ორსაწვავიანი: CNG", "Bifuel: CNG"),
        14 => L("ორსაწვავიანი: პროპანი", "Bifuel: propane"),
        15 => L("ორსაწვავიანი: ელექტრო", "Bifuel: electric"),
        16 => L("ორსაწვავიანი: ელექტრო + ძრავი", "Bifuel: electric + engine"),
        17 => L("ჰიბრიდი: ბენზინი", "Hybrid: gasoline"),
        18 => L("ჰიბრიდი: ეთანოლი", "Hybrid: ethanol"),
        19 => L("ჰიბრიდი: დიზელი", "Hybrid: diesel"),
        20 => L("ჰიბრიდი: ელექტრო", "Hybrid: electric"),
        21 => L("ჰიბრიდი: ელექტრო + ძრავი", "Hybrid: electric + engine"),
        22 => L("ჰიბრიდი: რეგენერაციით", "Hybrid: regenerative"),
        23 => L("ორსაწვავიანი: დიზელი", "Bifuel: diesel"),
        _ => L($"საწვავის კოდი {code}", $"Fuel code {code}")
    };

    /// <summary>Data bytes of a Mode 01 reply as hex (for PIDs without a decoder).</summary>
    public static string RawData(int pid, string raw)
    {
        try
        {
            return string.Join(" ", Read(raw, (byte)pid).Select(b => b.ToString("X2")));
        }
        catch
        {
            return string.Empty;
        }
    }

    public double DecodeRpm(string raw) => DecodeRequired(0x0C, raw);
    public double DecodeSpeed(string raw) => DecodeRequired(0x0D, raw);
    public double DecodeCoolant(string raw) => DecodeRequired(0x05, raw);
    public double DecodeThrottle(string raw) => DecodeRequired(0x11, raw);
    public double DecodeFuelLevel(string raw) => DecodeRequired(0x2F, raw);
    public double DecodeVoltage(string raw) => DecodeRequired(0x42, raw);
    public double DecodeEngineLoad(string raw) => DecodeRequired(0x04, raw);
    public double DecodeMAP(string raw) => DecodeRequired(0x0B, raw);
    public double DecodeMAF(string raw) => DecodeRequired(0x10, raw);
    public double DecodeFuelPressure(string raw) => DecodeRequired(0x0A, raw);
    public double DecodeFuelTrimShortBank1(string raw) => DecodeRequired(0x06, raw);
    public double DecodeFuelTrimLongBank1(string raw) => DecodeRequired(0x07, raw);
    public double DecodeIntakeAir(string raw) => DecodeRequired(0x0F, raw);
    public double DecodeAmbientTemp(string raw) => DecodeRequired(0x46, raw);
    public double DecodeOilTemp(string raw) => DecodeRequired(0x5C, raw);

    private double DecodeRequired(int pid, string raw)
    {
        if (!TryDecode(pid, raw, out var value)) throw new FormatException($"PID 01{pid:X2} response could not be decoded.");
        return value;
    }

    public static byte[] ParseHexBytes(string raw) => ObdResponseParser.ParseHex(raw);

    private static byte[] Read(string raw, byte pid)
    {
        var bytes = ParseHexBytes(raw);
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == 0x41 && bytes[i + 1] == pid) return bytes[(i + 2)..];
        }
        throw new FormatException($"PID 01{pid:X2} response not found.");
    }

    private static bool Supported(byte[] d, int bit) => d.Length > 0 && (d[0] & (1 << bit)) != 0;
    private static int At8(byte[] d, int i) { if (d.Length <= i) throw new FormatException("Missing data byte."); return d[i]; }
    private static int At16(byte[] d, int i) { if (d.Length < i + 2) throw new FormatException("Missing data bytes."); return (d[i] << 8) | d[i + 1]; }
    private static long At32(byte[] d, int i) { if (d.Length < i + 4) throw new FormatException("Missing data bytes."); return ((long)d[i] << 24) | ((long)d[i + 1] << 16) | ((long)d[i + 2] << 8) | d[i + 3]; }

    private static int U8(byte[] d) { if (d.Length < 1) throw new FormatException("Missing data byte."); return d[0]; }
    private static int U16(byte[] d) { if (d.Length < 2) throw new FormatException("Missing two data bytes."); return (d[0] << 8) | d[1]; }
    private static long U32(byte[] d) { if (d.Length < 4) throw new FormatException("Missing four data bytes."); return ((long)d[0] << 24) | ((long)d[1] << 16) | ((long)d[2] << 8) | d[3]; }
    public static IReadOnlyCollection<int> GetKnownPidCodes() => DecodablePids;

}
