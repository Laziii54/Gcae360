using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;

namespace CarScanner.Tests;

public sealed class OemCapabilityResponseEvaluatorTests
{
    private static readonly OemReadOnlyCommandDefinition Command = new()
    {
        Id = "identity.vin",
        Name = "Read VIN",
        CapabilityCode = "IDENTIFICATION.READ",
        RequestHex = "22 F1 90"
    };

    [Fact]
    public void PositiveResponse_IsAvailable()
    {
        var result = OemCapabilityResponseEvaluator.Evaluate(
            Command,
            new UdsResponse(UdsResponseKind.Positive, 0x62, [0xF1, 0x90]),
            21);

        Assert.Equal(OemCapabilityAvailability.Available, result.Availability);
        Assert.Equal(21, result.ElapsedMilliseconds);
    }

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x31)]
    public void UnsupportedNegativeResponses_AreNotSupported(byte nrc)
    {
        var result = OemCapabilityResponseEvaluator.Evaluate(
            Command,
            new UdsResponse(UdsResponseKind.Negative, 0x22, [nrc], nrc, "Rejected"),
            30);

        Assert.Equal(OemCapabilityAvailability.NotSupported, result.Availability);
    }

    [Fact]
    public void ConditionsNotCorrect_IsConditionalRatherThanAvailable()
    {
        var result = OemCapabilityResponseEvaluator.Evaluate(
            Command,
            new UdsResponse(UdsResponseKind.Negative, 0x22, [0x22], 0x22, "Conditions not correct"),
            30);

        Assert.Equal(OemCapabilityAvailability.Conditional, result.Availability);
    }

    [Fact]
    public void Timeout_IsNeverReportedAsSupported()
    {
        var result = OemCapabilityResponseEvaluator.Evaluate(
            Command,
            new UdsResponse(UdsResponseKind.Timeout, 0x22, Array.Empty<byte>(), Message: "Timeout"),
            1500);

        Assert.Equal(OemCapabilityAvailability.NoResponse, result.Availability);
    }
}
