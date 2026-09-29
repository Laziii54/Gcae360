using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

public sealed class VehicleProfileResolutionTests
{
    private static readonly VehicleBrandProfile Chevrolet =
        new("chevrolet", "Chevrolet", "CHEVROLET", "#FBBF24", string.Empty);

    [Fact]
    public void MatchingSelectionAndVinManufacturer_UnlocksVerifiedIdentity()
    {
        var vehicle = CreateVehicle("Chevrolet");

        var result = VehicleProfileResolutionEngine.Resolve(vehicle, Chevrolet);

        Assert.Equal(VehicleProfileVerificationState.Confirmed, result.VerificationState);
        Assert.True(result.CanUseManufacturerSpecificDefinitions);
        Assert.True(result.VIN.IsVerified);
        Assert.Equal(VehicleDataOrigin.EcuMode09, result.VIN.Origin);
        Assert.Equal("Chevrolet Volt 2013", result.DisplayName);
    }

    [Fact]
    public void SelectionMismatch_BlocksManufacturerSpecificDefinitions()
    {
        var bmw = new VehicleBrandProfile("bmw", "BMW", "BMW", "#3B82F6", string.Empty);

        var result = VehicleProfileResolutionEngine.Resolve(CreateVehicle("Chevrolet"), bmw);

        Assert.Equal(VehicleProfileVerificationState.SelectionMismatch, result.VerificationState);
        Assert.False(result.CanUseManufacturerSpecificDefinitions);
        Assert.Contains("BMW", result.VerificationMessage);
        Assert.Contains("Chevrolet", result.VerificationMessage);
    }

    [Fact]
    public void BrandWithoutVin_IsDisplayOnly()
    {
        var vehicle = new VehicleInfo
        {
            VIN = "Unknown",
            Manufacturer = "Unknown",
            Protocol = "ISO 15765-4 CAN"
        };

        var result = VehicleProfileResolutionEngine.Resolve(vehicle, Chevrolet);

        Assert.Equal(VehicleProfileVerificationState.SelectionOnly, result.VerificationState);
        Assert.False(result.CanUseManufacturerSpecificDefinitions);
        Assert.Equal(VehicleDataOrigin.UserSelection, result.Manufacturer.Origin);
        Assert.False(result.Manufacturer.IsVerified);
        Assert.Equal("Chevrolet", result.DisplayName);
    }

    [Fact]
    public void ValidVinWithUnresolvedManufacturer_DoesNotGuessFromSelection()
    {
        var vehicle = CreateVehicle("Unknown");

        var result = VehicleProfileResolutionEngine.Resolve(vehicle, Chevrolet);

        Assert.Equal(VehicleProfileVerificationState.VinManufacturerUnresolved, result.VerificationState);
        Assert.False(result.CanUseManufacturerSpecificDefinitions);
        Assert.Equal(VehicleDataOrigin.UserSelection, result.Manufacturer.Origin);
        Assert.False(result.Manufacturer.IsVerified);
    }

    [Fact]
    public void CommonManufacturerAlias_IsMatchedWithoutChangingDetectedValue()
    {
        var mercedes = new VehicleBrandProfile(
            "mercedes", "Mercedes-Benz", "MB", "#94A3B8", string.Empty);
        var vehicle = CreateVehicle("Daimler");

        var result = VehicleProfileResolutionEngine.Resolve(vehicle, mercedes);

        Assert.Equal(VehicleProfileVerificationState.Confirmed, result.VerificationState);
        Assert.Equal("Daimler", result.Manufacturer.Value);
        Assert.True(result.CanUseManufacturerSpecificDefinitions);
    }

    [Fact]
    public void ShortBrandAlias_DoesNotMatchPartOfAnotherBrandName()
    {
        var mg = new VehicleBrandProfile("mg", "MG", "MG", "#DC2626", string.Empty);

        var result = VehicleProfileResolutionEngine.Resolve(CreateVehicle("Mercedes-AMG"), mg);

        Assert.Equal(VehicleProfileVerificationState.SelectionMismatch, result.VerificationState);
        Assert.False(result.CanUseManufacturerSpecificDefinitions);
    }

    [Fact]
    public void UserEnteredModel_CannotSelectModelSpecificOemAddress()
    {
        var vehicle = CreateVehicle("Chevrolet");
        vehicle.ModelOrigin = VehicleDataOrigin.UserSelection;
        var profile = VehicleProfileResolutionEngine.Resolve(vehicle, Chevrolet);
        var registry = CreateRegistry("Volt 2013");

        var addresses = registry.GetForVehicle(profile);

        Assert.True(profile.CanUseManufacturerSpecificDefinitions);
        Assert.False(profile.HasVerifiedModel);
        Assert.Empty(addresses);
    }

    [Fact]
    public void VerifiedModelAndYear_SelectsOnlyMatchingOemAddress()
    {
        var profile = VehicleProfileResolutionEngine.Resolve(CreateVehicle("Chevrolet"), Chevrolet);
        var registry = CreateRegistry("Volt 2013");

        var addresses = registry.GetForVehicle(profile);

        Assert.Single(addresses);
        Assert.Equal("ECM", addresses[0].EcuId);
    }

    [Fact]
    public void SameModelAcrossGenerations_SelectsOnlyMatchingYearDefinition()
    {
        var profile = VehicleProfileResolutionEngine.Resolve(CreateVehicle("Chevrolet"), Chevrolet);
        var registry = new OemAddressRegistry();
        registry.Register(new VerifiedEcuAddress(
            "Chevrolet", "Volt", "ECM", 0x7E0, 0x7E8, false,
            "Reviewed Gen1", "Gen1", [0x22, 0xF1, 0x90],
            "chevrolet.volt.gen1.ecm", 2011, 2015));
        registry.Register(new VerifiedEcuAddress(
            "Chevrolet", "Volt", "ECM", 0x7E2, 0x7EA, false,
            "Reviewed Gen2", "Gen2", [0x22, 0xF1, 0x90],
            "chevrolet.volt.gen2.ecm", 2016, 2019));

        var addresses = registry.GetForVehicle(profile);

        Assert.Single(addresses);
        Assert.Equal("chevrolet.volt.gen1.ecm", addresses[0].DefinitionId);
    }

    [Fact]
    public void ManufacturerMarkedAsUserSelection_CannotConfirmIdentity()
    {
        var vehicle = CreateVehicle("Chevrolet");
        vehicle.ManufacturerOrigin = VehicleDataOrigin.UserSelection;

        var result = VehicleProfileResolutionEngine.Resolve(vehicle, Chevrolet);

        Assert.Equal(VehicleProfileVerificationState.VinManufacturerUnresolved, result.VerificationState);
        Assert.False(result.CanUseManufacturerSpecificDefinitions);
    }

    private static OemAddressRegistry CreateRegistry(string vehicleProfile)
    {
        var registry = new OemAddressRegistry();
        registry.Register(new VerifiedEcuAddress(
            "Chevrolet",
            vehicleProfile,
            "ECM",
            0x7E0,
            0x7E8,
            false,
            "Unit test verified definition",
            "Read-only identity request",
            [0x22, 0xF1, 0x90]));
        return registry;
    }

    private static VehicleInfo CreateVehicle(string manufacturer) => new()
    {
        VIN = "1G1RD6E40DU100001",
        VinOrigin = VehicleDataOrigin.EcuMode09,
        Manufacturer = manufacturer,
        ManufacturerOrigin = manufacturer == "Unknown"
            ? VehicleDataOrigin.Unknown
            : VehicleDataOrigin.VinDecoded,
        Model = "Volt",
        ModelOrigin = VehicleDataOrigin.VerifiedLocalDefinition,
        Year = "2013",
        YearOrigin = VehicleDataOrigin.VinDecoded,
        FuelType = "ბენზინი / ელექტრო",
        FuelTypeOrigin = VehicleDataOrigin.EcuPid,
        Protocol = "ISO 15765-4 CAN (11 bit, 500 kbaud)",
        ProtocolOrigin = VehicleDataOrigin.AdapterNegotiated
    };
}
