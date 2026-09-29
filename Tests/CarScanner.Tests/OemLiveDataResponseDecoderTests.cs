using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;

namespace CarScanner.Tests;

public sealed class OemLiveDataResponseDecoderTests
{
    [Fact]
    public void MatchingDid_DecodesReviewedNumericFields()
    {
        var (definition, command) = Definition();

        var result = OemLiveDataResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x62,
                [0x43, 0x69, 0x0E, 0x74, 0xFB]));

        Assert.Equal(OemLiveValueState.Available, result.State);
        Assert.Collection(result.Values,
            voltage =>
            {
                Assert.Equal("PACK_VOLTAGE", voltage.FieldKey);
                Assert.Equal(370.0, voltage.Value, 3);
            },
            temperature =>
            {
                Assert.Equal("PACK_TEMPERATURE", temperature.FieldKey);
                Assert.Equal(-5.0, temperature.Value, 3);
            });
    }

    [Fact]
    public void DifferentDid_IsRejectedWithoutValues()
    {
        var (definition, command) = Definition();

        var result = OemLiveDataResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x62,
                [0x43, 0x68, 0x0E, 0x74, 0xFB]));

        Assert.Equal(OemLiveValueState.InvalidResponse, result.State);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void ShortPayload_IsRejectedWithoutPartialValues()
    {
        var (definition, command) = Definition();

        var result = OemLiveDataResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x62,
                [0x43, 0x69, 0x0E]));

        Assert.Equal(OemLiveValueState.InvalidResponse, result.State);
        Assert.Empty(result.Values);
    }

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x31)]
    public void UnsupportedNrc_IsNotShownAsZero(byte nrc)
    {
        var (definition, command) = Definition();

        var result = OemLiveDataResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Negative, 0x22, [nrc], nrc, "unsupported"));

        Assert.Equal(OemLiveValueState.NotSupported, result.State);
        Assert.Empty(result.Values);
    }

    private static (OemEcuDefinition Definition, OemReadOnlyCommandDefinition Command) Definition()
    {
        var command = new OemReadOnlyCommandDefinition
        {
            Id = "data.battery",
            Name = "Battery data",
            CapabilityCode = "DATA.READ",
            RequestHex = "22 43 69",
            ResponseFields = new()
            {
                new OemResponseFieldDefinition
                {
                    Key = "PACK_VOLTAGE",
                    Label = "Pack Voltage",
                    Category = "Battery / BMS",
                    ByteOffset = 0,
                    ByteLength = 2,
                    Encoding = "UINT_BE",
                    Scale = 0.1,
                    Unit = "V"
                },
                new OemResponseFieldDefinition
                {
                    Key = "PACK_TEMPERATURE",
                    Label = "Pack Temperature",
                    Category = "Temperature",
                    ByteOffset = 2,
                    ByteLength = 1,
                    Encoding = "INT_BE",
                    Unit = "°C"
                }
            }
        };
        var definition = new OemEcuDefinition
        {
            DefinitionId = "test.bms",
            EcuId = "BMS",
            EcuName = "Battery Management System",
            Evidence = new OemDefinitionEvidence { SourceReference = "reviewed-capture" },
            ReadOnlyCommands = new() { command }
        };
        return (definition, command);
    }
}
