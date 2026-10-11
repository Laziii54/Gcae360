using CarScanner.Services.OBD;
using CarScanner.Services.UDS;

namespace CarScanner.Services.Coding;

/// <summary>What one request to one module got back, for the Coding page.</summary>
/// <param name="Data">Bytes after "service+0x40 identifier" of a positive reply (empty array = positive reply without data); null otherwise.</param>
/// <param name="Error">"no answer", "NRC xx", or "reply from another module (0x…)"; empty when <paramref name="Data"/> is not null.</param>
/// <param name="ForeignSender">CAN id of the module that actually answered when it was not the one asked.</param>
public sealed record CodingReply(byte[]? Data, string Error, byte? Nrc, int? ForeignSender = null)
{
    public bool IsForeign => ForeignSender.HasValue;
}

/// <summary>
/// Reads a module's answer for the Coding page (backups, the read-back after a write, restore).
/// The rule that matters: a reply counts only when it carries the asked module's reply id. Real captures show the
/// engine ECU's frames (0x7E8) delivered for requests to 20+ other addresses; storing those under another module,
/// or taking a foreign "6E &lt;DID&gt;" as the acknowledgement of a write, would be wrong in the worst possible place.
/// </summary>
public static class CodingReplyParser
{
    private static readonly IsoTpReassembler IsoTp = new();

    public static CodingReply Parse(string? raw, int? receiveCanId, byte service, byte[] idBytes)
    {
        if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
            return new CodingReply(null, "no answer", null);

        if (ObdResponseParser.IsOnlyFromOtherModules(raw, receiveCanId, out var senders))
            return new CodingReply(null, $"reply from another module (0x{senders[0]:X3})", null, senders[0]);

        var hasIds = ObdResponseParser.ExtractCanIdentifiers(raw).Count > 0;
        var candidates = new List<IReadOnlyList<byte[]>>();
        // With a known reply id only that id's frames are read; without one (or without ids at all) the old behaviour stays.
        candidates.Add(receiveCanId.HasValue ? ObdResponseParser.ParseCanFrames(raw, receiveCanId.Value) : ObdResponseParser.ParseCanFrames(raw));

        var messages = new List<byte[]>();
        foreach (var frames in candidates)
        {
            if (frames.Count == 0 || frames[0].Length == 0)
                continue;
            try
            {
                messages.Add((frames[0][0] >> 4) is 0 or 1
                    ? IsoTp.Reassemble(frames)
                    : frames.SelectMany(f => f).ToArray());
            }
            catch
            {
                // try the next interpretation
            }

            // Single frames each carry their own PCI byte (e.g. 03 7F 22 78 then 05 62 …).
            foreach (var frame in frames)
                if (frame.Length > 1 && (frame[0] >> 4) == 0)
                    messages.Add(frame.Skip(1).Take(frame[0] & 0x0F).ToArray());
        }

        // Adapter already removed the framing and the ids: the stream is all there is to judge.
        if (!hasIds)
            messages.Add(ObdResponseParser.ParseHex(raw));

        foreach (var message in messages)
        {
            if (message.Length >= 1 + idBytes.Length && message[0] == (byte)(service + 0x40) &&
                message.Skip(1).Take(idBytes.Length).SequenceEqual(idBytes))
                return new CodingReply(message.Skip(1 + idBytes.Length).ToArray(), string.Empty, null);
        }

        foreach (var message in messages)
        {
            if (message.Length >= 3 && message[0] == 0x7F && message[1] == service && message[2] != 0x78)
                return new CodingReply(null, $"NRC {message[2]:X2}", message[2]);
        }

        return new CodingReply(null, "no answer", null);
    }
}
