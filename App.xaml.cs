using CarScanner.Services;
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
        MaintenanceCalendarService maintenanceCalendar)
    {
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
        StartEntitlementSynchronization();

        if (sender is Window window && window.Page is LoadingPage loading)
        {
            await loading.PlayAsync();
            window.Page = _shell;
        }

        if (Interlocked.Exchange(ref _pendingNotificationRoute, null) is { } route)
            await Shell.Current.GoToAsync(route);

        try
        {
            await _notifications.InitializeAsync();
        }
        catch { }

        _ = _maintenanceCalendar.RescheduleAllAsync();

        _ = _usage.ReportAsync();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        StartEntitlementSynchronization();
        _ = _usage.ReportAsync();
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        CancelEntitlementSynchronization();

        // The launcher icon follows the app theme. It is switched only while
        // the app is in the background, so the running screen is not disturbed.
        AppIconSwitcher.Apply();
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
            ? nameof(CalendarPage)
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
                    await Shell.Current.GoToAsync(route);
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
