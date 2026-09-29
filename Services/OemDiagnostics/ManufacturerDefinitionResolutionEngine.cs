using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Deterministic, side-effect-free manufacturer package resolution.</summary>
public static class ManufacturerDefinitionResolutionEngine
{
    public static ManufacturerDefinitionResolution Resolve(
        IReadOnlyList<ManufacturerDefinitionPackage> packages,
        ResolvedVehicleProfile vehicle)
    {
        if (!vehicle.CanUseManufacturerSpecificDefinitions)
        {
            return new(
                ManufacturerDefinitionResolutionState.VehicleNotVerified,
                null,
                Array.Empty<VehicleDefinitionCoverage>(),
                $"{vehicle.VerificationMessage} Manufacturer definition არ გააქტიურდა.");
        }

        var package = packages.FirstOrDefault(candidate => MatchesManufacturer(candidate, vehicle));
        if (package is null)
        {
            return new(
                ManufacturerDefinitionResolutionState.ManufacturerUnknown,
                null,
                Array.Empty<VehicleDefinitionCoverage>(),
                $"{vehicle.Manufacturer.Value}-ისთვის manufacturer package ჯერ არ არსებობს.");
        }

        var matching = package.VehicleDefinitions
            .Where(coverage => MatchesVehicle(coverage, vehicle))
            .ToArray();
        if (matching.Length == 0)
        {
            return new(
                ManufacturerDefinitionResolutionState.MetadataOnly,
                package,
                matching,
                $"{package.DisplayName} ცნობილია, მაგრამ ამ ზუსტი მოდელის/წლის OEM definition დადასტურებული არ არის.");
        }

        var level = matching.Min(item => item.VerificationLevel);
        return new(
            ToResolutionState(level),
            package,
            matching,
            $"{package.DisplayName}: {matching.Length} დადასტურებული vehicle definition მოიძებნა.");
    }

    public static bool Authorizes(
        IReadOnlyList<ManufacturerDefinitionPackage> packages,
        OemEcuDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DefinitionId) ||
            string.IsNullOrWhiteSpace(definition.PackageId))
            return false;

        var package = packages.FirstOrDefault(item =>
            item.Id.Equals(definition.PackageId, StringComparison.OrdinalIgnoreCase));
        if (package is null)
            return false;

        var definitionBrand = Normalize(definition.Brand);
        if (definitionBrand.Length == 0 ||
            !package.Aliases.Append(package.BrandId).Any(alias =>
                Normalize(alias).Equals(definitionBrand, StringComparison.Ordinal)))
            return false;

        return package.VehicleDefinitions.Any(coverage =>
            coverage.VerificationLevel >= ManufacturerDefinitionVerificationLevel.VerifiedReadOnly &&
            coverage.VehicleProfile.Equals(definition.VehicleProfile, StringComparison.OrdinalIgnoreCase) &&
            coverage.MinimumYear.HasValue && coverage.MaximumYear.HasValue &&
            definition.MinimumYear >= coverage.MinimumYear.Value &&
            definition.MaximumYear <= coverage.MaximumYear.Value &&
            coverage.DefinitionIds.Contains(definition.DefinitionId, StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesManufacturer(
        ManufacturerDefinitionPackage package,
        ResolvedVehicleProfile vehicle) =>
        package.BrandId.Equals(vehicle.SelectedBrand.Id, StringComparison.OrdinalIgnoreCase) ||
        package.Aliases.Any(alias =>
            alias.Equals(vehicle.Manufacturer.Value, StringComparison.OrdinalIgnoreCase) ||
            vehicle.Manufacturer.Value.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
            alias.Contains(vehicle.Manufacturer.Value, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesVehicle(VehicleDefinitionCoverage coverage, ResolvedVehicleProfile vehicle)
    {
        if (!vehicle.HasVerifiedModel ||
            !coverage.VehicleProfile.Equals(vehicle.Model.Value, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!coverage.MinimumYear.HasValue && !coverage.MaximumYear.HasValue)
            return true;
        if (!vehicle.HasVerifiedYear || !int.TryParse(vehicle.Year.Value, out var year))
            return false;
        return (!coverage.MinimumYear.HasValue || year >= coverage.MinimumYear) &&
               (!coverage.MaximumYear.HasValue || year <= coverage.MaximumYear);
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static ManufacturerDefinitionResolutionState ToResolutionState(
        ManufacturerDefinitionVerificationLevel level) => level switch
        {
            ManufacturerDefinitionVerificationLevel.VerifiedReadOnly => ManufacturerDefinitionResolutionState.VerifiedReadOnly,
            ManufacturerDefinitionVerificationLevel.VerifiedService => ManufacturerDefinitionResolutionState.VerifiedService,
            ManufacturerDefinitionVerificationLevel.VerifiedCoding => ManufacturerDefinitionResolutionState.VerifiedCoding,
            _ => ManufacturerDefinitionResolutionState.MetadataOnly
        };
}
