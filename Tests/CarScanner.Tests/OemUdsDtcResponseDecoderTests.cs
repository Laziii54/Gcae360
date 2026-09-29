using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;

namespace CarScanner.Tests;

public sealed class OemUdsDtcResponseDecoderTests
{
    [Fact]
    public void EmptyPositiveResponse_IsSuccessfulNoDtcResult()
    {
        var result = OemUdsDtcResponseDecoder.Decode(
            Command(),
            new UdsResponse(UdsResponseKind.Positive, 0x59, [0x02, 0xFF]));

        Assert.Equal(OemDtcReadState.Available, result.State);
        Assert.Equal(0xFF, result.StatusAvailabilityMask);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void ThreeByteRecordsAndStatusBits_AreDecodedExactly()
    {
        var result = OemUdsDtcResponseDecoder.Decode(
            Command(),
            new UdsResponse(UdsResponseKind.Positive, 0x59,
                [0x02, 0xFF, 0x12, 0x34, 0x56, 0x0D, 0xAB, 0xCD, 0xEF, 0x80]));

        Assert.Equal(OemDtcReadState.Available, result.State);
        Assert.Collection(result.Records,
            first =>
            {
                Assert.Equal("123456", first.RawCodeHex);
                Assert.True(first.IsCurrent);
                Assert.True(first.IsPending);
                Assert.True(first.IsConfirmed);
                Assert.False(first.RequestsWarningIndicator);
            },
            second =>
            {
                Assert.Equal("ABCDEF", second.RawCodeHex);
                Assert.True(second.RequestsWarningIndicator);
                Assert.False(second.IsCurrent);
            });
    }

    [Fact]
    public void IncompleteRecord_IsRejectedWithoutPartialCodes()
    {
        var result = OemUdsDtcResponseDecoder.Decode(
            Command(),
            new UdsResponse(UdsResponseKind.Positive, 0x59, [0x02, 0xFF, 0x12, 0x34]));

        Assert.Equal(OemDtcReadState.InvalidResponse, result.State);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void AllZeroDtcRecord_IsRejected()
    {
        var result = OemUdsDtcResponseDecoder.Decode(
            Command(),
            new UdsResponse(UdsResponseKind.Positive, 0x59, [0x02, 0xFF, 0x00, 0x00, 0x00, 0x08]));

        Assert.Equal(OemDtcReadState.InvalidResponse, result.State);
        Assert.Empty(result.Records);
    }

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x31)]
    public void UnsupportedNegativeResponses_AreNotReportedAsNoFaults(byte nrc)
    {
        var result = OemUdsDtcResponseDecoder.Decode(
            Command(),
            new UdsResponse(UdsResponseKind.Negative, 0x19, [nrc], nrc, "rejected"));

        Assert.Equal(OemDtcReadState.NotSupported, result.State);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void DifferentSubFunction_IsRejected()
    {
        var result = OemUdsDtcResponseDecoder.Decode(
            Command(),
            new UdsResponse(UdsResponseKind.Positive, 0x59, [0x0A, 0xFF]));

        Assert.Equal(OemDtcReadState.InvalidResponse, result.State);
    }

    [Fact]
    public void ReportSupportedDtcSubFunction_UsesItsTwoByteRequest()
    {
        var command = Command();
        command.RequestHex = "19 0A";

        var result = OemUdsDtcResponseDecoder.Decode(
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x59, [0x0A, 0xFF, 0x12, 0x34, 0x56, 0x08]));

        Assert.Equal(OemDtcReadState.Available, result.State);
        Assert.Single(result.Records);
    }

    private static OemReadOnlyCommandDefinition Command() => new()
    {
        Id = "dtc.by-status",
        Name = "Read DTC by status mask",
        CapabilityCode = "DTC.READ",
        RequestHex = "19 02 FF"
    };
}
