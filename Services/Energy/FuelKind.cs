namespace CarScanner.Services.Energy;

/// <summary>The combustion fuel of the car. Decides whether, and how, a fuel flow can be calculated from air flow.</summary>
public enum FuelKind
{
    /// <summary>Not known. No air-flow based estimate is made: assuming gasoline for a diesel would overstate fuel use several times over.</summary>
    Unknown,
    Gasoline,
    Diesel,
    Ethanol,
    Methanol,
    Lpg,
    /// <summary>Compressed natural gas is measured in kg or m³, not litres: no litre-based estimate exists.</summary>
    Cng,
    /// <summary>Battery-electric: there is no liquid fuel.</summary>
    Electric,
    /// <summary>A hybrid / bi-fuel code that does not say which fuel burns (SAE J1979 PID 51 codes 15, 16, 20, 21, 22).</summary>
    Mixed
}

/// <summary>Constants behind an air-flow (MAF) based estimate. They are typical values, not measurements of this car.</summary>
public sealed record MafConstants(double StoichiometricAfr, double DensityKgPerLiter, string Range);

public static class FuelKinds
{
    /// <summary>SAE J1979 PID 01 51 "fuel type" → the combustion fuel (when the code names one).</summary>
    public static FuelKind FromPidCode(int? code) => code switch
    {
        1 or 9 or 17 => FuelKind.Gasoline,           // gasoline, bi-fuel running gasoline, hybrid gasoline
        2 or 10 => FuelKind.Methanol,
        3 or 11 or 18 => FuelKind.Ethanol,
        4 or 19 or 23 => FuelKind.Diesel,            // diesel, hybrid diesel, bi-fuel running diesel
        5 or 7 or 12 or 14 => FuelKind.Lpg,          // LPG, propane and the bi-fuel variants
        6 or 13 => FuelKind.Cng,
        8 => FuelKind.Electric,
        15 or 16 or 20 or 21 or 22 => FuelKind.Mixed,
        _ => FuelKind.Unknown
    };

    /// <summary>The combustion fuel named in vehicle-info text ("Gasoline", "Diesel", "Electric, Gasoline" …).</summary>
    public static FuelKind FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return FuelKind.Unknown;

        var value = text.ToLowerInvariant();
        if (value.Contains("diesel") || value.Contains("დიზ")) return FuelKind.Diesel;
        if (value.Contains("gasoline") || value.Contains("petrol") || value.Contains("ბენზ")) return FuelKind.Gasoline;
        if (value.Contains("ethanol") || value.Contains("e85") || value.Contains("flex")) return FuelKind.Ethanol;
        if (value.Contains("methanol")) return FuelKind.Methanol;
        if (value.Contains("lpg") || value.Contains("propane") || value.Contains("autogas")) return FuelKind.Lpg;
        if (value.Contains("cng") || value.Contains("natural gas")) return FuelKind.Cng;
        if (value.Contains("electric") || value.Contains("ელექტრ")) return FuelKind.Electric;
        return FuelKind.Unknown;
    }

    /// <summary>
    /// The best statement of the fuel: a user's choice first, then a PID 51 code that names a fuel, then the vehicle text.
    /// A "hybrid, fuel unspecified" code never overrides a clear text.
    /// </summary>
    public static FuelKind Resolve(FuelKind? userChoice, int? pidCode, string? vehicleText)
    {
        if (userChoice is { } chosen && chosen != FuelKind.Unknown)
            return chosen;

        var fromCode = FromPidCode(pidCode);
        if (fromCode is not (FuelKind.Unknown or FuelKind.Mixed))
        {
            // PID 51 8 ("electric") on a plug-in whose text names a combustion fuel: the text says what burns.
            var text = FromText(vehicleText);
            if (fromCode == FuelKind.Electric && text is not (FuelKind.Unknown or FuelKind.Electric))
                return text;
            return fromCode;
        }

        var fromText = FromText(vehicleText);
        return fromText != FuelKind.Unknown ? fromText : fromCode;
    }

    /// <summary>
    /// Stoichiometric air-fuel ratio and liquid density for an air-flow estimate. Diesel returns its stoichiometric ratio too,
    /// but a diesel runs lean (the real ratio is 20–70 or more), so the estimate is only allowed with a measured λ.
    /// </summary>
    public static bool TryGetMafConstants(FuelKind kind, out MafConstants constants)
    {
        switch (kind)
        {
            case FuelKind.Gasoline:
                constants = new MafConstants(14.7, 0.745, "AFR 14.7:1 (λ = 1); density 0.72–0.775 kg/L");
                return true;
            case FuelKind.Diesel:
                constants = new MafConstants(14.5, 0.835, "stoichiometric 14.5:1 divided by the measured λ; density 0.82–0.85 kg/L");
                return true;
            case FuelKind.Ethanol:
                constants = new MafConstants(9.0, 0.789, "AFR 9.0:1 (E100); a blend such as E85 differs a little");
                return true;
            case FuelKind.Methanol:
                constants = new MafConstants(6.5, 0.792, "AFR 6.5:1");
                return true;
            case FuelKind.Lpg:
                constants = new MafConstants(15.5, 0.55, "AFR about 15.5:1; liquid density 0.51–0.58 kg/L depends on the propane/butane mix");
                return true;
            default:
                constants = null!;
                return false;
        }
    }

    public static string Display(FuelKind kind, bool english) => kind switch
    {
        FuelKind.Gasoline => english ? "Gasoline" : "ბენზინი",
        FuelKind.Diesel => english ? "Diesel" : "დიზელი",
        FuelKind.Ethanol => english ? "Ethanol / E85" : "ეთანოლი / E85",
        FuelKind.Methanol => english ? "Methanol" : "მეთანოლი",
        FuelKind.Lpg => english ? "LPG / propane" : "აირი (LPG)",
        FuelKind.Cng => english ? "CNG" : "CNG",
        FuelKind.Electric => english ? "Electric" : "ელექტრო",
        FuelKind.Mixed => english ? "Hybrid / bi-fuel (fuel not stated)" : "ჰიბრიდი (საწვავი არ არის მითითებული)",
        _ => english ? "Unknown" : "უცნობია"
    };
}
