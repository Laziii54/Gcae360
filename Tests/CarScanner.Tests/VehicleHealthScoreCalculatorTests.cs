using CarScanner.Models;
using CarScanner.Services;

namespace CarScanner.Tests;

public sealed class VehicleHealthScoreCalculatorTests
{
    [Fact]
    public void NoDetectedProblems_Returns100()
    {
        Assert.Equal(100, VehicleHealthScoreCalculator.Calculate(Array.Empty<VehicleHealthIssue>(), 0));
    }

    [Fact]
    public void MinorDtcAndTwoIncompleteMonitors_Returns95()
    {
        var issue = Issue("P0442", VehicleHealthSeverity.Minor, VehicleHealthCategory.Emissions, 3);
        Assert.Equal(95, VehicleHealthScoreCalculator.Calculate(new[] { issue }, 2));
    }

    [Fact]
    public void CriticalDtcPenalty_IsCappedAt45()
    {
        var issues = Enumerable.Range(0, 4)
            .Select(index => Issue($"P052{index}", VehicleHealthSeverity.Critical, VehicleHealthCategory.Engine, 15));
        Assert.Equal(55, VehicleHealthScoreCalculator.Calculate(issues, 0));
    }

    [Fact]
    public void CommunicationPenalty_IsCappedAt10()
    {
        var issues = Enumerable.Range(0, 3)
            .Select(index => Issue($"U010{index}", VehicleHealthSeverity.Major, VehicleHealthCategory.Communication, 8));
        Assert.Equal(90, VehicleHealthScoreCalculator.Calculate(issues, 0));
    }

    [Fact]
    public void SensorPenalty_IsCappedAt30()
    {
        var issues = Enumerable.Range(0, 4)
            .Select(index => Issue($"PID 01{index:X2}", VehicleHealthSeverity.Critical, VehicleHealthCategory.Sensor, 15));
        Assert.Equal(70, VehicleHealthScoreCalculator.Calculate(issues, 0));
    }

    private static VehicleHealthIssue Issue(
        string code,
        VehicleHealthSeverity severity,
        VehicleHealthCategory category,
        int penalty) => new()
    {
        Code = code,
        Severity = severity,
        Category = category,
        Penalty = penalty
    };
}
