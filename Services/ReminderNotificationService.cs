using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace CarScanner.Services;

public sealed class ReminderNotificationService
{
    private const int NotificationId = 240814;
    private const string LastScanKey = "last_scan_utc";
    private const string EnabledKey = "notifications";

    public async Task InitializeAsync()
    {
        if (!Preferences.Default.Get(EnabledKey, false))
            return;

        try
        {
            var enabled = await LocalNotificationCenter.Current.AreNotificationsEnabled();
            if (!enabled)
            {
                try { await LocalNotificationCenter.Current.RequestNotificationPermission(); }
                catch { return; }
            }

            await ScheduleFromLastScanAsync();
        }
        catch
        {
            // Notification support must never be allowed to crash application startup.
        }
    }

    public async Task MarkScanCompletedAsync()
    {
        Preferences.Default.Set(LastScanKey, DateTimeOffset.UtcNow.ToString("O"));
        try { await ScheduleFromLastScanAsync(); } catch { }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        Preferences.Default.Set(EnabledKey, enabled);
        try
        {
            if (!enabled)
            {
                LocalNotificationCenter.Current.Cancel(NotificationId);
                return;
            }

            await InitializeAsync();
        }
        catch { }
    }

    private async Task ScheduleFromLastScanAsync()
    {
        if (!Preferences.Default.Get(EnabledKey, false)) return;

        var raw = Preferences.Default.Get(LastScanKey, string.Empty);
        var lastScan = DateTimeOffset.TryParse(raw, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        var notifyAt = lastScan.AddDays(14);
        if (notifyAt <= DateTimeOffset.UtcNow)
            notifyAt = DateTimeOffset.UtcNow.AddMinutes(1);

        try { LocalNotificationCenter.Current.Cancel(NotificationId); } catch { }

        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = NotificationId,
            Title = "Georgian Car Scanner",
            Description = "უკვე 2 კვირაა მანქანა არ დაგისკანერებიათ. დროა ახალი OBD-II სკანირება ჩაატაროთ.",
            Schedule = new NotificationRequestSchedule { NotifyTime = notifyAt.ToLocalTime() },
            ReturningData = "scan-reminder"
        });
    }
}
