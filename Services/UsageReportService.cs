using System.Net.Http.Json;
using CarScanner.Services.Localization;
using Plugin.LocalNotification;

namespace CarScanner.Services;

/// <summary>
/// Tells the GCar360 server, at most every few hours, that this installation
/// is in use: app version, Android version, language and whether reminders
/// can be shown. Only a random installation id is sent (no name, phone
/// number or location). The owner sees totals on the admin page.
/// </summary>
public sealed class UsageReportService(AccountService account)
{
    private const string LastSentKey = "usage_last_sent_utc";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private int _running;

    public async Task ReportAsync(bool force = false)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;

        try
        {
            var last = Preferences.Default.Get(LastSentKey, string.Empty);
            if (!force &&
                DateTimeOffset.TryParse(last, out var sentAt) &&
                DateTimeOffset.UtcNow - sentAt < Interval)
                return;

            bool systemNotifications;
            try { systemNotifications = await LocalNotificationCenter.Current.AreNotificationsEnabled(); }
            catch { systemNotifications = false; }

            using var request = await account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/telemetry/heartbeat");
            request.Content = JsonContent.Create(new
            {
                appVersion = AppInfo.Current.VersionString,
                build = AppInfo.Current.BuildString,
                platform = DeviceInfo.Current.Platform.ToString(),
                osVersion = DeviceInfo.Current.VersionString,
                model = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim(),
                language = Preferences.Default.Get(LocalizationService.LanguageKey, "English"),
                notificationsAllowed = systemNotifications,
                remindersOn = Preferences.Default.Get("notifications", false)
            });

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
                Preferences.Default.Set(LastSentKey, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // Usage statistics must never disturb the app.
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
