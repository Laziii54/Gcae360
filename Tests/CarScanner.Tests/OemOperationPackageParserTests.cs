using System.Text.Json;
using System.Text.Json.Serialization;
using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

public sealed class OemOperationPackageParserTests
{
    [Fact]
    public void AcceptsReviewedActiveTestWithMandatoryStop()
    {
        var operation = CreateOperation(OemOperationType.ActiveTest);
        operation.Steps =
        [
            Step("run", "EXECUTE", "2F010101"),
            Step("stop", "STOP", "2F010100")
        ];

        var result = Parse(operation);

        Assert.Single(result.Operations);
        Assert.Equal("STOP", result.Operations[0].Steps[1].Role);
    }

    [Fact]
    public void RejectsActiveTestWithoutStop()
    {
        var operation = CreateOperation(OemOperationType.ActiveTest);
        operation.Steps = [Step("run", "EXECUTE", "2F010101")];

        Assert.Throws<InvalidOperationException>(() => Parse(operation));
    }

    [Theory]
    [InlineData("2701")]
    [InlineData("340000")]
    [InlineData("3601")]
    [InlineData("3700")]
    public void RejectsEmbeddedSecurityOrProgrammingServices(string request)
    {
        var operation = CreateOperation(OemOperationType.Service);
        operation.Steps =
        [
            Step("run", "EXECUTE", request),
            Step("verify", "VERIFY", "221234", "123400")
        ];

        Assert.ThrowsAny<Exception>(() => Parse(operation));
    }

    [Fact]
    public void RejectsServiceUsingActiveTestService()
    {
        var operation = CreateOperation(OemOperationType.Service);
        operation.Steps =
        [
            Step("run", "EXECUTE", "2F010101"),
            Step("verify", "VERIFY", "221234", "123400")
        ];

        Assert.Throws<InvalidOperationException>(() => Parse(operation));
    }

    [Fact]
    public void AcceptsReviewedServiceWithIndependentVerification()
    {
        var operation = CreateOperation(OemOperationType.Service);
        operation.Steps =
        [
            Step("run", "EXECUTE", "3101ABCD"),
            Step("verify", "VERIFY", "221234", "123400")
        ];

        Assert.Single(Parse(operation).Operations);
    }

    [Fact]
    public void RejectsServiceWithoutExpectedVerificationValue()
    {
        var operation = CreateOperation(OemOperationType.Service);
        operation.Steps =
        [
            Step("run", "EXECUTE", "3101ABCD"),
            Step("verify", "VERIFY", "221234")
        ];

        Assert.ThrowsAny<Exception>(() => Parse(operation));
    }

    [Fact]
    public void AcceptsCodingOnlyWithBackupAndReadBackVerification()
    {
        var operation = CreateOperation(OemOperationType.Coding);
        operation.BackupReadHex = "221234";
        operation.Steps =
        [
            Step("write", "EXECUTE", "2E123401"),
            Step("verify", "VERIFY", "221234", "123401")
        ];

        Assert.Single(Parse(operation).Operations);
    }

    [Fact]
    public void RejectsCodingWithoutBackupRead()
    {
        var operation = CreateOperation(OemOperationType.Coding);
        operation.Steps =
        [
            Step("write", "EXECUTE", "2E123401"),
            Step("verify", "VERIFY", "221234", "123401")
        ];

        Assert.ThrowsAny<Exception>(() => Parse(operation));
    }

    [Fact]
    public void RejectsUnapprovedEvidence()
    {
        var operation = CreateOperation(OemOperationType.Service);
        operation.Evidence.ReviewStatus = "DRAFT";
        operation.Steps =
        [
            Step("run", "EXECUTE", "3101ABCD"),
            Step("verify", "VERIFY", "221234", "123400")
        ];

        Assert.Throws<InvalidOperationException>(() => Parse(operation));
    }

    [Fact]
    public void RejectsOperationWithoutRealVehicleValidation()
    {
        var operation = CreateOperation(OemOperationType.Service);
        operation.Validation.VehicleValidated = false;
        operation.Validation.VehicleCaptureReference = string.Empty;
        operation.Steps =
        [
            Step("run", "EXECUTE", "3101ABCD"),
            Step("verify", "VERIFY", "221234", "123400")
        ];

        Assert.Throws<InvalidOperationException>(() => Parse(operation));
    }

    [Fact]
    public void RejectsOperationWithMismatchedSimulationResponse()
    {
        var operation = CreateOperation(OemOperationType.Service);
        var execute = Step("run", "EXECUTE", "3101ABCD");
        execute.SimulationResponseHex = "7101ABCE";
        operation.Steps =
        [
            execute,
            Step("verify", "VERIFY", "221234", "123400")
        ];

        Assert.Throws<InvalidOperationException>(() => Parse(operation));
    }

    private static OemOperationPackage Parse(OemOperationDefinition operation)
    {
        var json = JsonSerializer.Serialize(new OemOperationPackage { Operations = [operation] },
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
        return OemOperationPackageParser.ParseAndValidate(json);
    }

    private static OemOperationDefinition CreateOperation(OemOperationType type) => new()
    {
        OperationId = $"test-{type.ToString().ToLowerInvariant()}",
        Version = 1,
        DefinitionId = "test-ecu-definition",
        PackageId = "test-package",
        Brand = "Test",
        VehicleProfile = "Test Model",
        MinimumYear = 2020,
        MaximumYear = 2024,
        EcuId = "ECM",
        EcuName = "Engine Control Module",
        Type = type,
        Group = "Test",
        Name = "Reviewed operation",
        Description = "Operation used to validate the fail-closed package parser.",
        Safety = new OemOperationSafetyDefinition
        {
            RequiresUserConfirmation = true,
            RequiresStationary = true,
            RequiresIgnitionOn = true,
            RequiredEngineState = "OFF",
            MinimumVoltage = 12,
            MaximumVoltage = 15.5,
            MaximumRunSeconds = 1
        },
        Validation = new OemOperationValidationDefinition
        {
            SimulationValidated = true,
            VehicleValidated = true,
            VehicleCaptureReference = "capture-test-1",
            ValidationNotes = "Unit-test fixture"
        },
        Evidence = new OemDefinitionEvidence
        {
            EvidenceId = "evidence-1",
            SourceType = "VEHICLE_CAPTURE",
            SourceReference = "capture-1",
            SourceSha256 = new string('A', 64),
            ObservedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ReviewedAtUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            ReviewedBy = "unit-test-reviewer",
            ReviewStatus = "APPROVED"
        }
    };

    private static OemOperationStepDefinition Step(
        string id,
        string role,
        string request,
        string expected = "") => new()
    {
        Id = id,
        Name = id,
        Role = role,
        RequestHex = request,
        ExpectedResponsePayloadHex = expected,
        SimulationResponseHex = SimulatedResponse(request, expected),
        TimeoutMs = 1000
    };

    private static string SimulatedResponse(string request, string expected)
    {
        var normalized = new string(request.Where(Uri.IsHexDigit).ToArray());
        var bytes = Enumerable.Range(0, normalized.Length / 2)
            .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
            .ToArray();
        return bytes[0] switch
        {
            0x10 => $"50{bytes[1]:X2}",
            0x22 => $"62{(string.IsNullOrWhiteSpace(expected) ? $"{bytes[1]:X2}{bytes[2]:X2}" : expected)}",
            0x2E => $"6E{bytes[1]:X2}{bytes[2]:X2}",
            0x2F => $"6F{bytes[1]:X2}{bytes[2]:X2}",
            0x31 => $"71{bytes[1]:X2}{bytes[2]:X2}{bytes[3]:X2}",
            _ => $"7F{bytes[0]:X2}11"
        };
    }
}
