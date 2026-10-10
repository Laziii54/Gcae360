using System.Globalization;
using System.Text;
using CarScanner.Models;

namespace CarScanner.Services.ServiceProcedures;

/// <summary>
/// Brand / model / year matching shared by the procedure catalog and the compatibility evaluator
/// (one implementation, so the page and the catalog can never disagree).
/// </summary>
public static class ServiceMatching
{
    private static readonly Dictionary<string, string> BrandAliases = new(StringComparer.Ordinal)
    {
        ["mercedesbenz"] = "mercedes",
        ["mb"] = "mercedes",
        ["daimler"] = "mercedes",
        ["vw"] = "volkswagen",
        ["chevy"] = "chevrolet",
        ["rangerover"] = "landrover",
        ["dsautomobiles"] = "ds",
        ["kgm"] = "ssangyong",
        ["kgmssangyong"] = "ssangyong",
        ["vauxhall"] = "vauxhall",
        ["gm"] = "chevrolet",
        ["gmc"] = "gmc",
        ["alfa"] = "alfaromeo",
        ["greatwallmotor"] = "greatwall",
        ["greatwallmotors"] = "greatwall",
        ["teslamotors"] = "tesla",
        ["teslainc"] = "tesla",
        ["rollsroycemotorcars"] = "rollsroyce",
        ["ramtrucks"] = "ram",
        ["minicooper"] = "mini",
        ["seatcupra"] = "cupra",
        ["lynkco"] = "lynkco"
    };

    /// <summary>"Chrysler/Dodge/Jeep" or "KGM / SsangYong" → normalised keys. "Unknown"/empty → none.</summary>
    public static IEnumerable<string> BrandKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            yield break;

        foreach (var part in value.Split(new[] { '/', '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var key = BrandKey(part);
            if (key.Length > 0)
                yield return key;
        }
    }

    public static string BrandKey(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        var key = builder.ToString();
        return BrandAliases.TryGetValue(key, out var alias) ? alias : key;
    }

    public static bool BrandMatches(string catalogBrand, IReadOnlyCollection<string> vehicleBrands)
    {
        if (string.IsNullOrWhiteSpace(catalogBrand) || vehicleBrands.Count == 0)
            return false;

        return BrandKeys(catalogBrand).Any(vehicleBrands.Contains);
    }

    public static bool ModelMatches(string catalogModel, string vehicleModel)
    {
        if (string.IsNullOrWhiteSpace(catalogModel))
            return false;

        if (catalogModel == "*")
            return true;

        if (string.IsNullOrWhiteSpace(vehicleModel))
            return false;

        var expected = ModelKey(catalogModel);
        var actual = ModelKey(vehicleModel);
        if (expected.Length == 0 || actual.Length == 0)
            return false;

        return expected.Equals(actual, StringComparison.Ordinal) ||
               actual.Contains(expected, StringComparison.Ordinal) ||
               expected.Contains(actual, StringComparison.Ordinal);
    }

    public static string ModelKey(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>0 means "no limit" on that side. Unknown vehicle year → in range (the caller reports it as missing).</summary>
    public static bool YearInRange(ServiceProcedureDefinition procedure, int? vehicleYear)
    {
        if (!vehicleYear.HasValue)
            return true;
        if (procedure.MinimumYear > 0 && vehicleYear.Value < procedure.MinimumYear)
            return false;
        if (procedure.MaximumYear > 0 && vehicleYear.Value > procedure.MaximumYear)
            return false;
        return true;
    }

    /// <summary>Exact entries need an exact year; with an unknown year only year-less entries match.</summary>
    public static bool YearMatchesExact(ServiceProcedureDefinition procedure, int? vehicleYear) =>
        vehicleYear.HasValue
            ? YearInRange(procedure, vehicleYear)
            : procedure.MinimumYear == 0 && procedure.MaximumYear == 0;

    public static string NormalizeModel(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }

    public static int? ParseYear(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) && year is > 1950 and < 2100 ? year : null;

    /// <summary>
    /// Which catalog entries apply to a vehicle (the existing rule, unchanged):
    /// exact model/year entries; else brand-wide entries (model "*") for the brand and year; plus the universal entry.
    /// </summary>
    public static List<ServiceProcedureDefinition> SelectForVehicle(
        IEnumerable<ServiceProcedureDefinition> all, VehicleInfo? vehicle, string? fallbackBrand = null)
    {
        var procedures = all as IReadOnlyCollection<ServiceProcedureDefinition> ?? all.ToArray();
        var brands = BrandKeys(vehicle?.Manufacturer).Concat(BrandKeys(fallbackBrand)).Distinct(StringComparer.Ordinal).ToArray();
        var model = NormalizeModel(vehicle?.Model);
        var year = ParseYear(vehicle?.Year);

        var exact = procedures
            .Where(item => item.Model != "*" && item.Brand != "*")
            .Where(item => BrandMatches(item.Brand, brands))
            .Where(item => ModelMatches(item.Model, model))
            .Where(item => YearMatchesExact(item, year))
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.NameEn, StringComparer.Ordinal)
            .ToList();

        var result = new List<ServiceProcedureDefinition>(exact);

        if (exact.Count == 0)
        {
            result.AddRange(procedures
                .Where(item => item.Model == "*" && item.Brand != "*")
                .Where(item => BrandMatches(item.Brand, brands))
                .Where(item => YearInRange(item, year)));
        }

        result.AddRange(procedures.Where(item => item.Brand == "*"));
        return result;
    }
}
