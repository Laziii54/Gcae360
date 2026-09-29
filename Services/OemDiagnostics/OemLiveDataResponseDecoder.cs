using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Decodes only reviewed numeric fields from one exact UDS 0x22 DID.</summary>
public static class OemLiveDataResponseDecoder
{
    public static OemLiveCommandResult Decode(
        OemEcuDefinition definition,
        OemReadOnlyCommandDefinition command,
        UdsResponse response)
    {
        var request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
        var did = request.Length == 3 ? (request[1] << 8) | request[2] : 0;
        if (response.Kind != UdsResponseKind.Positive || response.Service != 0x62)
            return Failure(definition, did, MapFailure(response), ResponseDetail(response));
        if (request.Length != 3 || request[0] != 0x22 || response.Payload.Length < 2 ||
            response.Payload[0] != request[1] || response.Payload[1] != request[2])
            return Failure(
                definition, did, OemLiveValueState.InvalidResponse,
                "Positive response DID does not match the reviewed live-data request.");

        var data = response.Payload.AsSpan(2);
        var values = new List<OemLiveValue>();
        foreach (var field in command.ResponseFields)
        {
            if (field.ByteOffset < 0 || field.ByteLength is < 1 or > 8 ||
                field.ByteOffset + field.ByteLength > data.Length)
                return Failure(
                    definition, did, OemLiveValueState.InvalidResponse,
                    $"Payload is {data.Length} data bytes; {field.Key} needs bytes {field.ByteOffset}–{field.ByteOffset + field.ByteLength - 1}.");

            var bytes = data.Slice(field.ByteOffset, field.ByteLength);
            if (!TryDecodeNumeric(bytes, field, out var value))
                return Failure(
                    definition, did, OemLiveValueState.InvalidResponse,
                    $"{field.Key} has an unsupported numeric encoding.");

            values.Add(new OemLiveValue(
                definition.DefinitionId,
                definition.EcuId,
                definition.EcuName,
                did,
                field.Key,
                field.Label,
                field.Category,
                value,
                field.Unit,
                OemLiveValueState.Available,
                $"ECU returned DID 0x{did:X4}; decoded from reviewed byte range {field.ByteOffset}–{field.ByteOffset + field.ByteLength - 1}.",
                definition.Evidence.SourceReference));
        }

        return new OemLiveCommandResult(
            definition.DefinitionId,
            definition.EcuId,
            definition.EcuName,
            did,
            OemLiveValueState.Available,
            values,
            $"DID 0x{did:X4}: {values.Count} real value(s).");
    }

    private static bool TryDecodeNumeric(
        ReadOnlySpan<byte> bytes,
        OemResponseFieldDefinition field,
        out double value)
    {
        value = 0;
        ulong raw = 0;
        foreach (var item in bytes)
            raw = (raw << 8) | item;

        double number;
        if (field.Encoding.Equals("INT_BE", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length < 8 && (bytes[0] & 0x80) != 0)
                raw |= ulong.MaxValue << (bytes.Length * 8);
            number = unchecked((long)raw);
        }
        else if (field.Encoding.Equals("UINT_BE", StringComparison.OrdinalIgnoreCase))
        {
            number = raw;
        }
        else
        {
            return false;
        }

        value = number * field.Scale + field.ValueOffset;
        return double.IsFinite(value);
    }

    private static OemLiveCommandResult Failure(
        OemEcuDefinition definition,
        int did,
        OemLiveValueState state,
        string detail) => new(
            definition.DefinitionId,
            definition.EcuId,
            definition.EcuName,
            did,
            state,
            Array.Empty<OemLiveValue>(),
            detail);

    private static OemLiveValueState MapFailure(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode is 0x11 or 0x12 or 0x31 =>
            OemLiveValueState.NotSupported,
        UdsResponseKind.Timeout => OemLiveValueState.NoResponse,
        UdsResponseKind.Malformed => OemLiveValueState.InvalidResponse,
        UdsResponseKind.Unsupported => OemLiveValueState.NotSupported,
        _ => OemLiveValueState.Error
    };

    private static string ResponseDetail(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode.HasValue =>
            $"ECU NRC 0x{response.NegativeResponseCode.Value:X2}: {response.Message}",
        UdsResponseKind.Timeout => "ECU response timeout.",
        _ => string.IsNullOrWhiteSpace(response.Message) ? "OEM live-data response unavailable." : response.Message
    };
}
