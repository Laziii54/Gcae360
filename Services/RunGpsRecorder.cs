namespace CarScanner.Services;

/// <summary>
/// Records the phone's GPS speed while a performance run is armed, so the
/// ECU speed can be checked against an independent source (ranking rules).
/// Coordinates are kept only with the run on the phone and are sent to the
/// server only when a ranking result is published.
/// </summary>
public sealed class RunGpsRecorder
{
    private readonly object _gate = new();
    private readonly List<RunGpsSample> _samples = new();
    private bool _listening;
    private Location? _previous;

    public bool IsAvailable { get; private set; }

    /// <summary>Every GPS speed sample (UTC, km/h) — used for timing without an OBD adapter.</summary>
    public event Action<DateTime, double>? SpeedSample;

    /// <summary>Starts listening (asks for location permission the first time).</summary>
    public async Task StartAsync()
    {
        lock (_gate)
        {
            _samples.Clear();
            _previous = null;
        }

        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var current = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                return current == PermissionStatus.Granted
                    ? current
                    : await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            });

            if (status != PermissionStatus.Granted)
            {
                IsAvailable = false;
                return;
            }

            if (_listening)
            {
                IsAvailable = true;
                return;
            }

            Geolocation.Default.LocationChanged += OnLocationChanged;
            var started = await MainThread.InvokeOnMainThreadAsync(() =>
                Geolocation.Default.StartListeningForegroundAsync(
                    new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromMilliseconds(200))));
            _listening = started;
            IsAvailable = started;
            if (!started)
                Geolocation.Default.LocationChanged -= OnLocationChanged;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException or InvalidOperationException)
        {
            IsAvailable = false;
            System.Diagnostics.Debug.WriteLine($"Run GPS unavailable: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_listening)
            return;

        try
        {
            Geolocation.Default.LocationChanged -= OnLocationChanged;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { Geolocation.Default.StopListeningForeground(); } catch { }
            });
        }
        finally
        {
            _listening = false;
        }
    }

    /// <summary>Samples recorded between two moments (UTC).</summary>
    public IReadOnlyList<RunGpsSample> Between(DateTime fromUtc, DateTime toUtc)
    {
        lock (_gate)
            return _samples.Where(s => s.Utc >= fromUtc && s.Utc <= toUtc).ToArray();
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        var location = e.Location;
        if (location is null)
            return;

        // Location.Speed is m/s; some fixes have none, then it is derived
        // from the distance to the previous fix.
        double? kph = location.Speed is { } metersPerSecond && metersPerSecond >= 0 ? metersPerSecond * 3.6 : null;
        lock (_gate)
        {
            if (kph is null && _previous is { } previous)
            {
                var seconds = (location.Timestamp - previous.Timestamp).TotalSeconds;
                if (seconds is > 0.05 and < 3)
                    kph = Location.CalculateDistance(previous, location, DistanceUnits.Kilometers) * 1000 / seconds * 3.6;
            }

            _previous = location;
            if (kph is null)
                return;

            // The phone clock is used for both ECU and GPS samples.
            _samples.Add(new RunGpsSample(DateTime.UtcNow, kph.Value, location.Latitude, location.Longitude, location.Accuracy ?? 0));
            if (_samples.Count > 3000)
                _samples.RemoveRange(0, 1000);
        }

        SpeedSample?.Invoke(DateTime.UtcNow, kph.Value);
    }
}

public sealed record RunGpsSample(DateTime Utc, double Kph, double Latitude, double Longitude, double AccuracyMeters);
