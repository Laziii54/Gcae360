namespace CarScanner.Services.Energy;

/// <summary>What the Fuel page measures for the connected car.</summary>
public enum EnergyMode
{
    Fuel,
    Hybrid,
    Electric
}

/// <summary>
/// One value inside an ECU reply (OBDb bit numbering: bit 0 = most
/// significant bit of the first data byte after the identifier echo).
/// </summary>
public sealed record EvSignal(
    string Name,
    int BitOffset,
    int BitLength,
    bool Signed,
    double Mul,
    double Div,
    double Add);

/// <summary>Pure decoding helpers for high-voltage battery values.</summary>
public static class EvSignalDecoder
{
    public const double MinimumPackVolts = 60;
    public const double MaximumPackVolts = 1000;
    public const double MaximumPackAmps = 2000;

    public static bool TryDecode(EvSignal signal, byte[] data, out double value)
    {
        value = 0;
        if (signal.BitLength is < 1 or > 32 || signal.BitOffset < 0 ||
            signal.BitOffset + signal.BitLength > data.Length * 8 ||
            Math.Abs(signal.Div) < double.Epsilon)
            return false;

        long raw = 0;
        for (var bit = 0; bit < signal.BitLength; bit++)
        {
            var absolute = signal.BitOffset + bit;
            raw = (raw << 1) | (long)((data[absolute / 8] >> (7 - absolute % 8)) & 1);
        }

        var allOnes = (1L << signal.BitLength) - 1;
        if (signal.Signed)
        {
            if ((raw & (1L << (signal.BitLength - 1))) != 0)
                raw -= 1L << signal.BitLength;
        }
        else if (signal.BitLength >= 8 && raw == allOnes)
        {
            // All bits set = "not available" in most ECUs.
            return false;
        }

        value = raw * signal.Mul / signal.Div + signal.Add;
        return double.IsFinite(value);
    }

    /// <summary>
    /// Battery current. Some catalog rows describe a signed 16-bit current as
    /// unsigned (e.g. Hyundai/Kia BMS 22 0101); a pack never carries
    /// thousands of amps, so a huge unsigned value is read as negative.
    /// </summary>
    public static bool TryDecodeCurrent(EvSignal signal, byte[] data, out double amps)
    {
        if (!TryDecode(signal, data, out amps))
            return false;

        if (!signal.Signed &&
            Math.Abs(signal.Add) < double.Epsilon &&
            signal.BitLength is 16 &&
            Math.Abs(amps) > MaximumPackAmps)
        {
            var signedSignal = signal with { Signed = true };
            if (!TryDecode(signedSignal, data, out amps))
                return false;
        }

        return IsPlausibleCurrent(amps);
    }

    public static bool TryDecodeVoltage(EvSignal signal, byte[] data, out double volts) =>
        TryDecode(signal, data, out volts) && IsPlausibleVoltage(volts);

    public static bool IsPlausibleVoltage(double volts) =>
        volts is >= MinimumPackVolts and <= MaximumPackVolts;

    public static bool IsPlausibleCurrent(double amps) =>
        double.IsFinite(amps) && Math.Abs(amps) <= MaximumPackAmps;

    /// <summary>
    /// Standard SAE J1979 PID 01 9A (Hybrid/EV battery system data).
    /// <paramref name="data"/> starts after "41 9A": A = support bits,
    /// B = charging state, C-D = voltage (1/64 V), E-F = current (0.1 A, signed).
    /// </summary>
    public static bool TryDecodeStandard9A(byte[] data, out double volts, out double amps)
    {
        volts = 0;
        amps = 0;
        if (data.Length < 6)
            return false;

        var voltageSupported = (data[0] & 0x02) != 0;
        var currentSupported = (data[0] & 0x04) != 0;
        if (!voltageSupported || !currentSupported)
            return false;

        volts = ((data[2] << 8) | data[3]) / 64d;
        amps = (short)((data[4] << 8) | data[5]) / 10d;
        return IsPlausibleVoltage(volts) && IsPlausibleCurrent(amps);
    }

    /// <summary>Standard PID 01 5B (hybrid/EV battery remaining charge).</summary>
    public static double? DecodeStandardSoc(byte[]? data) =>
        data is { Length: > 0 } ? data[0] * 100d / 255d : null;

    /// <summary>SAE J1979 PID 01 51 fuel type → page mode.</summary>
    public static EnergyMode? ModeFromFuelTypeCode(int? code) => code switch
    {
        8 => EnergyMode.Electric,
        >= 17 and <= 22 => EnergyMode.Hybrid,
        null => null,
        _ => EnergyMode.Fuel
    };

    /// <summary>Fuel type text from VIN decoding / vehicle info.</summary>
    public static EnergyMode? ModeFromText(string? fuelType)
    {
        if (string.IsNullOrWhiteSpace(fuelType) ||
            fuelType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return null;

        var text = fuelType.ToLowerInvariant();
        if (text.Contains("hybrid") || text.Contains("hev") || text.Contains("ჰიბრიდ") ||
            text.Contains("plug-in") || text.Contains("plug in") || text.Contains("erev") || text.Contains("range extender"))
            return EnergyMode.Hybrid;
        // "Electric, Gasoline" (e.g. Chevrolet Volt): an electric car with an engine = hybrid page.
        var hasElectric = text.Contains("electric") || text.Contains("ელექტრ");
        var hasEngine = text.Contains("gasoline") || text.Contains("petrol") || text.Contains("diesel") ||
                        text.Contains("ბენზ") || text.Contains("flex");
        if (hasElectric && hasEngine)
            return EnergyMode.Hybrid;
        if (text.Contains("electric") || text.Contains("bev") || text.Contains("ელექტრ"))
            return EnergyMode.Electric;
        return EnergyMode.Fuel;
    }
}

/// <summary>
/// Adds up battery energy from voltage × current samples. Energy that leaves
/// the battery is "consumed", energy that goes back in is "regenerated"; both
/// only ever grow, the net value is their difference.
///
/// Brands use opposite current signs. The sign is learned once and then
/// locked: from strong acceleration with the engine off (the battery must be
/// giving energy) and from standing still with the engine off (only
/// consumers run). Once learned the sign is locked, so a downhill (speed
/// rising while the battery charges) can never swap "used" and "recovered";
/// only the user can flip it.
/// </summary>
public sealed class EvEnergyAccumulator
{
    private const double MaximumGapSeconds = 5;
    private const int VotesNeeded = 3;
    private const double AccelerationVoteWatts = 10000;
    private const double AccelerationVoteKph = 5;

    private double _positiveWh;
    private double _negativeWh;
    private int _votesPositive;
    private int _votesNegative;
    private double? _lastSpeedKph;
    private DateTime _lastStandstillVote = DateTime.MinValue;
    private int? _lockedSign;

    /// <summary>+1 = positive current means the battery is discharging.</summary>
    public int DefaultSign { get; set; } = 1;
    public double LastVolts { get; private set; }
    public double LastAmps { get; private set; }
    public bool HasSample { get; private set; }

    /// <summary>The sign is known (learned, saved for this car, or set by the user).</summary>
    public bool IsSignConfirmed => _lockedSign is not null;

    public int Sign => _lockedSign ?? DefaultSign;

    /// <summary>Raised once when the sign is learned, so it can be saved for this car.</summary>
    public event Action<int>? SignLearned;

    /// <summary>kW out of the battery (+) or into it (−).</summary>
    public double PowerKw => HasSample ? LastVolts * LastAmps * Sign / 1000d : 0;

    public double ConsumedKWh => (Sign > 0 ? _positiveWh : _negativeWh) / 1000d;
    public double RegeneratedKWh => (Sign > 0 ? _negativeWh : _positiveWh) / 1000d;
    public double NetKWh => ConsumedKWh - RegeneratedKWh;

    /// <summary>Uses a sign saved for this car (or chosen by the user); stops learning.</summary>
    public void LockSign(int sign) => _lockedSign = sign >= 0 ? 1 : -1;

    /// <summary>Another car: learn its sign again.</summary>
    public void ForgetSign()
    {
        _lockedSign = null;
        _votesPositive = _votesNegative = 0;
    }

    /// <summary>"Used" and "recovered" were swapped: the user flips the direction.</summary>
    public int FlipSign()
    {
        _lockedSign = -Sign;
        return _lockedSign.Value;
    }

    public void Reset()
    {
        _positiveWh = _negativeWh = 0;
        _lastSpeedKph = null;
        LastVolts = LastAmps = 0;
        HasSample = false;
        // A locked sign belongs to the car, not to the trip: keep it.
        if (_lockedSign is null)
            _votesPositive = _votesNegative = 0;
    }

    /// <param name="seconds">Time since the previous sample (0 for the first one).</param>
    /// <param name="speedKph">Latest vehicle speed, used to learn the current sign.</param>
    /// <param name="engineRunning">Plug-in/hybrid engine is running (it can charge the battery).</param>
    public void Add(double volts, double amps, double seconds, double? speedKph, bool engineRunning = false)
    {
        if (!EvSignalDecoder.IsPlausibleVoltage(volts) || !EvSignalDecoder.IsPlausibleCurrent(amps))
            return;

        LastVolts = volts;
        LastAmps = amps;
        HasSample = true;

        var watts = volts * amps;
        if (seconds > 0 && seconds <= MaximumGapSeconds)
        {
            var wattHours = watts * seconds / 3600d;
            if (wattHours >= 0)
                _positiveWh += wattHours;
            else
                _negativeWh -= wattHours;
        }

        if (speedKph is { } speed)
        {
            if (_lockedSign is null && !engineRunning)
                Learn(watts, speed);
            _lastSpeedKph = speed;
        }
    }

    private void Learn(double watts, double speed)
    {
        // Strong acceleration with the engine off: the battery is discharging.
        if (_lastSpeedKph is { } previous &&
            speed > 5 &&
            speed - previous >= AccelerationVoteKph &&
            Math.Abs(watts) >= AccelerationVoteWatts)
        {
            Vote(watts);
        }
        // Standing still with the engine off: only consumers (A/C, 12 V) draw power.
        else if (speed < 1 &&
                 Math.Abs(watts) is >= 300 and <= 8000 &&
                 DateTime.UtcNow - _lastStandstillVote >= TimeSpan.FromSeconds(5))
        {
            _lastStandstillVote = DateTime.UtcNow;
            Vote(watts);
        }

        var leading = Math.Max(_votesPositive, _votesNegative);
        var trailing = Math.Min(_votesPositive, _votesNegative);
        if (leading >= VotesNeeded && leading >= trailing * 3)
        {
            _lockedSign = _votesNegative > _votesPositive ? -1 : 1;
            SignLearned?.Invoke(_lockedSign.Value);
        }
    }

    private void Vote(double watts)
    {
        if (watts > 0)
            _votesPositive++;
        else
            _votesNegative++;
    }

    public static double? KWhPer100Km(double kilowattHours, double kilometers) =>
        kilometers >= 0.1 ? kilowattHours / kilometers * 100d : null;

    public static double? InstantKWhPer100Km(double powerKw, double speedKph) =>
        speedKph > 5 ? powerKw / speedKph * 100d : null;
}
