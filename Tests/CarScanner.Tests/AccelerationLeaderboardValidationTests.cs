using CarScanner.Api.Services;

namespace CarScanner.Tests;

public sealed class AccelerationLeaderboardValidationTests
{
    [Fact]
    public void ValidEcuVinConfirmedRun_IsAccepted()
    {
        Assert.Null(AccelerationLeaderboardStore.Validate(ValidRequest()));
    }

    [Fact]
    public void ImpossiblyFastRun_IsRejected()
    {
        Assert.Equal("implausible time", AccelerationLeaderboardStore.Validate(ValidRequest() with
        {
            DurationSeconds = 0.8
        }));
    }

    [Fact]
    public void NonEcuSpeedSource_IsRejected()
    {
        Assert.Equal("ECU speed source required", AccelerationLeaderboardStore.Validate(ValidRequest() with
        {
            SpeedSource = "GPS"
        }));
    }

    [Fact]
    public void UnverifiedVin_IsRejected()
    {
        Assert.Equal("VIN verification required", AccelerationLeaderboardStore.Validate(ValidRequest() with
        {
            VinVerified = false
        }));
    }

    private static AccelerationLeaderboardRequest ValidRequest() => new(
        Guid.NewGuid().ToString("N"), "Driver", "Toyota Prius 2014",
        "JTDBR32E720123456", true, "OBD_PID_010D", "0-100", 0, 100,
        9.84, 30, 101, 140, 4.2, DateTime.UtcNow, false);
}
