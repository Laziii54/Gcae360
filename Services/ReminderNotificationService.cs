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
                // Nothing is scheduled without the system permission.
                if (!await LocalNotificationCenter.Current.RequestNotificationPermission())
                    return;
            }

            await ScheduleFromLastScanAsync();
        }
        catch (Exception ex)
        {
            // Notification support must never be allowed to crash application startup.
            Log("Initialize", ex);
        }
    }

    public async Task MarkScanCompletedAsync()
    {
        Preferences.Default.Set(LastScanKey, DateTimeOffset.UtcNow.ToString("O"));
        try
        {
            if (!Preferences.Default.Get(EnabledKey, false) ||
                !await LocalNotificationCenter.Current.AreNotificationsEnabled())
                return;

            await ScheduleFromLastScanAsync();
        }
        catch (Exception ex)
        {
            Log("MarkScanCompleted", ex);
        }
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
        catch (Exception ex)
        {
            Log("SetEnabled", ex);
        }
    }

    private static void Log(string where, Exception ex) =>
        System.Diagnostics.Debug.WriteLine($"[ReminderNotificationService.{where}] {ex}");

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

        try { LocalNotificationCenter.Current.Cancel(NotificationId); }
        catch (Exception ex) { Log("Cancel", ex); }

        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = NotificationId,
            Title = "Georgian Car Scanner",
            Description = CarScanner.Services.OBD.ObdLang.IsEnglish
                ? "It has been 2 weeks since your last scan. Time for a new OBD-II check."
                : "უკვე 2 კვირაა მანქანა არ დაგისკანერებიათ. დროა ახალი OBD-II სკანირება ჩაატაროთ.",
            Schedule = new NotificationRequestSchedule { NotifyTime = notifyAt.ToLocalTime() },
            ReturningData = "scan-reminder"
        });
    }
}
