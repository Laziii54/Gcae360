using CarScanner.Models;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using CarScanner.Services.Localization;

namespace CarScanner.Services;

/// <summary>Schedules the two calendar alerts without changing the existing scan reminder.</summary>
public sealed class CalendarNotificationService
{
    private const int IdBase = 510000;

    public async Task ScheduleAsync(MaintenanceReminder reminder)
    {
        Cancel(reminder.Id);
        if (reminder.Id <= 0 || reminder.IsCompleted)
            return;

        try
        {
            if (!await LocalNotificationCenter.Current.AreNotificationsEnabled())
            {
                var granted = await LocalNotificationCenter.Current.RequestNotificationPermission();
                if (!granted)
                    return;
            }

            await ScheduleOneAsync(reminder, reminder.DueDate.Date.AddDays(-1), 0, tomorrow: true);
            await ScheduleOneAsync(reminder, reminder.DueDate.Date, 1, tomorrow: false);
        }
        catch (Exception ex)
        {
            // A denied permission or unavailable platform must not affect the
            // calendar — but the failure is logged instead of disappearing.
            System.Diagnostics.Debug.WriteLine($"[CalendarNotificationService.Schedule] {ex}");
        }
    }

    public void Cancel(int reminderId)
    {
        if (reminderId <= 0) return;
        try
        {
            LocalNotificationCenter.Current.Cancel(NotificationId(reminderId, 0));
            LocalNotificationCenter.Current.Cancel(NotificationId(reminderId, 1));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CalendarNotificationService.Cancel] {ex}");
        }
    }

    private static async Task ScheduleOneAsync(
        MaintenanceReminder reminder,
        DateTime date,
        int slot,
        bool tomorrow)
    {
        var now = DateTime.Now;
        var notifyAt = date.AddHours(9);
        if (date.Date < now.Date)
            return;
        if (notifyAt <= now)
            notifyAt = now.AddMinutes(1);

        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = NotificationId(reminder.Id, slot),
            Title = CarScanner.Services.Localization.LocalizationService.T(L("OBD360 · სერვისის კალენდარი", "OBD360 · Service calendar")),
            // reminder.Title must be substituted AFTER translation: T() only matches exact (or {0}-style
            // template) source text, so interpolating it in first meant this notification could never match a
            // dictionary/pack row and always showed English regardless of the other 7 added languages.
            Description = string.Format(
                CarScanner.Services.Localization.LocalizationService.T(tomorrow
                    ? "ხვალ შესასრულებელია: {0}"
                    : "დღეს შესასრულებელია: {0}"),
                reminder.Title),
            ReturningData = $"calendar-reminder:{reminder.Id}",
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = notifyAt
            }
        });
    }

    private static string L(string ka, string en) => CarScanner.Services.OBD.ObdLang.IsEnglish ? en : ka;

    private static int NotificationId(int reminderId, int slot) =>
        IdBase + (Math.Abs(reminderId) % 40000) * 2 + slot;
}
