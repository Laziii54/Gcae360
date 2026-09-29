using Plugin.BLE.Abstractions.Contracts;

namespace CarScanner.Models;

public sealed class BleDeviceItem
{
    public IDevice? Device { get; init; }

    public string? ClassicAddress { get; init; }

    public string? NameOverride { get; init; }

    public bool IsPaired { get; init; }

    public bool IsClassicBluetooth =>
        !string.IsNullOrWhiteSpace(ClassicAddress);

    public string ConnectionType =>
        IsClassicBluetooth
            ? IsPaired ? "Bluetooth Classic • დაპეარებული" : "Bluetooth Classic"
            : "Bluetooth Low Energy (BLE)";

    public string Name =>
        CleanName(NameOverride ?? Device?.Name);

    /// <summary>
    /// Android does not always expose a BLE advertising name.  Keep that fact
    /// separate from the display fallback so scan results can be deduplicated
    /// without treating every unnamed device as a different OBD adapter.
    /// </summary>
    public bool HasRecognizableName =>
        !string.IsNullOrWhiteSpace(NameOverride ?? Device?.Name) &&
        !string.Equals(NameOverride ?? Device?.Name, "Unknown", StringComparison.OrdinalIgnoreCase);

    public string Id
    {
        get
        {
            if (IsClassicBluetooth)
                return $"classic:{ClassicAddress}";

            if (Device != null)
                return $"ble:{Device.Id}";

            return $"name:{DisplayName}";
        }
    }

    public int Rssi { get; init; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? "OBD-II Adapter"
            : Name;

    public string DeviceIdentifier =>
        IsClassicBluetooth
            ? $"Bluetooth: {ClassicAddress}"
            : Device != null
                ? $"BLE: {Device.Id}"
                : "იდენტიფიკატორი მიუწვდომელია";

    // Never infer that an unnamed BLE advertisement is an OBD adapter from
    // our display fallback ("OBD-II Adapter").  That used to turn every
    // nearby unnamed BLE device into a false OBD result.
    public bool LooksLikeObd =>
        HasRecognizableName &&
        IsObdName(NameOverride ?? Device?.Name ?? string.Empty);

    public static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "OBD-II Adapter";

        var value = raw.Trim();

        var pieces = value.Split(
            new[] { ' ', '-', '_', ':', '/' },
            StringSplitOptions.RemoveEmptyEntries);

        var cleaned = string.Join(
            " ",
            pieces.Where(p =>
                !IsAddressLike(p) &&
                !IsSerialLike(p)));

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned =
                value.Contains(
                    "ELM",
                    StringComparison.OrdinalIgnoreCase)
                    ? "ELM327"
                    : "OBD-II Adapter";
        }

        return cleaned.Length > 32
            ? cleaned[..32]
            : cleaned;
    }

    private static bool IsObdName(string name)
    {
        string[] obdNames =
        {
            "OBD",
            "ELM",
            "ELM327",
            "VGATE",
            "V-LINK",
            "VLINK",
            "OBDLINK",
            "KONNWEI",
            "ICAR",
            "VEEPEAK",
            "LAUNCH",
            "THINKDIAG",
            "AUTOPHIX",
            "ANCEL",
            "FOXWELL",
            "TOPDON"
        };

        return obdNames.Any(
            x => name.Contains(
                x,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAddressLike(string value)
    {
        if (value.Length < 8)
            return false;

        var hexCount =
            value.Count(c =>
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'F') ||
                (c >= 'a' && c <= 'f'));

        return hexCount >= 8 &&
               value.Any(char.IsDigit);
    }

    private static bool IsSerialLike(string value)
    {
        return value.Length <= 8 &&
               value.Contains('0') &&
               value.All(char.IsLetterOrDigit);
    }
}
