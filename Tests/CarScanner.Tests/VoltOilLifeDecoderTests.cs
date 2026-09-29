using Xunit;

namespace CarScanner.Tests;

public class VoltOilLifeDecoderTests
{
    [Theory]
    [InlineData("7E9 04 62 11 9F FF")]
    [InlineData("7E8 03 62 11 9F FF")]
    [InlineData("7E8 04 62 11 9F")]
    [InlineData("62 11 9F FF\r62 11 9F 01")]
    [InlineData("7F 22 31 62 11 9F FF")]
    [InlineData("AA 62 11 9F FF")]
    [InlineData("62 11 A0 FF")]
    [InlineData("garbage 62 11 9F FF")]
    [InlineData("OK")]
    public void RejectsWrongSenderFramingAndEmbeddedData(string raw) =>
        Assert.False(CarScanner.Services.OemDiagnostics.VoltOilLifeDecoder.TryDecodeFromRaw(raw, out _));

    [Theory]
    [InlineData("7E8 04 62 11 9F 6B 00 00 00")]
    [InlineData("7E80462119F6B000000")]
    [InlineData("22119F\r7E8 04 62 11 9F 6B\r>")]
    public void AcceptsExpectedFramedResponse(string raw)
    {
        Assert.True(CarScanner.Services.OemDiagnostics.VoltOilLifeDecoder.TryDecodeFromRaw(raw, out var p));
        Assert.Equal(107 * 100d / 255d, p, 6);
    }
    [Fact]
    public void DecodesValidResponse()
    {
        var raw = "62 11 9F 6B"; // 0x6B = 107 -> 107*100/255 = 41.960... -> 42.0
        Assert.True(CarScanner.Services.OemDiagnostics.VoltOilLifeDecoder.TryDecodeFromRaw(raw, out var p));
        Assert.Equal(107 * 100d / 255d, p, 6);
    }

    [Fact]
    public void DecodesZeroPercent()
    {
        var raw = "62 11 9F 00";
        Assert.True(CarScanner.Services.OemDiagnostics.VoltOilLifeDecoder.TryDecodeFromRaw(raw, out var p));
        Assert.Equal(0d, p, 6);
    }

    [Fact]
    public void DecodesHundredPercent()
    {
        var raw = "62 11 9F FF";
        Assert.True(CarScanner.Services.OemDiagnostics.VoltOilLifeDecoder.TryDecodeFromRaw(raw, out var p));
        Assert.Equal(100d, p, 6);
    }

    [Fact]
    public void FailsOnMalformedResponse()
    {
        var raw = "62 11 9F"; // missing data byte
        Assert.False(CarScanner.Services.OemDiagnostics.VoltOilLifeDecoder.TryDecodeFromRaw(raw, out _));
    }

    [Fact]
    public void FailsOnNegativeNoData()
    {
        var raw = "NO DATA";
        Assert.False(CarScanner.Services.OemDiagnostics.VoltOilLifeDecoder.TryDecodeFromRaw(raw, out _));
    }
}
