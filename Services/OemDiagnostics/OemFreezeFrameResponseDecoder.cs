using System.Globalization;
using System.Text;
using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Strict UDS 0x19/0x04 DTC snapshot decoder.</summary>
public static class OemFreezeFrameResponseDecoder
{
    public static OemFreezeFrameDecodeResult Decode(
        OemReadOnlyCommandDefinition command,
        string expectedRawDtcHex,
        UdsResponse response)
    {
        if (response.Kind != UdsResponseKind.Positive)
            return OemFreezeFrameDecodeResult.Failure(MapFailure(response), ResponseDetail(response));
        if (response.Service != 0x59)
            return OemFreezeFrameDecodeResult.Failure(
                OemFreezeFrameReadState.InvalidResponse,
                $"Expected UDS positive service 0x59, received 0x{response.Service:X2}.");

        byte[] request;
        byte[] expectedDtc;
        try
        {
            request = OemDefinitionPackageParser.ParseReadOnlyRequest(command.RequestHex);
            expectedDtc = Convert.FromHexString(expectedRawDtcHex);
        }
        catch (Exception exception)
        {
            return OemFreezeFrameDecodeResult.Failure(OemFreezeFrameReadState.Error, exception.Message);
        }

        if (request.Length != 6 || request[0] != 0x19 || request[1] != 0x04 || expectedDtc.Length != 3)
            return OemFreezeFrameDecodeResult.Failure(
                OemFreezeFrameReadState.Error,
                "Invalid reviewed freeze-frame template or DTC number.");
        if (response.Payload.Length < 5 || response.Payload[0] != 0x04)
            return OemFreezeFrameDecodeResult.Failure(
                OemFreezeFrameReadState.InvalidResponse,
                "DTC snapshot response header is incomplete or has a different sub-function.");
        if (!response.Payload.AsSpan(1, 3).SequenceEqual(expectedDtc))
            return OemFreezeFrameDecodeResult.Failure(
                OemFreezeFrameReadState.InvalidResponse,
                "DTC snapshot response belongs to a different DTC.");

        var rawDtc = Convert.ToHexString(expectedDtc);
        var dtcStatus = response.Payload[4];
        var cursor = 5;
        var records = new List<OemFreezeFrameRecord>();
        while (cursor < response.Payload.Length)
        {
            if (response.Payload.Length - cursor < 2)
                return Invalid("Snapshot record header is truncated.");

            var recordNumber = response.Payload[cursor++];
            var identifierCount = response.Payload[cursor++];
            if (request[5] != 0xFF && recordNumber != request[5])
                return Invalid(
                    $"ECU returned snapshot record 0x{recordNumber:X2}; 0x{request[5]:X2} was requested.");

            var values = new List<OemFreezeFrameValue>();
            for (var index = 0; index < identifierCount; index++)
            {
                if (response.Payload.Length - cursor < 2)
                    return Invalid("Snapshot DID header is truncated.");
                var did = (response.Payload[cursor] << 8) | response.Payload[cursor + 1];
                cursor += 2;
                var layout = command.SnapshotDids.FirstOrDefault(item => item.DataIdentifier == did);
                if (layout is null)
                    return Invalid(
                        $"Snapshot contains unreviewed DID 0x{did:X4}; its byte length cannot be guessed safely.");
                if (response.Payload.Length - cursor < layout.DataLength)
                    return Invalid($"Snapshot DID 0x{did:X4} data is truncated.");

                var data = response.Payload.AsSpan(cursor, layout.DataLength);
                foreach (var field in layout.Fields)
                {
                    if (!TryDecode(data.Slice(field.ByteOffset, field.ByteLength), field, out var value, out var error))
                        return Invalid($"DID 0x{did:X4}/{field.Key}: {error}");
                    values.Add(new OemFreezeFrameValue(
                        did, field.Key, field.Label, value, field.Unit));
                }
                cursor += layout.DataLength;
            }

            records.Add(new OemFreezeFrameRecord(rawDtc, dtcStatus, recordNumber, values));
        }

        return new OemFreezeFrameDecodeResult(
            OemFreezeFrameReadState.Available,
            records,
            records.Count == 0
                ? "ECU-მ snapshot მოთხოვნას უპასუხა, მაგრამ snapshot record არ დააბრუნა."
                : $"{records.Count} DTC snapshot record რეალურად წაკითხულია.");

        OemFreezeFrameDecodeResult Invalid(string detail) =>
            OemFreezeFrameDecodeResult.Failure(OemFreezeFrameReadState.InvalidResponse, detail);
    }

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
            if (trimmed.Length == 0 || trimmed.Any(item => item < 0x20 || item > 0x7E))
            {
                error = "ASCII data is empty or contains non-printable bytes.";
                return false;
            }
            value = Encoding.ASCII.GetString(trimmed).Trim();
            return value.Length > 0;
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

    private static OemFreezeFrameReadState MapFailure(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode is 0x11 or 0x12 or 0x31 =>
            OemFreezeFrameReadState.NotSupported,
        UdsResponseKind.Timeout => OemFreezeFrameReadState.NoResponse,
        UdsResponseKind.Malformed => OemFreezeFrameReadState.InvalidResponse,
        UdsResponseKind.Unsupported => OemFreezeFrameReadState.NotSupported,
        _ => OemFreezeFrameReadState.Error
    };

    private static string ResponseDetail(UdsResponse response) => response.Kind switch
    {
        UdsResponseKind.Negative when response.NegativeResponseCode.HasValue =>
            $"ECU NRC 0x{response.NegativeResponseCode.Value:X2}: {response.Message}",
        UdsResponseKind.Timeout => "ECU response timeout.",
        _ => string.IsNullOrWhiteSpace(response.Message) ? "DTC snapshot response unavailable." : response.Message
    };
}
