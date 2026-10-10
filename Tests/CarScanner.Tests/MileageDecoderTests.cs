using CarScanner.Services.Mileage;

namespace CarScanner.Tests;

public sealed class StandardMileageDecoderTests
{
    [Fact]
    public void OdometerPid_A6_IsDecodedAt_TenthOfAKilometre()
    {
        var result = StandardMileageDecoder.Decode(0xA6, "41 A6 00 0F 42 40"); // 1 000 000 × 0.1
        Assert.True(result.IsValue);
        Assert.Equal(MileageMeasurement.TotalOdometer, result.Measurement);
        Assert.Equal(100_000.0, result.Value!.Value, 6);
        Assert.Equal("km", result.Unit);

        Assert.Equal(123.4, StandardMileageDecoder.Decode(0xA6, "41 A6 00 00 04 D2").Value!.Value, 6);
        Assert.Equal(0.0, StandardMileageDecoder.Decode(0xA6, "41 A6 00 00 00 00").Value!.Value, 6);
    }

    [Fact]
    public void A6_WithAllBitsSet_IsNotAvailable_NotAHugeMileage()
    {
        var result = StandardMileageDecoder.Decode(0xA6, "41 A6 FF FF FF FF");
        Assert.False(result.IsValue);
        Assert.Equal(StandardReadKind.Malformed, result.Kind);
    }

    [Theory]
    [InlineData("41 A6 00 0F 42")]      // one byte short
    [InlineData("41 A6")]                // no data
    [InlineData("41")]
    [InlineData("A6 00 0F 42 40")]       // no positive-response byte
    [InlineData("41 A7 00 0F 42 40")]    // answer to a different PID
    [InlineData("41 0C 1A F8")]
    public void Truncated_Or_WrongReplies_AreMalformed(string raw)
    {
        var result = StandardMileageDecoder.Decode(0xA6, raw);
        Assert.False(result.IsValue);
        Assert.Equal(StandardReadKind.Malformed, result.Kind);
    }

    [Theory]
    [InlineData("", StandardReadKind.NoResponse)]
    [InlineData("   ", StandardReadKind.NoResponse)]
    [InlineData("NO DATA", StandardReadKind.NoData)]
    [InlineData("UNABLE TO CONNECT", StandardReadKind.AdapterError)]
    [InlineData("CAN ERROR", StandardReadKind.AdapterError)]
    [InlineData("BUS BUSY", StandardReadKind.AdapterError)]
    [InlineData("?", StandardReadKind.AdapterError)]
    [InlineData("7F 01 12", StandardReadKind.NegativeResponse)]
    [InlineData("7F 01 11", StandardReadKind.NegativeResponse)]
    [InlineData("\u0001@@##\u0002", StandardReadKind.Malformed)]
    public void NoAnswer_Negative_AndGarbage_NeverProduceAValue(string raw, StandardReadKind expected)
    {
        var result = StandardMileageDecoder.Decode(0xA6, raw);
        Assert.Equal(expected, result.Kind);
        Assert.False(result.IsValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ValueBytesThatLookLikeANegativeReply_DoNotHideAValidAnswer()
    {
        var result = StandardMileageDecoder.Decode(0x31, "41 31 7F 01");
        Assert.True(result.IsValue);
        Assert.Equal(0x7F01, result.Value!.Value);
    }

    [Fact]
    public void SeveralEcusAnswering_FirstReplyIsUsed()
    {
        // Two ECUs answer with different values: the first complete reply is used, the second is ignored.
        var result = StandardMileageDecoder.Decode(0xA6, "41 A6 00 0F 42 40 41 A6 00 00 04 D2");
        Assert.True(result.IsValue);
        Assert.Equal(100_000.0, result.Value!.Value, 6);
    }

    [Theory]
    [InlineData(0x31, MileageMeasurement.DistanceSinceDtcClear, "km")]
    [InlineData(0x21, MileageMeasurement.DistanceWithMilOn, "km")]
    [InlineData(0x4E, MileageMeasurement.MinutesSinceDiagnosticEvent, "min")]
    [InlineData(0x4D, MileageMeasurement.MinutesSinceDiagnosticEvent, "min")]
    public void ContextPids_AreLabelledWithWhatTheyAre_NotAsTheOdometer(int pid, MileageMeasurement measurement, string unit)
    {
        var result = StandardMileageDecoder.Decode(pid, $"41 {pid:X2} 00 64");
        Assert.True(result.IsValue);
        Assert.Equal(100, result.Value!.Value);
        Assert.Equal(measurement, result.Measurement);
        Assert.NotEqual(MileageMeasurement.TotalOdometer, result.Measurement);
        Assert.Equal(unit, result.Unit);
    }

    [Fact]
    public void ContextCounter_AtItsMaximum_IsFlaggedAsSaturated()
    {
        var result = StandardMileageDecoder.Decode(0x31, "41 31 FF FF");
        Assert.True(result.IsValue);
        Assert.True(result.Saturated);
        Assert.Equal(65_535, result.Value!.Value);
        Assert.Contains("at least", result.Detail);
    }

    [Fact]
    public void OnlyDocumentedPids_AreDecoded()
    {
        Assert.False(StandardMileageDecoder.IsSupportedPid(0x0C));
        var result = StandardMileageDecoder.Decode(0x0C, "41 0C 1A F8");
        Assert.False(result.IsValue);
        Assert.Equal(StandardReadKind.Malformed, result.Kind);
    }

    [Fact]
    public void PidUnits_AreNeverMixed()
    {
        Assert.Equal("km", StandardMileageDecoder.UnitOf(0xA6));
        Assert.Equal("km", StandardMileageDecoder.UnitOf(0x31));
        Assert.Equal("min", StandardMileageDecoder.UnitOf(0x4D));
        Assert.Equal(MileageMeasurement.TotalOdometer, StandardMileageDecoder.MeasurementOf(0xA6));
        Assert.DoesNotContain(0xA6, StandardMileageDecoder.ContextPids);
    }
}

public sealed class OdometerResponseDecoderTests
{
    private static OdometerSignalSpec Spec(int offset = 0, int length = 24, double mul = 1, double div = 1, double add = 0, bool miles = false, bool meters = false) =>
        new(offset, length, mul, div, add, miles, meters);

    [Fact]
    public void ValidSingleFrameReply_IsDecoded()
    {
        var payload = OdometerResponseDecoder.ExtractPayload("7E8 06 62 F4 0D 01 86 A0", 0x22, 0xF40D, 0x7E8, out var failure);
        Assert.Equal(OdometerDecodeFailure.None, failure);
        Assert.Equal(new byte[] { 0x01, 0x86, 0xA0 }, payload);

        var result = OdometerResponseDecoder.Decode(Spec(), payload!);
        Assert.True(result.IsValue);
        Assert.Equal(100_000, result.Km!.Value, 6);
        Assert.Equal("km", result.Unit);
    }

    [Fact]
    public void MultiFrameReply_IsReassembled()
    {
        // 9-byte message: 62 F4 0D 00 00 01 86 A0 00
        var payload = OdometerResponseDecoder.ExtractPayload("7E8 10 09 62 F4 0D 00 00 01 7E8 21 86 A0 00", 0x22, 0xF40D, 0x7E8, out _);
        Assert.NotNull(payload);
        var result = OdometerResponseDecoder.Decode(Spec(offset: 16, length: 24), payload!);
        Assert.True(result.IsValue);
        Assert.Equal(100_000, result.Km!.Value, 6);
    }

    [Fact]
    public void Miles_AreConvertedToKilometres_AndKeepTheirUnit()
    {
        var result = OdometerResponseDecoder.Decode(Spec(miles: true), new byte[] { 0x01, 0x86, 0xA0 });
        Assert.True(result.IsValue);
        Assert.Equal("mi", result.Unit);
        Assert.Equal(100_000, result.RawValue!.Value, 6);
        Assert.Equal(160_934.4, result.Km!.Value, 3);
    }

    [Fact]
    public void Metres_AreShownAsKilometres()
    {
        var result = OdometerResponseDecoder.Decode(Spec(length: 32, meters: true), new byte[] { 0x05, 0xF5, 0xE1, 0x00 }); // 100 000 000 m
        Assert.True(result.IsValue);
        Assert.Equal("km", result.Unit);
        Assert.Equal(100_000, result.Km!.Value, 6);
        Assert.Equal(100_000, result.RawValue!.Value, 6);
    }

    [Fact]
    public void ScalingAndOffset_AreApplied()
    {
        // raw 1 000 000 × 0.1 + 5
        var result = OdometerResponseDecoder.Decode(Spec(mul: 1, div: 10, add: 5), new byte[] { 0x0F, 0x42, 0x40 });
        Assert.Equal(100_005, result.Km!.Value, 6);

        // 1/64 km per bit
        var gm = OdometerResponseDecoder.Decode(Spec(length: 32, mul: 1, div: 64), new byte[] { 0x00, 0x62, 0x4A, 0x00 });
        Assert.Equal(0x624A00 / 64.0, gm.Km!.Value, 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void InvalidScaling_IsRejected(double div)
    {
        var result = OdometerResponseDecoder.Decode(Spec(div: div), new byte[] { 1, 2, 3 });
        Assert.False(result.IsValue);
        Assert.Equal(OdometerDecodeFailure.BadScaling, result.Failure);

        var bad = OdometerResponseDecoder.Decode(Spec(mul: double.PositiveInfinity), new byte[] { 1, 2, 3 });
        Assert.Equal(OdometerDecodeFailure.BadScaling, bad.Failure);
    }

    [Fact]
    public void TooShortData_IsRejected()
    {
        var result = OdometerResponseDecoder.Decode(Spec(length: 24), new byte[] { 0x01, 0x86 });
        Assert.Equal(OdometerDecodeFailure.TooShort, result.Failure);
        Assert.False(result.IsValue);

        Assert.Equal(OdometerDecodeFailure.TooShort, OdometerResponseDecoder.Decode(Spec(offset: 16, length: 16), new byte[] { 1, 2, 3 }).Failure);
        Assert.Equal(OdometerDecodeFailure.BadScaling, OdometerResponseDecoder.Decode(Spec(length: 0), new byte[] { 1 }).Failure);
        Assert.Equal(OdometerDecodeFailure.BadScaling, OdometerResponseDecoder.Decode(Spec(offset: -1), new byte[] { 1, 2, 3 }).Failure);
    }

    [Fact]
    public void AllBitsSet_IsNotAvailable()
    {
        var result = OdometerResponseDecoder.Decode(Spec(), new byte[] { 0xFF, 0xFF, 0xFF });
        Assert.Equal(OdometerDecodeFailure.NotAvailable, result.Failure);
        Assert.False(result.IsValue);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00 })]               // 0 km
    public void ZeroKilometres_IsNotAcceptedAsAReading(byte[] data)
    {
        var result = OdometerResponseDecoder.Decode(Spec(), data);
        Assert.False(result.IsValue);
        Assert.Equal(OdometerDecodeFailure.Implausible, result.Failure);
    }

    [Fact]
    public void ValueBeyondTheBelievableRange_IsImplausible_ButKeepsTheNumberForReview()
    {
        var result = OdometerResponseDecoder.Decode(Spec(length: 32), new byte[] { 0x00, 0x30, 0xD4, 0x00 }); // 3 200 000
        Assert.False(result.IsValue);
        Assert.Equal(OdometerDecodeFailure.Implausible, result.Failure);
        Assert.Equal(3_200_000, result.Km!.Value, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyReply_IsNoReply(string raw)
    {
        Assert.Null(OdometerResponseDecoder.ExtractPayload(raw, 0x22, 0xF40D, 0x7E8, out var failure));
        Assert.Equal(OdometerDecodeFailure.NoReply, failure);
    }

    [Fact]
    public void NegativeReply_IsReportedAsSuch()
    {
        Assert.Null(OdometerResponseDecoder.ExtractPayload("7E8 03 7F 22 31", 0x22, 0xF40D, 0x7E8, out var failure));
        Assert.Equal(OdometerDecodeFailure.Negative, failure);
    }

    [Fact]
    public void ReplyForAnotherIdentifier_IsNotAccepted()
    {
        Assert.Null(OdometerResponseDecoder.ExtractPayload("7E8 06 62 F4 0E 01 86 A0", 0x22, 0xF40D, 0x7E8, out var failure));
        Assert.Equal(OdometerDecodeFailure.NotPositiveForRequest, failure);
    }

    [Fact]
    public void ReplyForAnotherService_IsNotAccepted()
    {
        Assert.Null(OdometerResponseDecoder.ExtractPayload("7E8 06 50 F4 0D 01 86 A0", 0x22, 0xF40D, 0x7E8, out _));
    }

    [Fact]
    public void Garbage_NeverThrows_AndNeverYieldsAPayload()
    {
        var garbage = new string(Enumerable.Range(1, 200).Select(i => (char)(i % 250 + 1)).ToArray());
        Assert.Null(OdometerResponseDecoder.ExtractPayload(garbage, 0x22, 0xF40D, 0x7E8, out _));
        Assert.Null(OdometerResponseDecoder.ExtractPayload("7E8 ZZ ?? 62 F4", 0x22, 0xF40D, 0x7E8, out _));
        Assert.Null(OdometerResponseDecoder.ExtractPayload(null, 0x22, 0xF40D, 0x7E8, out var failure));
        Assert.Equal(OdometerDecodeFailure.NoReply, failure);
    }

    [Fact]
    public void Service21_UsesAOneByteIdentifier()
    {
        var payload = OdometerResponseDecoder.ExtractPayload("7E8 05 61 A0 01 86 A0", 0x21, 0xA0, 0x7E8, out var failure);
        Assert.Equal(OdometerDecodeFailure.None, failure);
        Assert.Equal(new byte[] { 0x01, 0x86, 0xA0 }, payload);
    }
}
