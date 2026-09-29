namespace CarScanner.Services.Mileage;

public static class CanMileageDecoder
{
    public static bool TryDecode(ReadOnlySpan<byte> data, VerifiedCanMileageProfile profile,
        out double valueKm, out string error)
    {
        valueKm = 0; error = string.Empty;
        if (profile.BitLength <= 0 || profile.BitLength > 64 ||
            profile.StartBit < 0 || profile.StartBit + profile.BitLength > 64)
        { error = "Invalid CAN signal definition."; return false; }

        ulong raw = profile.LittleEndian
            ? ExtractIntel(data, profile.StartBit, profile.BitLength)
            : ExtractMotorola(data, profile.StartBit, profile.BitLength);

        valueKm = raw * profile.Factor + profile.Offset;
        if (!double.IsFinite(valueKm) || valueKm < profile.MinKm || valueKm > profile.MaxKm)
        { error = "Decoded CAN mileage is outside the verified range."; valueKm = 0; return false; }
        return true;
    }

    private static ulong ExtractIntel(ReadOnlySpan<byte> data, int startBit, int bitLength)
    {
        ulong value = 0;
        for (int i = 0; i < bitLength; i++) {
            int bit = startBit + i, bi = bit / 8, oi = bit % 8;
            if (bi >= data.Length) break;
            value |= (ulong)((data[bi] >> oi) & 1) << i;
        }
        return value;
    }

    private static ulong ExtractMotorola(ReadOnlySpan<byte> data, int startBit, int bitLength)
    {
        ulong value = 0; int current = startBit;
        for (int i = 0; i < bitLength; i++) {
            int bi = current / 8, oi = 7 - (current % 8);
            if (bi >= data.Length) break;
            value = (value << 1) | (((ulong)data[bi] >> oi) & 1UL);
            current = current % 8 == 0 ? current + 15 : current - 1;
        }
        return value;
    }
}
