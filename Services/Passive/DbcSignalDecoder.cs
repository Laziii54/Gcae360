namespace CarScanner.Services.Passive;

/// <summary>A signal definition from a public CAN database (opendbc), as stored in the catalog.</summary>
public sealed record DbcSignal(
    string DbcFile,
    int MessageId,
    string Name,
    int StartBit,
    int BitLength,
    bool BigEndian,
    bool IsSigned,
    double Factor,
    double Offset,
    double? Minimum,
    double? Maximum,
    string Unit);

/// <summary>Pure bit-level decoding of a DBC signal from the data bytes of one CAN frame.</summary>
public static class DbcSignalDecoder
{
    /// <summary>
    /// Raw integer of the signal, or null when the frame is too short or the definition is unusable.
    /// DBC byte order 1 = Intel (little endian), 0 = Motorola (big endian, start bit = MSB in sawtooth numbering).
    /// </summary>
    public static long? ReadRaw(DbcSignal signal, ReadOnlySpan<byte> data)
    {
        if (signal.BitLength is < 1 or > 64 || signal.StartBit < 0)
            return null;

        ulong value = 0;
        if (!signal.BigEndian)
        {
            if (signal.StartBit + signal.BitLength > data.Length * 8)
                return null;

            for (var i = 0; i < signal.BitLength; i++)
            {
                var bit = signal.StartBit + i;
                if ((data[bit / 8] >> (bit % 8) & 1) != 0)
                    value |= 1UL << i;
            }
        }
        else
        {
            var position = signal.StartBit;
            for (var i = 0; i < signal.BitLength; i++)
            {
                if (position < 0 || position / 8 >= data.Length)
                    return null;

                value = value << 1 | (uint)(data[position / 8] >> (position % 8) & 1);
                position = position % 8 == 0 ? position + 15 : position - 1;
            }
        }

        if (signal.IsSigned && signal.BitLength < 64 && (value >> (signal.BitLength - 1) & 1) != 0)
            value |= ulong.MaxValue << signal.BitLength;

        return unchecked((long)value);
    }

    /// <summary>Physical value, or null when it cannot be decoded or falls outside the database's own min/max.</summary>
    public static double? Decode(DbcSignal signal, ReadOnlySpan<byte> data)
    {
        var raw = ReadRaw(signal, data);
        if (raw is null)
            return null;

        var physical = signal.IsSigned || signal.BitLength < 63
            ? raw.Value * signal.Factor + signal.Offset
            : unchecked((ulong)raw.Value) * signal.Factor + signal.Offset;

        if (double.IsNaN(physical) || double.IsInfinity(physical))
            return null;

        // The database states the legal range; a value outside it means this is not the signal we think it is.
        if (signal.Minimum is { } min && signal.Maximum is { } max && max > min)
        {
            var tolerance = Math.Abs(signal.Factor) * 0.5 + 1e-9;
            if (physical < min - tolerance || physical > max + tolerance)
                return null;
        }

        return physical;
    }
}
