using System.Text.Json;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Oil-service reminder kept by the app (for cars that do not report one).</summary>
public sealed record AppOilReminderStatus(
    bool IsStarted,
    double? KmDriven,
    double? KmLeft,
    int DaysLeft,
    int IntervalKm,
    int IntervalDays,
    double Fraction,
    DateTime ChangedLocal,
    bool OdometerAvailable = false);

/// <summary>
/// App-side oil reminder for cars that do not report their own oil service
/// but do report the standard odometer (OBD PID 01 A6, mostly 2019+).
/// The user presses "Oil changed" once; distance is then exact whenever the
/// car is connected again (no need to open the app often). Cars without an
/// odometer do not get this card, so no approximate number is ever shown.
/// Nothing is written to the car; the state is stored on the phone per VIN.
/// </summary>
public sealed class AppOilReminderService
{
    private const string KeyPrefix = "app_oil_reminder_v1_";
    private readonly OBDService _obd;

    public AppOilReminderService(OBDService obd) => _obd = obd;

    private sealed class State
    {
        public DateTime ChangedUtc { get; set; }
        public int IntervalKm { get; set; }
        public int IntervalDays { get; set; }
        public double? OdometerAtChange { get; set; }
        public double DistanceKm { get; set; }
        public bool HasDistance { get; set; }
    }

    /// <summary>Typical oil interval by make (km). Long-life European brands use 15 000 km.</summary>
    public static int DefaultIntervalKm(string? make)
    {
        var value = (make ?? string.Empty).ToLowerInvariant();
        string[] longLife =
        {
            "bmw", "mini", "mercedes", "audi", "volkswagen", "vw", "skoda", "seat", "cupra",
            "porsche", "volvo", "land rover", "jaguar", "smart"
        };
        return longLife.Any(value.Contains) ? 15000 : 10000;
    }

    public const int DefaultIntervalDays = 365;

    /// <summary>Stored status without talking to the car.</summary>
    public AppOilReminderStatus Peek(string vehicleKey, string? make)
    {
        var state = Load(vehicleKey);
        return state is null ? NotStarted(make) : ToStatus(state);
    }

    /// <summary>
    /// Reads the odometer (if connected) and returns the updated status.
    /// <see cref="AppOilReminderStatus.OdometerAvailable"/> tells whether this
    /// car reports an odometer at all.
    /// </summary>
    public async Task<AppOilReminderStatus> UpdateAsync(string vehicleKey, string? make, CancellationToken token)
    {
        var odometer = _obd.IsInitialized ? await ReadOdometerAsync(token).ConfigureAwait(false) : null;
        var state = Load(vehicleKey);
        if (state is null)
            return NotStarted(make) with { OdometerAvailable = odometer.HasValue };

        if (odometer.HasValue && state.OdometerAtChange.HasValue && odometer.Value >= state.OdometerAtChange.Value)
        {
            state.DistanceKm = odometer.Value - state.OdometerAtChange.Value;
            state.HasDistance = true;
            Save(vehicleKey, state);
        }

        return ToStatus(state) with { OdometerAvailable = odometer.HasValue || state.OdometerAtChange.HasValue };
    }

    /// <summary>Starts a new oil interval now (after an oil change).</summary>
    public async Task<AppOilReminderStatus> MarkOilChangedAsync(string vehicleKey, string? make, CancellationToken token)
    {
        var previous = Load(vehicleKey);
        var state = new State
        {
            ChangedUtc = DateTime.UtcNow,
            IntervalKm = previous is { IntervalKm: > 0 } ? previous.IntervalKm : DefaultIntervalKm(make),
            IntervalDays = previous is { IntervalDays: > 0 } ? previous.IntervalDays : DefaultIntervalDays
        };

        var odometer = _obd.IsInitialized ? await ReadOdometerAsync(token).ConfigureAwait(false) : null;
        if (odometer is null)
            return ToStatus(state) with { IsStarted = false, OdometerAvailable = false };

        state.OdometerAtChange = odometer;
        state.HasDistance = true;
        Save(vehicleKey, state);
        return ToStatus(state) with { OdometerAvailable = true };
    }

    private async Task<double?> ReadOdometerAsync(CancellationToken token)
    {
        try
        {
            var odometer = await _obd.ReadStandardOdometerAsync(token).ConfigureAwait(false);
            return odometer is > 0 and < 2_000_000 ? odometer : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static AppOilReminderStatus ToStatus(State state)
    {
        var daysUsed = (int)Math.Floor((DateTime.UtcNow - state.ChangedUtc).TotalDays);
        var daysLeft = state.IntervalDays - daysUsed;
        double? kmDriven = state.HasDistance ? state.DistanceKm : null;
        double? kmLeft = kmDriven.HasValue ? state.IntervalKm - kmDriven.Value : null;

        var dayFraction = (double)daysLeft / Math.Max(1, state.IntervalDays);
        var kmFraction = kmLeft.HasValue ? kmLeft.Value / Math.Max(1, state.IntervalKm) : 1d;
        var fraction = Math.Clamp(Math.Min(dayFraction, kmFraction), 0d, 1d);

        return new AppOilReminderStatus(
            true,
            kmDriven,
            kmLeft,
            daysLeft,
            state.IntervalKm,
            state.IntervalDays,
            fraction,
            state.ChangedUtc.ToLocalTime());
    }

    private static AppOilReminderStatus NotStarted(string? make) =>
        new(false, null, null, DefaultIntervalDays, DefaultIntervalKm(make), DefaultIntervalDays, 0, DateTime.Now);

    private static State? Load(string vehicleKey)
    {
        try
        {
            var json = Preferences.Default.Get(KeyPrefix + vehicleKey, string.Empty);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<State>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void Save(string vehicleKey, State state)
    {
        try
        {
            Preferences.Default.Set(KeyPrefix + vehicleKey, JsonSerializer.Serialize(state));
        }
        catch
        {
        }
    }
}
