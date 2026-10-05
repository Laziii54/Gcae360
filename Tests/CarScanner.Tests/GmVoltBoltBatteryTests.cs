using CarScanner.Services;
using Xunit;

namespace CarScanner.Tests;

/// <summary>Recorded car responses from OBDb (Chevrolet-Volt, Chevrolet-Bolt-EV; CC BY-SA 4.0).</summary>
public class GmVoltBoltBatteryTests
{
    private static byte[]? Data(string raw, HvRequest request) => HvPackProtocol.ExtractData(raw, request);

    [Fact]
    public void CellIdentifiers_SkipTheGap()
    {
        Assert.Equal(0x4181, GmVoltBoltBattery.CellDid(0));
        Assert.Equal(0x419F, GmVoltBoltBattery.CellDid(30));
        Assert.Equal(0x4200, GmVoltBoltBattery.CellDid(31));
        Assert.Equal(0x4240, GmVoltBoltBattery.CellDid(95));
    }

    [Fact]
    public void Volt2013_CellsAndTemperature()
    {
        Assert.Equal(4.040, GmVoltBoltBattery.DecodeCell(Data("7EF05624181CED8", GmVoltBoltBattery.CellRequest(0)))!.Value, 3);
        Assert.Equal(4.036, GmVoltBoltBattery.DecodeCell(Data("7EF05624240CEA7", GmVoltBoltBattery.CellRequest(95)))!.Value, 3);
        Assert.Equal(23, GmVoltBoltBattery.DecodeTemperature(Data("7EC0462434F3F", GmVoltBoltBattery.BatteryTemperature))!.Value, 1);
        // A waking BECM answers 0 V: not a cell value.
        Assert.Null(GmVoltBoltBattery.DecodeCell(Data("7EF056241810000", GmVoltBoltBattery.CellRequest(0))));
    }

    [Fact]
    public void Bolt_SocVoltageTemperatureCapacity()
    {
        Assert.Equal(76.1, GmVoltBoltBattery.DecodeSoc(Data("7EC0462432FC2", GmVoltBoltBattery.BoltSoc))!.Value, 1);
        Assert.Equal(374.4, GmVoltBoltBattery.DecodePackVoltage(Data("7EC0562432D02D0", GmVoltBoltBattery.BoltPackVoltage))!.Value, 1);
        Assert.Equal(21, GmVoltBoltBattery.DecodeTemperature(Data("7EC046243493D", GmVoltBoltBattery.BoltMaxTemperature))!.Value, 1);
        Assert.Equal(19, GmVoltBoltBattery.DecodeTemperature(Data("7EC0462434A3B", GmVoltBoltBattery.BoltMinTemperature))!.Value, 1);
        Assert.Equal(177.1, GmVoltBoltBattery.DecodeCapacity(Data("7EC056245F94532", GmVoltBoltBattery.BoltCapacityFrom2019), true)!.Value, 1);
        Assert.Equal(182.1, GmVoltBoltBattery.DecodeCapacity(Data("7EC056241A3071D", GmVoltBoltBattery.BoltCapacityTo2018), false)!.Value, 1);
    }

    [Fact]
    public void SpacedFrames_AreParsedToo()
    {
        Assert.Equal(3.930, GmVoltBoltBattery.DecodeCell(Data("7EF 05 62 41 81 C9 36 ", GmVoltBoltBattery.CellRequest(0)))!.Value, 3);
    }

    [Theory]
    [InlineData("Chevrolet", "Volt", false)]
    [InlineData("Chevrolet", "Bolt EUV", true)]
    [InlineData("Opel", "Ampera-e", true)]
    [InlineData("Opel", "Ampera", false)]
    [InlineData("Cadillac", "ELR", false)]
    public void Family(string make, string model, bool bolt) =>
        Assert.Equal(bolt, GmVoltBoltBattery.IsBolt(make, model));
}
