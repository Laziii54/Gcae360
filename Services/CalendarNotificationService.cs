using CarScanner.Models;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

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

            await ScheduleOneAsync(reminder, reminder.DueDate.Date.AddDays(-1), 0, "ხვალ");
            await ScheduleOneAsync(reminder, reminder.DueDate.Date, 1, "დღეს");
        }
        catch
        {
            // A denied permission or unavailable platform must not affect the calendar.
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
        catch { }
    }

    private static async Task ScheduleOneAsync(
        MaintenanceReminder reminder,
        DateTime date,
        int slot,
        string when)
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
            Title = "GCar360 · სერვისის კალენდარი",
            Description = $"{when} შესასრულებელია: {reminder.Title}",
            ReturningData = $"calendar-reminder:{reminder.Id}",
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = notifyAt
            }
        });
    }

    private static int NotificationId(int reminderId, int slot) =>
        IdBase + (Math.Abs(reminderId) % 40000) * 2 + slot;
}
