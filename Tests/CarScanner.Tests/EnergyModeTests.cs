using CarScanner.Services.Energy;

namespace CarScanner.Tests;

public class EnergyModeTests
{
    [Theory]
    [InlineData("Electric, Gasoline", EnergyMode.Hybrid)]   // Chevrolet Volt (NHTSA)
    [InlineData("Plug-in Hybrid", EnergyMode.Hybrid)]
    [InlineData("Gasoline Hybrid", EnergyMode.Hybrid)]
    [InlineData("Electric", EnergyMode.Electric)]
    [InlineData("Gasoline", EnergyMode.Fuel)]
    [InlineData("Diesel", EnergyMode.Fuel)]
    public void FuelTypeText_PicksThePage(string text, EnergyMode expected) =>
        Assert.Equal(expected, EvSignalDecoder.ModeFromText(text));

    [Fact]
    public void VoltPackVoltage_Decodes_Div64()
    {
        // 0x5DC0 / 64 = 375 V
        var signal = new EvSignal("HV battery voltage", 0, 16, false, 1, 64, 0);
        Assert.True(EvSignalDecoder.TryDecodeVoltage(signal, new byte[] { 0x5D, 0xC0 }, out var volts));
        Assert.Equal(375, volts, 1);
    }

    [Fact]
    public void VoltPackCurrent_IsSigned_Div20()
    {
        // 0xFF38 = −200 → −10 A (charging)
        var signal = new EvSignal("HV battery current", 0, 16, true, 1, 20, 0);
        Assert.True(EvSignalDecoder.TryDecodeCurrent(signal, new byte[] { 0xFF, 0x38 }, out var amps));
        Assert.Equal(-10, amps, 1);
    }
}
