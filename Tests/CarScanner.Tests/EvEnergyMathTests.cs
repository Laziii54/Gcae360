using CarScanner.Services.Energy;
using Xunit;

namespace CarScanner.Tests;

public class EvEnergyMathTests
{
    [Fact]
    public void Standard9A_DecodesVoltageAndSignedCurrent()
    {
        // Support bits V+A, charge state 0, 356.0 V (22784/64), -12.5 A.
        var data = new byte[] { 0x06, 0x00, 0x59, 0x00, 0xFF, 0x83 };

        Assert.True(EvSignalDecoder.TryDecodeStandard9A(data, out var volts, out var amps));
        Assert.Equal(356.0, volts, 3);
        Assert.Equal(-12.5, amps, 3);
    }

    [Fact]
    public void Standard9A_RejectsWhenNotSupported()
    {
        var data = new byte[] { 0x01, 0x00, 0x59, 0x00, 0x00, 0x10 };
        Assert.False(EvSignalDecoder.TryDecodeStandard9A(data, out _, out _));
    }

    [Fact]
    public void ToyotaHvBattery_DecodesFromCatalogFormat()
    {
        // 22 1F9A: voltage bix16 len16 *156.25/10000, current bix32 len16 signed /10.
        var voltage = new EvSignal("HV battery voltage", 16, 16, false, 156.25, 10000, 0);
        var current = new EvSignal("HV battery current", 32, 16, true, 1, 10, 0);
        var data = new byte[] { 0x00, 0x00, 0x35, 0x80, 0xFE, 0x0C };

        Assert.True(EvSignalDecoder.TryDecodeVoltage(voltage, data, out var volts));
        Assert.Equal(214.0, volts, 3);
        Assert.True(EvSignalDecoder.TryDecodeCurrent(current, data, out var amps));
        Assert.Equal(-50.0, amps, 3);
    }

    [Fact]
    public void UnsignedCatalogCurrent_IsReadAsNegativeWhenHuge()
    {
        // Hyundai/Kia 22 0101 current is described as unsigned but is signed.
        var current = new EvSignal("HV battery current", 0, 16, false, 1, 10, 0);
        Assert.True(EvSignalDecoder.TryDecodeCurrent(current, new byte[] { 0xFF, 0x9C }, out var amps));
        Assert.Equal(-10.0, amps, 3);
    }

    [Fact]
    public void TwelveVoltReading_IsNotAcceptedAsPackVoltage()
    {
        var voltage = new EvSignal("Battery voltage", 0, 8, false, 1, 10, 0);
        Assert.False(EvSignalDecoder.TryDecodeVoltage(voltage, new byte[] { 0x8C }, out _));
    }

    [Fact]
    public void Accumulator_SplitsConsumptionAndRegeneration()
    {
        var energy = new EvEnergyAccumulator();
        energy.Add(400, 0, 0, null);
        energy.Add(400, 100, 3600, null);   // ignored: gap too long
        energy.Add(400, 100, 1, null);      // 40 kW for 1 s
        energy.Add(400, -50, 1, null);      // -20 kW for 1 s

        Assert.Equal(40000d / 3600 / 1000, energy.ConsumedKWh, 6);
        Assert.Equal(20000d / 3600 / 1000, energy.RegeneratedKWh, 6);
        Assert.Equal(-20.0, energy.PowerKw, 3);
    }

    [Fact]
    public void Accumulator_LearnsReversedCurrentSign()
    {
        var energy = new EvEnergyAccumulator();
        var speed = 10.0;
        energy.Add(350, -60, 0, speed);
        for (var i = 0; i < 4; i++)
        {
            speed += 5;
            energy.Add(350, -60, 1, speed);   // accelerating while current is negative
        }

        Assert.True(energy.IsSignConfirmed);
        Assert.Equal(-1, energy.Sign);
        Assert.True(energy.PowerKw > 0);
        Assert.True(energy.ConsumedKWh > 0);
        Assert.Equal(0, energy.RegeneratedKWh, 6);
    }

    [Fact]
    public void Accumulator_DownhillNeverReducesUsedEnergy()
    {
        var energy = new EvEnergyAccumulator();
        var speed = 10.0;
        energy.Add(350, 60, 0, speed);
        for (var i = 0; i < 4; i++)
        {
            speed += 6;
            energy.Add(350, 60, 1, speed);   // accelerating: +21 kW is consumption
        }

        Assert.True(energy.IsSignConfirmed);
        var usedBeforeHill = energy.ConsumedKWh;

        for (var i = 0; i < 20; i++)
        {
            speed += 6;                      // rolling faster downhill …
            energy.Add(350, -60, 1, speed);  // … while the battery charges
        }

        Assert.Equal(1, energy.Sign);
        Assert.Equal(usedBeforeHill, energy.ConsumedKWh, 9);
        Assert.True(energy.RegeneratedKWh > 0);
        Assert.True(energy.NetKWh < usedBeforeHill);
    }

    [Fact]
    public void Accumulator_UserCanFlipAndSignSurvivesTripReset()
    {
        var energy = new EvEnergyAccumulator();
        energy.LockSign(1);
        Assert.Equal(-1, energy.FlipSign());
        energy.Reset();
        Assert.True(energy.IsSignConfirmed);
        Assert.Equal(-1, energy.Sign);
    }

    [Theory]
    [InlineData(8, EnergyMode.Electric)]
    [InlineData(17, EnergyMode.Hybrid)]
    [InlineData(1, EnergyMode.Fuel)]
    public void FuelTypeCode_MapsToMode(int code, EnergyMode expected) =>
        Assert.Equal(expected, EvSignalDecoder.ModeFromFuelTypeCode(code));

    [Fact]
    public void ConsumptionPer100Km()
    {
        Assert.Equal(15.0, EvEnergyAccumulator.KWhPer100Km(4.5, 30)!.Value, 6);
        Assert.Null(EvEnergyAccumulator.KWhPer100Km(1, 0.05));
        Assert.Equal(20.0, EvEnergyAccumulator.InstantKWhPer100Km(20, 100)!.Value, 6);
    }
}
