using CarScanner.Services.OBD;
using CarScanner.Services.UDS;

namespace CarScanner.Services.Mileage;

/// <summary>Where the number sits in a documented read-only (0x22 / 0x21) reply and how it is scaled.</summary>
public sealed record OdometerSignalSpec(
    int BitOffset,
    int BitLength,
    double Mul,
    double Div,
    double Add,
    bool Miles,
    bool Meters);

public enum OdometerDecodeFailure
{
    None,
    NoReply,
    Negative,
    NotPositiveForRequest,
    TooShort,
    BadScaling,
    NotAvailable,
    Implausible,

    /// <summary>Frames came back, but only from another module's reply id: not this module's answer.</summary>
    ForeignReply
}

public sealed record OdometerDecodeResult(
    OdometerDecodeFailure Failure,
    double? Km,
    double? RawValue,
    string Unit)
{
    public bool IsValue => Failure == OdometerDecodeFailure.None && Km.HasValue;
}

/// <summary>
/// Pure decoding of a UDS / KWP read reply that carries an odometer. Nothing here sends anything.
/// The reply must be a positive reply to exactly this service and identifier; anything else is rejected.
/// </summary>
public static class OdometerResponseDecoder
{
    private static readonly IsoTpReassembler IsoTp = new();

    /// <summary>The data bytes that follow "(service+0x40) identifier", or null when the reply is not that positive reply.</summary>
    public static byte[]? ExtractPayload(string? raw, int serviceId, int identifier, int? receiveCanId, out OdometerDecodeFailure failure) =>
        ExtractPayload(raw, serviceId, identifier, receiveCanId, out failure, out _);

    /// <param name="responderCanId">
    /// The CAN id the accepted frames came from, when the adapter printed ids. With a known <paramref name="receiveCanId"/>
    /// only that id is accepted; without one, any responder that echoes the identifier is accepted and named here so the
    /// caller does not attribute the value to the module that was merely addressed.
    /// </param>
    public static byte[]? ExtractPayload(string? raw, int serviceId, int identifier, int? receiveCanId, out OdometerDecodeFailure failure, out int? responderCanId)
    {
        failure = OdometerDecodeFailure.None;
        responderCanId = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            failure = OdometerDecodeFailure.NoReply;
            return null;
        }

        if (ObdResponseParser.IsNegative(raw))
        {
            failure = OdometerDecodeFailure.Negative;
            return null;
        }

        // A reply that carries ids, none of them the asked module's, belongs to somebody else.
        if (ObdResponseParser.IsOnlyFromOtherModules(raw, receiveCanId, out var senders))
        {
            failure = OdometerDecodeFailure.ForeignReply;
            responderCanId = senders[0];
            return null;
        }

        var positive = (byte)(serviceId + 0x40);
        var idBytes = serviceId == 0x21
            ? new[] { (byte)identifier }
            : new[] { (byte)(identifier >> 8), (byte)identifier };

        var candidates = new List<(int? Id, IReadOnlyList<byte[]> Frames)>();
        if (receiveCanId.HasValue)
        {
            candidates.Add((receiveCanId.Value, ObdResponseParser.ParseCanFrames(raw, receiveCanId.Value)));
        }
        else
        {
            foreach (var id in ObdResponseParser.ExtractCanIdentifiers(raw))
                candidates.Add((id, ObdResponseParser.ParseCanFrames(raw, id)));
            candidates.Add((null, ObdResponseParser.ParseCanFrames(raw)));
        }

        foreach (var (candidateId, frames) in candidates)
        {
            if (frames.Count == 0 || frames[0].Length == 0)
                continue;

            byte[] message;
            try
            {
                message = (frames[0][0] >> 4) is 0 or 1
                    ? IsoTp.Reassemble(frames)
                    : frames.SelectMany(frame => frame).ToArray();
            }
            catch
            {
                continue;
            }

            if (Matches(message, positive, idBytes, out var data))
            {
                responderCanId = candidateId;
                return data;
            }
        }

        // Adapter already removed the framing (no ids printed at all).
        if (Matches(ObdResponseParser.ParseHex(raw), positive, idBytes, out var plain))
            return plain;

        // No positive reply: was it an explicit negative reply (7F <service> <code>) for this request?
        var stream = ObdResponseParser.ParseHex(raw);
        for (var i = 0; i + 1 < stream.Length; i++)
        {
            if (stream[i] == 0x7F && stream[i + 1] == (byte)serviceId)
            {
                failure = OdometerDecodeFailure.Negative;
                return null;
            }
        }

        failure = OdometerDecodeFailure.NotPositiveForRequest;
        return null;
    }

    /// <summary>
    /// true when the CAN id that answered cannot be the module that was addressed. With a known reply id that is any other id.
    /// Without one, ISO 15765-4 still fixes the pairs 0x7E0–0x7E7 → +8, and the ids 0x7E8–0x7EF belong to those OBD ECUs: a
    /// request to any other address (e.g. an instrument cluster at 0x7C0) answered from 0x7E8 came from the engine/OBD ECU.
    /// Functional requests (0x7DF) and 29-bit ids are not judged.
    /// </summary>
    public static bool IsResponderForeign(int transmitCanId, int? expectedReceiveCanId, int? responderCanId)
    {
        if (responderCanId is not { } responder || responder > 0x7FF || transmitCanId > 0x7FF)
            return false;
        if (expectedReceiveCanId is { } expected)
            return responder != expected;
        if (transmitCanId == 0x7DF)
            return false;
        if (transmitCanId is >= 0x7E0 and <= 0x7E7)
            return responder != transmitCanId + 8;
        return responder is >= 0x7E8 and <= 0x7EF;
    }

    private static bool Matches(byte[] bytes, byte positive, byte[] idBytes, out byte[] data)
    {
        data = Array.Empty<byte>();
        for (var i = 0; i + idBytes.Length < bytes.Length; i++)
        {
            if (bytes[i] != positive)
                continue;

            var ok = true;
            for (var j = 0; j < idBytes.Length && ok; j++)
                ok = bytes[i + 1 + j] == idBytes[j];

            if (!ok)
                continue;

            data = bytes[(i + 1 + idBytes.Length)..];
            return data.Length > 0;
        }

        return false;
    }

    public static OdometerDecodeResult Decode(OdometerSignalSpec signal, byte[] data)
    {
        var unit = signal.Miles ? "mi" : "km";

        if (signal.BitOffset < 0 || signal.BitLength is < 1 or > 64 || Math.Abs(signal.Div) < double.Epsilon ||
            !double.IsFinite(signal.Mul) || !double.IsFinite(signal.Div) || !double.IsFinite(signal.Add))
            return new OdometerDecodeResult(OdometerDecodeFailure.BadScaling, null, null, unit);

        if (signal.BitOffset + signal.BitLength > data.Length * 8)
            return new OdometerDecodeResult(OdometerDecodeFailure.TooShort, null, null, unit);

        ulong raw = 0;
        for (var bit = 0; bit < signal.BitLength; bit++)
        {
            var absolute = signal.BitOffset + bit;
            raw = (raw << 1) | (((ulong)data[absolute / 8] >> (7 - absolute % 8)) & 1UL);
        }

        // All bits set = "not available" in most ECUs.
        if (raw == (signal.BitLength >= 64 ? ulong.MaxValue : (1UL << signal.BitLength) - 1))
            return new OdometerDecodeResult(OdometerDecodeFailure.NotAvailable, null, null, unit);

        var value = raw * signal.Mul / signal.Div + signal.Add;
        if (!double.IsFinite(value))
            return new OdometerDecodeResult(OdometerDecodeFailure.BadScaling, null, null, unit);

        var km = signal.Miles ? MileagePlausibility.MilesToKm(value) : signal.Meters ? value / 1000d : value;
        // The reported number keeps the unit the module uses (metres are shown as kilometres).
        var reported = signal.Meters ? value / 1000d : value;
        var reportedUnit = signal.Miles ? "mi" : "km";
        if (!MileagePlausibility.IsPlausibleOdometerKm(km) || km < 1)
            return new OdometerDecodeResult(OdometerDecodeFailure.Implausible, km, reported, reportedUnit);

        return new OdometerDecodeResult(OdometerDecodeFailure.None, km, reported, reportedUnit);
    }
}
