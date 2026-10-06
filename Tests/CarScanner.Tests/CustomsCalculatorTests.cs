using CarScanner.Services.Customs;

namespace CarScanner.Tests;

/// <summary>Excise per the Tax Code amendment in force from 2 April 2026.</summary>
public class CustomsCalculatorTests
{
    private static readonly CustomsRules Rules = CustomsRules.BuiltIn;

    private static CustomsResult Run(int year, int cc, CustomsFuel fuel, bool rhd = false,
        decimal price = 0, decimal rate = 0, int taxYear = 2026) =>
        CustomsCalculator.Calculate(Rules, new CustomsInput(year, cc, fuel, rhd, price, 0, 0, rate, 0, taxYear));

    [Fact]
    public void YoungPetrol_Is150PerCc() => Assert.Equal(3000m, Run(2021, 2000, CustomsFuel.Petrol).ExciseGel);

    [Fact]
    public void SixYearsOld_IsStillYoung() => Assert.Equal(3000m, Run(2020, 2000, CustomsFuel.Diesel).ExciseGel);

    [Fact]
    public void SevenYearsOld_Is450PerCc() => Assert.Equal(9000m, Run(2019, 2000, CustomsFuel.Petrol).ExciseGel);

    [Fact]
    public void YoungLeftHandHybrid_Gets60PercentRelief() => Assert.Equal(1200m, Run(2022, 2000, CustomsFuel.Hybrid).ExciseGel);

    [Fact]
    public void YoungPlugInHybrid_GetsHybridRelief() => Assert.Equal(1200m, Run(2022, 2000, CustomsFuel.PlugInHybrid).ExciseGel);

    [Fact]
    public void OldHybrid_NoRelief() => Assert.Equal(9000m, Run(2018, 2000, CustomsFuel.Hybrid).ExciseGel);

    [Fact]
    public void RightHandHybrid_NoRelief_TripleRate() => Assert.Equal(9000m, Run(2022, 2000, CustomsFuel.Hybrid, rhd: true).ExciseGel);

    [Fact]
    public void OldRightHand_Is1350PerCc() => Assert.Equal(27000m, Run(2015, 2000, CustomsFuel.Petrol, rhd: true).ExciseGel);

    [Fact]
    public void Electric_LeftHand_Zero_RightHand_Flat()
    {
        Assert.Equal(0m, Run(2015, 0, CustomsFuel.Electric).ExciseGel);
        Assert.Equal(3000m, Run(2015, 0, CustomsFuel.Electric, rhd: true).ExciseGel);
    }

    [Fact]
    public void Total_IncludesCarValueExciseAndFee()
    {
        var result = Run(2021, 2000, CustomsFuel.Petrol, price: 10000m, rate: 2.7m);
        Assert.Equal(27000m, result.CarValueGel);
        Assert.Equal(300m, result.CustomsFeeGel);
        Assert.Equal(27000m + 3000m + 300m, result.TotalGel);
    }

    [Fact]
    public void SixYearOldCar_WarnsAboutNextYear() =>
        Assert.Contains(Run(2020, 2000, CustomsFuel.Petrol).Insights, i => i.IsWarning && i.En.Contains("1 January"));
}
