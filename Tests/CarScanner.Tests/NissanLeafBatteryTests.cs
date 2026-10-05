using CarScanner.Services;
using Xunit;

namespace CarScanner.Tests;

/// <summary>Recorded car responses from OBDb (Nissan-Leaf; CC BY-SA 4.0).</summary>
public class NissanLeafBatteryTests
{
    [Theory]
    [InlineData("7BB100C61610AD3130C\r7BB21061305130C03FF", 48.8)]
    [InlineData("7BB100C61610E911BA0\r7BB21FF1A861BA003FF", 70.7)]
    [InlineData("7BB100C6161140F1C9A\r7BB21FF1CA41C9A03FF", 73.2)]
    public void Health(string raw, double soh) =>
        Assert.Equal(soh, NissanLeafBattery.DecodeSoh(HvPackProtocol.ExtractData(raw, NissanLeafBattery.Health))!.Value, 1);

    [Fact]
    public void Status2012_VoltageSocCapacity()
    {
        const string raw = "7BB1029610100000328\r7BB2102880000049FFF\r7BB22FFFFFF02EE16A0\r7BB23905238F3038B01\r7BB24F90AD60005322B\r7BB250004E23E800005";
        var values = NissanLeafBattery.DecodeStatus(HvPackProtocol.ExtractData(raw, NissanLeafBattery.Status), 2012);
        Assert.NotNull(values);
        Assert.Equal(369.5, values!.PackVoltage!.Value, 1);
        Assert.Equal(34.1, values.SocPercent!.Value, 1);
        Assert.Equal(32.0, values.CapacityAh!.Value, 1);
    }

    [Fact]
    public void CellRange2018()
    {
        const string raw = "7BB101C6103FFFF0229\r7BB2100350004F5BC0D\r7BB225B0D360003C200\r7BB23000000003D145E\r7BB240BFFFFFFFFFFFF";
        var range = NissanLeafBattery.DecodeCellRange(HvPackProtocol.ExtractData(raw, NissanLeafBattery.CellRange));
        Assert.NotNull(range);
        Assert.Equal(3.419, range!.Value.Max, 3);
        Assert.Equal(3.382, range.Value.Min, 3);
    }

    [Fact]
    public void Cells_NeedAll96Plausible_AndMustMatchTheRange()
    {
        var data = new byte[196];
        for (var i = 0; i < 96; i++)
        {
            var mv = i == 10 ? 3760 : 3800;
            data[i * 2] = (byte)(mv >> 8);
            data[i * 2 + 1] = (byte)mv;
        }
        var cells = NissanLeafBattery.DecodeCells(data);
        Assert.NotNull(cells);
        Assert.True(NissanLeafBattery.AgreesWithRange(cells!, (3.800, 3.760)));
        Assert.False(NissanLeafBattery.AgreesWithRange(cells!, (3.419, 3.382)));

        data[0] = 0; data[1] = 0;
        Assert.Null(NissanLeafBattery.DecodeCells(data));
    }

    [Theory]
    [InlineData("Nissan", "Leaf", true)]
    [InlineData("Nissan", "e-NV200", true)]
    [InlineData("Nissan", "Altima", false)]
    public void Family(string make, string model, bool leaf) =>
        Assert.Equal(leaf, NissanLeafBattery.IsLeaf(make, model));
}
