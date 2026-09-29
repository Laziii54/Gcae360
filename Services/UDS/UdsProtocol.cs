using CarScanner.Services.OBD;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services.UDS;

/// <summary>
/// UDS over ISO-TP using a physically addressed, verified CAN TX/RX pair.
/// The ELM327 handles outgoing PCI formatting and response flow control; this
/// class independently validates and reassembles the returned ISO-TP frames.
/// </summary>
public sealed class UdsProtocol
{
    private readonly Elm327Protocol _transport;
    private readonly ILogger<UdsProtocol> _logger;
    private readonly IsoTpReassembler _isoTp = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UdsProtocol(Elm327Protocol transport, ILogger<UdsProtocol> logger)
    {
        _transport = transport;
        _logger = logger;
    }

    public Task<UdsResponse> SendSingleFrameAsync(
        byte service,
        byte[] data,
        CancellationToken token = default)
        => SendAsync(new[] { service }.Concat(data ?? Array.Empty<byte>()).ToArray(), token);

    /// <summary>
    /// UDS without a verified physical ECU address is deliberately rejected.
    /// Sending a manufacturer request on the generic functional OBD header can
    /// wake or address multiple controllers and is not safe discovery.
    /// </summary>
    public Task<UdsResponse> SendAsync(byte[] requestPayload, CancellationToken token = default)
    {
        if (requestPayload is null || requestPayload.Length == 0)
            throw new ArgumentException("UDS request cannot be empty.", nameof(requestPayload));

        return Task.FromResult(new UdsResponse(
            UdsResponseKind.Unsupported,
            requestPayload[0],
            Array.Empty<byte>(),
            Message: "UDS requires a verified physical ECU TX/RX address. Use SendIsoTpAsync."));
    }

    public Task<UdsResponse> SendIsoTpAsync(
        byte[] requestPayload,
        int transmitCanId,
        int receiveCanId,
        CancellationToken token = default)
        => SendIsoTpAsync(requestPayload, transmitCanId, receiveCanId, extended29Bit: false, token);

    public async Task<UdsResponse> SendIsoTpAsync(
        byte[] requestPayload,
        int transmitCanId,
        int receiveCanId,
        bool extended29Bit,
        CancellationToken token = default)
        => await SendIsoTpAsync(
            requestPayload,
            transmitCanId,
            receiveCanId,
            extended29Bit,
            TimeSpan.FromSeconds(5),
            token).ConfigureAwait(false);

    public async Task<UdsResponse> SendIsoTpAsync(
        byte[] requestPayload,
        int transmitCanId,
        int receiveCanId,
        bool extended29Bit,
        TimeSpan responseTimeout,
        CancellationToken token = default)
    {
        if (requestPayload is null || requestPayload.Length == 0)
            throw new ArgumentException("UDS request cannot be empty.", nameof(requestPayload));
        if (responseTimeout < TimeSpan.FromMilliseconds(250) ||
            responseTimeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));

        // This path is used for read-only discovery and identification. Do not
        // pretend a low-cost ELM clone can safely segment arbitrary writes.
        if (requestPayload.Length > 7)
            return new UdsResponse(
                UdsResponseKind.Unsupported,
                requestPayload[0],
                Array.Empty<byte>(),
                Message: "Segmented outgoing UDS requests require a verified adapter-specific transport.");

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var safeToRetry = requestPayload[0] is 0x19 or 0x22;
            var attempts = safeToRetry ? 2 : 1;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var raw = await _transport.SendCanIsoTpPayloadAsync(
                        requestPayload,
                        transmitCanId,
                        receiveCanId,
                        extended29Bit,
                        responseTimeout,
                        token).ConfigureAwait(false);

                    var response = ParseRaw(raw, receiveCanId);
                    response = ValidateResponse(response, requestPayload);

                    if (attempt < attempts &&
                        (response.Kind == UdsResponseKind.Timeout ||
                         response.NegativeResponseCode == 0x21))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
                        continue;
                    }

                    return response;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Elm327TimeoutException ex)
                {
                    if (attempt < attempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
                        continue;
                    }

                    return new UdsResponse(
                        UdsResponseKind.Timeout,
                        requestPayload[0],
                        Array.Empty<byte>(),
                        Message: ex.Message);
                }
                catch (NotSupportedException ex)
                {
                    return new UdsResponse(
                        UdsResponseKind.Unsupported,
                        requestPayload[0],
                        Array.Empty<byte>(),
                        Message: ex.Message);
                }
                catch (Elm327CommandException ex)
                {
                    return new UdsResponse(
                        UdsResponseKind.Unsupported,
                        requestPayload[0],
                        ObdResponseParser.ParseHex(ex.RawResponse),
                        Message: ex.Message);
                }
            }

            return new UdsResponse(
                UdsResponseKind.Timeout,
                requestPayload[0],
                Array.Empty<byte>(),
                Message: "UDS request timed out.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ISO-TP UDS request failed");
            return new UdsResponse(
                UdsResponseKind.Malformed,
                requestPayload[0],
                Array.Empty<byte>(),
                Message: ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private UdsResponse ParseRaw(string raw, int receiveCanId)
    {
        if (ObdResponseParser.IsNegative(raw))
            return new UdsResponse(
                UdsResponseKind.Timeout,
                0,
                ObdResponseParser.ParseHex(raw),
                Message: string.IsNullOrWhiteSpace(raw) ? "Empty UDS response." : raw);

        var frames = ObdResponseParser.ParseCanFrames(raw, receiveCanId);
        if (frames.Count == 0)
            return new UdsResponse(
                UdsResponseKind.Timeout,
                0,
                Array.Empty<byte>(),
                Message: "Empty UDS response.");

        var messages = ReassembleMessages(frames);
        if (messages.Count == 0)
            return new UdsResponse(
                UdsResponseKind.Malformed,
                0,
                frames.SelectMany(frame => frame).ToArray(),
                Message: "No complete ISO-TP message was returned by the ECU.");

        // Some ECUs emit 7F xx 78 (response pending) before their final reply.
        // Prefer the last completed non-pending message, but preserve a lone
        // pending response as an honest negative result.
        UdsResponse? pending = null;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var parsed = UdsResponseParser.Parse(messages[i]);
            if (parsed.NegativeResponseCode == 0x78)
            {
                pending ??= parsed;
                continue;
            }

            return parsed;
        }

        return pending ?? new UdsResponse(
            UdsResponseKind.Malformed,
            0,
            Array.Empty<byte>(),
            Message: "UDS response could not be parsed.");
    }

    private IReadOnlyList<byte[]> ReassembleMessages(IReadOnlyList<byte[]> frames)
    {
        var messages = new List<byte[]>();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Length == 0)
                continue;

            var frameType = frame[0] >> 4;
            if (frameType == 0)
            {
                messages.Add(_isoTp.Reassemble(new[] { frame }));
                continue;
            }

            if (frameType == 1)
            {
                if (frame.Length < 2)
                    throw new FormatException("Malformed ISO-TP First Frame.");

                var expectedLength = ((frame[0] & 0x0F) << 8) | frame[1];
                var segment = new List<byte[]> { frame };
                var available = Math.Max(0, frame.Length - 2);

                while (available < expectedLength && i + 1 < frames.Count)
                {
                    var next = frames[i + 1];
                    if (next.Length == 0 || (next[0] >> 4) != 2)
                        break;

                    segment.Add(next);
                    available += Math.Max(0, next.Length - 1);
                    i++;
                }

                messages.Add(_isoTp.Reassemble(segment));
                continue;
            }

            if (frameType == 3)
                continue; // Flow Control is transport metadata, not UDS data.

            // Some adapters strip PCI bytes even with headers enabled. In that
            // case the frame begins directly with 0x4x/0x6x/0x7F UDS data.
            messages.Add(frame);
        }

        return messages;
    }

    private static UdsResponse ValidateResponse(UdsResponse response, byte[] requestPayload)
    {
        var requestService = requestPayload[0];
        if (response.Kind == UdsResponseKind.Positive)
        {
            var expected = (byte)(requestService + 0x40);
            if (response.Service != expected)
                return new UdsResponse(
                    UdsResponseKind.Malformed,
                    response.Service,
                    response.Payload,
                    Message: $"Unexpected UDS service 0x{response.Service:X2}; expected 0x{expected:X2}.");

            if (requestService == 0x22 &&
                (requestPayload.Length != 3 || response.Payload.Length < 2 ||
                 response.Payload[0] != requestPayload[1] || response.Payload[1] != requestPayload[2]))
            {
                return new UdsResponse(
                    UdsResponseKind.Malformed,
                    response.Service,
                    response.Payload,
                    Message: "ReadDataByIdentifier response did not echo the requested DID.");
            }

            if (requestService == 0x19 && requestPayload.Length >= 2 &&
                (response.Payload.Length == 0 || response.Payload[0] != requestPayload[1]))
            {
                return new UdsResponse(
                    UdsResponseKind.Malformed,
                    response.Service,
                    response.Payload,
                    Message: "ReadDTCInformation response did not echo the requested sub-function.");
            }
        }
        else if (response.Kind == UdsResponseKind.Negative && response.Service != requestService)
        {
            return new UdsResponse(
                UdsResponseKind.Malformed,
                response.Service,
                response.Payload,
                response.NegativeResponseCode,
                $"Negative response belongs to service 0x{response.Service:X2}, not 0x{requestService:X2}.");
        }

        return response;
    }
}
