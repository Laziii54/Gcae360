using System.Text.Json;
using System.Security.Cryptography;
using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

public sealed class OemDefinitionPackageParserTests
{
    [Fact]
    public void BundledExecutableCatalog_ContainsReviewedHyundaiSonataAbsDefinition()
    {
        var rawDirectory = RawResourcesDirectory();
        var path = Path.Combine(rawDirectory, "oem_definitions.json");

        var catalog = OemDefinitionPackageParser.ParseAndValidate(File.ReadAllText(path));

        Assert.Equal(2, catalog.SchemaVersion);
        Assert.Equal(2, catalog.CatalogVersion);
        var definition = Assert.Single(catalog.Definitions);
        Assert.Equal("hyundai.sonata-dn8.2020-2023.abs", definition.DefinitionId);
        Assert.Equal("Sonata", definition.VehicleProfile);
        Assert.Equal("ABS", definition.EcuId);
        Assert.Equal(0x7D1, definition.Transport.TxCanId);
        Assert.Equal(0x7D9, definition.Transport.RxCanId);

        var command = Assert.Single(definition.ReadOnlyCommands);
        Assert.Equal("IDENTIFICATION.READ", command.CapabilityCode);
        Assert.Equal(new byte[] { 0x22, 0xF1, 0x00 },
            OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex));
        Assert.DoesNotContain(definition.ReadOnlyCommands,
            item => item.CapabilityCode.Equals("DTC.READ", StringComparison.OrdinalIgnoreCase));

        var evidencePath = Path.Combine(rawDirectory, definition.Evidence.SourceReference.Replace('/', Path.DirectorySeparatorChar));
        using var stream = File.OpenRead(evidencePath);
        Assert.Equal(definition.Evidence.SourceSha256, Convert.ToHexString(SHA256.HashData(stream)));
    }

    [Fact]
    public void ManufacturerCatalog_AuthorizesBundledHyundaiSonataAbsDefinition()
    {
        var rawDirectory = RawResourcesDirectory();
        var manufacturers = ManufacturerDefinitionCatalogParser.ParseAndValidate(
            File.ReadAllText(Path.Combine(rawDirectory, "manufacturer_definitions.json")));
        var definitions = OemDefinitionPackageParser.ParseAndValidate(
            File.ReadAllText(Path.Combine(rawDirectory, "oem_definitions.json")));

        var definition = Assert.Single(definitions.Definitions);
        Assert.True(ManufacturerDefinitionResolutionEngine.Authorizes(
            manufacturers.Manufacturers,
            definition));
    }

    [Fact]
    public void ReviewedReadOnlyDefinition_IsAccepted()
    {
        var definition = ValidDefinition();

        var result = OemDefinitionPackageParser.ParseAndValidate(ToJson(definition));

        Assert.Single(result.Definitions);
        Assert.Equal(new byte[] { 0x22, 0xF1, 0x90 },
            OemDefinitionPackageParser.ParseReadOnlyRequest("22 F1 90"));
    }

    [Theory]
    [InlineData("2E F1 90 01")]
    [InlineData("10 03")]
    [InlineData("14 FF FF FF")]
    [InlineData("27 01")]
    [InlineData("31 01 FF 00")]
    public void WriteSessionSecurityAndRoutineServices_AreRejected(string request)
    {
        var definition = ValidDefinition();
        definition.ReadOnlyCommands[0].RequestHex = request;

        Assert.Throws<ArgumentException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void GenericObdCapture_CannotMasqueradeAsOemEvidence()
    {
        var definition = ValidDefinition();
        definition.Evidence.ObservedProtocol = "AUTO, ISO 15765-4 (CAN 11/500)";

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void InvalidCanAddress_IsRejected()
    {
        var definition = ValidDefinition();
        definition.Transport.TxCanId = 0x18DA10F1;
        definition.Transport.Extended29Bit = false;

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void MissingEvidenceHash_IsRejected()
    {
        var definition = ValidDefinition();
        definition.Evidence.SourceSha256 = string.Empty;

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void CapabilityCode_MustMatchReadOnlyUdsService()
    {
        var definition = ValidDefinition();
        definition.ReadOnlyCommands[0].CapabilityCode = "DTC.READ";

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Theory]
    [InlineData("19 04 FF")]
    [InlineData("19 06 FF")]
    [InlineData("19 02")]
    [InlineData("19 02 FF 00")]
    [InlineData("19 0A FF")]
    public void DtcReader_RejectsUnimplementedOrAmbiguousSubFunctions(string request)
    {
        var definition = ValidDefinition();
        definition.ReadOnlyCommands.Add(new OemReadOnlyCommandDefinition
        {
            Id = "dtc.read",
            Name = "Read DTC",
            CapabilityCode = "DTC.READ",
            RequestHex = request,
            ProbeDuringCapabilityDetection = true
        });

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void ReviewedDtcMapping_IsAcceptedAndNormalized()
    {
        var definition = ValidDefinition();
        definition.DtcMappings.Add(new OemDtcMappingDefinition
        {
            RawCodeHex = "a1b2c3",
            CatalogCode = "p0300"
        });

        var result = OemDefinitionPackageParser.ParseAndValidate(ToJson(definition));

        var mapping = Assert.Single(result.Definitions[0].DtcMappings);
        Assert.Equal("A1B2C3", mapping.RawCodeHex);
        Assert.Equal("P0300", mapping.CatalogCode);
    }

    [Theory]
    [InlineData("12345", "P0300")]
    [InlineData("12345Z", "P0300")]
    [InlineData("123456", "X0300")]
    public void InvalidDtcMapping_IsRejected(string rawCode, string catalogCode)
    {
        var definition = ValidDefinition();
        definition.DtcMappings.Add(new OemDtcMappingDefinition
        {
            RawCodeHex = rawCode,
            CatalogCode = catalogCode
        });

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void ReviewedFreezeFrameTemplate_IsAccepted()
    {
        var definition = ValidDefinition();
        definition.ReadOnlyCommands.Add(ValidFreezeFrameCommand());

        var result = OemDefinitionPackageParser.ParseAndValidate(ToJson(definition));

        var command = Assert.Single(result.Definitions[0].ReadOnlyCommands, item =>
            item.CapabilityCode == "FREEZEFRAME.READ");
        Assert.Equal(0xF40D, Assert.Single(command.SnapshotDids).DataIdentifier);
    }

    [Theory]
    [InlineData("19 04 12 34 56 FF", false)]
    [InlineData("19 04 00 00 00", false)]
    [InlineData("19 04 00 00 00 FF", true)]
    public void UnsafeFreezeFrameTemplate_IsRejected(string request, bool automaticProbe)
    {
        var definition = ValidDefinition();
        var command = ValidFreezeFrameCommand();
        command.RequestHex = request;
        command.ProbeDuringCapabilityDetection = automaticProbe;
        definition.ReadOnlyCommands.Add(command);

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void SnapshotFieldOutsideReviewedDidLength_IsRejected()
    {
        var definition = ValidDefinition();
        var command = ValidFreezeFrameCommand();
        command.SnapshotDids[0].Fields[0].ByteLength = 3;
        definition.ReadOnlyCommands.Add(command);

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    [Fact]
    public void ReviewedNumericLiveDataDid_IsAccepted()
    {
        var definition = ValidDefinition();
        definition.ReadOnlyCommands.Add(ValidLiveDataCommand());

        var result = OemDefinitionPackageParser.ParseAndValidate(ToJson(definition));

        var command = Assert.Single(result.Definitions[0].ReadOnlyCommands, item =>
            item.CapabilityCode == "DATA.READ");
        Assert.Equal("BATTERY / BMS", command.ResponseFields[0].Category.ToUpperInvariant());
        Assert.Equal("PACK_VOLTAGE", command.ResponseFields[0].Key);
    }

    [Theory]
    [InlineData("ASCII", "Battery / BMS")]
    [InlineData("HEX", "Battery / BMS")]
    [InlineData("UINT_BE", "Invented Category")]
    public void UnsafeLiveDataField_IsRejected(string encoding, string category)
    {
        var definition = ValidDefinition();
        var command = ValidLiveDataCommand();
        command.ResponseFields[0].Encoding = encoding;
        command.ResponseFields[0].Category = category;
        definition.ReadOnlyCommands.Add(command);

        Assert.Throws<InvalidOperationException>(() =>
            OemDefinitionPackageParser.ParseAndValidate(ToJson(definition)));
    }

    private static OemEcuDefinition ValidDefinition() => new()
    {
        DefinitionId = "test.vehicle.ecm",
        DefinitionVersion = 1,
        PackageId = "manufacturer.test",
        Brand = "Test",
        VehicleProfile = "Vehicle",
        Generation = "G1",
        MinimumYear = 2020,
        MaximumYear = 2022,
        EcuId = "ECM",
        EcuName = "Engine Control Module",
        Transport = new OemTransportDefinition
        {
            Protocol = "UDS_ISOTP_CAN",
            CanBus = "HS-CAN",
            BaudRateKbps = 500,
            TxCanId = 0x7E0,
            RxCanId = 0x7E8,
            Extended29Bit = false
        },
        ReadOnlyCommands = new()
        {
            new OemReadOnlyCommandDefinition
            {
                Id = "identity.vin",
                Name = "Read VIN",
                CapabilityCode = "IDENTIFICATION.READ",
                RequestHex = "22 F1 90",
                DiscoveryProbe = true,
                TimeoutMs = 1500,
                ResponseFields = new()
                {
                    new OemResponseFieldDefinition
                    {
                        Key = "VIN",
                        Label = "VIN",
                        ByteOffset = 0,
                        ByteLength = 17,
                        Encoding = "ASCII"
                    }
                }
            }
        },
        Evidence = new OemDefinitionEvidence
        {
            EvidenceId = "capture-001",
            SourceType = "VEHICLE_CAPTURE",
            SourceReference = "reviewed/capture-001.json",
            SourceSha256 = new string('A', 64),
            ObservedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ObservedProtocol = "UDS_ISOTP_CAN",
            ReviewStatus = "APPROVED",
            ReviewedBy = "catalog-reviewer",
            ReviewedAtUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
        }
    };

    private static OemReadOnlyCommandDefinition ValidFreezeFrameCommand() => new()
    {
        Id = "freeze.snapshot",
        Name = "Read DTC snapshot",
        CapabilityCode = "FREEZEFRAME.READ",
        RequestHex = "19 04 00 00 00 FF",
        ProbeDuringCapabilityDetection = false,
        MaxSnapshotDtcs = 3,
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
            }
        }
    };

    private static OemReadOnlyCommandDefinition ValidLiveDataCommand() => new()
    {
        Id = "data.pack-voltage",
        Name = "Read battery pack voltage",
        CapabilityCode = "DATA.READ",
        RequestHex = "22 43 69",
        ProbeDuringCapabilityDetection = true,
        ResponseFields = new()
        {
            new OemResponseFieldDefinition
            {
                Key = "pack_voltage",
                Label = "Pack Voltage",
                Category = "Battery / BMS",
                ByteOffset = 0,
                ByteLength = 2,
                Encoding = "UINT_BE",
                Scale = 0.1,
                Unit = "V"
            }
        }
    };

    private static string ToJson(OemEcuDefinition definition) =>
        JsonSerializer.Serialize(new OemDefinitionPackage
        {
            SchemaVersion = 2,
            CatalogVersion = 1,
            Definitions = new() { definition }
        });

    private static string RawResourcesDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "Resources", "Raw"));
}
