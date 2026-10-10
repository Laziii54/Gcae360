using System.Text.Json;
using System.Text.Json.Serialization;
using CarScanner.Models;
using CarScanner.Services.ServiceProcedures;

namespace CarScanner.Services;

public sealed class ServiceProcedureCatalogService
{
    private const string AssetName = "service_procedures.json";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly List<ServiceProcedureDefinition> _procedures = new();

    private bool _loaded;

    private readonly List<ServiceProcedureIssue> _issues = new();

    /// <summary>Records that were excluded (or flagged) while loading; empty for a clean catalog.</summary>
    public IReadOnlyList<ServiceProcedureIssue> Issues => _issues.ToArray();

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
            _issues.Clear();

            using var stream =
                await FileSystem.OpenAppPackageFileAsync(AssetName);

            using var reader =
                new StreamReader(stream);

            var json =
                await reader.ReadToEndAsync(token);

            var parsed = ServiceProcedureParser.Parse(json);

            _procedures.AddRange(parsed.Procedures);
            _issues.AddRange(parsed.Issues);

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

        return ServiceMatching.SelectForVehicle(_procedures, vehicle, fallbackBrand);
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

        var brands = ServiceMatching.BrandKeys(brand).ToArray();
        if (brands.Length == 0)
            return Array.Empty<ServiceProcedureDefinition>();

        return _procedures
            .Where(item => item.Brand != "*" && ServiceMatching.BrandMatches(item.Brand, brands))
            .OrderBy(item => item.Model)
            .ThenBy(item => item.Category)
            .ToArray();
    }
}
