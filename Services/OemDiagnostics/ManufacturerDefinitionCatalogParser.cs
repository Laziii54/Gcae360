using System.Text.Json;
using System.Text.Json.Serialization;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Pure parser/validator shared by the app and unit tests.</summary>
public static class ManufacturerDefinitionCatalogParser
{
    public static ManufacturerDefinitionCatalogFile ParseAndValidate(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var catalog = JsonSerializer.Deserialize<ManufacturerDefinitionCatalogFile>(json, options)
            ?? throw new InvalidOperationException("Manufacturer catalog is empty.");

        if (catalog.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported manufacturer catalog schema: {catalog.SchemaVersion}.");
        if (catalog.Manufacturers.Count == 0)
            throw new InvalidOperationException("Manufacturer catalog must contain at least one package.");

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in catalog.Manufacturers)
        {
            Require(package.Id, "Package id");
            Require(package.BrandId, $"{package.Id}: brand id");
            Require(package.DisplayName, $"{package.Id}: display name");
            if (package.Version < 1)
                throw new InvalidOperationException($"{package.Id}: version must be positive.");
            if (!packageIds.Add(package.Id))
                throw new InvalidOperationException($"Duplicate manufacturer package id: {package.Id}.");
            if (!brandIds.Add(package.BrandId))
                throw new InvalidOperationException($"Duplicate manufacturer brand id: {package.BrandId}.");

            package.Aliases = package.Aliases
                .Append(package.DisplayName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (package.VerificationLevel == ManufacturerDefinitionVerificationLevel.MetadataOnly &&
                package.VehicleDefinitions.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{package.Id}: metadata-only package cannot expose vehicle definitions.");
            }

            var coverageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var coverage in package.VehicleDefinitions)
                ValidateCoverage(package, coverage, coverageIds);
        }

        return catalog;
    }

    private static void ValidateCoverage(
        ManufacturerDefinitionPackage package,
        VehicleDefinitionCoverage coverage,
        HashSet<string> coverageIds)
    {
        Require(coverage.Id, $"{package.Id}: coverage id");
        Require(coverage.VehicleProfile, $"{package.Id}/{coverage.Id}: vehicle profile");
        Require(coverage.SourceReference, $"{package.Id}/{coverage.Id}: source reference");
        if (!coverageIds.Add(coverage.Id))
            throw new InvalidOperationException($"{package.Id}: duplicate coverage id {coverage.Id}.");
        if (coverage.VerificationLevel == ManufacturerDefinitionVerificationLevel.MetadataOnly)
            throw new InvalidOperationException($"{package.Id}/{coverage.Id}: vehicle coverage must be verified.");
        if (coverage.VerificationLevel > package.VerificationLevel)
            throw new InvalidOperationException($"{package.Id}/{coverage.Id}: coverage exceeds package verification level.");
        if (!coverage.MinimumYear.HasValue || !coverage.MaximumYear.HasValue)
            throw new InvalidOperationException($"{package.Id}/{coverage.Id}: exact year range is required.");
        if (coverage.MinimumYear is < 1886 || coverage.MaximumYear is < 1886)
            throw new InvalidOperationException($"{package.Id}/{coverage.Id}: invalid model year.");
        if (coverage.MinimumYear.HasValue && coverage.MaximumYear.HasValue &&
            coverage.MinimumYear > coverage.MaximumYear)
            throw new InvalidOperationException($"{package.Id}/{coverage.Id}: invalid year range.");
        if (coverage.DefinitionIds.Count == 0 || coverage.DefinitionIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"{package.Id}/{coverage.Id}: verified coverage needs definition IDs.");

        coverage.DefinitionIds = coverage.DefinitionIds
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{field} is required.");
    }
}
