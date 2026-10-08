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

    private PowerManager.WakeLock? _wakeLock;
    private string _title = "GCar360";

    public static TripForegroundService? Current { get; private set; }

    /// <summary>The trip ended while the service was still starting.</summary>
    public static volatile bool StopRequested;

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
            // e.g. missing Bluetooth permission on Android 14: keep the app
            // running; the trip then only records while the app is open.
            System.Diagnostics.Debug.WriteLine($"Trip service could not start: {ex.Message}");
            StopNow();
            return StartCommandResult.NotSticky;
        }

        if (StopRequested)
            StopNow();

        // If Android kills the process the trip is gone anyway; do not restart empty.
        return StartCommandResult.NotSticky;
    }

    public void UpdateText(string text)
    {
        try
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Notify(NotificationId, BuildNotification(text));
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
        // Safety limit: a forgotten trip can never hold the CPU for more than 12 hours.
        _wakeLock?.Acquire((long)TimeSpan.FromHours(12).TotalMilliseconds);
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
