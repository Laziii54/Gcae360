using System.Globalization;
using System.Text;
using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>Pure identity rules used by the runtime resolver and unit tests.</summary>
public static class VehicleProfileResolutionEngine
{
    private static readonly IReadOnlyDictionary<string, string[]> BrandAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["mercedes"] = ["mercedes", "mercedesbenz", "daimler"],
            ["volkswagen"] = ["volkswagen", "vw", "vag"],
            ["skoda"] = ["skoda"],
            ["citroen"] = ["citroen"],
            ["ssangyong"] = ["ssangyong", "kgm"],
            ["haval"] = ["haval", "greatwall", "gwm"],
            ["mg"] = ["mg", "morrisgarages"],
            ["opel"] = ["opel", "vauxhall"],
            ["land-rover"] = ["landrover"],
            ["alfa-romeo"] = ["alfaromeo"],
            ["chevrolet"] = ["chevrolet", "chevy"],
            ["mini"] = ["mini"],
            ["byd"] = ["byd"],
            ["tesla"] = ["tesla"]
        };

    public static ResolvedVehicleProfile Resolve(
        VehicleInfo detected,
        VehicleBrandProfile selectedBrand)
    {
        ArgumentNullException.ThrowIfNull(detected);
        ArgumentNullException.ThrowIfNull(selectedBrand);

        var hasVin = IsValidVin(detected.VIN);
        var hasDetectedManufacturer = IsKnown(detected.Manufacturer);
        var manufacturerOrigin = hasDetectedManufacturer
            ? OriginOr(detected.ManufacturerOrigin, VehicleDataOrigin.VinDecoded)
            : VehicleDataOrigin.Unknown;
        var hasVerifiedManufacturer = hasVin &&
                                      hasDetectedManufacturer &&
                                      IsTrustedIdentityOrigin(manufacturerOrigin);
        var hasSpecificSelection = !selectedBrand.Id.Equals("other", StringComparison.OrdinalIgnoreCase);
        var selectionMatches = hasVerifiedManufacturer &&
                               MatchesBrand(selectedBrand, detected.Manufacturer);

        var state = hasVerifiedManufacturer
            ? hasSpecificSelection && !selectionMatches
                ? VehicleProfileVerificationState.SelectionMismatch
                : VehicleProfileVerificationState.Confirmed
            : hasVin
                ? VehicleProfileVerificationState.VinManufacturerUnresolved
                : hasSpecificSelection
                    ? VehicleProfileVerificationState.SelectionOnly
                    : VehicleProfileVerificationState.Unavailable;

        var manufacturerValue = hasDetectedManufacturer
            ? detected.Manufacturer
            : hasSpecificSelection
                ? selectedBrand.DisplayName
                : "მიუწვდომელია";
        if (!hasVerifiedManufacturer && hasSpecificSelection)
            manufacturerOrigin = VehicleDataOrigin.UserSelection;

        var verificationMessage = state switch
        {
            VehicleProfileVerificationState.Confirmed when hasSpecificSelection =>
                $"{selectedBrand.DisplayName} VIN-ით დადასტურდა.",
            VehicleProfileVerificationState.Confirmed =>
                $"VIN-ით დადასტურდა: {detected.Manufacturer}.",
            VehicleProfileVerificationState.SelectionMismatch =>
                $"არჩეულია {selectedBrand.DisplayName}, მაგრამ VIN მიუთითებს {detected.Manufacturer}-ზე.",
            VehicleProfileVerificationState.VinManufacturerUnresolved =>
                "VIN მიღებულია, მაგრამ მწარმოებელი ადგილობრივად ვერ განისაზღვრა.",
            VehicleProfileVerificationState.SelectionOnly =>
                $"{selectedBrand.DisplayName} მხოლოდ მომხმარებლის არჩევანია — VIN-ით ჯერ არ დადასტურებულა.",
            _ => "VIN და მწარმოებელი ECU-დან მიუწვდომელია."
        };

        var evidence = state switch
        {
            VehicleProfileVerificationState.Confirmed =>
                $"VIN {detected.VIN} · manufacturer {detected.Manufacturer} · source {OriginText(manufacturerOrigin)}",
            VehicleProfileVerificationState.SelectionMismatch =>
                $"Selection {selectedBrand.DisplayName} ≠ VIN manufacturer {detected.Manufacturer}; OEM profile blocked.",
            VehicleProfileVerificationState.VinManufacturerUnresolved =>
                $"Valid ECU VIN {detected.VIN}; manufacturer decoder returned no verified value.",
            VehicleProfileVerificationState.SelectionOnly =>
                "No valid ECU VIN. Brand selection is used for display only.",
            _ => "No valid ECU VIN or verified manufacturer."
        };

        return new ResolvedVehicleProfile
        {
            DiagnosticVehicle = detected,
            SelectedBrand = selectedBrand,
            VerificationState = state,
            VIN = Field(detected.VIN, detected.VinOrigin, hasVin, VehicleDataOrigin.EcuMode09),
            Manufacturer = new ResolvedVehicleField(
                manufacturerValue,
                manufacturerOrigin,
                hasVerifiedManufacturer),
            Model = Field(
                detected.Model,
                detected.ModelOrigin,
                hasVin && IsKnown(detected.Model) && IsTrustedVehicleDetailOrigin(detected.ModelOrigin)),
            Year = Field(
                detected.Year,
                detected.YearOrigin,
                hasVin && IsKnown(detected.Year) &&
                IsTrustedVehicleDetailOrigin(OriginOr(detected.YearOrigin, VehicleDataOrigin.VinDecoded)),
                VehicleDataOrigin.VinDecoded),
            Engine = Field(
                detected.Engine,
                detected.EngineOrigin,
                hasVin && IsKnown(detected.Engine) && IsTrustedVehicleDetailOrigin(detected.EngineOrigin)),
            FuelType = Field(
                detected.FuelType,
                detected.FuelTypeOrigin,
                IsKnown(detected.FuelType) && IsTrustedTelemetryOrigin(detected.FuelTypeOrigin)),
            Protocol = Field(
                detected.Protocol,
                detected.ProtocolOrigin,
                IsKnown(detected.Protocol) &&
                OriginOr(detected.ProtocolOrigin, VehicleDataOrigin.AdapterNegotiated) == VehicleDataOrigin.AdapterNegotiated,
                VehicleDataOrigin.AdapterNegotiated),
            VerificationMessage = verificationMessage,
            Evidence = evidence
        };
    }

    public static bool MatchesBrand(VehicleBrandProfile selectedBrand, string? detectedManufacturer)
    {
        if (selectedBrand.Id.Equals("other", StringComparison.OrdinalIgnoreCase) ||
            !IsKnown(detectedManufacturer))
            return false;

        var detected = Normalize(detectedManufacturer!);
        var detectedTokens = Tokenize(detectedManufacturer!);
        var candidates = BrandAliases.TryGetValue(selectedBrand.Id, out var aliases)
            ? aliases
            : [selectedBrand.Id, selectedBrand.DisplayName];

        return candidates
            .Select(Normalize)
            .Where(candidate => candidate.Length >= 2)
            .Any(candidate =>
                detected.Equals(candidate, StringComparison.Ordinal) ||
                (candidate.Length <= 3
                    ? detectedTokens.Contains(candidate)
                    : detected.Contains(candidate, StringComparison.Ordinal) ||
                      candidate.Contains(detected, StringComparison.Ordinal)));
    }

    public static bool IsValidVin(string? vin) =>
        vin is { Length: 17 } &&
        vin.All(character => char.IsAsciiLetterOrDigit(character) &&
                             character is not ('I' or 'O' or 'Q' or 'i' or 'o' or 'q'));

    private static ResolvedVehicleField Field(
        string? value,
        VehicleDataOrigin origin,
        bool verified,
        VehicleDataOrigin fallbackOrigin = VehicleDataOrigin.Unknown) =>
        IsKnown(value)
            ? new ResolvedVehicleField(value!, OriginOr(origin, fallbackOrigin), verified)
            : new ResolvedVehicleField("მიუწვდომელია", VehicleDataOrigin.Unknown, false);

    private static VehicleDataOrigin OriginOr(VehicleDataOrigin origin, VehicleDataOrigin fallback) =>
        origin == VehicleDataOrigin.Unknown ? fallback : origin;

    private static bool IsTrustedIdentityOrigin(VehicleDataOrigin origin) => origin is
        VehicleDataOrigin.VinDecoded or
        VehicleDataOrigin.OnlineVinDecoder or
        VehicleDataOrigin.ExactVinCache or
        VehicleDataOrigin.VerifiedLocalDefinition;

    private static bool IsTrustedVehicleDetailOrigin(VehicleDataOrigin origin) => origin is
        VehicleDataOrigin.VinDecoded or
        VehicleDataOrigin.OnlineVinDecoder or
        VehicleDataOrigin.ExactVinCache or
        VehicleDataOrigin.VerifiedLocalDefinition;

    private static bool IsTrustedTelemetryOrigin(VehicleDataOrigin origin) => origin is
        VehicleDataOrigin.EcuPid or
        VehicleDataOrigin.OnlineVinDecoder or
        VehicleDataOrigin.ExactVinCache or
        VehicleDataOrigin.VerifiedLocalDefinition;

    private static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("უცნობია", StringComparison.OrdinalIgnoreCase) &&
        value != "—";

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static IReadOnlySet<string> Tokenize(string value) =>
        value.Split(
                [' ', '-', '_', '/', '\\', '.', ',', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static string OriginText(VehicleDataOrigin origin) => origin switch
    {
        VehicleDataOrigin.EcuMode09 => "ECU Mode 09",
        VehicleDataOrigin.VinDecoded => "VIN decoder",
        VehicleDataOrigin.EcuPid => "ECU PID",
        VehicleDataOrigin.AdapterNegotiated => "ELM327",
        VehicleDataOrigin.ExactVinCache => "exact VIN cache",
        VehicleDataOrigin.OnlineVinDecoder => "online VIN decoder",
        VehicleDataOrigin.VerifiedLocalDefinition => "verified local definition",
        VehicleDataOrigin.UserSelection => "user selection",
        _ => "unknown"
    };
}
