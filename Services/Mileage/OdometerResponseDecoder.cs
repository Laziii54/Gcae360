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
    Implausible
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
    public static byte[]? ExtractPayload(string? raw, int serviceId, int identifier, int? receiveCanId, out OdometerDecodeFailure failure)
    {
        failure = OdometerDecodeFailure.None;
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

        var positive = (byte)(serviceId + 0x40);
        var idBytes = serviceId == 0x21
            ? new[] { (byte)identifier }
            : new[] { (byte)(identifier >> 8), (byte)identifier };

        var candidates = new List<IReadOnlyList<byte[]>>();
        if (receiveCanId.HasValue)
            candidates.Add(ObdResponseParser.ParseCanFrames(raw, receiveCanId.Value));
        foreach (var id in ObdResponseParser.ExtractCanIdentifiers(raw))
            candidates.Add(ObdResponseParser.ParseCanFrames(raw, id));
        candidates.Add(ObdResponseParser.ParseCanFrames(raw));

        foreach (var frames in candidates)
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
                return data;
        }

        // Adapter already removed the framing.
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
