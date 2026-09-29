using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

public sealed class OemOperationCatalogService
{
    private const string AssetName = "oem_operations.json";
    private readonly OemDefinitionCatalogService _ecuDefinitions;
    private readonly ManufacturerDefinitionCatalogService _manufacturers;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<OemOperationDefinition> _operations = new();
    private readonly List<string> _loadErrors = new();
    private bool _loaded;

    public IReadOnlyList<OemOperationDefinition> All => _operations.ToArray();
    public IReadOnlyList<string> LoadErrors => _loadErrors.ToArray();

    public OemOperationCatalogService(
        OemDefinitionCatalogService ecuDefinitions,
        ManufacturerDefinitionCatalogService manufacturers)
    {
        _ecuDefinitions = ecuDefinitions;
        _manufacturers = manufacturers;
    }

    public async Task EnsureLoadedAsync(CancellationToken token = default)
    {
        if (_loaded) return;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_loaded) return;
            _operations.Clear();
            _loadErrors.Clear();
            await _ecuDefinitions.EnsureLoadedAsync(token).ConfigureAwait(false);
            await _manufacturers.EnsureLoadedAsync(token).ConfigureAwait(false);
            using var stream = await FileSystem.OpenAppPackageFileAsync(AssetName).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var package = OemOperationPackageParser.ParseAndValidate(
                await reader.ReadToEndAsync(token).ConfigureAwait(false));
            foreach (var operation in package.Operations)
            {
                try
                {
                    ValidateAuthorization(operation);
                    _operations.Add(operation);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    _loadErrors.Add($"{operation.OperationId}: {exception.Message}");
                }
            }
            _loaded = true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            _loadErrors.Add(exception.Message);
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<OemOperationDefinition> GetForVehicle(ResolvedVehicleProfile vehicle)
    {
        if (!_loaded || !vehicle.CanUseManufacturerSpecificDefinitions ||
            !vehicle.HasVerifiedModel || !vehicle.HasVerifiedYear ||
            !int.TryParse(vehicle.Year.Value, out var year))
            return Array.Empty<OemOperationDefinition>();
        return _operations
            .Where(operation => operation.Brand.Equals(vehicle.Manufacturer.Value, StringComparison.OrdinalIgnoreCase) ||
                                vehicle.Manufacturer.Value.Contains(operation.Brand, StringComparison.OrdinalIgnoreCase))
            .Where(operation => operation.VehicleProfile.Equals(vehicle.Model.Value, StringComparison.OrdinalIgnoreCase))
            .Where(operation => year >= operation.MinimumYear && year <= operation.MaximumYear)
            .ToArray();
    }

    private void ValidateAuthorization(OemOperationDefinition operation)
    {
        var ecu = _ecuDefinitions.LoadedDefinitions.FirstOrDefault(definition =>
            definition.DefinitionId.Equals(operation.DefinitionId, StringComparison.OrdinalIgnoreCase));
        if (ecu is null || !ecu.PackageId.Equals(operation.PackageId, StringComparison.OrdinalIgnoreCase) ||
            !ecu.Brand.Equals(operation.Brand, StringComparison.OrdinalIgnoreCase) ||
            !ecu.VehicleProfile.Equals(operation.VehicleProfile, StringComparison.OrdinalIgnoreCase) ||
            !ecu.EcuId.Equals(operation.EcuId, StringComparison.OrdinalIgnoreCase) ||
            operation.MinimumYear < ecu.MinimumYear || operation.MaximumYear > ecu.MaximumYear)
            throw new InvalidOperationException("Operation is not tied to the exact reviewed ECU definition.");

        var required = operation.Type switch
        {
            OemOperationType.Coding => ManufacturerDefinitionVerificationLevel.VerifiedCoding,
            _ => ManufacturerDefinitionVerificationLevel.VerifiedService
        };
        var package = _manufacturers.All.FirstOrDefault(item =>
            item.Id.Equals(operation.PackageId, StringComparison.OrdinalIgnoreCase));
        var authorized = package?.VehicleDefinitions.Any(coverage =>
            coverage.DefinitionIds.Contains(operation.DefinitionId, StringComparer.OrdinalIgnoreCase) &&
            coverage.VerificationLevel >= required &&
            coverage.VehicleProfile.Equals(operation.VehicleProfile, StringComparison.OrdinalIgnoreCase) &&
            (!coverage.MinimumYear.HasValue || operation.MinimumYear >= coverage.MinimumYear) &&
            (!coverage.MaximumYear.HasValue || operation.MaximumYear <= coverage.MaximumYear)) == true;
        if (!authorized)
            throw new InvalidOperationException($"Manufacturer catalog does not authorize {required} for this ECU definition.");
    }
}
