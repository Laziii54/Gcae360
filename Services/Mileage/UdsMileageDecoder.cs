namespace CarScanner.Services.Mileage;

public static class UdsMileageDecoder
{
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        VerifiedUdsMileageProfile profile,
        out double valueKm,
        out string error)
    {
        valueKm = 0;
        error = string.Empty;

        if (profile.DataOffset < 0 ||
            profile.DataLength <= 0 ||
            profile.DataOffset + profile.DataLength > payload.Length)
        {
            error = "UDS response does not contain the configured mileage field.";
            return false;
        }

        var field =
            payload.Slice(
                profile.DataOffset,
                profile.DataLength);

        ulong raw;

        try
        {
            raw =
                profile.ByteOrder.Equals(
                    "little",
                    StringComparison.OrdinalIgnoreCase)
                    ? ReadLittleEndian(field)
                    : ReadBigEndian(field);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (profile.NumericEncoding.Equals(
                "bcd",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!TryBcdToUInt(field, profile.ByteOrder, out raw))
            {
                error = "Invalid BCD mileage payload.";
                return false;
            }
        }

        valueKm =
            raw * profile.Scale +
            profile.Offset;

        if (!double.IsFinite(valueKm) ||
            valueKm < profile.MinKm ||
            valueKm > profile.MaxKm)
        {
            error = "Decoded mileage is outside the verified profile range.";
            valueKm = 0;
            return false;
        }

        return true;
    }

    private static ulong ReadBigEndian(
        ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;

        foreach (var b in bytes)
            value = (value << 8) | b;

        return value;
    }

    private static ulong ReadLittleEndian(
        ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;

        for (var i = bytes.Length - 1; i >= 0; i--)
            value = (value << 8) | bytes[i];

        return value;
    }

    private static bool TryBcdToUInt(
        ReadOnlySpan<byte> bytes,
        string byteOrder,
        out ulong value)
    {
        value = 0;

        IEnumerable<byte> sequence =
            byteOrder.Equals(
                "little",
                StringComparison.OrdinalIgnoreCase)
                ? bytes.ToArray()
                : bytes.ToArray().Reverse();

        ulong multiplier = 1;

        foreach (var b in sequence)
        {
            var lo = b & 0x0F;
            var hi = (b >> 4) & 0x0F;

            if (lo > 9 || hi > 9)
                return false;

            value += (ulong)lo * multiplier;
            multiplier *= 10;

            value += (ulong)hi * multiplier;
            multiplier *= 10;
        }

        return true;
    }
}
