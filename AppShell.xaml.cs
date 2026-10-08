using CarScanner.Services;
using CarScanner.ViewModels;
using CarScanner.Views;
using CarScanner.Services.Localization;
using CarScanner.VinCheck.Views;

namespace CarScanner;



public partial class AppShell : Shell
{
    private readonly ProService _pro;
    private bool _allowVerifiedProNavigation;

    public AppShell(ProService pro, IBluetoothService bluetooth, LocalizationService localization, VehicleContextService vehicleContext)
    {
        InitializeComponent();
        _pro = pro;
        BindingContext = new ShellStatusViewModel(pro, bluetooth, vehicleContext);
        Navigating += OnNavigating;
        Navigated += (_, __) =>
        {
            try
            {
                // Theme once per navigation (cheap: skipped when unchanged);
                // ApplyCurrentPage only translates texts.
                ThemeService.Apply();
                localization.ApplyCurrentPage();

                if (CurrentPage != null)
                {
                    // Home refreshes its connection summary / VIN itself in
                    // HomePage.OnAppearing; doing it here too ran it twice.
                    _ = FadeInCurrentPageAsync(CurrentPage);
                }
            }
            catch (Exception navigatedException)
            {
                CarScanner.Services.CommandErrors.Report(navigatedException, "AppShell.Navigated");
            }
        };
        if (Application.Current != null) Application.Current.RequestedThemeChanged += (_, __) => MainThread.BeginInvokeOnMainThread(() => { if (CurrentPage != null) ThemeService.Apply(); });
        Routing.RegisterRoute(nameof(BluetoothPage), typeof(BluetoothPage));
        Routing.RegisterRoute(nameof(LiveDataPage), typeof(LiveDataPage));
        Routing.RegisterRoute(nameof(HistoryPage), typeof(HistoryPage));
        Routing.RegisterRoute(nameof(ResetRecorderPage), typeof(ResetRecorderPage));
        Routing.RegisterRoute(nameof(CustomsCalculatorPage), typeof(CustomsCalculatorPage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(SensorsPage), typeof(SensorsPage));
        Routing.RegisterRoute(nameof(EmissionsPage), typeof(EmissionsPage));
        Routing.RegisterRoute(nameof(TechnicalInspectionPage), typeof(TechnicalInspectionPage));
        Routing.RegisterRoute(nameof(VehicleHealthPage), typeof(VehicleHealthPage));
        Routing.RegisterRoute(nameof(BatteryPage), typeof(BatteryPage));
        Routing.RegisterRoute(nameof(HybridHealthPage), typeof(HybridHealthPage));
        Routing.RegisterRoute(nameof(MaintenancePartPage), typeof(MaintenancePartPage));
        Routing.RegisterRoute(nameof(MaintenanceSetupPage), typeof(MaintenanceSetupPage));
        Routing.RegisterRoute(nameof(AdapterCenterPage), typeof(AdapterCenterPage));
        Routing.RegisterRoute(nameof(EcuIdentifiersPage), typeof(EcuIdentifiersPage));
        Routing.RegisterRoute(nameof(MileagePage), typeof(MileagePage));
        Routing.RegisterRoute(nameof(DiagnosticReportPage), typeof(DiagnosticReportPage));
        Routing.RegisterRoute(nameof(QrReportPage), typeof(QrReportPage));
        Routing.RegisterRoute(nameof(ProPage), typeof(ProPage));
        Routing.RegisterRoute(nameof(AiPage), typeof(AiPage));
        Routing.RegisterRoute(nameof(FuelPage), typeof(FuelPage));
        Routing.RegisterRoute(nameof(GraphStudioPage), typeof(GraphStudioPage));
        Routing.RegisterRoute(nameof(CodingPage), typeof(CodingPage));
        Routing.RegisterRoute(nameof(VehicleReportPage), typeof(VehicleReportPage));
        Routing.RegisterRoute(nameof(GraphChannelsPage), typeof(GraphChannelsPage));
        Routing.RegisterRoute(nameof(ServicePage), typeof(ServicePage));
        Routing.RegisterRoute(nameof(CalendarPage), typeof(CalendarPage));
        Routing.RegisterRoute(nameof(RepairCostPage), typeof(RepairCostPage));
        Routing.RegisterRoute(nameof(ModuleScanPage), typeof(ModuleScanPage));
        Routing.RegisterRoute(nameof(ModuleLiveDataPage), typeof(ModuleLiveDataPage));
        Routing.RegisterRoute(nameof(PrePurchasePage), typeof(PrePurchasePage));
        Routing.RegisterRoute(nameof(PrePurchaseAiPage), typeof(PrePurchaseAiPage));
        Routing.RegisterRoute(nameof(FreezeFramePage), typeof(FreezeFramePage));
        Routing.RegisterRoute(nameof(CapabilityCenterPage), typeof(CapabilityCenterPage));
        Routing.RegisterRoute(nameof(Mode05Page), typeof(Mode05Page));
        Routing.RegisterRoute(nameof(Mode06Page), typeof(Mode06Page));
        Routing.RegisterRoute(nameof(ContactPage), typeof(ContactPage));
        Routing.RegisterRoute(nameof(VehicleBrandSelectionPage), typeof(VehicleBrandSelectionPage));
        Routing.RegisterRoute(nameof(AccelerationHistoryPage), typeof(AccelerationHistoryPage));
        Routing.RegisterRoute(nameof(AccelerationLeaderboardPage), typeof(AccelerationLeaderboardPage));
        Routing.RegisterRoute(nameof(AccelerationPage), typeof(AccelerationPage));
        Routing.RegisterRoute(nameof(PerformanceVideoPage), typeof(PerformanceVideoPage));
        Routing.RegisterRoute(nameof(VinLookupPage), typeof(VinLookupPage));
        Routing.RegisterRoute(nameof(VinReportPage), typeof(VinReportPage));
        Routing.RegisterRoute(nameof(VinHistoryPage), typeof(VinHistoryPage));
        Routing.RegisterRoute(nameof(VinPhotoGalleryPage), typeof(VinPhotoGalleryPage));
        Routing.RegisterRoute(nameof(VinScanPage), typeof(VinScanPage));
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        Routing.RegisterRoute(nameof(VehicleJournalPage), typeof(VehicleJournalPage));
        Routing.RegisterRoute(nameof(AllToolsPage), typeof(AllToolsPage));
        localization.Apply(this);


    }

    /// <summary>
    /// Android back on the OBD main page returns to the start screen
    /// (OBD / VIN Check) instead of closing the app.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        try
        {
            if (CurrentPage is HomePage &&
                Navigation.NavigationStack.Count <= 1 &&
                Navigation.ModalStack.Count == 0)
            {
                _ = GoToAsync("//StartPage");
                return true;
            }
        }
        catch
        {
        }

        return base.OnBackButtonPressed();
    }

    private void OnNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        var destination = e.Target.Location.OriginalString;

        if (_allowVerifiedProNavigation)
        {
            _allowVerifiedProNavigation = false;
            return;
        }

        if (!e.CanCancel || !IsProFeatureRoute(destination))
            return;

        // Cancel before MAUI creates the requested page. This protects direct
        // route navigation as well as buttons on the home screen.
        e.Cancel();
        _ = ContinueToProFeatureAsync(destination);
    }

    private async Task ContinueToProFeatureAsync(string destination)
    {
        if (await _pro.EnsureProAsync())
        {
            _allowVerifiedProNavigation = true;
            await GoToAsync(destination);
            return;
        }

        var openActivation = await DisplayAlertAsync(
            CarScanner.Services.Localization.LocalizationService.T("PRO ფუნქცია"),
            CarScanner.Services.Localization.LocalizationService.T("ეს შესაძლებლობა ხელმისაწვდომია მხოლოდ აქტიური PRO ვერსიით."),
            CarScanner.Services.Localization.LocalizationService.T("გააქტიურება"),
            CarScanner.Services.Localization.LocalizationService.T("ახლა არა"));

        if (openActivation)
            await GoToAsync(nameof(ProPage));
    }

    private static bool IsProFeatureRoute(string route) =>
        route.Contains(nameof(AiPage), StringComparison.Ordinal)
        || route.Contains(nameof(MileagePage), StringComparison.Ordinal)
        || route.Contains(nameof(DiagnosticReportPage), StringComparison.Ordinal)
        || route.Contains(nameof(FuelPage), StringComparison.Ordinal);

    private static async Task FadeInCurrentPageAsync(Page page)
    {
        page.Opacity = 0;
        await page.FadeToAsync(1, 180, Easing.CubicOut);
    }
}
