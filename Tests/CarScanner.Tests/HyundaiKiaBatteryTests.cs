using CarScanner.Services;
using Xunit;

namespace CarScanner.Tests;

/// <summary>Recorded car responses from OBDb (Hyundai-Kona-Electric, Kia-Niro; CC BY-SA 4.0).</summary>
public class HyundaiKiaBatteryTests
{
    private static HvRequest Uds(byte id) => new(0x7E4, 0x7EC, new byte[] { 0x22, 0x01, id });

    [Fact]
    public void KonaElectric_StatusCellsAndHealth()
    {
        const string status = "7EC103E620101FFF7E7\r7EC21FF490000000080\r7EC2200000DD9090908\r7EC23080908000007B4\r7EC2402B40100007E00\r7EC2500DB540000D915\r7EC260000525900004F\r7EC2757007B02130000\r7EC2800000000000BB8";
        var values = HyundaiKiaBattery.DecodeStatus(HvPackProtocol.ExtractData(status, Uds(0x01)));
        Assert.NotNull(values);
        Assert.Equal(36.5, values!.SocPercent!.Value, 1);
        Assert.Equal(0, values.CurrentA!.Value, 1);

        const string cells = "7EC1027620102FFFFFF\r7EC21FFB4B4B4B4B4B4\r7EC22B4B4B4B4B4B4B4\r7EC23B4B4B4B4B4B4B4\r7EC24B4B4B4B4B4B4B4\r7EC25B4B4B4B4B4AAAA";
        var decoded = HyundaiKiaBattery.DecodeCells(HvPackProtocol.ExtractData(cells, Uds(0x02)));
        Assert.Equal(32, decoded.Length);
        Assert.All(decoded, cell => Assert.Equal(3.6, cell, 2));

        const string health = "7EC102E620105003FFF\r7EC2190000000000000\r7EC220000077E000041\r7EC238C3E7800006407\r7EC244B03E032003137\r7EC254B0000B4B40000\r7EC260900000000AAAA";
        var data = HvPackProtocol.ExtractData(health, Uds(0x05));
        Assert.Equal(99.2, HyundaiKiaBattery.DecodeSoh(data)!.Value, 1);
        Assert.Equal(2, HyundaiKiaBattery.DecodeExtraCells(data).Length);
    }

    [Fact]
    public void NiroHybrid2018_OlderRequests_FilterOtherEcus()
    {
        var request = new HvRequest(0x7E4, 0x7EC, new byte[] { 0x21, 0x01 });
        const string status = "7EB10146101FF010000\r7EC103D6101FFFFFFFF\r7EB216000601D60A034\r7EC21890F3C10680300\r7EB220050055204DF34\r7EC222C09961F1F1F1F\r7EC23A74006001ABF16\r7EC24BF010000860009\r7EC2511490009104800\r7EC2602245300020E66\r7EC27013AF1FE6D00F6\r7EC280000000003E800";
        var values = HyundaiKiaBattery.DecodeStatus(HvPackProtocol.ExtractData(status, request));
        Assert.NotNull(values);
        Assert.Equal(68.5, values!.SocPercent!.Value, 1);
        Assert.Equal(4.4, values.CurrentA!.Value, 1);
        Assert.Equal(245.4, values.PackVoltage!.Value, 1);
        Assert.Equal(new double[] { 31 }, values.Temperatures);

        var cells = new HvRequest(0x7E4, 0x7EC, new byte[] { 0x21, 0x02 });
        const string raw = "7EB103B6102FFFFFF0F\r7EC10266102FFFFFFFF\r7EB211105A1F9A1F910\r7EC21BFBFBFBFBFBFBF\r7EB22072521FBFFFCFF\r7EC22BFBFBFBFBFBFBF\r7EB235A170500000000\r7EC23BFBFBFBFBFBFBF\r7EB249004CDABC2CD1E\r7EC24BFBFBFBFBFBFBF\r7EB2500510308007304\r7EC25BFBFBFBE000000";
        var decoded = HyundaiKiaBattery.DecodeCells(HvPackProtocol.ExtractData(raw, cells));
        Assert.Equal(32, decoded.Length);
        Assert.Equal(3.82, decoded[0], 2);
        Assert.Equal(3.80, decoded[31], 2);
    }

    [Fact]
    public void UnusedSlots_EndTheCellList()
    {
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xB4, 0xB4, 0x00, 0x00 };
        Assert.Equal(2, HyundaiKiaBattery.DecodeCells(data).Length);
        Assert.Null(HyundaiKiaBattery.DecodeSoh(new byte[30]));
    }
}
