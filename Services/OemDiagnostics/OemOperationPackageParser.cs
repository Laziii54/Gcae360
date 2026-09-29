using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Fail-closed parser for reviewed ECU-changing operation packages.</summary>
public static class OemOperationPackageParser
{
    private static readonly HashSet<string> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PREPARE", "EXECUTE", "STOP", "VERIFY"
    };
    private static readonly HashSet<string> EvidenceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MANUFACTURER_DOCUMENTATION", "REVIEWED_TECHNICAL_SOURCE", "VEHICLE_CAPTURE"
    };

    public static OemOperationPackage ParseAndValidate(string json)
    {
        var package = JsonSerializer.Deserialize<OemOperationPackage>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        }) ?? throw new InvalidOperationException("OEM operation package is empty.");
        if (package.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported OEM operation schema: {package.SchemaVersion}.");
        if (package.CatalogVersion < 1 || package.Operations is null)
            throw new InvalidOperationException("OEM operation catalog/version is invalid.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in package.Operations)
        {
            Validate(operation);
            if (!ids.Add(operation.OperationId))
                throw new InvalidOperationException($"Duplicate OEM operation id: {operation.OperationId}.");
        }
        return package;
    }

    public static byte[] ParseRequest(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !Uri.IsHexDigit(character) && !char.IsWhiteSpace(character) && character is not ('-' or ':')))
            throw new FormatException("Operation request must contain hexadecimal bytes only.");
        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length < 4 || normalized.Length % 2 != 0)
            throw new FormatException("Operation request must contain complete service/data bytes.");
        var bytes = Enumerable.Range(0, normalized.Length / 2)
            .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
            .ToArray();
        if (bytes.Length > 7)
            throw new NotSupportedException("Current verified ELM transport supports operation requests up to 7 UDS bytes.");
        if (bytes[0] is 0x27 or 0x34 or 0x36 or 0x37)
            throw new InvalidOperationException("Security-key and programming/download services cannot be embedded in an operation package.");
        return bytes;
    }

    private static void Validate(OemOperationDefinition operation)
    {
        Require(operation.OperationId, "Operation id");
        Require(operation.DefinitionId, $"{operation.OperationId}: ECU definition id");
        Require(operation.PackageId, $"{operation.OperationId}: package id");
        Require(operation.Brand, $"{operation.OperationId}: brand");
        Require(operation.VehicleProfile, $"{operation.OperationId}: vehicle profile");
        Require(operation.EcuId, $"{operation.OperationId}: ECU id");
        Require(operation.EcuName, $"{operation.OperationId}: ECU name");
        Require(operation.Group, $"{operation.OperationId}: group");
        Require(operation.Name, $"{operation.OperationId}: name");
        Require(operation.Description, $"{operation.OperationId}: description");
        if (operation.Version < 1 || operation.MinimumYear < 1886 || operation.MaximumYear < operation.MinimumYear)
            throw new InvalidOperationException($"{operation.OperationId}: exact version/year range is required.");

        var safety = operation.Safety ?? throw new InvalidOperationException($"{operation.OperationId}: safety definition is required.");
        if (!safety.RequiresUserConfirmation || !safety.RequiresStationary || !safety.RequiresIgnitionOn)
            throw new InvalidOperationException($"{operation.OperationId}: confirmation, ignition and stationary checks are mandatory.");
        safety.RequiredEngineState = safety.RequiredEngineState.Trim().ToUpperInvariant();
        if (safety.RequiredEngineState is not ("ANY" or "OFF" or "RUNNING"))
            throw new InvalidOperationException($"{operation.OperationId}: engine state must be ANY/OFF/RUNNING.");
        if (!double.IsFinite(safety.MinimumVoltage) || !double.IsFinite(safety.MaximumVoltage) ||
            safety.MinimumVoltage < 10 || safety.MaximumVoltage > 16.5 || safety.MinimumVoltage >= safety.MaximumVoltage)
            throw new InvalidOperationException($"{operation.OperationId}: safe 12V range must be within 10.0–16.5 V.");
        if (safety.MaximumRunSeconds is < 1 or > 30)
            throw new InvalidOperationException($"{operation.OperationId}: maximum run time must be 1–30 seconds.");

        operation.Steps ??= new List<OemOperationStepDefinition>();
        if (operation.Steps.Count == 0 || operation.Steps.Count > 10)
            throw new InvalidOperationException($"{operation.OperationId}: 1–10 reviewed steps are required.");
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in operation.Steps)
        {
            Require(step.Id, $"{operation.OperationId}: step id");
            Require(step.Name, $"{operation.OperationId}/{step.Id}: step name");
            step.Role = step.Role.Trim().ToUpperInvariant();
            if (!Roles.Contains(step.Role) || !stepIds.Add(step.Id))
                throw new InvalidOperationException($"{operation.OperationId}: invalid/duplicate operation step.");
            if (step.TimeoutMs is < 250 or > 10000 || step.DelayAfterMs is < 0 or > 5000)
                throw new InvalidOperationException($"{operation.OperationId}/{step.Id}: timeout/delay is outside safe limits.");
            var request = ParseRequest(step.RequestHex);
            ValidateServiceForRole(operation, step, request);
        }

        if (operation.Type == OemOperationType.ActiveTest &&
            operation.Steps.Count(step => step.Role == "STOP") != 1)
            throw new InvalidOperationException($"{operation.OperationId}: active test requires exactly one reviewed STOP step.");
        if (operation.Type != OemOperationType.ActiveTest && operation.Steps.Any(step => step.Role == "STOP"))
            throw new InvalidOperationException($"{operation.OperationId}: STOP role is reserved for active tests.");
        if (!operation.Steps.Any(step => step.Role == "EXECUTE"))
            throw new InvalidOperationException($"{operation.OperationId}: EXECUTE step is required.");
        if (operation.Type is OemOperationType.Service or OemOperationType.Coding &&
            !operation.Steps.Any(step => step.Role == "VERIFY"))
            throw new InvalidOperationException($"{operation.OperationId}: service/coding requires a read-only VERIFY step.");
        if (operation.Type is OemOperationType.Service or OemOperationType.Coding &&
            operation.Steps.Where(step => step.Role == "VERIFY").Any(step =>
                string.IsNullOrWhiteSpace(step.ExpectedResponsePayloadHex)))
            throw new InvalidOperationException($"{operation.OperationId}: each VERIFY step requires an expected response payload prefix.");
        if (operation.Type == OemOperationType.Coding)
        {
            var backup = ParseRequest(operation.BackupReadHex);
            if (backup.Length != 3 || backup[0] != 0x22)
                throw new InvalidOperationException($"{operation.OperationId}: coding requires an exact 0x22 backup DID.");
        }
        else if (!string.IsNullOrWhiteSpace(operation.BackupReadHex))
        {
            throw new InvalidOperationException($"{operation.OperationId}: backup read is reserved for coding.");
        }

        var validation = operation.Validation ??
            throw new InvalidOperationException($"{operation.OperationId}: validation state is required.");
        if (!validation.SimulationValidated)
            throw new InvalidOperationException($"{operation.OperationId}: simulation validation is required.");
        OemOperationSimulationValidator.Validate(operation);
        if (!validation.VehicleValidated || string.IsNullOrWhiteSpace(validation.VehicleCaptureReference))
            throw new InvalidOperationException(
                $"{operation.OperationId}: a real-vehicle validation capture is required before execution.");

        ValidateEvidence(operation);
    }

    private static void ValidateServiceForRole(
        OemOperationDefinition operation,
        OemOperationStepDefinition step,
        byte[] request)
    {
        if (step.Role == "VERIFY")
        {
            if (request.Length != 3 || request[0] != 0x22)
                throw new InvalidOperationException($"{operation.OperationId}/{step.Id}: VERIFY must be one exact 0x22 DID read.");
            var expected = ParseOptionalPayload(step.ExpectedResponsePayloadHex);
            if (expected.Length < 2 || expected[0] != request[1] || expected[1] != request[2])
                throw new InvalidOperationException(
                    $"{operation.OperationId}/{step.Id}: VERIFY expected payload must begin with the requested DID.");
            return;
        }
        if (step.Role == "PREPARE")
        {
            if (request[0] != 0x10 || request.Length != 2)
                throw new InvalidOperationException($"{operation.OperationId}/{step.Id}: PREPARE supports only exact DiagnosticSessionControl.");
            return;
        }

        var allowed = operation.Type switch
        {
            OemOperationType.ActiveTest => request[0] is 0x2F or 0x31,
            OemOperationType.Service => request[0] is 0x2E or 0x31,
            OemOperationType.Coding => request[0] == 0x2E,
            _ => false
        };
        if (!allowed)
            throw new InvalidOperationException(
                $"{operation.OperationId}/{step.Id}: UDS service 0x{request[0]:X2} is not allowed for {operation.Type}.");
    }

    private static byte[] ParseOptionalPayload(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<byte>();
        if (value.Any(character => !Uri.IsHexDigit(character) && !char.IsWhiteSpace(character) && character is not ('-' or ':')))
            throw new FormatException("Expected response payload must contain hexadecimal bytes only.");
        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length == 0 || normalized.Length % 2 != 0)
            throw new FormatException("Expected response payload must contain complete bytes.");
        return Enumerable.Range(0, normalized.Length / 2)
            .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
            .ToArray();
    }

    private static void ValidateEvidence(OemOperationDefinition operation)
    {
        var evidence = operation.Evidence ?? throw new InvalidOperationException($"{operation.OperationId}: evidence is required.");
        Require(evidence.EvidenceId, $"{operation.OperationId}: evidence id");
        Require(evidence.SourceReference, $"{operation.OperationId}: evidence source");
        Require(evidence.ReviewedBy, $"{operation.OperationId}: reviewer");
        if (!EvidenceTypes.Contains(evidence.SourceType) ||
            !evidence.ReviewStatus.Equals("APPROVED", StringComparison.OrdinalIgnoreCase) ||
            evidence.SourceSha256.Length != 64 || evidence.SourceSha256.Any(character => !Uri.IsHexDigit(character)) ||
            evidence.ObservedAtUtc == default || evidence.ReviewedAtUtc < evidence.ObservedAtUtc)
            throw new InvalidOperationException($"{operation.OperationId}: approved, hashed and timestamped evidence is required.");
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }
}
