using CarScanner.Services.UDS;
using CarScanner.Services.OBD;

namespace CarScanner.Tests;

public class IsoTpAndUdsTests
{
    [Fact]
    public void SingleFrame_IsReassembled()
    {
        var sut = new IsoTpReassembler();
        var result = sut.Reassemble(new[] { new byte[] { 0x03, 0x50, 0x03, 0x00 } });
        Assert.Equal(new byte[] { 0x50, 0x03, 0x00 }, result);
    }

    [Fact]
    public void MultiFrame_IsReassembledInOrder()
    {
        var sut = new IsoTpReassembler();
        var result = sut.Reassemble(new[]
        {
            new byte[] { 0x10, 0x0A, 0x62, 0xF1, 0x90, 0x12, 0x34, 0x56 },
            new byte[] { 0x21, 0x78, 0x90, 0xAB, 0xCD, 0xEF, 0x01, 0x02 }
        });
        Assert.Equal(new byte[] { 0x62, 0xF1, 0x90, 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD }, result);
    }

    [Fact]
    public void NegativeResponse_DecodesNrc()
    {
        var result = UdsResponseParser.Parse(new byte[] { 0x7F, 0x22, 0x31 });
        Assert.Equal(UdsResponseKind.Negative, result.Kind);
        Assert.Equal((byte)0x31, result.NegativeResponseCode);
        Assert.Contains("Request out of range", result.Message);
    }

    [Fact]
    public void ElmCanFrames_StripsElevenBitHeaders_AndFiltersOtherEcus()
    {
        const string raw =
            "7E9 03 7F 22 31 00 00 00 00\n" +
            "7E8 10 0A 62 F1 90 12 34 56\n" +
            "7E8 21 78 90 AB CD EF 01 02";

        var frames = ObdResponseParser.ParseCanFrames(raw, 0x7E8);

        Assert.Equal(2, frames.Count);
        Assert.Equal((byte)0x10, frames[0][0]);
        Assert.Equal((byte)0x21, frames[1][0]);

        var payload = new IsoTpReassembler().Reassemble(frames);
        Assert.Equal(
            new byte[] { 0x62, 0xF1, 0x90, 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD },
            payload);
    }

    [Fact]
    public void ElmCanFrames_SupportsTwentyNineBitIdentifiers()
    {
        const string raw = "18DAF110 04 62 F1 90 41 00 00 00";

        var frames = ObdResponseParser.ParseCanFrames(raw, 0x18DAF110);

        Assert.Single(frames);
        Assert.Equal(new byte[] { 0x04, 0x62, 0xF1, 0x90, 0x41, 0x00, 0x00, 0x00 }, frames[0]);
    }

    [Fact]
    public void ElmCanFrames_DoesNotAcceptAnotherEcuResponse()
    {
        const string raw = "7E9 03 7F 22 31 00 00 00 00";

        var frames = ObdResponseParser.ParseCanFrames(raw, 0x7E8);

        Assert.Empty(frames);
    }

    [Fact]
    public void ElmCanIdentifiers_ReturnsOnlyElevenAndTwentyNineBitHeaders()
    {
        const string raw =
            "7E8 10 0A 49 02 01 31 47\n" +
            "18DAF110 04 62 F1 90 41 00 00 00\n" +
            "7E8 21 31 4A 43 35 34 34 34";

        var identifiers = ObdResponseParser.ExtractCanIdentifiers(raw);

        Assert.Equal(new[] { 0x7E8, 0x18DAF110 }, identifiers);
    }
}
