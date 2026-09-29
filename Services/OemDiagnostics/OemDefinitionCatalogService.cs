using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Loads verified, read-only OEM ECU definitions from a package asset. Keeping
/// definitions outside the transport code lets the app add vehicle coverage
/// without turning a brand selection into a guessed CAN scan.
/// </summary>
public sealed class OemDefinitionCatalogService
{
    private const string AssetName = "oem_definitions.json";
    private readonly OemAddressRegistry _registry;
    private readonly ManufacturerDefinitionCatalogService _manufacturers;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;

    public int LoadedDefinitionCount { get; private set; }
    public IReadOnlyList<OemEcuDefinition> LoadedDefinitions => _loadedDefinitions.ToArray();
    public IReadOnlyList<string> LoadErrors => _loadErrors.ToArray();
    private readonly List<OemEcuDefinition> _loadedDefinitions = new();
    private readonly List<string> _loadErrors = new();

    public OemDefinitionCatalogService(
        OemAddressRegistry registry,
        ManufacturerDefinitionCatalogService manufacturers)
    {
        _registry = registry;
        _manufacturers = manufacturers;
    }

    public async Task EnsureLoadedAsync(CancellationToken token = default)
    {
        if (_loaded)
            return;

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_loaded)
                return;

            _loadErrors.Clear();
            _loadedDefinitions.Clear();
            LoadedDefinitionCount = 0;
            await _manufacturers.EnsureLoadedAsync(token).ConfigureAwait(false);
            using var stream = await FileSystem.OpenAppPackageFileAsync(AssetName).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(token).ConfigureAwait(false);
            OemDefinitionPackage package;
            try
            {
                package = OemDefinitionPackageParser.ParseAndValidate(json);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException)
            {
                // A malformed executable catalog fails closed. Generic OBD
                // remains usable, but no OEM address is registered.
                _loadErrors.Add(ex.Message);
                _loaded = true;
                return;
            }

            foreach (var definition in package.Definitions)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!_manufacturers.Authorizes(definition))
                        throw new ArgumentException(
                            "Definition is not authorized by a verified manufacturer/vehicle package.");

                    var discoveryProbe = definition.ReadOnlyCommands.Single(command => command.DiscoveryProbe);

                    _registry.Register(new VerifiedEcuAddress(
                        definition.Brand,
                        definition.VehicleProfile,
                        definition.EcuId,
                        definition.Transport.TxCanId,
                        definition.Transport.RxCanId,
                        definition.Transport.Extended29Bit,
                        definition.Evidence.SourceReference,
                        $"{definition.EcuName} · {definition.Evidence.EvidenceId} · {definition.Notes}".Trim(' ', '·'),
                        OemDefinitionPackageParser.ParseReadOnlyRequest(discoveryProbe.RequestHex),
                        definition.DefinitionId,
                        definition.MinimumYear,
                        definition.MaximumYear));
                    _loadedDefinitions.Add(definition);
                    LoadedDefinitionCount++;
                }
                catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or FormatException)
                {
                    _loadErrors.Add($"{definition.Brand}/{definition.VehicleProfile}/{definition.EcuId}: {ex.Message}");
                }
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<OemEcuDefinition> GetForVehicle(
        ResolvedVehicleProfile vehicle,
        ManufacturerDefinitionResolution manufacturerResolution)
    {
        if (!_loaded || !manufacturerResolution.HasVerifiedReadOnlyDefinition ||
            !vehicle.HasVerifiedModel || !vehicle.HasVerifiedYear ||
            !int.TryParse(vehicle.Year.Value, out var year))
            return Array.Empty<OemEcuDefinition>();

        var allowedIds = manufacturerResolution.MatchingDefinitions
            .SelectMany(item => item.DefinitionIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _loadedDefinitions
            .Where(item => allowedIds.Contains(item.DefinitionId))
            .Where(item => item.VehicleProfile.Equals(vehicle.Model.Value, StringComparison.OrdinalIgnoreCase))
            .Where(item => year >= item.MinimumYear && year <= item.MaximumYear)
            .ToArray();
    }
}
