using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

public sealed class ManufacturerDefinitionCatalogTests
{
    [Fact]
    public void BundledCatalog_ContainsUniquePackagesAndOnlyReviewedOemClaims()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manufacturer_definitions.json"));

        var catalog = ManufacturerDefinitionCatalogParser.ParseAndValidate(json);

        Assert.True(catalog.Manufacturers.Count >= 49);
        Assert.Equal(catalog.Manufacturers.Count,
            catalog.Manufacturers.Select(item => item.BrandId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var hyundai = Assert.Single(catalog.Manufacturers,
            package => package.BrandId.Equals("hyundai", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ManufacturerDefinitionVerificationLevel.VerifiedReadOnly, hyundai.VerificationLevel);
        var sonata = Assert.Single(hyundai.VehicleDefinitions);
        Assert.Equal("Sonata", sonata.VehicleProfile);
        Assert.Equal(2020, sonata.MinimumYear);
        Assert.Equal(2023, sonata.MaximumYear);
        Assert.Equal("hyundai.sonata-dn8.2020-2023.abs", Assert.Single(sonata.DefinitionIds));

        Assert.All(catalog.Manufacturers.Where(package =>
            !package.BrandId.Equals("hyundai", StringComparison.OrdinalIgnoreCase)), package =>
        {
            Assert.Equal(ManufacturerDefinitionVerificationLevel.MetadataOnly, package.VerificationLevel);
            Assert.Empty(package.VehicleDefinitions);
        });
    }

    [Fact]
    public void MetadataOnlyPackage_CannotContainVehicleDefinitions()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "manufacturers": [{
            "id": "manufacturer.test",
            "brandId": "test",
            "displayName": "Test",
            "version": 1,
            "verificationLevel": "MetadataOnly",
            "vehicleDefinitions": [{
              "id": "test-model",
              "vehicleProfile": "Model",
              "verificationLevel": "VerifiedReadOnly",
              "sourceReference": "reviewed-source",
              "definitionIds": ["test.ecm"]
            }]
          }]
        }
        """;

        Assert.Throws<InvalidOperationException>(() =>
            ManufacturerDefinitionCatalogParser.ParseAndValidate(json));
    }

    [Fact]
    public void ConfirmedBrand_WithMetadataOnlyPackage_DoesNotUnlockOemDefinition()
    {
        var package = MetadataPackage("chevrolet", "Chevrolet");
        var vehicle = ConfirmedVehicle("Chevrolet", "Volt", "2014", "chevrolet");

        var result = ManufacturerDefinitionResolutionEngine.Resolve(new[] { package }, vehicle);

        Assert.Equal(ManufacturerDefinitionResolutionState.MetadataOnly, result.State);
        Assert.False(result.HasVerifiedReadOnlyDefinition);
    }

    [Fact]
    public void VerifiedDefinition_RequiresExactModelYearAndDefinitionId()
    {
        var package = MetadataPackage("chevrolet", "Chevrolet");
        package.VerificationLevel = ManufacturerDefinitionVerificationLevel.VerifiedReadOnly;
        package.VehicleDefinitions.Add(new VehicleDefinitionCoverage
        {
            Id = "volt-gen1",
            VehicleProfile = "Volt",
            MinimumYear = 2011,
            MaximumYear = 2015,
            VerificationLevel = ManufacturerDefinitionVerificationLevel.VerifiedReadOnly,
            SourceReference = "reviewed-capture",
            DefinitionIds = new() { "chevrolet.volt.gen1.ecm" }
        });
        var vehicle = ConfirmedVehicle("Chevrolet", "Volt", "2014", "chevrolet");

        var resolution = ManufacturerDefinitionResolutionEngine.Resolve(new[] { package }, vehicle);
        var authorized = ManufacturerDefinitionResolutionEngine.Authorizes(new[] { package }, new OemEcuDefinition
        {
            DefinitionId = "chevrolet.volt.gen1.ecm",
            PackageId = package.Id,
            Brand = "Chevrolet",
            VehicleProfile = "Volt",
            MinimumYear = 2011,
            MaximumYear = 2015
        });
        var wrongDefinition = ManufacturerDefinitionResolutionEngine.Authorizes(new[] { package }, new OemEcuDefinition
        {
            DefinitionId = "unreviewed.ecm",
            PackageId = package.Id,
            Brand = "Chevrolet",
            VehicleProfile = "Volt",
            MinimumYear = 2011,
            MaximumYear = 2015
        });
        var wrongBrand = ManufacturerDefinitionResolutionEngine.Authorizes(new[] { package }, new OemEcuDefinition
        {
            DefinitionId = "chevrolet.volt.gen1.ecm",
            PackageId = package.Id,
            Brand = "BMW",
            VehicleProfile = "Volt",
            MinimumYear = 2011,
            MaximumYear = 2015
        });

        Assert.Equal(ManufacturerDefinitionResolutionState.VerifiedReadOnly, resolution.State);
        Assert.True(resolution.HasVerifiedReadOnlyDefinition);
        Assert.True(authorized);
        Assert.False(wrongDefinition);
        Assert.False(wrongBrand);
    }

    [Fact]
    public void SelectedBrandWithoutVerifiedVin_DoesNotResolvePackage()
    {
        var package = MetadataPackage("bmw", "BMW");
        var vehicle = ConfirmedVehicle("Chevrolet", "Volt", "2014", "bmw");
        vehicle = new ResolvedVehicleProfile
        {
            DiagnosticVehicle = vehicle.DiagnosticVehicle,
            SelectedBrand = vehicle.SelectedBrand,
            VerificationState = VehicleProfileVerificationState.SelectionMismatch,
            VIN = vehicle.VIN,
            Manufacturer = vehicle.Manufacturer,
            Model = vehicle.Model,
            Year = vehicle.Year
        };

        var result = ManufacturerDefinitionResolutionEngine.Resolve(new[] { package }, vehicle);

        Assert.Equal(ManufacturerDefinitionResolutionState.VehicleNotVerified, result.State);
    }

    private static ManufacturerDefinitionPackage MetadataPackage(string brandId, string displayName) => new()
    {
        Id = $"manufacturer.{brandId}",
        BrandId = brandId,
        DisplayName = displayName,
        Aliases = new() { displayName },
        VerificationLevel = ManufacturerDefinitionVerificationLevel.MetadataOnly
    };

    private static ResolvedVehicleProfile ConfirmedVehicle(
        string manufacturer,
        string model,
        string year,
        string selectedBrandId) => new()
    {
        DiagnosticVehicle = new VehicleInfo { Manufacturer = manufacturer, Model = model, Year = year, VIN = "1G1RA6E43EU000001" },
        SelectedBrand = new VehicleBrandProfile(selectedBrandId, manufacturer, manufacturer, "#000000", string.Empty),
        VerificationState = VehicleProfileVerificationState.Confirmed,
        VIN = new ResolvedVehicleField("1G1RA6E43EU000001", VehicleDataOrigin.EcuMode09, true),
        Manufacturer = new ResolvedVehicleField(manufacturer, VehicleDataOrigin.VinDecoded, true),
        Model = new ResolvedVehicleField(model, VehicleDataOrigin.VerifiedLocalDefinition, true),
        Year = new ResolvedVehicleField(year, VehicleDataOrigin.VinDecoded, true)
    };
}
