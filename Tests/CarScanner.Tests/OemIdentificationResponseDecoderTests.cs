using System.Text;
using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;

namespace CarScanner.Tests;

public sealed class OemIdentificationResponseDecoderTests
{
    [Fact]
    public void MatchingDidAndAsciiField_DecodesExactValue()
    {
        var (definition, command) = DefinitionWithField("ASCII", 17);
        var payload = new byte[] { 0xF1, 0x90 }
            .Concat(Encoding.ASCII.GetBytes("1G1RA6E43EU000001"))
            .ToArray();

        var values = OemIdentificationResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x62, payload));

        var value = Assert.Single(values);
        Assert.Equal(OemIdentificationValueState.Available, value.State);
        Assert.Equal("1G1RA6E43EU000001", value.Value);
    }

    [Fact]
    public void DifferentDid_IsRejectedInsteadOfDecoded()
    {
        var (definition, command) = DefinitionWithField("ASCII", 4);

        var value = Assert.Single(OemIdentificationResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x62, [0xF1, 0x91, 0x41, 0x42, 0x43, 0x44])));

        Assert.Equal(OemIdentificationValueState.InvalidResponse, value.State);
        Assert.Equal("—", value.DisplayValue);
    }

    [Fact]
    public void ShortPayload_IsUnavailableAndNeverThrows()
    {
        var (definition, command) = DefinitionWithField("ASCII", 17);

        var value = Assert.Single(OemIdentificationResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x62, [0xF1, 0x90, 0x41])));

        Assert.Equal(OemIdentificationValueState.InvalidResponse, value.State);
        Assert.Equal("—", value.DisplayValue);
    }

    [Fact]
    public void UnsignedBigEndian_UsesReviewedScaleAndOffset()
    {
        var (definition, command) = DefinitionWithField("UINT_BE", 2);
        command.ResponseFields[0].Scale = 0.5;
        command.ResponseFields[0].ValueOffset = 1;
        command.ResponseFields[0].Unit = "rev";

        var value = Assert.Single(OemIdentificationResponseDecoder.Decode(
            definition,
            command,
            new UdsResponse(UdsResponseKind.Positive, 0x62, [0xF1, 0x90, 0x00, 0x0A])));

        Assert.Equal("6", value.Value);
        Assert.Equal("6 rev", value.DisplayValue);
    }

    private static (OemEcuDefinition Definition, OemReadOnlyCommandDefinition Command)
        DefinitionWithField(string encoding, int length)
    {
        var command = new OemReadOnlyCommandDefinition
        {
            Id = "identity.vin",
            Name = "Read identity",
            CapabilityCode = "IDENTIFICATION.READ",
            RequestHex = "22 F1 90",
            DiscoveryProbe = true,
            ResponseFields = new()
            {
                new OemResponseFieldDefinition
                {
                    Key = "VIN",
                    Label = "VIN",
                    ByteOffset = 0,
                    ByteLength = length,
                    Encoding = encoding
                }
            }
        };
        var definition = new OemEcuDefinition
        {
            DefinitionId = "test.ecm",
            EcuId = "ECM",
            EcuName = "Engine Control Module",
            Evidence = new OemDefinitionEvidence { SourceReference = "reviewed-capture" },
            ReadOnlyCommands = new() { command }
        };
        return (definition, command);
    }
}
