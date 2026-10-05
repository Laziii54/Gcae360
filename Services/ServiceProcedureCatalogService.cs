using System.Text.Json;
using System.Text.Json.Serialization;
using CarScanner.Models;

namespace CarScanner.Services;

public sealed class ServiceProcedureCatalogService
{
    private const string AssetName = "service_procedures.json";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly List<ServiceProcedureDefinition> _procedures = new();

    private bool _loaded;

    public IReadOnlyList<ServiceProcedureDefinition> All =>
        _procedures.ToArray();

    public async Task EnsureLoadedAsync(
        CancellationToken token = default)
    {
        if (_loaded)
            return;

        await _gate.WaitAsync(token);

        try
        {
            if (_loaded)
                return;

            _procedures.Clear();

            using var stream =
                await FileSystem.OpenAppPackageFileAsync(AssetName);

            using var reader =
                new StreamReader(stream);

            var json =
                await reader.ReadToEndAsync(token);

            var options =
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

            options.Converters.Add(
                new JsonStringEnumConverter());

            var package =
                JsonSerializer.Deserialize<ServiceProcedurePackage>(
                    json,
                    options);

            if (package is null)
                throw new InvalidOperationException(
                    "Service procedure catalog could not be loaded.");

            if (package.SchemaVersion != 1)
                throw new InvalidOperationException(
                    $"Unsupported service procedure schema version: {package.SchemaVersion}");

            _procedures.AddRange(
                package.Procedures);

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Guided (manual) procedures for a vehicle:
    /// 1) exact model/year entries when they exist;
    /// 2) otherwise brand-wide entries (model "*") for the brand and year;
    /// 3) plus the universal fallback entry (brand "*").
    /// The brand comes from the detected manufacturer and, when that is
    /// unknown, from the brand the user selected before connecting.
    /// </summary>
    public async Task<IReadOnlyList<ServiceProcedureDefinition>>
        GetForVehicleAsync(
            VehicleInfo? vehicle,
            string? fallbackBrand = null,
            CancellationToken token = default)
    {
        await EnsureLoadedAsync(token);

        var brands = BrandKeys(vehicle?.Manufacturer)
            .Concat(BrandKeys(fallbackBrand))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var model = Normalize(vehicle?.Model);
        if (model.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            model = string.Empty;

        int? year = int.TryParse(vehicle?.Year, out var parsedYear) ? parsedYear : null;

        var exact = _procedures
            .Where(item => item.Model != "*" && item.Brand != "*")
            .Where(item => CatalogBrandMatches(item.Brand, brands))
            .Where(item => ModelMatches(item.Model, model))
            .Where(item => YearMatches(item, year))
            .OrderBy(item => item.Category)
            .ThenBy(item => item.NameEn)
            .ToList();

        var result = new List<ServiceProcedureDefinition>(exact);

        if (exact.Count == 0)
        {
            result.AddRange(_procedures
                .Where(item => item.Model == "*" && item.Brand != "*")
                .Where(item => CatalogBrandMatches(item.Brand, brands))
                .Where(item => YearInRange(item, year)));
        }

        result.AddRange(_procedures.Where(item => item.Brand == "*"));

        return result;
    }

    // Backwards-compatible overload.
    public Task<IReadOnlyList<ServiceProcedureDefinition>>
        GetForVehicleAsync(
            VehicleInfo vehicle,
            CancellationToken token) =>
        GetForVehicleAsync(vehicle, null, token);

    public async Task<IReadOnlyList<ServiceProcedureDefinition>>
        GetForBrandAsync(
            string brand,
            CancellationToken token = default)
    {
        await EnsureLoadedAsync(token);

        var brands = BrandKeys(brand).ToArray();
        if (brands.Length == 0)
            return Array.Empty<ServiceProcedureDefinition>();

        return _procedures
            .Where(item => item.Brand != "*" && CatalogBrandMatches(item.Brand, brands))
            .OrderBy(item => item.Model)
            .ThenBy(item => item.Category)
            .ToArray();
    }

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

    /// <summary>"Chrysler/Dodge/Jeep" or "KGM / SsangYong" → normalized keys.</summary>
    private static IEnumerable<string> BrandKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            yield break;

        foreach (var part in value.Split(new[] { '/', '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var key = BrandKey(part);
            if (key.Length > 0)
                yield return key;
        }
    }

    private static string BrandKey(string value)
    {
        var decomposed = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) ==
                System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        var key = builder.ToString();
        return BrandAliases.TryGetValue(key, out var alias) ? alias : key;
    }

    private static bool CatalogBrandMatches(string catalogBrand, IReadOnlyCollection<string> vehicleBrands)
    {
        if (string.IsNullOrWhiteSpace(catalogBrand) || vehicleBrands.Count == 0)
            return false;

        return BrandKeys(catalogBrand).Any(vehicleBrands.Contains);
    }

    private static bool ModelMatches(
        string catalogModel,
        string vehicleModel)
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

    private static string ModelKey(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool YearMatches(
        ServiceProcedureDefinition procedure,
        int? vehicleYear)
    {
        // 0 ნიშნავს, რომ შესაბამის მხარეს შეზღუდვა არ აქვს.
        if (!vehicleYear.HasValue)
        {
            return procedure.MinimumYear == 0 &&
                   procedure.MaximumYear == 0;
        }

        return YearInRange(procedure, vehicleYear);
    }

    /// <summary>Unknown year → every generation of the brand is shown.</summary>
    private static bool YearInRange(
        ServiceProcedureDefinition procedure,
        int? vehicleYear)
    {
        if (!vehicleYear.HasValue)
            return true;

        if (procedure.MinimumYear > 0 &&
            vehicleYear.Value < procedure.MinimumYear)
        {
            return false;
        }

        if (procedure.MaximumYear > 0 &&
            vehicleYear.Value > procedure.MaximumYear)
        {
            return false;
        }

        return true;
    }

    private static string Normalize(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private sealed class ServiceProcedurePackage
    {
        public int SchemaVersion { get; set; } = 1;

        public int CatalogVersion { get; set; } = 1;

        public List<ServiceProcedureDefinition> Procedures
        {
            get;
            set;
        } = new();
    }
}