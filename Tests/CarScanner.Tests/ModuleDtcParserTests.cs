using CarScanner.Services.OemDiagnostics;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Trouble-code list decoding (UDS 59 02, KWP 58, GMLAN). The BMW bytes are the engine module's real reply from the
/// capture; the app turns 19 0F 08 into the SAE-style letter code P190F, but the bytes themselves stay available
/// because the format of that module is not established.
/// </summary>
public sealed class ModuleDtcParserTests
{
    [Fact]
    public void RealBmwReply_IsDecoded_AndKeepsItsRawBytes()
    {
        var codes = ModuleDtcParsers.ParseUds(new byte[] { 0x59, 0x02, 0xFF, 0x19, 0x0F, 0x08, 0x68 });

        var code = Assert.Single(codes);
        Assert.Equal("P190F", code.Code);
        Assert.Equal("190F08", code.RawBytesHex);
        Assert.Equal((byte)0x08, code.FailureType);
        Assert.Equal((byte)0x68, code.StatusByte);
        Assert.True(code.Confirmed);
        Assert.False(code.Active);
        Assert.False(code.Pending);
        Assert.Equal("P190F-08", code.DisplayCode);
    }

    [Fact]
    public void RealFordPrefix_OfACutOffList_YieldsNoRecord_NotAnInventedOne()
    {
        // 59 02 FF 00 11 00: only three of the four bytes of the first record arrived.
        Assert.Empty(ModuleDtcParsers.ParseUds(new byte[] { 0x59, 0x02, 0xFF, 0x00, 0x11, 0x00 }));
    }

    [Fact]
    public void ARecordCutInTheMiddle_IsIgnored_WhileTheWholeOnesBeforeItAreKept()
    {
        var codes = ModuleDtcParsers.ParseUds(new byte[] { 0x59, 0x02, 0xFF, 0x01, 0x33, 0x00, 0x09, 0x02, 0x00 });

        var code = Assert.Single(codes);
        Assert.Equal("P0133", code.Code);
        Assert.True(code.Active);
        Assert.True(code.Confirmed);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x59 })]
    [InlineData(new byte[] { 0x59, 0x02 })]
    [InlineData(new byte[] { 0x59, 0x02, 0xFF })]
    public void MessagesWithoutRecords_GiveAnEmptyList(byte[] message) =>
        Assert.Empty(ModuleDtcParsers.ParseUds(message));

    [Fact]
    public void RecordsThatAreAllZero_OrNeverRan_AreNotCodes()
    {
        var codes = ModuleDtcParsers.ParseUds(new byte[]
        {
            0x59, 0x02, 0xFF,
            0x00, 0x00, 0x00, 0x09,   // all-zero DTC: padding
            0x01, 0x33, 0x00, 0x40,   // only "test not completed this cycle": no fault information
            0x01, 0x34, 0x00, 0x08    // confirmed
        });

        var code = Assert.Single(codes);
        Assert.Equal("P0134", code.Code);
    }

    [Fact]
    public void RepeatedRecords_AreListedOnce()
    {
        var codes = ModuleDtcParsers.ParseUds(new byte[] { 0x59, 0x02, 0xFF, 0x01, 0x33, 0x00, 0x08, 0x01, 0x33, 0x00, 0x08 });

        Assert.Single(codes);
    }

    [Fact]
    public void SameCodeWithDifferentFailureTypes_StaysTwoCodes()
    {
        var codes = ModuleDtcParsers.ParseUds(new byte[] { 0x59, 0x02, 0xFF, 0x01, 0x33, 0x01, 0x08, 0x01, 0x33, 0x02, 0x08 });

        Assert.Equal(2, codes.Count);
    }

    [Fact]
    public void Kwp_RecordsAreThreeBytes_AndCutOffOnesAreDropped()
    {
        var whole = ModuleDtcParsers.ParseKwp(new byte[] { 0x58, 0x01, 0x01, 0x33, 0x60 });
        var cut = ModuleDtcParsers.ParseKwp(new byte[] { 0x58, 0x01, 0x01, 0x33 });

        var code = Assert.Single(whole);
        Assert.Equal("P0133", code.Code);
        Assert.Equal("0133", code.RawBytesHex);
        Assert.True(code.Active);
        Assert.Empty(cut);
    }

    [Fact]
    public void Gmlan_OnlyWellFormedFrames_AreRecords()
    {
        var codes = ModuleDtcParsers.ParseGmlan(new List<byte[]>
        {
            new byte[] { 0x81, 0x01, 0x33, 0x00, 0x08 },   // record
            new byte[] { 0x81, 0x00, 0x00, 0x00, 0x00 },   // end marker
            new byte[] { 0x81, 0x01, 0x34 },                 // too short
            new byte[] { 0x82, 0x01, 0x35, 0x00, 0x08 }     // not a DTC frame
        });

        var code = Assert.Single(codes);
        Assert.Equal("P0133", code.Code);
        Assert.Equal("013300", code.RawBytesHex);
    }

    [Theory]
    [InlineData(0x01, 0x33, "P0133")]
    [InlineData(0x41, 0x00, "C0100")]
    [InlineData(0x81, 0x23, "B0123")]
    [InlineData(0xC1, 0xFF, "U01FF")]
    [InlineData(0x19, 0x0F, "P190F")]
    public void FormatCode_UsesTheSaeLetters(byte high, byte low, string expected) =>
        Assert.Equal(expected, ModuleDtcParsers.FormatCode(high, low));
}
