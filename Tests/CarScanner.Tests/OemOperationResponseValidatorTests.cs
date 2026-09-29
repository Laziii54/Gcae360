using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;

namespace CarScanner.Tests;

public sealed class OemOperationResponseValidatorTests
{
    [Theory]
    [InlineData(UdsResponseKind.Timeout)]
    [InlineData(UdsResponseKind.Malformed)]
    [InlineData(UdsResponseKind.Unsupported)]
    public void NonPositiveOutcomeCannotSucceed(UdsResponseKind kind) =>
        Assert.False(OemOperationResponseValidator.Validate([0x22, 0x11, 0x9F],
            new UdsResponse(kind, 0, [])).Success);

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x22)]
    [InlineData(0x31)]
    [InlineData(0x33)]
    [InlineData(0x78)]
    public void NegativeCodesCannotSucceed(byte nrc) =>
        Assert.False(OemOperationResponseValidator.Validate([0x22, 0x11, 0x9F],
            UdsResponseParser.Parse([0x7F, 0x22, nrc])).Success);
    [Fact]
    public void AcceptsMatchingWriteDidEcho()
    {
        var result = OemOperationResponseValidator.Validate(
            [0x2E, 0x12, 0x34, 0x01],
            new UdsResponse(UdsResponseKind.Positive, 0x6E, [0x12, 0x34]));

        Assert.True(result.Success);
    }

    [Fact]
    public void RejectsMismatchedWriteDidEcho()
    {
        var result = OemOperationResponseValidator.Validate(
            [0x2E, 0x12, 0x34, 0x01],
            new UdsResponse(UdsResponseKind.Positive, 0x6E, [0x12, 0x35]));

        Assert.False(result.Success);
    }

    [Fact]
    public void AcceptsMatchingRoutineEcho()
    {
        var result = OemOperationResponseValidator.Validate(
            [0x31, 0x01, 0xAB, 0xCD],
            new UdsResponse(UdsResponseKind.Positive, 0x71, [0x01, 0xAB, 0xCD]));

        Assert.True(result.Success);
    }

    [Fact]
    public void RequiresReviewedValueDuringVerification()
    {
        var accepted = OemOperationResponseValidator.Validate(
            [0x22, 0x12, 0x34],
            new UdsResponse(UdsResponseKind.Positive, 0x62, [0x12, 0x34, 0x01]),
            "123401");
        var rejected = OemOperationResponseValidator.Validate(
            [0x22, 0x12, 0x34],
            new UdsResponse(UdsResponseKind.Positive, 0x62, [0x12, 0x34, 0x00]),
            "123401");

        Assert.True(accepted.Success);
        Assert.False(rejected.Success);
    }

    [Fact]
    public void RejectsNegativeResponse()
    {
        var result = OemOperationResponseValidator.Validate(
            [0x31, 0x01, 0xAB, 0xCD],
            new UdsResponse(UdsResponseKind.Negative, 0x31, [0x22], 0x22, "Conditions not correct"));

        Assert.False(result.Success);
        Assert.Contains("0x22", result.Detail);
    }
}
