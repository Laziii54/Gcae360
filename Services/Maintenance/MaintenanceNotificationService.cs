using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace CarScanner.Services.Maintenance;

/// <summary>
/// Phone notifications for service items. Uses the same "notifications"
/// switch as the scan reminder in Settings.
/// • By date: scheduled 14 days before the item is due (works with the app closed).
/// • By distance: shown once when the app sees the item is due soon / overdue.
/// </summary>
public sealed class MaintenanceNotificationService
{
    private const string EnabledKey = "notifications";
    private const string ShownKeyPrefix = "maintenance_notified_v1_";
    private const int BaseNotificationId = 241000;
    private const int DaysBefore = 14;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private static int IdOf(PartId part) => BaseNotificationId + (int)part;

    public async Task UpdateAsync(MaintenanceTrackerService tracker, bool english)
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (!Preferences.Default.Get(EnabledKey, false))
            {
                CancelAll();
                return;
            }

            if (!await LocalNotificationCenter.Current.AreNotificationsEnabled().ConfigureAwait(false))
                return;

            var key = tracker.CurrentKey;
            var data = tracker.Load(key);
            var vehicle = tracker.CurrentVehicle;
            var car = string.Join(" ", new[] { vehicle?.Manufacturer, vehicle?.Model }
                .Where(text => !string.IsNullOrWhiteSpace(text) && !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));
            var title = car.Length > 0 ? $"GCar360 · {car}" : "GCar360";

            foreach (var part in MaintenanceSchedule.PartsFor(tracker.Powertrain))
            {
                var id = IdOf(part.Id);
                var status = tracker.StatusOf(data, part.Id);
                var name = english ? part.NameEn : part.NameKa;

                if (status.State == PartState.Unknown)
                {
                    Cancel(id);
                    continue;
                }

                if (status.State is PartState.Soon or PartState.Overdue)
                {
                    // Once per record and state, so it does not repeat on every visit.
                    var last = MaintenanceTrackerService.LastRecord(data, part.Id);
                    var stamp = $"{key}|{part.Id}|{last?.DateUtc?.Ticks}|{last?.Km}|{status.State}";
                    var shownKey = ShownKeyPrefix + key + "_" + part.Id;
                    if (Preferences.Default.Get(shownKey, string.Empty) == stamp)
                        continue;

                    Preferences.Default.Set(shownKey, stamp);
                    var text = status.State == PartState.Overdue
                        ? (english ? $"{name}: overdue. Time to replace it." : $"{name}: ვადა გავიდა, დროა შეცვალო.")
                        : (english ? $"{name}: due soon ({ValueText(status, true)} left)." : $"{name}: მალე შესაცვლელია (დარჩა {ValueText(status, false)}).");
                    await ShowAsync(id, title, text, DateTimeOffset.Now.AddSeconds(5)).ConfigureAwait(false);
                    continue;
                }

                // Still fine: remind by date before it is due.
                Cancel(id);
                var interval = tracker.IntervalFor(data, part.Id);
                if (interval.Months <= 0 || status.LastDate is not { } lastDate)
                    continue;

                var due = lastDate.Date.AddMonths(interval.Months);
                var remindAt = new DateTimeOffset(due.AddDays(-DaysBefore).AddHours(10));
                if (remindAt <= DateTimeOffset.Now)
                    continue;

                var dueText = due.ToString("dd.MM.yyyy");
                await ShowAsync(
                    id,
                    title,
                    english ? $"{name}: due on {dueText}." : $"{name}: შეცვლის ვადაა {dueText}.",
                    remindAt).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Notifications are optional; never break the Service page.
            System.Diagnostics.Debug.WriteLine($"Maintenance notifications failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ValueText(PartStatus status, bool english)
    {
        if (status.KmLeft is { } km)
            return CarScanner.ViewModels.MaintenanceText.Km(Math.Max(0, km));
        if (status.DaysLeft is { } days)
            return english ? $"{Math.Max(0, days)} days" : $"{Math.Max(0, days)} დღე";
        return "—";
    }

    private static Task ShowAsync(int id, string title, string text, DateTimeOffset when) =>
        LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = text,
            Schedule = new NotificationRequestSchedule { NotifyTime = when.ToLocalTime() },
            ReturningData = "maintenance"
        });

    private static void Cancel(int id)
    {
        try
        {
            LocalNotificationCenter.Current.Cancel(id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification cancel failed: {ex.Message}");
        }
    }

    private static void CancelAll()
    {
        try
        {
            LocalNotificationCenter.Current.Cancel(
                Enum.GetValues<PartId>().Select(IdOf).ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification cancel failed: {ex.Message}");
        }
    }
}
