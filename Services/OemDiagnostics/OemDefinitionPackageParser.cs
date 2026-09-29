using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Fail-closed parser for executable OEM data. A definition is accepted only
/// when its transport, evidence and every command pass validation.
/// </summary>
public static class OemDefinitionPackageParser
{
    private static readonly IReadOnlyDictionary<string, byte> CapabilityServices =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["IDENTIFICATION.READ"] = 0x22,
            ["DTC.READ"] = 0x19,
            ["DATA.READ"] = 0x22,
            ["FREEZEFRAME.READ"] = 0x19
        };
    private static readonly HashSet<int> SupportedBaudRates = [125, 250, 500, 1000];
    private static readonly HashSet<string> AcceptedEvidenceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "VEHICLE_CAPTURE",
        "MANUFACTURER_DOCUMENTATION",
        "REVIEWED_TECHNICAL_SOURCE"
    };
    private static readonly HashSet<string> IdentificationFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "VIN", "MANUFACTURER", "SUPPLIER", "PART_NUMBER", "SERIAL_NUMBER",
        "HARDWARE_NUMBER", "HARDWARE_VERSION", "SOFTWARE_NUMBER", "SOFTWARE_VERSION",
        "CALIBRATION_ID", "CVN", "BOOTLOADER", "DIAGNOSTIC_VERSION",
        "PROGRAMMING_DATE", "MANUFACTURING_DATE"
    };
    private static readonly HashSet<string> FieldEncodings = new(StringComparer.OrdinalIgnoreCase)
    {
        "ASCII", "HEX", "UINT_BE", "INT_BE"
    };
    private static readonly HashSet<string> LiveDataCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Engine", "Fuel", "Air / Intake", "Temperature", "Oxygen Sensors",
        "Emissions", "Throttle", "Vehicle Speed", "Electrical", "Transmission",
        "ABS / Wheel", "Safety", "Body", "Hybrid / EV", "Battery / BMS"
    };

    public static OemDefinitionPackage ParseAndValidate(string json)
    {
        var package = JsonSerializer.Deserialize<OemDefinitionPackage>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("OEM definition package is empty.");

        if (package.SchemaVersion != 2)
            throw new InvalidOperationException($"Unsupported OEM definition schema: {package.SchemaVersion}.");
        if (package.CatalogVersion < 1)
            throw new InvalidOperationException("OEM catalog version must be positive.");
        if (package.Definitions is null)
            throw new InvalidOperationException("OEM definitions collection is required.");

        var definitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in package.Definitions)
        {
            ValidateDefinition(definition);
            if (!definitionIds.Add(definition.DefinitionId))
                throw new InvalidOperationException($"Duplicate OEM definition id: {definition.DefinitionId}.");
        }

        return package;
    }

    public static byte[] ParseReadOnlyRequest(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Read-only request is required.");
        if (value.Any(character => !Uri.IsHexDigit(character) &&
                                   !char.IsWhiteSpace(character) &&
                                   character is not ('-' or ':')))
            throw new FormatException("Read-only request contains non-hexadecimal characters.");

        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length == 0 || normalized.Length % 2 != 0)
            throw new FormatException("Read-only request must contain complete hexadecimal bytes.");

        var bytes = Enumerable.Range(0, normalized.Length / 2)
            .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
            .ToArray();
        if (bytes.Length > 7)
            throw new ArgumentException("Automatic OEM commands must fit in one ISO-TP request frame.");
        if (bytes[0] is not (0x19 or 0x22))
            throw new ArgumentException(
                $"UDS service 0x{bytes[0]:X2} is not permitted. Only read-only 0x19 and 0x22 are allowed.");
        if (bytes[0] == 0x19 && bytes.Length < 2)
            throw new ArgumentException("UDS ReadDTCInformation requires a sub-function.");
        if (bytes[0] == 0x22 && bytes.Length != 3)
            throw new ArgumentException("Automatic UDS ReadDataByIdentifier must request exactly one two-byte identifier.");
        return bytes;
    }

    private static void ValidateDefinition(OemEcuDefinition definition)
    {
        Require(definition.DefinitionId, "Definition id");
        Require(definition.PackageId, $"{definition.DefinitionId}: package id");
        Require(definition.Brand, $"{definition.DefinitionId}: brand");
        Require(definition.VehicleProfile, $"{definition.DefinitionId}: vehicle profile");
        Require(definition.EcuId, $"{definition.DefinitionId}: ECU id");
        Require(definition.EcuName, $"{definition.DefinitionId}: ECU name");
        if (definition.DefinitionVersion < 1)
            throw new InvalidOperationException($"{definition.DefinitionId}: definition version must be positive.");
        if (definition.MinimumYear < 1886 || definition.MaximumYear < definition.MinimumYear)
            throw new InvalidOperationException($"{definition.DefinitionId}: exact valid year range is required.");

        ValidateTransport(definition);
        ValidateCommands(definition);
        ValidateDtcMappings(definition);
        ValidateEvidence(definition);
    }

    private static void ValidateTransport(OemEcuDefinition definition)
    {
        var transport = definition.Transport ??
            throw new InvalidOperationException($"{definition.DefinitionId}: transport is required.");
        if (!transport.Protocol.Equals("UDS_ISOTP_CAN", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{definition.DefinitionId}: only the implemented UDS_ISOTP_CAN transport is accepted.");
        Require(transport.CanBus, $"{definition.DefinitionId}: CAN bus");
        if (!SupportedBaudRates.Contains(transport.BaudRateKbps))
            throw new InvalidOperationException($"{definition.DefinitionId}: unsupported CAN baud rate.");
        if (transport.TxCanId == transport.RxCanId)
            throw new InvalidOperationException($"{definition.DefinitionId}: request and response CAN IDs must differ.");

        var maximum = transport.Extended29Bit ? 0x1FFFFFFF : 0x7FF;
        if (transport.TxCanId < 0 || transport.TxCanId > maximum ||
            transport.RxCanId < 0 || transport.RxCanId > maximum)
            throw new InvalidOperationException($"{definition.DefinitionId}: CAN ID is outside the declared address width.");
    }

    private static void ValidateCommands(OemEcuDefinition definition)
    {
        if (definition.ReadOnlyCommands is null || definition.ReadOnlyCommands.Count == 0)
            throw new InvalidOperationException($"{definition.DefinitionId}: at least one read-only command is required.");
        if (definition.ReadOnlyCommands.Count(command => command.DiscoveryProbe) != 1)
            throw new InvalidOperationException($"{definition.DefinitionId}: exactly one discovery probe is required.");
        if (definition.ReadOnlyCommands.Count(command => command.ProbeDuringCapabilityDetection) > 4)
            throw new InvalidOperationException($"{definition.DefinitionId}: at most four automatic capability probes are allowed.");
        if (definition.ReadOnlyCommands.Count(command =>
                command.CapabilityCode.Equals("FREEZEFRAME.READ", StringComparison.OrdinalIgnoreCase)) > 1)
            throw new InvalidOperationException($"{definition.DefinitionId}: at most one freeze-frame template is allowed.");

        var commandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var automaticCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in definition.ReadOnlyCommands)
        {
            Require(command.Id, $"{definition.DefinitionId}: command id");
            Require(command.Name, $"{definition.DefinitionId}/{command.Id}: command name");
            Require(command.CapabilityCode, $"{definition.DefinitionId}/{command.Id}: capability code");
            if (!commandIds.Add(command.Id))
                throw new InvalidOperationException($"{definition.DefinitionId}: duplicate command id {command.Id}.");
            if (!CapabilityServices.TryGetValue(command.CapabilityCode, out var requiredService))
                throw new InvalidOperationException($"{definition.DefinitionId}/{command.Id}: unsupported capability code.");
            if (command.DiscoveryProbe && !command.ProbeDuringCapabilityDetection)
                throw new InvalidOperationException($"{definition.DefinitionId}/{command.Id}: discovery probe must be an automatic capability probe.");
            if (command.ProbeDuringCapabilityDetection && !automaticCapabilities.Add(command.CapabilityCode))
                throw new InvalidOperationException($"{definition.DefinitionId}: only one automatic probe is allowed per capability.");
            if (command.TimeoutMs is < 250 or > 10000)
                throw new InvalidOperationException($"{definition.DefinitionId}/{command.Id}: timeout must be 250–10000 ms.");
            var request = ParseReadOnlyRequest(command.RequestHex);
            if (request[0] != requiredService)
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}: {command.CapabilityCode} requires UDS service 0x{requiredService:X2}.");
            if (command.CapabilityCode.Equals("DTC.READ", StringComparison.OrdinalIgnoreCase) &&
                (request[0] != 0x19 ||
                 request[1] == 0x02 && request.Length != 3 ||
                 request[1] == 0x0A && request.Length != 2 ||
                 request[1] is not (0x02 or 0x0A)))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}: DTC reader supports only UDS 19 02 + status mask or 19 0A.");
            if (command.CapabilityCode.Equals("FREEZEFRAME.READ", StringComparison.OrdinalIgnoreCase))
                ValidateFreezeFrameTemplate(definition, command, request);
            ValidateResponseFields(definition, command, request);
        }
    }

    private static void ValidateFreezeFrameTemplate(
        OemEcuDefinition definition,
        OemReadOnlyCommandDefinition command,
        byte[] request)
    {
        if (request.Length != 6 || request[0] != 0x19 || request[1] != 0x04 ||
            request[2] != 0 || request[3] != 0 || request[4] != 0)
            throw new InvalidOperationException(
                $"{definition.DefinitionId}/{command.Id}: freeze-frame template must be 19 04 00 00 00 RR; zero DTC bytes are replaced only after a real DTC is read.");
        if (command.DiscoveryProbe || command.ProbeDuringCapabilityDetection)
            throw new InvalidOperationException(
                $"{definition.DefinitionId}/{command.Id}: a DTC-specific freeze-frame template cannot run as an automatic capability/discovery probe.");
        if (command.MaxSnapshotDtcs is < 1 or > 10)
            throw new InvalidOperationException(
                $"{definition.DefinitionId}/{command.Id}: MaxSnapshotDtcs must be 1–10.");

        command.SnapshotDids ??= new List<OemSnapshotDidDefinition>();
        if (command.SnapshotDids.Count == 0)
            throw new InvalidOperationException(
                $"{definition.DefinitionId}/{command.Id}: at least one reviewed snapshot DID layout is required.");
        var dids = new HashSet<int>();
        foreach (var did in command.SnapshotDids)
        {
            if (did.DataIdentifier is < 0 or > 0xFFFF || !dids.Add(did.DataIdentifier))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}: snapshot DID must be unique and within 0x0000–0xFFFF.");
            if (did.DataLength is < 1 or > 256)
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/0x{did.DataIdentifier:X4}: invalid DID data length.");
            did.Fields ??= new List<OemResponseFieldDefinition>();
            if (did.Fields.Count == 0)
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/0x{did.DataIdentifier:X4}: decoded fields are required.");

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in did.Fields)
            {
                Require(field.Key, $"{definition.DefinitionId}/{command.Id}/0x{did.DataIdentifier:X4}: field key");
                Require(field.Label, $"{definition.DefinitionId}/{command.Id}/{field.Key}: field label");
                if (!keys.Add(field.Key))
                    throw new InvalidOperationException(
                        $"{definition.DefinitionId}/{command.Id}/0x{did.DataIdentifier:X4}: duplicate field {field.Key}.");
                if (field.ByteOffset < 0 || field.ByteLength < 1 ||
                    field.ByteOffset + field.ByteLength > did.DataLength)
                    throw new InvalidOperationException(
                        $"{definition.DefinitionId}/{command.Id}/{field.Key}: field is outside the declared DID data.");
                if (!FieldEncodings.Contains(field.Encoding))
                    throw new InvalidOperationException(
                        $"{definition.DefinitionId}/{command.Id}/{field.Key}: unsupported snapshot encoding.");
                if ((field.Encoding.Equals("UINT_BE", StringComparison.OrdinalIgnoreCase) ||
                     field.Encoding.Equals("INT_BE", StringComparison.OrdinalIgnoreCase)) && field.ByteLength > 8)
                    throw new InvalidOperationException(
                        $"{definition.DefinitionId}/{command.Id}/{field.Key}: numeric field cannot exceed 8 bytes.");
                if (!double.IsFinite(field.Scale) || !double.IsFinite(field.ValueOffset))
                    throw new InvalidOperationException(
                        $"{definition.DefinitionId}/{command.Id}/{field.Key}: scale/offset must be finite.");
            }
        }
    }

    private static void ValidateDtcMappings(OemEcuDefinition definition)
    {
        definition.DtcMappings ??= new List<OemDtcMappingDefinition>();
        var rawCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in definition.DtcMappings)
        {
            var raw = mapping.RawCodeHex?.Trim() ?? string.Empty;
            var catalog = mapping.CatalogCode?.Trim() ?? string.Empty;
            if (raw.Length != 6 || raw.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}: OEM DTC raw code must contain exactly three hexadecimal bytes.");
            if (!rawCodes.Add(raw))
                throw new InvalidOperationException($"{definition.DefinitionId}: duplicate OEM DTC mapping {raw}.");
            if (catalog.Length != 5 || "PBCU".IndexOf(char.ToUpperInvariant(catalog[0])) < 0 ||
                catalog.Skip(1).Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{raw}: catalogCode must be an existing P/B/C/U style five-character code.");

            mapping.RawCodeHex = raw.ToUpperInvariant();
            mapping.CatalogCode = catalog.ToUpperInvariant();
        }
    }

    private static void ValidateResponseFields(
        OemEcuDefinition definition,
        OemReadOnlyCommandDefinition command,
        byte[] request)
    {
        command.ResponseFields ??= new List<OemResponseFieldDefinition>();
        command.SnapshotDids ??= new List<OemSnapshotDidDefinition>();
        var isIdentification = command.CapabilityCode.Equals("IDENTIFICATION.READ", StringComparison.OrdinalIgnoreCase);
        var isLiveData = command.CapabilityCode.Equals("DATA.READ", StringComparison.OrdinalIgnoreCase);
        if (isIdentification || isLiveData)
        {
            if (request.Length != 3 || request[0] != 0x22)
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}: 0x22 decoder requires exactly one UDS DID.");
            if (command.ResponseFields.Count == 0)
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}: response fields are required.");
        }
        else if (command.ResponseFields.Count > 0)
        {
            throw new InvalidOperationException(
                $"{definition.DefinitionId}/{command.Id}: response fields are supported only for identification/live-data commands.");
        }
        if (!command.CapabilityCode.Equals("FREEZEFRAME.READ", StringComparison.OrdinalIgnoreCase) &&
            command.SnapshotDids.Count > 0)
            throw new InvalidOperationException(
                $"{definition.DefinitionId}/{command.Id}: snapshot DID layouts are allowed only for freeze-frame commands.");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in command.ResponseFields)
        {
            Require(field.Key, $"{definition.DefinitionId}/{command.Id}: field key");
            Require(field.Label, $"{definition.DefinitionId}/{command.Id}/{field.Key}: field label");
            if (isIdentification && !IdentificationFieldKeys.Contains(field.Key))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/{field.Key}: unsupported identification field.");
            if (isLiveData && (field.Key.Length > 64 ||
                field.Key.Any(character => !char.IsLetterOrDigit(character) && character != '_')))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}: live-data field key must contain only letters, digits or underscore.");
            if (!keys.Add(field.Key))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}: duplicate response field {field.Key}.");
            if (field.ByteOffset < 0 || field.ByteLength < 1 || field.ByteOffset + field.ByteLength > 256)
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/{field.Key}: invalid byte range.");
            if (!FieldEncodings.Contains(field.Encoding))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/{field.Key}: unsupported encoding.");
            if (isLiveData && !field.Encoding.Equals("UINT_BE", StringComparison.OrdinalIgnoreCase) &&
                !field.Encoding.Equals("INT_BE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/{field.Key}: live telemetry must use a numeric encoding.");
            if (isLiveData && !LiveDataCategories.Contains(field.Category))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/{field.Key}: a supported live-data category is required.");
            if ((field.Encoding.Equals("UINT_BE", StringComparison.OrdinalIgnoreCase) ||
                 field.Encoding.Equals("INT_BE", StringComparison.OrdinalIgnoreCase)) && field.ByteLength > 8)
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/{field.Key}: numeric fields cannot exceed 8 bytes.");
            if (!double.IsFinite(field.Scale) || !double.IsFinite(field.ValueOffset))
                throw new InvalidOperationException(
                    $"{definition.DefinitionId}/{command.Id}/{field.Key}: scale/offset must be finite.");

            if (isLiveData)
            {
                field.Key = field.Key.ToUpperInvariant();
                field.Category = LiveDataCategories.First(category =>
                    category.Equals(field.Category, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private static void ValidateEvidence(OemEcuDefinition definition)
    {
        var evidence = definition.Evidence ??
            throw new InvalidOperationException($"{definition.DefinitionId}: evidence is required.");
        Require(evidence.EvidenceId, $"{definition.DefinitionId}: evidence id");
        Require(evidence.SourceReference, $"{definition.DefinitionId}: evidence source reference");
        if (evidence.SourceSha256.Length != 64 || evidence.SourceSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"{definition.DefinitionId}: evidence SHA-256 is required.");
        Require(evidence.ObservedProtocol, $"{definition.DefinitionId}: observed protocol");
        Require(evidence.ReviewedBy, $"{definition.DefinitionId}: reviewer");
        if (!AcceptedEvidenceTypes.Contains(evidence.SourceType))
            throw new InvalidOperationException($"{definition.DefinitionId}: unsupported evidence source type.");
        if (!evidence.ReviewStatus.Equals("APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{definition.DefinitionId}: evidence has not been approved.");
        if (evidence.ObservedAtUtc == default || evidence.ReviewedAtUtc == default ||
            evidence.ReviewedAtUtc < evidence.ObservedAtUtc)
            throw new InvalidOperationException($"{definition.DefinitionId}: valid observation/review timestamps are required.");
        if (!evidence.ObservedProtocol.Equals(definition.Transport.Protocol, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{definition.DefinitionId}: observed protocol does not match transport.");
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{field} is required.");
    }
}
