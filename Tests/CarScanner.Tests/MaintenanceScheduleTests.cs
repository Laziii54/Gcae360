using CarScanner.Services.Maintenance;
using Xunit;

namespace CarScanner.Tests;

public class MaintenanceScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 1);

    [Fact]
    public void NoRecord_IsUnknown()
    {
        var status = MaintenanceSchedule.Calculate(new PartInterval(15000, 12), null, null, 80000, Now);
        Assert.Equal(PartState.Unknown, status.State);
    }

    [Fact]
    public void KmAndTimeLeft_UsesTheEarlierOne()
    {
        // 10 000 km of 15 000 driven, but only 1 month of 12 left.
        var status = MaintenanceSchedule.Calculate(new PartInterval(15000, 12), Now.AddMonths(-11), 70000, 80000, Now);
        Assert.Equal(5000, status.KmLeft!.Value, 3);
        Assert.Equal(PartState.Soon, status.State);
    }

    [Fact]
    public void DistancePassed_IsOverdue()
    {
        var status = MaintenanceSchedule.Calculate(new PartInterval(15000, 12), Now.AddMonths(-2), 60000, 76000, Now);
        Assert.Equal(PartState.Overdue, status.State);
        Assert.Equal(-1000, status.KmLeft!.Value, 3);
    }

    [Fact]
    public void TimeOnlyInterval_IgnoresDistance()
    {
        // BMW / VW brake fluid: every 2 years whatever the distance.
        var status = MaintenanceSchedule.Calculate(new PartInterval(0, 24), Now.AddMonths(-30), 50000, 90000, Now);
        Assert.Equal(PartState.Overdue, status.State);
        Assert.Null(status.KmLeft);
        Assert.True(status.DaysLeft < 0);
    }

    [Fact]
    public void WithoutOdometer_CountsByDate()
    {
        var status = MaintenanceSchedule.Calculate(new PartInterval(15000, 12), Now.AddMonths(-3), null, null, Now);
        Assert.Equal(PartState.Ok, status.State);
        Assert.Null(status.KmLeft);
        Assert.InRange(status.Fraction, 0.7, 0.8);
    }

    [Theory]
    [InlineData("TOYOTA", MaintenanceSchedule.Toyota)]
    [InlineData("Lexus", MaintenanceSchedule.Toyota)]
    [InlineData("Mercedes-Benz", MaintenanceSchedule.Mercedes)]
    [InlineData("KIA MOTORS", MaintenanceSchedule.Hyundai)]
    [InlineData("BMW", MaintenanceSchedule.Bmw)]
    [InlineData("Volkswagen", MaintenanceSchedule.Vag)]
    [InlineData("Skoda", MaintenanceSchedule.Vag)]
    [InlineData("Nissan", MaintenanceSchedule.Nissan)]
    [InlineData("FORD", MaintenanceSchedule.Ford)]
    [InlineData("Chevrolet", MaintenanceSchedule.Gm)]
    [InlineData("Acura", MaintenanceSchedule.Honda)]
    [InlineData("Peugeot", MaintenanceSchedule.Psa)]
    [InlineData("Citroën", MaintenanceSchedule.Psa)]
    [InlineData("Dacia", MaintenanceSchedule.Renault)]
    [InlineData("Jeep", MaintenanceSchedule.Fca)]
    [InlineData("Tesla", MaintenanceSchedule.Tesla)]
    [InlineData("Lada", MaintenanceSchedule.Other)]
    public void BrandGroups(string make, string expected) =>
        Assert.Equal(expected, MaintenanceSchedule.BrandGroup(make));

    [Fact]
    public void PartsDependOnPowertrain()
    {
        var diesel = MaintenanceSchedule.PartsFor(PowertrainKind.Diesel).Select(part => part.Id).ToArray();
        Assert.Contains(PartId.FuelFilter, diesel);
        Assert.DoesNotContain(PartId.SparkPlugs, diesel);

        var electric = MaintenanceSchedule.PartsFor(PowertrainKind.Electric).Select(part => part.Id).ToArray();
        Assert.DoesNotContain(PartId.Oil, electric);
        Assert.Contains(PartId.BrakeFluid, electric);
    }

    [Fact]
    public void EveryShownPart_HasAnInterval()
    {
        foreach (var group in new[] { MaintenanceSchedule.Toyota, MaintenanceSchedule.Mercedes, MaintenanceSchedule.Hyundai, MaintenanceSchedule.Bmw, MaintenanceSchedule.Vag,
                     MaintenanceSchedule.Ford, MaintenanceSchedule.Gm, MaintenanceSchedule.Honda, MaintenanceSchedule.Nissan, MaintenanceSchedule.Mazda,
                     MaintenanceSchedule.Subaru, MaintenanceSchedule.Volvo, MaintenanceSchedule.Renault, MaintenanceSchedule.Psa, MaintenanceSchedule.Fca,
                     MaintenanceSchedule.Tesla, MaintenanceSchedule.Other })
        foreach (var part in MaintenanceSchedule.AllParts)
            Assert.False(MaintenanceSchedule.DefaultInterval(group, part.Id).IsEmpty);
    }

    [Theory]
    [InlineData(37.0, null, PartState.Ok)]
    [InlineData(10.0, null, PartState.Soon)]
    [InlineData(0.0, null, PartState.Overdue)]
    [InlineData(null, 800.0, PartState.Soon)]
    [InlineData(null, 12000.0, PartState.Ok)]
    public void CarValue_DecidesOilState(double? percent, double? km, PartState expected)
    {
        var status = MaintenanceSchedule.FromCar(percent, km, Now.AddMonths(-6), 70000);
        Assert.Equal(expected, status.State);
        Assert.True(status.FromCar);
        Assert.Equal(percent, status.CarPercent);
    }

    [Fact]
    public void UsBrands_UseMileIntervals()
    {
        // Ford oil: 10 000 miles.
        Assert.Equal(16093, MaintenanceSchedule.DefaultInterval(MaintenanceSchedule.Ford, PartId.Oil).Km);
        Assert.Equal(12, MaintenanceSchedule.DefaultInterval(MaintenanceSchedule.Ford, PartId.Oil).Months);
    }
}
