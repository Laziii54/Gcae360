using CarScanner.Services;
using Xunit;

namespace CarScanner.Tests;

/// <summary>Recorded car responses from OBDb (Toyota-Prius, Toyota-RAV4-Hybrid; CC BY-SA 4.0).</summary>
public class ToyotaHybridBatteryTests
{
    [Fact]
    public void Gen2Blocks_DecodeSocCurrentAndFourteenBlocks()
    {
        const string raw = "7EB102161CE68709186\r7EB21888682867C867B\r7EB22867F867E867086\r7EB2372867B867B866D\r7EB24866E8683868700";
        var sample = ToyotaHybridBattery.DecodeGen2Blocks(HvPackProtocol.ExtractData(raw, ToyotaHybridBattery.Gen2Blocks));

        Assert.NotNull(sample);
        Assert.Equal(14, sample!.Blocks.Length);
        Assert.Equal(16.72, sample.Blocks[0], 2);
        Assert.Equal(16.48, sample.Blocks[6], 2);
        Assert.Equal(16.71, sample.Blocks[13], 2);
        Assert.Equal(52, sample.SocPercent!.Value, 1);
        Assert.Equal(-39.51, sample.CurrentA!.Value, 2);
    }

    [Fact]
    public void Gen2Thermal_And_Resistance()
    {
        const string thermal = "7EB101261CF85EC80C5\r7EB2158A80008000086\r7EB22A4874686960000";
        var temperatures = ToyotaHybridBattery.DecodeGen2Temperatures(HvPackProtocol.ExtractData(thermal, ToyotaHybridBattery.Gen2Thermal));
        Assert.Equal(new[] { 17.0, 18.62, 16.86 }, temperatures);

        const string resistance = "7EB101F61D00E000000\r7EB210000000000862C\r7EB2206863503141313\r7EB2313131313131312\r7EB2413131314000000";
        var values = ToyotaHybridBattery.DecodeGen2Resistances(HvPackProtocol.ExtractData(resistance, ToyotaHybridBattery.Gen2Resistance));
        Assert.Equal(14, values.Length);
        Assert.Equal(20, values[0]);
        Assert.Equal(18, values[9]);
        Assert.Empty(HvPackAssessment.HighResistanceBlocks(values));
    }

    [Fact]
    public void TngaPower_DecodesVoltageAndCurrent()
    {
        const string raw = "7DA1009621F9A0E0033\r7DA2140057500000000";
        var (voltage, current) = ToyotaHybridBattery.DecodeTngaPower(HvPackProtocol.ExtractData(raw, ToyotaHybridBattery.TngaPower));
        Assert.Equal(205, voltage!.Value, 1);
        Assert.Equal(139.7, current!.Value, 1);
    }

    [Fact]
    public void NegativeAnswer_IsNotData()
    {
        Assert.Null(HvPackProtocol.ExtractData("7EB037F2112", ToyotaHybridBattery.Gen2Blocks));
        Assert.True(HvPackProtocol.IsResponsePending("7EB037F2178", ToyotaHybridBattery.Gen2Blocks));
    }

    [Fact]
    public void WeakestBlock_IsFoundOnlyWhenClearlyLower()
    {
        Assert.Equal(3, HvPackAssessment.WeakestBlock(new[] { 15.0, 15.02, 14.6, 15.01, 14.99 }, false).Block);
        Assert.Null(HvPackAssessment.WeakestBlock(new[] { 15.0, 15.02, 14.95, 15.01, 14.99 }, false).Block);
    }

    [Theory]
    [InlineData(0.10, false, false, HvPackAssessment.Verdict.Good)]
    [InlineData(0.45, false, false, HvPackAssessment.Verdict.Watch)]
    [InlineData(1.40, false, true, HvPackAssessment.Verdict.Failing)]
    [InlineData(0.02, true, false, HvPackAssessment.Verdict.Good)]
    [InlineData(0.10, true, false, HvPackAssessment.Verdict.Weak)]
    public void Judge_UsesPackLimits(double spread, bool lithium, bool load, HvPackAssessment.Verdict expected) =>
        Assert.Equal(expected, HvPackAssessment.Judge(spread, lithium, load));

    [Fact]
    public void Rav4Hybrid2017_BlocksResistanceTemperatureCurrent()
    {
        const string blocks = "7EA1028618130393039\r7EA2130393010303930\r7EA2210304930493049\r7EA2330203062304930\r7EA2449302830623049\r7EA253049AD780A0000";
        var sample = ToyotaHybridBattery.DecodeGen3Blocks(HvPackProtocol.ExtractData(blocks, ToyotaHybridBattery.Gen3Blocks));
        Assert.NotNull(sample);
        Assert.Equal(17, sample!.Blocks.Length);
        Assert.Equal(15.068, sample.Blocks[0], 2);
        Assert.Equal(15.118, sample.Blocks[10], 2);

        const string resistance = "7EA1013619518171818\r7EA21181818181A1A19\r7EA2218191918181900";
        var values = ToyotaHybridBattery.DecodeGen3Resistances(HvPackProtocol.ExtractData(resistance, ToyotaHybridBattery.Gen3Resistance), 17);
        Assert.Equal(17, values.Length);
        Assert.Equal(24, values[0]);
        Assert.Equal(26, values[8]);

        const string thermal = "7EA101061874FEE5B8F\r7EA215BAE5ACF5AF05B\r7EA226E5B6E00000000";
        Assert.Equal(new[] { 41.5, 41.6, 40.8 }, ToyotaHybridBattery.DecodeGen3Temperatures(HvPackProtocol.ExtractData(thermal, ToyotaHybridBattery.Gen3Thermal)));

        const string current = "7EA100A619868524AB3\r7EA2100727E44000000";
        Assert.Equal(-60.62, ToyotaHybridBattery.DecodeGen3Current(HvPackProtocol.ExtractData(current, ToyotaHybridBattery.Gen3Current))!.Value, 2);
    }
}
