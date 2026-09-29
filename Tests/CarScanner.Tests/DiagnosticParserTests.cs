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
