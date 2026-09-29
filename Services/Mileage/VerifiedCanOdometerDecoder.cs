namespace CarScanner.Services.Mileage;

public static class VerifiedCanOdometerDecoder
{
    private const double MilesToKilometers = 1.609344;

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        VerifiedCanOdometerProfile profile,
        out double kilometers,
        out string details)
    {
        kilometers = 0;
        details = string.Empty;

        if (!TryExtractMotorola(
                data,
                profile.OdometerStartBit,
                profile.OdometerBitLength,
                out var rawOdometer))
        {
            details = "CAN odometer field is unavailable.";
            return false;
        }

        if (!TryExtractMotorola(
                data,
                profile.UnitStartBit,
                profile.UnitBitLength,
                out var unit))
        {
            details = "CAN unit field is unavailable.";
            return false;
        }

        if (unit == (ulong)profile.KmUnitValue)
        {
            kilometers = rawOdometer;
        }
        else if (unit == (ulong)profile.MilesUnitValue)
        {
            kilometers = rawOdometer * MilesToKilometers;
        }
        else
        {
            details = $"Unsupported odometer unit code: {unit}.";
            return false;
        }

        if (!double.IsFinite(kilometers) ||
            kilometers < 0 ||
            kilometers > 2_000_000)
        {
            kilometers = 0;
            details = "Decoded odometer is outside a safe range.";
            return false;
        }

        details =
            $"CAN 0x{profile.CanId:X3}, raw={rawOdometer}, " +
            $"unit={unit}, normalized={kilometers:0.0} km.";

        return true;
    }

    private static bool TryExtractMotorola(
        ReadOnlySpan<byte> data,
        int startBit,
        int bitLength,
        out ulong value)
    {
        value = 0;

        if (startBit < 0 ||
            bitLength <= 0 ||
            bitLength > 64 ||
            startBit + bitLength > data.Length * 8)
            return false;

        var current = startBit;

        for (var i = 0; i < bitLength; i++)
        {
            var byteIndex = current / 8;
            var bitIndex = 7 - (current % 8);

            if ((uint)byteIndex >= (uint)data.Length)
                return false;

            var bit =
                (data[byteIndex] >> bitIndex) & 0x01;

            value =
                (value << 1) | (uint)bit;

            current =
                current % 8 == 0
                    ? current + 15
                    : current - 1;
        }

        return true;
    }
}
