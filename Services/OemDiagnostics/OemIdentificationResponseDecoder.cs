using System.Globalization;
using System.Text;
using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Decodes only byte ranges explicitly defined in a reviewed package.</summary>
public static class OemIdentificationResponseDecoder
{
    public static IReadOnlyList<OemIdentificationValue> Decode(
        OemEcuDefinition definition,
        OemReadOnlyCommandDefinition command,
        UdsResponse response)
    {
        if (response.Kind != UdsResponseKind.Positive || response.Service != 0x62)
            return FailureValues(definition, command, MapFailure(response), ResponseEvidence(response));

        var request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
        if (request.Length != 3 || response.Payload.Length < 2 ||
            response.Payload[0] != request[1] || response.Payload[1] != request[2])
        {
            return FailureValues(
                definition,
                command,
                OemIdentificationValueState.InvalidResponse,
                "Positive response DID does not match the reviewed request.");
        }

        var data = response.Payload.AsSpan(2);
        var values = new List<OemIdentificationValue>();
        foreach (var field in command.ResponseFields)
        {
            if (field.ByteOffset < 0 || field.ByteLength < 1 ||
                field.ByteOffset + field.ByteLength > data.Length)
            {
                values.Add(Create(
                    definition, field, string.Empty,
                    OemIdentificationValueState.InvalidResponse,
                    $"Payload is {data.Length} data bytes; field needs bytes {field.ByteOffset}–{field.ByteOffset + field.ByteLength - 1}."));
                continue;
            }

            var bytes = data.Slice(field.ByteOffset, field.ByteLength);
            if (!TryDecode(bytes, field, out var value, out var error))
            {
                values.Add(Create(
                    definition, field, string.Empty,
                    OemIdentificationValueState.InvalidResponse, error));
                continue;
            }

            values.Add(Create(
                definition, field, value,
                OemIdentificationValueState.Available,
                $"ECU returned DID 0x{request[1]:X2}{request[2]:X2}; decoded by reviewed {field.Encoding} field."));
        }

        return values;
    }

    public static IReadOnlyList<OemIdentificationValue> FailureValues(
        OemEcuDefinition definition,
        OemReadOnlyCommandDefinition command,
        OemIdentificationValueState state,
        string evidence) => command.ResponseFields
        .Select(field => Create(definition, field, string.Empty, state, evidence))
        .ToArray();

    private static bool TryDecode(
        ReadOnlySpan<byte> bytes,
        OemResponseFieldDefinition field,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;
        if (field.Encoding.Equals("ASCII", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = bytes.ToArray().TakeWhile(item => item is not (0x00 or 0xFF)).ToArray();
            if (trimmed.Any(item => item < 0x20 || item > 0x7E))
            {
                error = "ASCII field contains non-printable bytes.";
                return false;
            }
            value = Encoding.ASCII.GetString(trimmed).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "ASCII field is empty.";
                return false;
            }
            return true;
        }

        if (field.Encoding.Equals("HEX", StringComparison.OrdinalIgnoreCase))
        {
            value = Convert.ToHexString(bytes);
            return true;
        }

        ulong raw = 0;
        foreach (var item in bytes)
            raw = (raw << 8) | item;

        double numeric;
        if (field.Encoding.Equals("INT_BE", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length < 8 && (bytes[0] & 0x80) != 0)
                raw |= ulong.MaxValue << (bytes.Length * 8);
            numeric = unchecked((long)raw);
        }
        else if (field.Encoding.Equals("UINT_BE", StringComparison.OrdinalIgnoreCase))
        {
            numeric = raw;
        }
        else
        {
            error = $"Unsupported encoding: {field.Encoding}.";
            return false;
        }

        value = (numeric * field.Scale + field.ValueOffset)
            .ToString("0.###", CultureInfo.InvariantCulture);
        return true;
    }

    private static OemIdentificationValue Create(
        OemEcuDefinition definition,
        OemResponseFieldDefinition field,
        string value,
        OemIdentificationValueState state,
        string evidence) => new(
            definition.DefinitionId,
            definition.EcuId,
            definition.EcuName,
            field.Key,
            field.Label,
            value,
            field.Unit,
            state,
            evidence,
            definition.Evidence.SourceReference);

    private static OemIdentificationValueState MapFailure(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode is 0x11 or 0x12 or 0x31 =>
            OemIdentificationValueState.NotSupported,
        UdsResponseKind.Timeout => OemIdentificationValueState.NoResponse,
        UdsResponseKind.Malformed => OemIdentificationValueState.InvalidResponse,
        _ => OemIdentificationValueState.Error
    };

    private static string ResponseEvidence(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode.HasValue =>
            $"ECU NRC 0x{response.NegativeResponseCode.Value:X2}: {response.Message}",
        UdsResponseKind.Timeout => "ECU response timeout.",
        _ => string.IsNullOrWhiteSpace(response.Message) ? "Identification response unavailable." : response.Message
    };
}
