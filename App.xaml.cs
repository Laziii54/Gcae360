using CarScanner.Services;
using CarScanner.Services.DataCollection;
using CarScanner.Services.Localization;
using CarScanner.Views;
using Plugin.LocalNotification;
using Plugin.LocalNotification.EventArgs;

namespace CarScanner;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly UserProfileService _userProfile;
    private readonly ReminderNotificationService _notifications;
    private readonly LocalizationService _localization;
    private readonly PurchaseService _purchase;
    private readonly CrashReportingService _crashReporting;
    private readonly UsageReportService _usage;
    private readonly MaintenanceCalendarService _maintenanceCalendar;
    private readonly ConsentPromptService _consent;
    private CancellationTokenSource? _lifecycleSyncCancellation;
    private string? _pendingNotificationRoute;

    public App(
        AppShell shell,
        UserProfileService userProfile,
        ReminderNotificationService notifications,
        LocalizationService localization,
        PurchaseService purchase,
        CrashReportingService crashReporting,
        UsageReportService usage,
        MaintenanceCalendarService maintenanceCalendar,
        ConsentPromptService consent)
    {
        _consent = consent;
        _usage = usage;
        _maintenanceCalendar = maintenanceCalendar;
        InitializeComponent();
        _shell = shell;
        _userProfile = userProfile;
        _notifications = notifications;
        _localization = localization;
        _purchase = purchase;
        _crashReporting = crashReporting;
        _crashReporting.Initialize();
        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationActionTapped;
        // Theme first (LocalizationService.Apply only translates texts now).
        ThemeService.Apply();
        _localization.Apply(_shell);
        RequestedThemeChanged += (_, __) => MainThread.BeginInvokeOnMainThread(() => { try { if (_shell.CurrentPage != null) ThemeService.Apply(); } catch { } });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Opening animation first (GIF, centred), then the app.
        var window = new Window(new LoadingPage());
        window.Created += OnWindowCreated;
        window.Resumed += OnWindowResumed;
        window.Stopped += OnWindowStopped;
        window.Destroying += OnWindowDestroying;
        return window;
    }

    private async void OnWindowCreated(object? sender, EventArgs e)
    {
        try
        {
        StartEntitlementSynchronization();

        if (sender is Window window && window.Page is LoadingPage loading)
        {
            await loading.PlayAsync();
            window.Page = _shell;
        }

        if (Interlocked.Exchange(ref _pendingNotificationRoute, null) is { } route)
            await CarScanner.Services.SafeNavigation.GoToAsync(route);

        try
        {
            await _notifications.InitializeAsync();
        }
        catch { }

        _ = _maintenanceCalendar.RescheduleAllAsync();

        _ = _usage.ReportAsync();
        ScheduleConsentCheck();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "App.OnWindowCreated");
        }
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        // A trip that records in the background: the app is visible again, so a background service the system
        // dropped (or refused while the app was hidden) can be started again, and the iOS location answer is read.
        CarScanner.Services.Background.TripKeepAlive.Recheck();

        StartEntitlementSynchronization();
        _ = _usage.ReportAsync();
        ScheduleConsentCheck();
    }

    /// <summary>
    /// Asks the data-collection question when it is due: at launch and when the app returns to the foreground.
    /// The coordinator makes this a no-op once consent is given, while a decline is less than 24 hours old, and
    /// while a question is already on screen, so calling it on every resume is safe.
    /// </summary>
    private void ScheduleConsentCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(900));   // let the main page settle first
                await _consent.CheckAsync();
            }
            catch (Exception ex)
            {
                CarScanner.Services.CommandErrors.Report(ex, "App.ScheduleConsentCheck");
            }
        });
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        CancelEntitlementSynchronization();

        // The launcher icon is switched only after the user explicitly changed
        // the theme in Settings (switching the alias removes pinned home-screen
        // icons, so OS theme changes must not trigger it). It is applied while
        // the app is in the background, so the running screen is not disturbed.
        // Not while a trip records: switching the launcher alias can close the
        // app on some Android phones and would end the trip.
        if (!CarScanner.Services.Background.TripKeepAlive.IsActive)
            AppIconSwitcher.ApplyIfRequested();
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        CancelEntitlementSynchronization();
        LocalNotificationCenter.Current.NotificationActionTapped -= OnNotificationActionTapped;

        if (sender is Window window)
        {
            window.Created -= OnWindowCreated;
            window.Resumed -= OnWindowResumed;
            window.Stopped -= OnWindowStopped;
            window.Destroying -= OnWindowDestroying;
        }
    }

    private void OnNotificationActionTapped(NotificationActionEventArgs e)
    {
        if (!e.IsTapped || e.Request?.ReturningData is not { } data)
            return;

        string? route = data.StartsWith("calendar-reminder:", StringComparison.Ordinal)
            ? nameof(ServicePage)
            : null;
        if (route is null)
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current is null)
                    _pendingNotificationRoute = route;
                else
                    await CarScanner.Services.SafeNavigation.GoToAsync(route);
            }
            catch
            {
                _pendingNotificationRoute = route;
            }
        });
    }

    private void StartEntitlementSynchronization()
    {
        CancelEntitlementSynchronization();
        _lifecycleSyncCancellation = new CancellationTokenSource();
        _ = SynchronizeEntitlementAsync(_lifecycleSyncCancellation.Token);
    }

    private void CancelEntitlementSynchronization()
    {
        var cancellation = Interlocked.Exchange(
            ref _lifecycleSyncCancellation,
            null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task SynchronizeEntitlementAsync(CancellationToken token)
    {
        try
        {
            await _purchase.SynchronizeOwnedPurchaseAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
