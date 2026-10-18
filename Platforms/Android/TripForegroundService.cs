using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace CarScanner.Services.Background;

public static partial class TripKeepAlive
{
    static partial void PlatformStart(string title, string text)
    {
        TripForegroundService.StopRequested = false;
        TripForegroundService.StartRequestedAtUtc = DateTime.UtcNow;
        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(TripForegroundService));
        intent.PutExtra(TripForegroundService.TitleExtra, title);
        intent.PutExtra(TripForegroundService.TextExtra, text);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    static partial void PlatformUpdate(string text) =>
        TripForegroundService.Current?.UpdateText(text);

    static partial void PlatformStop()
    {
        // Stopping a service that has not called StartForeground yet makes
        // Android close the app ("did not then call startForeground"). If it
        // is still starting, it stops itself right after StartForeground.
        TripForegroundService.StopRequested = true;
        TripForegroundService.Current?.StopNow();
    }

    static partial void PlatformRecheck(string title, string text)
    {
        // The service is gone although the trip still records: Android stopped it, or refused it because the app was
        // not visible when the trip started. The app is visible now (or the page just appeared), so start it again.
        // A start that was only requested a moment ago is still on its way and is left alone.
        if (TripForegroundService.Current is not null)
            return;

        if (DateTime.UtcNow - TripForegroundService.StartRequestedAtUtc < TimeSpan.FromSeconds(8))
            return;

        PlatformStart(title, text);
    }

    private static bool AndroidBatteryOptimized()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            return false;

        var context = Android.App.Application.Context;
        var power = context.GetSystemService(Context.PowerService) as PowerManager;
        return power is not null && !power.IsIgnoringBatteryOptimizations(context.PackageName);
    }

    private static void AndroidOpenBatterySettings()
    {
        var context = Android.App.Application.Context;

        // The list of apps with battery optimisation needs no special permission. A few phones do not have that
        // screen: the app's own settings page is the fallback.
        try
        {
            var list = new Intent(Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings);
            list.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(list);
        }
        catch (ActivityNotFoundException)
        {
            var details = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings);
            details.SetData(Android.Net.Uri.FromParts("package", context.PackageName, null));
            details.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(details);
        }
    }
}

/// <summary>
/// Foreground service that keeps the OBD Bluetooth link and the trip calculation
/// alive while the app is in the background or the screen is off.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class TripForegroundService : Service
{
    public const string TitleExtra = "title";
    public const string TextExtra = "text";
    private const string ChannelId = "gcar360_trip";
    private const int NotificationId = 4100;

    // The wake lock has a timeout so a forgotten trip can never hold the CPU for ever; while readings keep arriving
    // the timeout is pushed forward (UpdateText), so a long drive is never cut off by it.
    private static readonly TimeSpan WakeLockTimeout = TimeSpan.FromHours(12);
    private static readonly TimeSpan WakeLockRenewEvery = TimeSpan.FromHours(1);

    private PowerManager.WakeLock? _wakeLock;
    private DateTime _wakeLockAcquiredAtUtc = DateTime.MinValue;
    private string _title = "OBD360";

    public static TripForegroundService? Current { get; private set; }

    /// <summary>The trip ended while the service was still starting.</summary>
    public static volatile bool StopRequested;

    /// <summary>When the app last asked for the service to start (a start is asynchronous).</summary>
    public static DateTime StartRequestedAtUtc { get; set; } = DateTime.MinValue;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Current = this;
        _title = intent?.GetStringExtra(TitleExtra) ?? _title;
        var text = intent?.GetStringExtra(TextExtra) ?? string.Empty;

        try
        {
            CreateChannel();
            var notification = BuildNotification(text);
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
            else
                StartForeground(NotificationId, notification);

            AcquireWakeLock();
        }
        catch (Exception ex)
        {
            // e.g. a missing permission on Android 14. The app keeps running, but the trip then only records while
            // the app is open, and the Fuel page says so instead of leaving the user to find out on the road.
            System.Diagnostics.Debug.WriteLine($"Trip service could not start: {ex.Message}");
            TripKeepAlive.ReportProblem(TripKeepAliveProblem.Unavailable);
            StopNow();
            return StartCommandResult.NotSticky;
        }

        if (StopRequested)
        {
            StopNow();
            return StartCommandResult.NotSticky;
        }

        // The service is up (again): clear a refusal that an earlier start left behind.
        TripKeepAlive.ReportProblem(TripKeepAliveProblem.None);

        // If Android kills the process the trip is gone anyway; do not restart empty.
        return StartCommandResult.NotSticky;
    }

    public void UpdateText(string text)
    {
        try
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Notify(NotificationId, BuildNotification(text));
            RenewWakeLockIfOld();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip notification update failed: {ex.Message}");
        }
    }

    public void StopNow()
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                StopForeground(StopForegroundFlags.Remove);
            StopSelf();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip service stop failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The user swiped the app away from the recent apps. That is "closing the app": the notification and the wake
    /// lock go away, and the trip page keeps what was recorded as a paused trip (its checkpoint is saved).
    /// </summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        try
        {
            TripKeepAlive.NotifyAppClosed();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trip app-closed notification failed: {ex.Message}");
        }

        StopNow();
        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        try
        {
            if (_wakeLock?.IsHeld == true)
                _wakeLock.Release();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Wake lock release failed: {ex.Message}");
        }

        _wakeLock = null;
        Current = null;
        base.OnDestroy();
    }

    private void AcquireWakeLock()
    {
        if (_wakeLock?.IsHeld == true)
            return;

        var power = (PowerManager?)GetSystemService(PowerService);
        _wakeLock = power?.NewWakeLock(WakeLockFlags.Partial, "GCar360:trip");
        _wakeLock?.SetReferenceCounted(false);
        _wakeLock?.Acquire((long)WakeLockTimeout.TotalMilliseconds);
        _wakeLockAcquiredAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Acquiring a lock that is not reference-counted again restarts its timeout. This runs only while the trip
    /// still delivers readings, so a lock whose trip went quiet (car off, adapter gone) still ends by itself.
    /// </summary>
    private void RenewWakeLockIfOld()
    {
        if (_wakeLock is null || DateTime.UtcNow - _wakeLockAcquiredAtUtc < WakeLockRenewEvery)
            return;

        _wakeLock.Acquire((long)WakeLockTimeout.TotalMilliseconds);
        _wakeLockAcquiredAtUtc = DateTime.UtcNow;
    }

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager?.GetNotificationChannel(ChannelId) is not null)
            return;

        var georgian = !CarScanner.Services.Localization.LanguageCatalog.UsesEnglishBase;
        var channel = new NotificationChannel(
            ChannelId,
            georgian ? "მოგზაურობის ჩაწერა" : "Trip recording",
            NotificationImportance.Low)
        {
            Description = georgian
                ? "ჩანს, როცა საწვავის/ენერგიის გვერდი ფონურ რეჟიმში წერს მოგზაურობას."
                : "Shown while the fuel/energy page records a trip in the background."
        };
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string text)
    {
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        launch?.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
        var pending = launch is null
            ? null
            : PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

#pragma warning disable CA1422, CS0618 // Builder(Context) is needed below Android 8.
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
#pragma warning restore CA1422, CS0618

        builder
            .SetContentTitle(_title)
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDataBluetooth)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true);

        if (pending is not null)
            builder.SetContentIntent(pending);

        return builder.Build()!;
    }
}
