using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests.Integration;

// Synthetic parser-to-readback tests. These are NOT evidence for ECU commands.
public sealed class VoltOilLifeIntegrationTests
{
    private static readonly OemOperationDefinition Operation = new()
    {
        Type = OemOperationType.Service, Name = "Oil life reset",
        Steps = [new() { Role = "VERIFY", RequestHex = "22119F", ExpectedResponsePayloadHex = "119FFF" }]
    };

    private static ServiceMetricReading Reading(double value, int rx = 0x7E8) =>
        new("oil", ServiceMetricKind.OilLifePercent, "Oil Life", value, "%", "", "synthetic",
            0x7E0, 0x119F, rx);

    [Theory]
    [InlineData("7E8 04 62 11 9F FF 00 00 00", true)]
    [InlineData("7E8 04 62 11 9F 6B 00 00 00", false)]
    [InlineData("7E8 04 62 11 9F F2 00 00 00", false)]
    [InlineData("7E9 04 62 11 9F FF 00 00 00", false)]
    [InlineData("7E8 03 7F 22 31 00 00 00 00", false)]
    [InlineData("NO DATA", false)]
    [InlineData("", false)]
    public void ReadbackRequiresValidExpectedValue(string raw, bool expected)
    {
        var readings = VoltOilLifeDecoder.TryDecodeFromRaw(raw, out var value)
            ? new[] { Reading(value) } : Array.Empty<ServiceMetricReading>();
        Assert.Equal(expected, new ServiceResetVerificationService().Verify(Operation,
            [Reading(42)], readings).IsValueVerified);
    }

    [Fact]
    public void OtherEcuCannotConfirmReset() => Assert.False(
        new ServiceResetVerificationService().Verify(Operation, [Reading(42)], [Reading(100, 0x7E9)]).IsValueVerified);

    [Fact]
    public void OperationWithoutVerifyDidCannotConfirmReset() => Assert.False(
        new ServiceResetVerificationService().Verify(new() { Type = OemOperationType.Service, Name = "Oil" },
            [Reading(42)], [Reading(100)]).IsValueVerified);

    [Fact]
    public void FreshHundredPercentCanConfirmAlreadyResetValue() => Assert.True(
        new ServiceResetVerificationService().Verify(Operation, [Reading(100)], [Reading(100)]).IsValueVerified);

    [Fact]
    public void TraceIsBoundedAndExportable()
    {
        for (var i = 0; i < 220; i++) ServiceDiagnosticTrace.Record("synthetic", "22119F", "NO DATA", "Rejected", 0x7E0, 0x7E8);
        using var json = System.Text.Json.JsonDocument.Parse(ServiceDiagnosticTrace.ExportJson());
        Assert.Equal(200, json.RootElement.GetProperty("Entries").GetArrayLength());
    }

    [Theory]
    [InlineData("22 F1 90")]
    [InlineData("1A90")]
    [InlineData("09 02")]
    public void ExportRedactsVehicleIdentifierResponses(string request)
    {
        var context = $"privacy-{Guid.NewGuid():N}";
        ServiceDiagnosticTrace.Record(context, request, "62F190WDD12345678901234", "Positive", 0x7E0, 0x7E8);

        var entry = Assert.Single(ServiceDiagnosticTrace.SnapshotForExport(), item => item.Context == context);

        Assert.Equal("[redacted vehicle identifier]", entry.Response);
        Assert.Equal("Redacted", entry.Result);
    }

    [Fact]
    public void ExportPreservesNonIdentifierDiagnosticResponses()
    {
        var context = $"privacy-{Guid.NewGuid():N}";
        ServiceDiagnosticTrace.Record(context, "22119F", "62119FFF", "Positive", 0x7E0, 0x7E8);

        var entry = Assert.Single(ServiceDiagnosticTrace.SnapshotForExport(), item => item.Context == context);

        Assert.Equal("62119FFF", entry.Response);
        Assert.Equal("Positive", entry.Result);
    }
}
