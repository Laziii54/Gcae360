namespace CarScanner.Services.Background;

/// <summary>
/// Keeps a fuel/energy trip recording while the app is in the background or the
/// screen is off. Android: a foreground service with a "trip is recording"
/// notification and a partial wake lock. iOS: background location updates keep
/// the app running (Bluetooth alone only wakes it for incoming data).
/// </summary>
public static partial class TripKeepAlive
{
    private static DateTime _lastUpdate = DateTime.MinValue;

    public static bool IsActive { get; private set; }

    public static void Start(string title, string text)
    {
        IsActive = true;
        try
        {
            PlatformStart(title, text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip keep-alive start failed: {ex.Message}");
        }
    }

    /// <summary>Updates the notification text (at most every 5 seconds).</summary>
    public static void Update(string text)
    {
        if (!IsActive || DateTime.UtcNow - _lastUpdate < TimeSpan.FromSeconds(5))
            return;

        _lastUpdate = DateTime.UtcNow;
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
        if (!IsActive)
            return;

        IsActive = false;
        try
        {
            PlatformStop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip keep-alive stop failed: {ex.Message}");
        }
    }

    static partial void PlatformStart(string title, string text);
    static partial void PlatformUpdate(string text);
    static partial void PlatformStop();
}
