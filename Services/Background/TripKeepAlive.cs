namespace CarScanner.Services.Background;

/// <summary>What keeps the background keep-alive from working. Shown on the Fuel page, so a refusal is never silent.</summary>
public enum TripKeepAliveProblem
{
    None,

    /// <summary>The system refused or dropped the background service (Android) / background mode (iOS).</summary>
    Unavailable,

    /// <summary>iOS: location is switched off or denied, so the system suspends the app when the screen turns off.</summary>
    LocationNotAllowed
}

/// <summary>
/// Keeps a fuel/energy trip recording while the app is in the background or the
/// screen is off. Android: a foreground service with a "trip is recording"
/// notification and a partial wake lock. iOS: background location updates keep
/// the app running (Bluetooth alone only wakes it for incoming data).
/// </summary>
public static partial class TripKeepAlive
{
    /// <summary>How long after a start the keep-alive is looked at again (the service start itself is asynchronous).</summary>
    internal static readonly TimeSpan FirstRecheckDelay = TimeSpan.FromSeconds(12);

    private static DateTime _lastUpdate = DateTime.MinValue;
    private static string _lastTitle = string.Empty;
    private static string _lastText = string.Empty;
    private static int _generation;
    private static volatile TripKeepAliveProblem _problem;
    private static volatile bool _isActive;

    public static bool IsActive => _isActive;

    /// <summary>Why the background keep-alive is not working, or <see cref="TripKeepAliveProblem.None"/>.</summary>
    public static TripKeepAliveProblem Problem => _problem;

    /// <summary>Raised (on any thread) when <see cref="Problem"/> changes.</summary>
    public static event EventHandler? ProblemChanged;

    /// <summary>
    /// Raised (on the main thread) when the user closed the app from the recent-apps list while a trip was recording.
    /// That ends the background recording; the trip itself is kept as a paused trip.
    /// </summary>
    public static event EventHandler? AppClosed;

    public static void Start(string title, string text)
    {
        _lastTitle = title;
        _lastText = text;
        _isActive = true;
        var generation = Interlocked.Increment(ref _generation);
        SetProblem(TripKeepAliveProblem.None);
        try
        {
            PlatformStart(title, text);
        }
        catch (Exception ex)
        {
            // e.g. Android refuses a foreground service once the app is no longer visible. Not silent any more:
            // the Fuel page tells the user the trip only records while the app stays open.
            System.Diagnostics.Debug.WriteLine($"Trip keep-alive start failed: {ex.Message}");
            ReportProblem(TripKeepAliveProblem.Unavailable);
        }

        // The platform start is asynchronous (Android's service starts a moment later, iOS asks for location
        // permission): look at it again shortly after, so a late refusal is noticed too.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FirstRecheckDelay).ConfigureAwait(false);
                if (IsActive && Volatile.Read(ref _generation) == generation)
                    Recheck();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Trip keep-alive recheck failed: {ex.Message}");
            }
        });
    }

    /// <summary>Updates the notification text (at most every 5 seconds).</summary>
    public static void Update(string text)
    {
        if (!IsActive || DateTime.UtcNow - _lastUpdate < TimeSpan.FromSeconds(5))
            return;

        _lastUpdate = DateTime.UtcNow;
        _lastText = text;
        try
        {
            PlatformUpdate(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip keep-alive update failed: {ex.Message}");
        }
    }

    public static void Stop()
    {
        Interlocked.Increment(ref _generation);
        SetProblem(TripKeepAliveProblem.None);
        if (!IsActive)
            return;

        _isActive = false;
        try
        {
            PlatformStop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip keep-alive stop failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The app is visible again (or the trip page appeared): bring the background service back if the system dropped
    /// it, and look at what the platform allows now. A foreground service may be started again at this moment.
    /// </summary>
    public static void Recheck()
    {
        if (!IsActive)
            return;

        try
        {
            PlatformRecheck(_lastTitle, _lastText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip keep-alive recheck failed: {ex.Message}");
            ReportProblem(TripKeepAliveProblem.Unavailable);
        }
    }

    /// <summary>Android: true when battery optimisation is on for the app, which some phones use to stop background apps.</summary>
    public static bool BatteryOptimizationMayStopTrips
    {
        get
        {
#if ANDROID
            try
            {
                return AndroidBatteryOptimized();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Battery optimisation check failed: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }
    }

    /// <summary>Android: opens the system screen where the user can take the app out of battery optimisation.</summary>
    public static void OpenBatterySettings()
    {
#if ANDROID
        try
        {
            AndroidOpenBatterySettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery settings could not be opened: {ex.Message}");
        }
#endif
    }

    /// <summary>Called by the platform code when it finds the keep-alive working (<c>None</c>) or not.</summary>
    internal static void ReportProblem(TripKeepAliveProblem problem)
    {
        // A refusal that arrives after the trip was stopped is not worth showing.
        if (problem != TripKeepAliveProblem.None && !IsActive)
            return;

        SetProblem(problem);
    }

    /// <summary>The user swiped the app away: the background recording ends, the page keeps the trip as paused.</summary>
    internal static void NotifyAppClosed()
    {
        if (!IsActive)
            return;

        try
        {
            AppClosed?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip app-closed handler failed: {ex.Message}");
        }
    }

    private static void SetProblem(TripKeepAliveProblem problem)
    {
        if (_problem == problem)
            return;

        _problem = problem;
        try
        {
            ProblemChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip keep-alive listener failed: {ex.Message}");
        }
    }

    static partial void PlatformStart(string title, string text);
    static partial void PlatformUpdate(string text);
    static partial void PlatformStop();
    static partial void PlatformRecheck(string title, string text);
}
