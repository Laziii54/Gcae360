using System.Globalization;

namespace CarScanner.Services.Energy;

/// <summary>How fuel economy is shown. The value is always computed in L/100 km first.</summary>
public enum FuelEconomyUnit
{
    LitersPer100Km,
    KmPerLiter,
    MpgUs,
    MpgUk
}

/// <summary>
/// Fuel-economy and volume conversions. All internal calculations stay in litres and kilometres; only this class
/// turns them into the unit the user picked.
///
///   km/L      = 100 / (L/100 km)
///   MPG (US)  = 100 · 3.785411784 / (1.609344 · L/100 km) = 235.2145833 / (L/100 km)
///   MPG (UK)  = 100 · 4.54609     / (1.609344 · L/100 km) = 282.4809363 / (L/100 km)
///
/// A value that is zero, negative, infinite or not a number cannot be converted (division by zero or a meaningless
/// result): the conversion returns null and the page shows an idle / unavailable state instead of "∞".
/// </summary>
public static class FuelUnits
{
    public const double LitersPerUsGallon = 3.785411784d;
    public const double LitersPerUkGallon = 4.54609d;
    public const double KilometersPerMile = 1.609344d;

    /// <summary>Above this a "consumption" is a division artefact (almost standing still), not a measurement.</summary>
    public const double MaximumPlausibleLPer100Km = 200d;

    /// <summary>L/100 km → the chosen unit. Null when the input is not a usable positive number.</summary>
    public static double? FromLPer100Km(double? litersPer100Km, FuelEconomyUnit unit)
    {
        if (litersPer100Km is not { } value || !double.IsFinite(value) || value <= 0)
            return null;

        return unit switch
        {
            FuelEconomyUnit.LitersPer100Km => value,
            FuelEconomyUnit.KmPerLiter => 100d / value,
            FuelEconomyUnit.MpgUs => 100d * LitersPerUsGallon / (KilometersPerMile * value),
            FuelEconomyUnit.MpgUk => 100d * LitersPerUkGallon / (KilometersPerMile * value),
            _ => null
        };
    }

    /// <summary>The chosen unit → L/100 km. Null when the input is not a usable positive number.</summary>
    public static double? ToLPer100Km(double? value, FuelEconomyUnit unit)
    {
        if (value is not { } number || !double.IsFinite(number) || number <= 0)
            return null;

        return unit switch
        {
            FuelEconomyUnit.LitersPer100Km => number,
            FuelEconomyUnit.KmPerLiter => 100d / number,
            FuelEconomyUnit.MpgUs => 100d * LitersPerUsGallon / (KilometersPerMile * number),
            FuelEconomyUnit.MpgUk => 100d * LitersPerUkGallon / (KilometersPerMile * number),
            _ => null
        };
    }

    public static string Label(FuelEconomyUnit unit) => unit switch
    {
        FuelEconomyUnit.LitersPer100Km => "L/100 km",
        FuelEconomyUnit.KmPerLiter => "km/L",
        FuelEconomyUnit.MpgUs => "MPG (US)",
        _ => "MPG (UK)"
    };

    /// <summary>true for the units where a bigger number means better economy (distance per volume).</summary>
    public static bool HigherIsBetter(FuelEconomyUnit unit) => unit != FuelEconomyUnit.LitersPer100Km;

    /// <summary>Number only (one decimal), or "—" when it cannot be shown.</summary>
    public static string FormatNumber(double? litersPer100Km, FuelEconomyUnit unit)
    {
        var converted = FromLPer100Km(litersPer100Km, unit);
        return converted is { } value ? value.ToString("0.0", CultureInfo.InvariantCulture) : "—";
    }

    /// <summary>"7.4 L/100 km", "≈ 7.4 L/100 km" for an estimate, or "—".</summary>
    public static string Format(double? litersPer100Km, FuelEconomyUnit unit, bool estimate = false)
    {
        var number = FormatNumber(litersPer100Km, unit);
        return number == "—" ? number : $"{(estimate ? "≈ " : string.Empty)}{number} {Label(unit)}";
    }

    /// <summary>Volume in the unit that belongs to the economy unit: litres, US gallons or imperial gallons.</summary>
    public static (double Value, string Unit) Volume(double liters, FuelEconomyUnit unit) => unit switch
    {
        FuelEconomyUnit.MpgUs => (liters / LitersPerUsGallon, "gal (US)"),
        FuelEconomyUnit.MpgUk => (liters / LitersPerUkGallon, "gal (UK)"),
        _ => (liters, "L")
    };

    public static string FormatVolume(double liters, FuelEconomyUnit unit, bool estimate = false)
    {
        var (value, label) = Volume(liters, unit);
        return $"{(estimate ? "≈ " : string.Empty)}{value.ToString("0.00", CultureInfo.InvariantCulture)} {label}";
    }

    /// <summary>Fuel flow: L/h, or gallons per hour in the gallon units.</summary>
    public static string FormatRate(double litersPerHour, FuelEconomyUnit unit, bool estimate = false)
    {
        var (value, label) = Volume(litersPerHour, unit);
        return $"{(estimate ? "≈ " : string.Empty)}{value.ToString("0.00", CultureInfo.InvariantCulture)} {label}/h";
    }

    /// <summary>Starting choice before the user picks one: US region → MPG (US), UK → MPG (UK), else L/100 km.</summary>
    public static FuelEconomyUnit DefaultFor(bool imperialUnits, string? regionIso2)
    {
        if (string.Equals(regionIso2, "GB", StringComparison.OrdinalIgnoreCase))
            return FuelEconomyUnit.MpgUk;
        return imperialUnits ? FuelEconomyUnit.MpgUs : FuelEconomyUnit.LitersPer100Km;
    }

    public static FuelEconomyUnit Parse(string? text, FuelEconomyUnit fallback) =>
        Enum.TryParse<FuelEconomyUnit>(text, true, out var unit) && Enum.IsDefined(unit) ? unit : fallback;

    /// <summary>Energy economy for electric driving: kWh per 100 km (null without a usable distance).</summary>
    public static double? KWhPer100Km(double kilowattHours, double kilometers) =>
        double.IsFinite(kilowattHours) && double.IsFinite(kilometers) && kilometers >= 0.1
            ? kilowattHours / kilometers * 100d
            : null;

    /// <summary>The same electric economy as miles per kWh (for imperial users).</summary>
    public static double? MilesPerKWh(double? kWhPer100Km) =>
        kWhPer100Km is { } value && double.IsFinite(value) && value > 0
            ? 100d / (value * KilometersPerMile)
            : null;
}
