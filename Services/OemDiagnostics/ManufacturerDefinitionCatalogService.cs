using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Resolves a VIN-confirmed vehicle against manufacturer definition packages.
/// A metadata match is useful for routing, but never authorizes an ECU request.
/// </summary>
public sealed class ManufacturerDefinitionCatalogService
{
    private const string AssetName = "manufacturer_definitions.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ManufacturerDefinitionPackage> _packages = Array.Empty<ManufacturerDefinitionPackage>();
    private bool _loaded;

    public IReadOnlyList<ManufacturerDefinitionPackage> All => _packages;

    public async Task EnsureLoadedAsync(CancellationToken token = default)
    {
        if (_loaded)
            return;

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_loaded)
                return;

            using var stream = await FileSystem.OpenAppPackageFileAsync(AssetName).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(token).ConfigureAwait(false);
            _packages = ManufacturerDefinitionCatalogParser.ParseAndValidate(json).Manufacturers;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ManufacturerDefinitionResolution Resolve(ResolvedVehicleProfile vehicle)
    {
        if (!_loaded)
            throw new InvalidOperationException("Manufacturer definition catalog has not been loaded.");
        return ManufacturerDefinitionResolutionEngine.Resolve(_packages, vehicle);
    }

    public bool Authorizes(OemEcuDefinition definition)
    {
        return _loaded && ManufacturerDefinitionResolutionEngine.Authorizes(_packages, definition);
    }
}
