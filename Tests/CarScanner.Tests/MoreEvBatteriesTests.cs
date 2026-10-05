using CarScanner.Services;
using Xunit;

namespace CarScanner.Tests;

/// <summary>Recorded car responses from OBDb (BMW-i3, Ford-Fusion-Hybrid, Ford-Mustang-Mach-E, Volkswagen-e-Golf, Audi-e-tron, Haval-Jolion-HEV; CC BY-SA 4.0).</summary>
public class MoreEvBatteriesTests
{
    private static byte[]? Data(string raw, HvRequest request) => HvPackProtocol.ExtractData(raw, request);

    [Fact]
    public void BmwI3_ExtendedAddressing_HealthCellsCapacity()
    {
        Assert.Equal(87, BmwI3Battery.DecodeSoh(Data("607F110076263350000\n607F1210057FFFFFFFF", BmwI3Battery.Health))!.Value, 0);

        var range = BmwI3Battery.DecodeCellRange(Data("607F1100762DDBF0E0A\n607F1210E23FFFFFFFF", BmwI3Battery.CellRange));
        Assert.NotNull(range);
        Assert.Equal(3.619, range!.Value.Max, 3);
        Assert.Equal(3.594, range.Value.Min, 3);

        const string statistics = "607F1102962DFA02224\n607F12122A5225388C2\n607F122890F88E70588\n607F12305EC05BEFFFF\n607F124FFFF2CDF05D0\n607F125061005EA8A0C\n607F1268A348A1C2CDF";
        Assert.Equal(87.9, BmwI3Battery.DecodeAverageCapacity(Data(statistics, BmwI3Battery.CellStatistics))!.Value, 1);

        Assert.Equal(new[] { 14.2, 15.2 }, BmwI3Battery.DecodeTemperatures(Data("607F1100962DDC00588\n607F12105EC05C0FFFF", BmwI3Battery.Temperatures)));
        Assert.Equal(94, BmwI3Battery.NominalAh(2017));
    }

    [Fact]
    public void BmwI3_FramesWithoutTheTesterAddress_AreIgnored()
    {
        Assert.Null(Data("6071007626335000000", BmwI3Battery.Health));
    }

    [Fact]
    public void FordFusionHybrid_SpreadModuleSocTemperature()
    {
        Assert.Equal(0.007, FordHybridBattery.DecodeMillivolts(Data("7EC0562483F0007", FordHybridBattery.VoltageSpread), 2)!.Value, 3);
        Assert.Equal(3.658, FordHybridBattery.DecodeMillivolts(Data("7EC056248400E4A", FordHybridBattery.MinimumModule), 5)!.Value, 3);
        Assert.Equal(37.4, FordHybridBattery.DecodeSoc(Data("7EC0562480148FE", FordHybridBattery.Soc))!.Value, 1);
        Assert.Equal(new double[] { 36, 32 }, FordHybridBattery.DecodeTemperatureRange(Data("7EC0762480856523553", FordHybridBattery.TemperatureRange)));
        Assert.Equal(6.0, FordHybridBattery.DecodeAgeYears(Data("7EC056248103978", FordHybridBattery.Age))!.Value, 0);
    }

    [Fact]
    public void VwEGolf_HighestAndLowestCell()
    {
        var high = VwEGolfBattery.DecodeCell(Data("7ED07621E333A200001", VwEGolfBattery.MaximumCell));
        var low = VwEGolfBattery.DecodeCell(Data("7ED07621E3439E70007", VwEGolfBattery.MinimumCell));
        Assert.Equal(3.6328, high!.Value.Volts, 4);
        Assert.Equal(1, high.Value.Cell);
        Assert.Equal(3.6189, low!.Value.Volts, 4);
        Assert.Equal(7, low.Value.Cell);
        Assert.Equal(-130.5, VwEGolfBattery.DecodeCurrent(Data("7ED05621E3D05F2", VwEGolfBattery.Current))!.Value, 1);
    }

    [Theory]
    [InlineData("BMW", "i3", true)]
    [InlineData("BMW", "i3s", true)]
    [InlineData("BMW", "X3", false)]
    public void I3Family(string make, string model, bool expected) => Assert.Equal(expected, BmwI3Battery.IsI3(make, model));

    [Theory]
    [InlineData("Ford", "Fusion Hybrid", true)]
    [InlineData("Ford", "C-Max Energi", true)]
    [InlineData("Lincoln", "MKZ Hybrid", true)]
    [InlineData("Ford", "Focus", false)]
    public void FordFamily(string make, string model, bool expected) => Assert.Equal(expected, FordHybridBattery.IsFordHybrid(make, model));

    [Theory]
    [InlineData("7EC0462490CB3", 89.5)]
    [InlineData("7EC0462490CB7", 91.5)]
    public void MustangMachE_Health(string raw, double soh) =>
        Assert.Equal(soh, FordHybridBattery.DecodeSoh(Data(raw, FordHybridBattery.Health))!.Value, 1);

    [Fact]
    public void AudiEtron_HealthCellsModule()
    {
        Assert.Equal(87.5, AudiEtronBattery.DecodeSoh(Data("7ED056251E03A03", AudiEtronBattery.Health))!.Value, 1);
        Assert.Null(AudiEtronBattery.DecodeSoh(Data("7ED056251E0FFFE", AudiEtronBattery.Health)));

        // Newer software ÷ 10 000, older ÷ 1 000.
        Assert.Equal(3.401, AudiEtronBattery.DecodeCell(Data("7ED06621E3384DA1A", AudiEtronBattery.MaximumCell))!.Value, 3);
        Assert.Equal(3.616, AudiEtronBattery.DecodeCell(Data("7ED07621E330E200017", AudiEtronBattery.MaximumCell))!.Value, 3);
        Assert.Equal(-3, AudiEtronBattery.DecodeTemperature(Data("7ED05621E0E6109", AudiEtronBattery.MaximumTemperature))!.Value, 0);

        const string module = "7ED102E621850838214\n7ED2114000000008200\n7ED2214000000008232\n7ED231400000000821E\n7ED241400000000820A\n7ED2514000000008200\n7ED261400000000AAAA";
        var cells = AudiEtronBattery.DecodeModule(Data(module, AudiEtronBattery.Module(0)));
        Assert.Equal(6, cells.Length);
        Assert.Equal(3.33, cells[0], 3);
        Assert.Equal(3.328, cells[1], 3);
        Assert.Equal(3.333, cells[2], 3);
    }

    [Fact]
    public void HavalJolionHev_HealthCellsCurrent()
    {
        Assert.Equal(98, HavalHevBattery.DecodeSoh(Data("7CB0462E0F562", HavalHevBattery.Health))!.Value, 0);
        Assert.Equal(48.9, HavalHevBattery.DecodeSoc(Data("7CB0562E0F401E9", HavalHevBattery.Soc))!.Value, 1);
        Assert.Equal(3.656, HavalHevBattery.DecodeCell(Data("7CB0562E0080E48", HavalHevBattery.MaximumCell))!.Value, 3);
        Assert.Equal(3.645, HavalHevBattery.DecodeCell(Data("7CB0562E0090E3D", HavalHevBattery.MinimumCell))!.Value, 3);
        Assert.Equal(16, HavalHevBattery.DecodePosition(Data("7CB0462E00B0F", HavalHevBattery.MinimumCellPosition)));
        Assert.Equal(-9.4, HavalHevBattery.DecodeCurrent(Data("7CB0562E0A41741", HavalHevBattery.Current))!.Value, 1);
    }
}
