using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;

namespace CarScanner.Tests;

public sealed class OemFreezeFrameResponseDecoderTests
{
    [Fact]
    public void ReviewedSnapshotDids_AreDecodedWithExactLayouts()
    {
        var result = OemFreezeFrameResponseDecoder.Decode(
            Command(),
            "123456",
            new UdsResponse(UdsResponseKind.Positive, 0x59,
                [0x04, 0x12, 0x34, 0x56, 0x08, 0x01, 0x02,
                 0xF4, 0x0D, 0x04, 0xD2,
                 0xF4, 0x0C, 0x2E, 0xE0]));

        Assert.Equal(OemFreezeFrameReadState.Available, result.State);
        var record = Assert.Single(result.Records);
        Assert.Equal((byte)1, record.SnapshotRecordNumber);
        Assert.Collection(record.Values,
            speed =>
            {
                Assert.Equal("VEHICLE_SPEED", speed.Key);
                Assert.Equal("12.34 km/h", speed.DisplayValue);
            },
            rpm =>
            {
                Assert.Equal("ENGINE_RPM", rpm.Key);
                Assert.Equal("3000 rpm", rpm.DisplayValue);
            });
    }

    [Fact]
    public void SnapshotForDifferentDtc_IsRejected()
    {
        var result = OemFreezeFrameResponseDecoder.Decode(
            Command(),
            "123456",
            new UdsResponse(UdsResponseKind.Positive, 0x59,
                [0x04, 0x12, 0x34, 0x57, 0x08]));

        Assert.Equal(OemFreezeFrameReadState.InvalidResponse, result.State);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void UnknownDid_IsRejectedRatherThanLengthGuessed()
    {
        var result = OemFreezeFrameResponseDecoder.Decode(
            Command(),
            "123456",
            new UdsResponse(UdsResponseKind.Positive, 0x59,
                [0x04, 0x12, 0x34, 0x56, 0x08, 0x01, 0x01, 0x99, 0x99, 0x01]));

        Assert.Equal(OemFreezeFrameReadState.InvalidResponse, result.State);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void TruncatedDidData_IsRejectedWithoutPartialSnapshot()
    {
        var result = OemFreezeFrameResponseDecoder.Decode(
            Command(),
            "123456",
            new UdsResponse(UdsResponseKind.Positive, 0x59,
                [0x04, 0x12, 0x34, 0x56, 0x08, 0x01, 0x01, 0xF4, 0x0D, 0x04]));

        Assert.Equal(OemFreezeFrameReadState.InvalidResponse, result.State);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void UnsupportedNrc_IsNotReportedAsEmptySnapshot()
    {
        var result = OemFreezeFrameResponseDecoder.Decode(
            Command(),
            "123456",
            new UdsResponse(UdsResponseKind.Negative, 0x19, [0x31], 0x31, "out of range"));

        Assert.Equal(OemFreezeFrameReadState.NotSupported, result.State);
        Assert.Empty(result.Records);
    }

    private static OemReadOnlyCommandDefinition Command() => new()
    {
        Id = "freeze.snapshot",
        Name = "Read DTC snapshot",
        CapabilityCode = "FREEZEFRAME.READ",
        RequestHex = "19 04 00 00 00 FF",
        ProbeDuringCapabilityDetection = false,
        SnapshotDids = new()
        {
            new OemSnapshotDidDefinition
            {
                DataIdentifier = 0xF40D,
                DataLength = 2,
                Fields = new()
                {
                    new OemResponseFieldDefinition
                    {
                        Key = "VEHICLE_SPEED",
                        Label = "Vehicle Speed",
                        ByteOffset = 0,
                        ByteLength = 2,
                        Encoding = "UINT_BE",
                        Scale = 0.01,
                        Unit = "km/h"
                    }
                }
            },
            new OemSnapshotDidDefinition
            {
                DataIdentifier = 0xF40C,
                DataLength = 2,
                Fields = new()
                {
                    new OemResponseFieldDefinition
                    {
                        Key = "ENGINE_RPM",
                        Label = "Engine RPM",
                        ByteOffset = 0,
                        ByteLength = 2,
                        Encoding = "UINT_BE",
                        Scale = 0.25,
                        Unit = "rpm"
                    }
                }
            }
        }
    };
}
