using CarScanner.Models;
using CarScanner.Services.OBD;

namespace CarScanner.Tests;

public class DiagnosticParserTests
{
    [Fact]
    public void P0300_DecodesCorrectly()
    {
        var sut = new DTCDecoder();
        var code = sut.DecodeSingle(0x03, 0x00, DiagnosticCodeStatus.Stored);
        Assert.Equal("P0300", code.Code);
        Assert.Equal(DiagnosticCodeCategory.Powertrain, code.Category);
    }

    [Fact]
    public void ZeroPair_IsIgnored()
    {
        var sut = new DTCDecoder();
        var codes = sut.DecodeStored("43 00 00 01 00");
        Assert.Single(codes);
        Assert.Equal("P0100", codes[0].Code);
    }

    [Fact]
    public void Can_SingleCode_UsesCountByte()
    {
        var sut = new DTCDecoder();
        var codes = sut.DecodeStored("43 01 01 33");
        Assert.Single(codes);
        Assert.Equal("P0133", codes[0].Code);

        var explicitCan = sut.DecodeStored("43 01 01 33", true);
        Assert.Single(explicitCan);
        Assert.Equal("P0133", explicitCan[0].Code);
    }

    [Fact]
    public void Can_TwoCodes_AreDecoded()
    {
        var sut = new DTCDecoder();
        var codes = sut.DecodeStored("43 02 01 33 02 20", true);
        Assert.Equal(new[] { "P0133", "P0220" }, codes.Select(c => c.Code).ToArray());
    }

    [Fact]
    public void Can_MultiFrame_WithFrameIndexLines_IsDecoded()
    {
        var sut = new DTCDecoder();
        var codes = sut.DecodeStored("00A\r0: 43 04 01 33 02 20\r1: 03 00 04 20 00 00 00\r\r>");
        Assert.Equal(new[] { "P0133", "P0220", "P0300", "P0420" }, codes.Select(c => c.Code).ToArray());
    }

    [Fact]
    public void NonCan_FourCodes_OverTwoLines_AreDecoded()
    {
        var sut = new DTCDecoder();
        var codes = sut.DecodeStored("43 01 33 02 20 03 00\r43 04 20 00 00 00 00", false);
        Assert.Equal(new[] { "P0133", "P0220", "P0300", "P0420" }, codes.Select(c => c.Code).ToArray());

        var auto = sut.DecodeStored("43 01 33 02 20 03 00\r43 04 20 00 00 00 00");
        Assert.Equal(new[] { "P0133", "P0220", "P0300", "P0420" }, auto.Select(c => c.Code).ToArray());
    }

    [Fact]
    public void TwoEcus_Answering_AreMergedAndDeduplicated()
    {
        var sut = new DTCDecoder();
        var can = sut.DecodeStored("43 01 01 33\r43 02 07 00 01 33", true);
        Assert.Equal(new[] { "P0133", "P0700" }, can.Select(c => c.Code).ToArray());

        var nonCan = sut.DecodePending("47 01 33 00 00 00 00\r47 07 00 00 00 00 00", false);
        Assert.Equal(new[] { "P0133", "P0700" }, nonCan.Select(c => c.Code).ToArray());
        Assert.All(nonCan, c => Assert.Equal(DiagnosticCodeStatus.Pending, c.Status));
    }

    [Fact]
    public void NoData_ReturnsEmpty()
    {
        var sut = new DTCDecoder();
        Assert.Empty(sut.DecodeStored("NO DATA"));
        Assert.Empty(sut.DecodePermanent("SEARCHING...\rNO DATA\r\r>", true));
    }

    [Fact]
    public void Garbage_ThrowsFormatException()
    {
        var sut = new DTCDecoder();
        Assert.Throws<FormatException>(() => sut.DecodeStored("7F 03 12"));
    }

    [Fact]
    public void PID_RpmFormula_IsDecoded()
    {
        var sut = new PIDDecoder();
        Assert.True(sut.TryDecode(0x0C, "41 0C 1F 40", out var value));
        Assert.Equal(2000, value);
    }
}


public class AdvancedParserTests
{
    [Fact]
    public void Mode05_ReturnsStructuredItems_WhenResponseExists()
    {
        var values = AdvancedModeParser.ParseMode05("45 01 00 10 00 20 01");
        Assert.Single(values);
        Assert.Contains("Actual=16", values[0].RawValue);
    }

    [Fact]
    public void Mode06_ReturnsStatusFromRawLimits()
    {
        var values = AdvancedModeParser.ParseMode06("46 01 02 00 10 00 20 00 18 00 00");
        Assert.Single(values);
        Assert.Equal("PASS_RANGE", values[0].Status);
    }

    [Fact]
    public void PidDefinition_IsPresentForCommonRpm()
    {
        Assert.True(PidDefinitionRegistry.TryGet(0x0C, out var def));
        Assert.Equal("rpm", def.Unit);
        Assert.Contains("256", def.Formula);
    }
}
