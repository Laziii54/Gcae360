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
    private const int LegacyIdCount = 32;
    private const int CarSlots = 4000;
    private const int IdsPerCar = 16;
    private const int DaysBefore = 14;
    private const string KnownKeysKey = "maintenance_notified_keys_v1";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Notification id per car and part. The vehicle key is hashed with a
    /// stable FNV-1a hash (string.GetHashCode differs between app runs), so
    /// two cars no longer overwrite each other's reminders.
    /// </summary>
    private static int IdOf(string vehicleKey, PartId part) =>
        BaseNotificationId + LegacyIdCount + (int)(StableHash(vehicleKey) % CarSlots) * IdsPerCar + (int)part;

    /// <summary>The ids used before they were made per car (one set for all cars).</summary>
    private static int LegacyIdOf(PartId part) => BaseNotificationId + (int)part;

    private static uint StableHash(string? text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in (text ?? string.Empty).Trim().ToUpperInvariant())
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            return hash;
        }
    }

    private static List<string> LoadKnownKeys()
    {
        try
        {
            return Preferences.Default.Get(KnownKeysKey, string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void RememberKey(string key)
    {
        try
        {
            var keys = LoadKnownKeys();
            if (keys.Contains(key))
                return;
            keys.Add(key);
            Preferences.Default.Set(KnownKeysKey, string.Join("\n", keys.TakeLast(20)));
        }
        catch
        {
            // Bookkeeping only.
        }
    }

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

            // The user switched notifications on: ask the system once before scheduling.
            if (!await LocalNotificationCenter.Current.AreNotificationsEnabled().ConfigureAwait(false) &&
                !await LocalNotificationCenter.Current.RequestNotificationPermission().ConfigureAwait(false))
                return;

            var key = tracker.CurrentKey;
            RememberKey(key);
            var data = tracker.Load(key);
            var vehicle = tracker.CurrentVehicle;
            var car = string.Join(" ", new[] { vehicle?.Manufacturer, vehicle?.Model }
                .Where(text => !string.IsNullOrWhiteSpace(text) && !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));
            var title = car.Length > 0 ? $"OBD360 · {car}" : "OBD360";

            foreach (var part in MaintenanceSchedule.PartsFor(tracker.Powertrain))
            {
                var id = IdOf(key, part.Id);
                Cancel(LegacyIdOf(part.Id));
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

                    var text = status.State == PartState.Overdue
                        ? (english ? $"{name}: overdue. Time to replace it." : $"{name}: ვადა გავიდა, დროა შეცვალო.")
                        : (english ? $"{name}: due soon ({ValueText(status, true)} left)." : $"{name}: მალე შესაცვლელია (დარჩა {ValueText(status, false)}).");
                    try
                    {
                        await ShowAsync(id, title, text, DateTimeOffset.Now.AddSeconds(5)).ConfigureAwait(false);
                        // Marked as shown only after the notification was accepted,
                        // so a failure is retried on the next visit.
                        Preferences.Default.Set(shownKey, stamp);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Maintenance notification failed: {ex}");
                    }
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
            Title = CarScanner.Services.Localization.LocalizationService.T(title),
            Description = CarScanner.Services.Localization.LocalizationService.T(text),
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
            var parts = Enum.GetValues<PartId>();
            var ids = parts.Select(LegacyIdOf)
                .Concat(LoadKnownKeys().SelectMany(key => parts.Select(part => IdOf(key, part))))
                .Distinct()
                .ToArray();
            LocalNotificationCenter.Current.Cancel(ids);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification cancel failed: {ex.Message}");
        }
    }
}
