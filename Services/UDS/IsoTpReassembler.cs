namespace CarScanner.Services.UDS;

public sealed class IsoTpReassembler
{
    public int MaxPayloadLength { get; }
    public IsoTpReassembler(int maxPayloadLength = 4096) => MaxPayloadLength = maxPayloadLength;

    public byte[] Reassemble(IEnumerable<byte[]> frames)
    {
        var list = frames.SelectMany(x => x ?? Array.Empty<byte>()).ToArray();
        if (list.Length == 0) return Array.Empty<byte>();
        var first = list[0];
        var type = (byte)(first >> 4);
        if (type == 0)
        {
            var len = first & 0x0F;
            if (len > list.Length - 1) throw new FormatException("Malformed ISO-TP single frame.");
            return list.Skip(1).Take(len).ToArray();
        }
        if (type != 1 || list.Length < 2) throw new FormatException("Malformed ISO-TP first frame.");
        var total = ((first & 0x0F) << 8) | list[1];
        if (total <= 0 || total > MaxPayloadLength) throw new InvalidOperationException("ISO-TP payload exceeds safety limit.");
        var result = new List<byte>(total);
        result.AddRange(list.Skip(2).Take(Math.Min(6, total)));
        var sequence = 1;
        var offset = 8;
        while (result.Count < total && offset < list.Length)
        {
            var pci = list[offset];
            if ((pci >> 4) != 2) throw new FormatException("Missing ISO-TP consecutive frame.");
            if ((pci & 0x0F) != sequence) throw new FormatException("ISO-TP sequence counter mismatch.");
            result.AddRange(list.Skip(offset + 1).Take(7));
            sequence = (sequence + 1) & 0x0F;
            offset += 8;
        }
        if (result.Count < total) throw new TimeoutException("ISO-TP response is incomplete.");
        return result.Take(total).ToArray();
    }
}
