using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace CarScanner.ViewModels;

/// <summary>
/// Monthly automatic payment bought on the website (BOG saved card):
/// status, next charge, card and a cancel button. No purchase or website
/// link is shown here (store rules); only the existing plan is managed.
/// </summary>
public partial class ProViewModel
{
    private const int RenewReminderId = 242001;
    private static readonly HttpClient WebSubHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    [ObservableProperty] private bool showWebSubscription;
    [ObservableProperty] private string webSubTitleText = "";
    [ObservableProperty] private string webSubStateText = "";
    [ObservableProperty] private Color webSubStateColor = CarScanner.Services.Localization.ThemeService.Soft("#1F8A4C");
    [ObservableProperty] private Color webSubStateBackground = CarScanner.Services.Localization.ThemeService.Soft("#E8F5EE");
    [ObservableProperty] private string webSubDetailsText = "";
    [ObservableProperty] private bool showWebSubAlert;
    [ObservableProperty] private string webSubAlertText = "";
    [ObservableProperty] private bool canCancelWebSub;

    public string CancelWebSubText => L("გამოწერის გაუქმება", "Cancel subscription");

    /// <summary>Reads the website subscription of the signed-in account.</summary>
    public async Task LoadWebSubscriptionAsync()
    {
        if (!_account.IsSignedIn)
        {
            ShowWebSubscription = false;
            return;
        }

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/web/subscription");
            request.Content = JsonContent.Create(new { });
            using var response = await WebSubHttp.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ShowWebSubscription = false;
                return;
            }

            var status = await response.Content.ReadFromJsonAsync<WebSubDto>();
            MainThread.BeginInvokeOnMainThread(() => ApplyWebSubscription(status));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // Offline: keep whatever was shown before.
        }
    }

    private void ApplyWebSubscription(WebSubDto? s)
    {
        if (s is not { Success: true, HasSubscription: true })
        {
            ShowWebSubscription = false;
            CancelRenewReminder();
            return;
        }

        var name = s.ProductId switch
        {
            ProService.ObdProductId => "PRO",
            ProService.VinProductId => "VIN PRO",
            ProService.FullProductId => L("სრული PRO", "Full PRO"),
            _ => "PRO"
        };

        string Day(DateTimeOffset? at) => at is { } value
            ? value.ToLocalTime().ToString("d MMM yyyy", _localization.IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ka-GE"))
            : "—";

        // Google Play payments policy: no prices of outside purchases on Android
        // (status, renewal date and cancel stay so the user can manage the plan).
        var price = CanBuyInApp && s.Amount is { } amount ? amount.ToString("0.00", CultureInfo.InvariantCulture) + " ₾" : "";
        WebSubTitleText = price.Length > 0
            ? $"{name} · {price} {L("/ თვე", "/ month")}".Trim()
            : name;

        var lines = new List<string>();
        if (s.Status == "past_due")
        {
            WebSubStateText = L("გადაუხდელი", "Payment failed");
            WebSubStateColor = CarScanner.Services.Localization.ThemeService.Soft("#B3361F");
            WebSubStateBackground = CarScanner.Services.Localization.ThemeService.Soft("#FDECEA");
            lines.Add(L("შემდეგი ცდა: ", "Next try: ") + Day(s.NextChargeAt));
            ShowWebSubAlert = true;
            WebSubAlertText = L(
                $"ბარათიდან თანხა ვერ ჩამოიჭრა. ხელახლა ვცდით {Day(s.NextChargeAt)}-ს. PRO მოქმედებს {Day(s.ActiveUntil)}-მდე.",
                $"The card could not be charged. We will try again on {Day(s.NextChargeAt)}. PRO stays on until {Day(s.ActiveUntil)}.");
        }
        else if (s.AutoRenew)
        {
            WebSubStateText = L("აქტიური", "Active");
            WebSubStateColor = CarScanner.Services.Localization.ThemeService.Soft("#1F8A4C");
            WebSubStateBackground = CarScanner.Services.Localization.ThemeService.Soft("#E8F5EE");
            lines.Add(L("ავტომატურად განახლდება: ", "Renews automatically: ") + Day(s.NextChargeAt));
            ShowWebSubAlert = false;
        }
        else
        {
            WebSubStateText = L("გაუქმებული", "Cancelled");
            WebSubStateColor = CarScanner.Services.Localization.ThemeService.Soft("#5D636B");
            WebSubStateBackground = CarScanner.Services.Localization.ThemeService.Soft("#ECEEF0");
            lines.Add(L("მოქმედებს: ", "Active until: ") + Day(s.ActiveUntil));
            ShowWebSubAlert = false;
        }

        if (!string.IsNullOrWhiteSpace(s.CardMask))
            lines.Add(L("ბარათი: ", "Card: ") + s.CardMask);

        WebSubDetailsText = string.Join("\n", lines);
        CanCancelWebSub = s.AutoRenew;
        ShowWebSubscription = true;

        _ = ScheduleRenewReminderAsync(s, name, price);
    }

    [RelayCommand]
    private async Task CancelWebSubscription()
    {
        try
        {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var ok = await page.DisplayAlertLocalizedAsync(
            L("გავაუქმო გამოწერა?", "Cancel the subscription?"),
            L("PRO დარჩება გადახდილი პერიოდის ბოლომდე. ამის მერე თანხა აღარ ჩამოიჭრება და შენახული ბარათი წაიშლება.",
              "PRO stays on until the paid period ends. After that nothing is charged and the saved card is deleted."),
            L("გაუქმება", "Cancel subscription"),
            L("დატოვება", "Keep"));
        if (!ok)
            return;

        try
        {
            IsBusy = true;
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/web/subscription/cancel");
            request.Content = JsonContent.Create(new { });
            using var response = await WebSubHttp.SendAsync(request);
            var status = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<WebSubDto>() : null;
            if (status is null)
                throw new HttpRequestException();

            ApplyWebSubscription(status);
            Status = L("გამოწერა გაუქმდა. თანხა აღარ ჩამოიჭრება.", "Subscription cancelled. You will not be charged again.");
            StatusColor = Color.FromArgb("#22C55E");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Status = L("ვერ გაუქმდა. შეამოწმე ინტერნეტი და სცადე ხელახლა.", "Could not cancel. Check your connection and try again.");
            StatusColor = Color.FromArgb("#DC2626");
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ProViewModel.CancelWebSubscription");
        }
    }

    /// <summary>Phone reminder 3 days before the automatic charge (Settings → notifications).</summary>
    private async Task ScheduleRenewReminderAsync(WebSubDto s, string name, string price)
    {
        try
        {
            if (!s.AutoRenew || s.Status != "active" || s.NextChargeAt is not { } next ||
                !Preferences.Default.Get("notifications", false))
            {
                CancelRenewReminder();
                return;
            }

            var when = next.ToLocalTime().AddDays(-3);
            if (when <= DateTimeOffset.Now)
                return;

            if (!await LocalNotificationCenter.Current.AreNotificationsEnabled())
                return;

            await LocalNotificationCenter.Current.Show(new NotificationRequest
            {
                NotificationId = RenewReminderId,
                Title = "OBD360",
                Description = CarScanner.Services.Localization.LocalizationService.T(price.Length > 0
                    ? L($"3 დღეში {name} განახლდება: {price}.", $"{name} renews in 3 days: {price}.")
                    : L($"3 დღეში {name} განახლდება.", $"{name} renews in 3 days.")),
                Schedule = new NotificationRequestSchedule { NotifyTime = when },
                ReturningData = "web-renewal"
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Renewal reminder failed: {ex.Message}");
        }
    }

    private static void CancelRenewReminder()
    {
        try { LocalNotificationCenter.Current.Cancel(RenewReminderId); } catch { }
    }

    private sealed class WebSubDto
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("hasSubscription")] public bool HasSubscription { get; set; }
        [JsonPropertyName("productId")] public string? ProductId { get; set; }
        [JsonPropertyName("amount")] public decimal? Amount { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("nextChargeAt")] public DateTimeOffset? NextChargeAt { get; set; }
        [JsonPropertyName("activeUntil")] public DateTimeOffset? ActiveUntil { get; set; }
        [JsonPropertyName("cardMask")] public string? CardMask { get; set; }
        [JsonPropertyName("autoRenew")] public bool AutoRenew { get; set; }
        [JsonPropertyName("paymentFailed")] public bool PaymentFailed { get; set; }
    }
}
