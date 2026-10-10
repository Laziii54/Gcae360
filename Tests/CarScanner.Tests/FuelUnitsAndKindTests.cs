using CarScanner.Services.Energy;

namespace CarScanner.Tests;

public sealed class FuelUnitsTests
{
    // Expected numbers below were worked out by hand from the definitions:
    //   US gallon = 3.785411784 L, UK gallon = 4.54609 L, mile = 1.609344 km.

    [Fact]
    public void TenLitresPer100Km_InEveryUnit()
    {
        Assert.Equal(10.0, FuelUnits.FromLPer100Km(10, FuelEconomyUnit.LitersPer100Km)!.Value, 9);
        Assert.Equal(10.0, FuelUnits.FromLPer100Km(10, FuelEconomyUnit.KmPerLiter)!.Value, 9);        // 100 km on 10 L
        Assert.Equal(23.5214583, FuelUnits.FromLPer100Km(10, FuelEconomyUnit.MpgUs)!.Value, 6);       // 62.1371 mi / 2.64172 gal
        Assert.Equal(28.2480936, FuelUnits.FromLPer100Km(10, FuelEconomyUnit.MpgUk)!.Value, 6);       // 62.1371 mi / 2.19969 gal
    }

    [Theory]
    [InlineData(30, FuelEconomyUnit.MpgUs, 7.84048)]    // 235.2145833 / 30
    [InlineData(40, FuelEconomyUnit.MpgUk, 7.06202)]    // 282.4809363 / 40
    [InlineData(12.5, FuelEconomyUnit.KmPerLiter, 8.0)] // 100 / 12.5
    [InlineData(8.0, FuelEconomyUnit.LitersPer100Km, 8.0)]
    public void ToLPer100Km_KnownValues(double value, FuelEconomyUnit unit, double expected) =>
        Assert.Equal(expected, FuelUnits.ToLPer100Km(value, unit)!.Value, 4);

    [Theory]
    [InlineData(FuelEconomyUnit.LitersPer100Km)]
    [InlineData(FuelEconomyUnit.KmPerLiter)]
    [InlineData(FuelEconomyUnit.MpgUs)]
    [InlineData(FuelEconomyUnit.MpgUk)]
    public void RoundTrip_ReturnsTheSameValue(FuelEconomyUnit unit)
    {
        foreach (var l100 in new[] { 0.5, 3.2, 5.9, 7.4, 12.8, 25.0, 99.0 })
        {
            var shown = FuelUnits.FromLPer100Km(l100, unit);
            Assert.Equal(l100, FuelUnits.ToLPer100Km(shown, unit)!.Value, 9);
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-3d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void UnusableInput_IsNeverConverted_NoInfinityOrDivisionByZero(double value)
    {
        foreach (var unit in Enum.GetValues<FuelEconomyUnit>())
        {
            Assert.Null(FuelUnits.FromLPer100Km(value, unit));
            Assert.Null(FuelUnits.ToLPer100Km(value, unit));
            Assert.Equal("—", FuelUnits.Format(value, unit));
        }

        Assert.Null(FuelUnits.FromLPer100Km(null, FuelEconomyUnit.MpgUs));
    }

    [Fact]
    public void Volumes_AreConvertedWithTheMatchingGallon()
    {
        Assert.Equal((10.0, "L"), FuelUnits.Volume(10, FuelEconomyUnit.LitersPer100Km));
        Assert.Equal((10.0, "L"), FuelUnits.Volume(10, FuelEconomyUnit.KmPerLiter));
        var us = FuelUnits.Volume(10, FuelEconomyUnit.MpgUs);
        Assert.Equal(2.641720, us.Value, 5);
        Assert.Equal("gal (US)", us.Unit);
        var uk = FuelUnits.Volume(10, FuelEconomyUnit.MpgUk);
        Assert.Equal(2.199692, uk.Value, 5);
        Assert.Equal("gal (UK)", uk.Unit);
    }

    [Fact]
    public void ConsistencyBetweenEconomyAndVolume()
    {
        // 100 km with 7.5 L: 62.1371 miles on 7.5/3.785411784 gallons.
        var l100 = 7.5;
        var gallons = FuelUnits.Volume(7.5, FuelEconomyUnit.MpgUs).Value;
        var miles = 100 / FuelUnits.KilometersPerMile;
        Assert.Equal(miles / gallons, FuelUnits.FromLPer100Km(l100, FuelEconomyUnit.MpgUs)!.Value, 9);
        var ukGallons = FuelUnits.Volume(7.5, FuelEconomyUnit.MpgUk).Value;
        Assert.Equal(miles / ukGallons, FuelUnits.FromLPer100Km(l100, FuelEconomyUnit.MpgUk)!.Value, 9);
    }

    [Fact]
    public void Formatting_ShowsTheUnit_AndMarksEstimates()
    {
        Assert.Equal("7.4 L/100 km", FuelUnits.Format(7.4, FuelEconomyUnit.LitersPer100Km));
        Assert.Equal("≈ 7.4 L/100 km", FuelUnits.Format(7.4, FuelEconomyUnit.LitersPer100Km, estimate: true));
        Assert.Equal("13.5 km/L", FuelUnits.Format(7.4, FuelEconomyUnit.KmPerLiter));
        Assert.Equal("31.8 MPG (US)", FuelUnits.Format(7.4, FuelEconomyUnit.MpgUs));
        Assert.Equal("38.2 MPG (UK)", FuelUnits.Format(7.4, FuelEconomyUnit.MpgUk));
        Assert.Equal("2.64 gal (US)", FuelUnits.FormatVolume(10, FuelEconomyUnit.MpgUs));
        Assert.Equal("≈ 10.00 L", FuelUnits.FormatVolume(10, FuelEconomyUnit.LitersPer100Km, estimate: true));
        Assert.Equal("1.32 gal (US)/h", FuelUnits.FormatRate(5, FuelEconomyUnit.MpgUs));
    }

    [Fact]
    public void BetterMeansBiggerOnlyForTheDistancePerVolumeUnits()
    {
        Assert.False(FuelUnits.HigherIsBetter(FuelEconomyUnit.LitersPer100Km));
        Assert.True(FuelUnits.HigherIsBetter(FuelEconomyUnit.KmPerLiter));
        Assert.True(FuelUnits.HigherIsBetter(FuelEconomyUnit.MpgUs));
        Assert.True(FuelUnits.HigherIsBetter(FuelEconomyUnit.MpgUk));
    }

    [Theory]
    [InlineData(false, "DE", FuelEconomyUnit.LitersPer100Km)]
    [InlineData(true, "US", FuelEconomyUnit.MpgUs)]
    [InlineData(true, "GB", FuelEconomyUnit.MpgUk)]
    [InlineData(false, "GB", FuelEconomyUnit.MpgUk)]
    [InlineData(true, null, FuelEconomyUnit.MpgUs)]
    [InlineData(false, null, FuelEconomyUnit.LitersPer100Km)]
    public void DefaultUnit(bool imperial, string? region, FuelEconomyUnit expected) =>
        Assert.Equal(expected, FuelUnits.DefaultFor(imperial, region));

    [Theory]
    [InlineData("MpgUk", FuelEconomyUnit.MpgUk)]
    [InlineData("kmperliter", FuelEconomyUnit.KmPerLiter)]
    [InlineData("nonsense", FuelEconomyUnit.LitersPer100Km)]
    [InlineData("", FuelEconomyUnit.LitersPer100Km)]
    [InlineData(null, FuelEconomyUnit.LitersPer100Km)]
    [InlineData("99", FuelEconomyUnit.LitersPer100Km)]
    public void ParsingASavedChoice_FallsBackSafely(string? text, FuelEconomyUnit expected) =>
        Assert.Equal(expected, FuelUnits.Parse(text, FuelEconomyUnit.LitersPer100Km));

    [Fact]
    public void ElectricEconomy()
    {
        Assert.Equal(15.0, FuelUnits.KWhPer100Km(7.5, 50)!.Value, 9);
        Assert.Null(FuelUnits.KWhPer100Km(1, 0.05));    // too short to mean anything
        Assert.Null(FuelUnits.KWhPer100Km(1, 0));
        Assert.Null(FuelUnits.KWhPer100Km(double.NaN, 10));
        // 16.09344 kWh/100 km = 0.1 kWh per mile → 10 miles per kWh?  100 km = 62.137 mi on 16.09344 kWh = 3.861 mi/kWh.
        Assert.Equal(100 / (16.09344 * 1.609344), FuelUnits.MilesPerKWh(16.09344)!.Value, 9);
        Assert.Null(FuelUnits.MilesPerKWh(0));
    }
}

public sealed class FuelKindTests
{
    [Theory]
    [InlineData(1, FuelKind.Gasoline)]
    [InlineData(2, FuelKind.Methanol)]
    [InlineData(3, FuelKind.Ethanol)]
    [InlineData(4, FuelKind.Diesel)]
    [InlineData(5, FuelKind.Lpg)]
    [InlineData(6, FuelKind.Cng)]
    [InlineData(7, FuelKind.Lpg)]
    [InlineData(8, FuelKind.Electric)]
    [InlineData(9, FuelKind.Gasoline)]
    [InlineData(17, FuelKind.Gasoline)]
    [InlineData(19, FuelKind.Diesel)]    // hybrid diesel: was wrongly treated as petrol
    [InlineData(23, FuelKind.Diesel)]    // bi-fuel running diesel
    [InlineData(20, FuelKind.Mixed)]
    [InlineData(21, FuelKind.Mixed)]
    [InlineData(22, FuelKind.Mixed)]
    [InlineData(0, FuelKind.Unknown)]
    [InlineData(99, FuelKind.Unknown)]
    [InlineData(-1, FuelKind.Unknown)]
    public void Pid51Codes(int code, FuelKind expected) => Assert.Equal(expected, FuelKinds.FromPidCode(code));

    [Fact]
    public void NoCode_IsUnknown() => Assert.Equal(FuelKind.Unknown, FuelKinds.FromPidCode(null));

    [Theory]
    [InlineData("Diesel", FuelKind.Diesel)]
    [InlineData("Gasoline", FuelKind.Gasoline)]
    [InlineData("Petrol", FuelKind.Gasoline)]
    [InlineData("Electric, Gasoline", FuelKind.Gasoline)]
    [InlineData("Plug-in Hybrid Electric (Diesel)", FuelKind.Diesel)]
    [InlineData("Electric", FuelKind.Electric)]
    [InlineData("Flex Fuel (E85)", FuelKind.Ethanol)]
    [InlineData("CNG", FuelKind.Cng)]
    [InlineData("Unknown", FuelKind.Unknown)]
    [InlineData("", FuelKind.Unknown)]
    [InlineData(null, FuelKind.Unknown)]
    [InlineData("Hybrid", FuelKind.Unknown)]
    public void VehicleText(string? text, FuelKind expected) => Assert.Equal(expected, FuelKinds.FromText(text));

    [Fact]
    public void Resolve_PrefersTheUsersChoice_ThenTheCode_ThenTheText()
    {
        Assert.Equal(FuelKind.Diesel, FuelKinds.Resolve(FuelKind.Diesel, 1, "Gasoline"));
        Assert.Equal(FuelKind.Diesel, FuelKinds.Resolve(null, 4, "Gasoline"));
        Assert.Equal(FuelKind.Gasoline, FuelKinds.Resolve(null, null, "Gasoline"));
        Assert.Equal(FuelKind.Unknown, FuelKinds.Resolve(null, null, "Unknown"));
        Assert.Equal(FuelKind.Unknown, FuelKinds.Resolve(FuelKind.Unknown, null, null));
    }

    [Fact]
    public void Resolve_AHybridCodeThatNamesNoFuel_UsesTheText()
    {
        Assert.Equal(FuelKind.Diesel, FuelKinds.Resolve(null, 21, "Diesel"));
        Assert.Equal(FuelKind.Mixed, FuelKinds.Resolve(null, 21, null));
    }

    [Fact]
    public void Resolve_ElectricCodeOnAPlugInWithAnEngine_UsesTheCombustionFuelFromTheText()
    {
        Assert.Equal(FuelKind.Gasoline, FuelKinds.Resolve(null, 8, "Electric, Gasoline"));
        Assert.Equal(FuelKind.Electric, FuelKinds.Resolve(null, 8, "Electric"));
        Assert.Equal(FuelKind.Electric, FuelKinds.Resolve(null, 8, null));
    }

    [Fact]
    public void AirFlowConstants_ExistOnlyForLiquidFuelsThatCanBeEstimated()
    {
        foreach (var kind in new[] { FuelKind.Gasoline, FuelKind.Diesel, FuelKind.Ethanol, FuelKind.Methanol, FuelKind.Lpg })
        {
            Assert.True(FuelKinds.TryGetMafConstants(kind, out var constants));
            Assert.InRange(constants.StoichiometricAfr, 6, 16);
            Assert.InRange(constants.DensityKgPerLiter, 0.5, 0.9);
            Assert.False(string.IsNullOrWhiteSpace(constants.Range));
        }

        foreach (var kind in new[] { FuelKind.Unknown, FuelKind.Cng, FuelKind.Electric, FuelKind.Mixed })
            Assert.False(FuelKinds.TryGetMafConstants(kind, out _));
    }

    [Fact]
    public void EveryKindHasAName()
    {
        foreach (var kind in Enum.GetValues<FuelKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(FuelKinds.Display(kind, true)));
            Assert.False(string.IsNullOrWhiteSpace(FuelKinds.Display(kind, false)));
        }
    }
}
