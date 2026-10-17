using CarScanner.Database;
using CarScanner.Services;
using CarScanner.Services.Bluetooth;
using CarScanner.Services.Mileage;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.ViewModels;
using CarScanner.Views;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.LocalNotification;

namespace CarScanner;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // Every Label/Button shows its text in the selected language (Georgian/English).
        CarScanner.Services.Localization.LocalizationService.RegisterDisplayTranslation();

        builder
            .UseMauiApp<App>()
            .UseLocalNotification()
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                handlers.AddHandler<CarScanner.Controls.PerformanceCameraView, CarScanner.Droid.PerformanceVideo.PerformanceCameraViewHandler>();
#elif IOS
                handlers.AddHandler<CarScanner.Controls.PerformanceCameraView, CarScanner.Platforms.iOS.PerformanceVideo.PerformanceCameraViewHandler>();
#endif
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("opensans_regular.ttf", "OpenSansRegular");
                fonts.AddFont("opensans_semibold.ttf", "OpenSansSemibold");

                fonts.AddFont(
                    "NotoSansGeorgian.ttf",
                    "NotoSansGeorgian");
            });

        // ============================================================
        // BLUETOOTH
        // ============================================================

        builder.Services.AddSingleton<IBluetoothLE>(
            CrossBluetoothLE.Current);

        builder.Services.AddSingleton<IAdapter>(
            CrossBluetoothLE.Current.Adapter);

        builder.Services.AddSingleton<BluetoothService>();
        builder.Services.AddSingleton<BluetoothObdTransport>();
        builder.Services.AddSingleton<WifiObdTransport>();
        builder.Services.AddSingleton<ObdConnectionService>();
        builder.Services.AddSingleton<IBluetoothService>(services =>
            services.GetRequiredService<ObdConnectionService>());
        builder.Services.AddSingleton<IObdTransport>(services =>
            services.GetRequiredService<ObdConnectionService>());

        // ============================================================
        // CORE OBD / DTC
        // ============================================================

        builder.Services.AddSingleton<PIDDecoder>();

        builder.Services.AddSingleton<DTCDecoder>();

        // IMPORTANT:
        // DtcCatalogService must be registered ONLY ONCE.
        builder.Services.AddSingleton<DtcCatalogService>();
        builder.Services.AddSingleton<CarScanner.Services.OemDiagnostics.ModuleDtcScanService>();
        builder.Services.AddSingleton<CarScanner.Services.OemDiagnostics.ModuleLiveDataService>();
        builder.Services.AddSingleton<PrePurchaseCheckService>();
        builder.Services.AddSingleton<PrePurchaseAiViewModel>();

        builder.Services.AddSingleton<Elm327Protocol>();

        builder.Services.AddSingleton<OBDService>();

        // ============================================================
        // VEHICLE DIAGNOSTICS
        // ============================================================

        builder.Services.AddSingleton<VehicleContextService>();

        builder.Services.AddSingleton<VehicleBrandProfileService>();

        builder.Services.AddSingleton<VehicleProfileResolverService>();

        builder.Services.AddSingleton<
            CarScanner.Services.VehicleDiagnostics.Transports.IVehicleDiagnosticTransport,
            CarScanner.Services.VehicleDiagnostics.Transports.BleDiagnosticTransport>();

        builder.Services.AddSingleton<
            CarScanner.Services.VehicleDiagnostics.Protocols.IDiagnosticProtocol,
            CarScanner.Services.VehicleDiagnostics.Protocols.GenericObdProtocol>();

        builder.Services.AddSingleton<DiagnosticService>();

        builder.Services.AddSingleton<ServiceProcedureCatalogService>();

        builder.Services.AddSingleton<VehicleInfoService>();
        builder.Services.AddSingleton<FreezeFrameReaderService>();

        builder.Services.AddSingleton<LiveDataEngine>();

        builder.Services.AddSingleton<OemDataCaptureService>();

        // ============================================================
        // DATABASE
        // ============================================================

        builder.Services.AddSingleton<AppDatabase>();

        builder.Services.AddSingleton<DatabaseService>();

        // ============================================================
        // SETTINGS / LOCALIZATION / USER
        // ============================================================

        builder.Services.AddSingleton<SettingsService>();

        builder.Services.AddSingleton<UnitSystemService>();

        builder.Services.AddSingleton<
            CarScanner.Services.Localization.LocalizationService>();

        builder.Services.AddSingleton<UserProfileService>();

        builder.Services.AddSingleton<ContactService>();

        builder.Services.AddSingleton<
            ReminderNotificationService>();
        builder.Services.AddSingleton<CalendarNotificationService>();
        builder.Services.AddSingleton<MaintenanceCalendarService>();
        builder.Services.AddSingleton<RepairCostSession>();
        builder.Services.AddSingleton<RepairCostService>();

        builder.Services.AddSingleton<LoadingService>();

        builder.Services.AddSingleton<CrashReportingService>();

        builder.Services.AddSingleton<UsageReportService>();

        // ============================================================
        // PRO / PURCHASE
        // ============================================================

        builder.Services.AddSingleton<AccountService>();

        builder.Services.AddSingleton<ProService>();

        builder.Services.AddSingleton<PurchaseService>();

        // ============================================================
        // ADVANCED / OEM / UDS
        // ============================================================

        builder.Services.AddSingleton<AdvancedDiagnosticService>();

        builder.Services.AddSingleton<
            CarScanner.Services.UDS.UdsProtocol>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemProfileResolver>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemAddressRegistry>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.ManufacturerDefinitionCatalogService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemDefinitionCatalogService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemTransportReadinessService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemModuleDiscoveryService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemEcuCapabilityDetectionService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemEcuIdentificationService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemEcuDtcService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemFreezeFrameService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemLiveDataService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemOperationCatalogService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemCodingBackupService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemOperationService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.ServiceDataReaderService>();
        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.ServiceSearchReportService>();
        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.ResetRecorderService>();
        builder.Services.AddTransient<ResetRecorderViewModel>();
        builder.Services.AddTransient<ResetRecorderPage>();

        // Optional anonymous data collection (off until the user opts in) and passive, read-only CAN listening.
        builder.Services.AddSingleton<CarScanner.Services.DataCollection.DataCollectionService>();
        builder.Services.AddSingleton<CarScanner.Services.DataCollection.ConsentPromptService>();
        builder.Services.AddSingleton<CarScanner.Services.Passive.IDbcSource, CarScanner.Services.Passive.CatalogDbcSource>();
        builder.Services.AddSingleton<CarScanner.Services.Passive.PassiveCanService>();
        builder.Services.AddTransient<PassiveCanViewModel>();
        builder.Services.AddTransient<PassiveCanPage>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.ServiceResetVerificationService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.SafetyGatingService>();

        builder.Services.AddSingleton<
            CarScanner.Services.OemDiagnostics.OemDiagnosticsService>();

        builder.Services.AddSingleton<CapabilityDetectionService>();

        // ============================================================
        // OTHER SERVICES
        // ============================================================

        builder.Services.AddSingleton<ActualMileageService>();
        builder.Services.AddSingleton<CarScanner.Services.Mileage.OdometerFinderService>();
        builder.Services.AddSingleton<
            CarScanner.Services.Mileage.IActualMileageProvider,
            CarScanner.Services.Mileage.LearnedOdometerProvider>();

        // Generic PID A6 is the only mileage source that can be queried
        // universally. OEM/CAN sources stay unregistered until their verified
        // transport and profile definitions are available.
        builder.Services.AddSingleton<
            CarScanner.Services.Mileage.IActualMileageProvider,
            CarScanner.Services.Mileage.GenericObdMileageProvider>();
        // Standard distance / time counters (since codes cleared, MIL on): shown apart from the odometer.
        builder.Services.AddSingleton<
            CarScanner.Services.Mileage.IActualMileageProvider,
            CarScanner.Services.Mileage.GenericObdDistanceContextProvider>();

        // Odometer copies kept by several modules (dashboard, engine,
        // transmission …), read from the bundled OBDb catalog.
        builder.Services.AddSingleton<
            CarScanner.Services.Mileage.IActualMileageProvider,
            CarScanner.Services.Mileage.CatalogOdometerProvider>();

        // Odometer the car broadcasts by itself on CAN (GM Volt, Toyota, Honda, Hyundai/Kia, Ford, VW …).
        builder.Services.AddSingleton<
            CarScanner.Services.Mileage.IActualMileageProvider,
            CarScanner.Services.Mileage.BroadcastOdometerProvider>();

        builder.Services.AddSingleton<EmissionService>();
        builder.Services.AddSingleton<TechnicalInspectionService>();

        builder.Services.AddSingleton<VehicleHealthService>();
        builder.Services.AddSingleton<HybridHealthService>();

        // EV / hybrid battery energy (PID 01 9A + brand identifiers from the catalog).
        builder.Services.AddSingleton<CarScanner.Services.Energy.EvEnergyReader>();

        // Service page: parts and fluids tracked one by one (stored on the phone per VIN).
        builder.Services.AddSingleton<CarScanner.Services.Maintenance.MaintenanceTrackerService>();
        builder.Services.AddSingleton<CarScanner.Services.Maintenance.MaintenanceNotificationService>();

        builder.Services.AddSingleton<
            LiveDataRecorder>();

        builder.Services.AddSingleton<RecordingSessionService>();
        builder.Services.AddSingleton<VehicleJournalService>();

        builder.Services.AddSingleton<RunGpsRecorder>();
        builder.Services.AddSingleton<
            AccelerationMeasurementService>();
        builder.Services.AddSingleton<AccelerationLeaderboardService>();
        builder.Services.AddSingleton<QrReportService>();
        builder.Services.AddSingleton<QrReportSession>();

        builder.Services.AddSingleton<
            ObdReportService>();

        builder.Services.AddSingleton<
            FullObdScanService>();

        builder.Services.AddSingleton<
            CarScanner.AI.AiDiagnosticService>();

        // ============================================================
        // SHELL
        // ============================================================

        builder.Services.AddSingleton<AppShell>();

        // ============================================================
        // VIEW MODELS
        // ============================================================

        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddTransient<AccelerationHistoryViewModel>();
        builder.Services.AddTransient<AccelerationLeaderboardViewModel>();
        builder.Services.AddTransient<QrReportViewModel>();


        builder.Services.AddTransient<VehicleBrandSelectionViewModel>();

        builder.Services.AddSingleton<BluetoothViewModel>();


        builder.Services.AddSingleton<LiveDataViewModel>();

        builder.Services.AddTransient<HistoryViewModel>();

        builder.Services.AddSingleton<SettingsViewModel>();

        builder.Services.AddTransient<SensorsViewModel>();

        builder.Services.AddTransient<EmissionsViewModel>();
        builder.Services.AddTransient<TechnicalInspectionViewModel>();

        builder.Services.AddTransient<VehicleHealthViewModel>();
        builder.Services.AddTransient<BatteryViewModel>();
        builder.Services.AddTransient<HybridHealthViewModel>();
        builder.Services.AddTransient<MaintenancePartViewModel>();
        builder.Services.AddTransient<MaintenanceSetupViewModel>();
        builder.Services.AddTransient<AdapterCenterViewModel>();

        builder.Services.AddTransient<EcuIdentifiersViewModel>();

        builder.Services.AddTransient<MileageViewModel>();
        builder.Services.AddTransient<DiagnosticReportViewModel>();

        builder.Services.AddTransient<ProViewModel>();

        builder.Services.AddSingleton<AiDiagnosticViewModel>();

        builder.Services.AddSingleton<FuelViewModel>();
        builder.Services.AddSingleton<GraphStudioViewModel>();
        builder.Services.AddSingleton<CarScanner.Services.Coding.CodingService>();
        builder.Services.AddSingleton<CodingViewModel>();
        builder.Services.AddSingleton<CarScanner.Services.Reports.VehicleReportService>();
        builder.Services.AddSingleton<VehicleReportViewModel>();

        builder.Services.AddTransient<ServiceViewModel>();
        builder.Services.AddTransient<RepairCostViewModel>();

        builder.Services.AddTransient<ModuleScanViewModel>();
        builder.Services.AddTransient<ModuleLiveDataViewModel>();
        builder.Services.AddTransient<ResearchDataViewModel>();
        builder.Services.AddTransient<SensorGraphViewModel>();
        builder.Services.AddTransient<PrePurchaseViewModel>();
        builder.Services.AddTransient<FreezeFrameViewModel>();
        builder.Services.AddTransient<PerformanceVideoViewModel>();
#if ANDROID
        builder.Services.AddSingleton<CarScanner.Services.PerformanceVideo.IPerformanceVideoExporter, CarScanner.Droid.PerformanceVideo.AndroidPerformanceVideoExporter>();
#elif IOS
        builder.Services.AddSingleton<CarScanner.Services.PerformanceVideo.IPerformanceVideoExporter, CarScanner.Platforms.iOS.PerformanceVideo.iOSPerformanceVideoExporter>();
#else
        builder.Services.AddSingleton<CarScanner.Services.PerformanceVideo.IPerformanceVideoExporter, CarScanner.Services.PerformanceVideo.UnsupportedPerformanceVideoExporter>();
#endif
        builder.Services.AddTransient<CapabilityCenterViewModel>();
        builder.Services.AddTransient<Mode05ViewModel>();
        builder.Services.AddTransient<Mode06ViewModel>();

        // ============================================================
        // PAGES
        // ============================================================

        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<StartPage>();

        // ============================================================
        // VIN CHECK (vehicle history by VIN)
        // ============================================================

        builder.Services.AddSingleton<CarScanner.VinCheck.Services.IVinValidatorService, CarScanner.VinCheck.Services.VinValidatorService>();
        builder.Services.AddSingleton<CarScanner.VinCheck.Services.IVinHistoryDatabaseService, CarScanner.VinCheck.Services.VinHistoryDatabaseService>();
#if USE_MOCK_VIN_API
        // Sample data only (for UI work without the backend).
        builder.Services.AddSingleton<CarScanner.VinCheck.Services.IVinHistoryService, CarScanner.VinCheck.Services.MockVinHistoryService>();
#else
        // Real VIN history through the OBD360 backend (AIConfig.BackendBaseUrl).
        // The provider key and the monthly VIN count live only on the server.
        builder.Services.AddSingleton<CarScanner.VinCheck.Services.IVinHistoryService, CarScanner.VinCheck.Services.VinHistoryService>();
#endif
        builder.Services.AddTransient<CarScanner.VinCheck.ViewModels.VinLookupViewModel>();
        builder.Services.AddTransient<CarScanner.VinCheck.ViewModels.VinReportViewModel>();
        builder.Services.AddTransient<CarScanner.VinCheck.ViewModels.VinHistoryViewModel>();
        builder.Services.AddTransient<CarScanner.VinCheck.Views.VinLookupPage>();
        builder.Services.AddTransient<CarScanner.VinCheck.Views.VinReportPage>();
        builder.Services.AddTransient<CarScanner.VinCheck.Views.VinHistoryPage>();
        builder.Services.AddTransient<CarScanner.VinCheck.Views.VinPhotoGalleryPage>();

        builder.Services.AddTransient<FreezeFramePage>();
        builder.Services.AddTransient<AccelerationPage>();
        builder.Services.AddTransient<PerformanceVideoPage>();
        builder.Services.AddTransient<AccelerationHistoryPage>();
        builder.Services.AddTransient<AccelerationLeaderboardPage>();
        builder.Services.AddTransient<QrReportPage>();


        builder.Services.AddTransient<VehicleBrandSelectionPage>();

        builder.Services.AddTransient<BluetoothPage>();

        builder.Services.AddTransient<LiveDataPage>();


        builder.Services.AddTransient<HistoryPage>();

        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AccountPage>();

        builder.Services.AddTransient<ContactPage>();

        builder.Services.AddTransient<SensorsPage>();

        builder.Services.AddTransient<EmissionsPage>();
        builder.Services.AddTransient<TechnicalInspectionPage>();

        builder.Services.AddTransient<VehicleHealthPage>();
        builder.Services.AddTransient<BatteryPage>();
        builder.Services.AddTransient<HybridHealthPage>();
        builder.Services.AddTransient<MaintenancePartPage>();
        builder.Services.AddTransient<MaintenanceSetupPage>();
        builder.Services.AddTransient<AdapterCenterPage>();

        builder.Services.AddTransient<EcuIdentifiersPage>();

        builder.Services.AddTransient<MileagePage>();
        builder.Services.AddTransient<DiagnosticReportPage>();

        builder.Services.AddTransient<ProPage>();

        builder.Services.AddTransient<AiPage>();

        builder.Services.AddTransient<FuelPage>();
        builder.Services.AddTransient<GraphStudioPage>();
        builder.Services.AddTransient<CodingPage>();
        builder.Services.AddTransient<VehicleReportPage>();
        builder.Services.AddTransient<VehicleJournalPage>();
        builder.Services.AddTransient<GraphChannelsPage>();

        builder.Services.AddTransient<ServicePage>();
        builder.Services.AddTransient<RepairCostPage>();

        builder.Services.AddTransient<ModuleScanPage>();
        builder.Services.AddTransient<ModuleLiveDataPage>();
        builder.Services.AddTransient<ResearchDataPage>();
        builder.Services.AddTransient<SensorGraphPage>();
        builder.Services.AddTransient<PrePurchasePage>();
        builder.Services.AddTransient<PrePurchaseAiPage>();
        builder.Services.AddTransient<CapabilityCenterPage>();
        builder.Services.AddTransient<Mode05Page>();
        builder.Services.AddTransient<Mode06Page>();

        // ============================================================
        // PRO / PURCHASE
        // ============================================================

// Android: no in-app purchases (PRO bought on the website unlocks by signing in).

#if IOS
builder.Services.AddSingleton<
    CarScanner.Platforms.iOS.Billing.iOSBillingService>();
#endif

        // ============================================================
        // DEBUG LOGGING
        // ============================================================

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // LiveDataEngine.cs has no reference to Microsoft.Maui.Storage so it can be linked into the
        // plain unit-test project; this is the one place that gives it the real preference store.
        CarScanner.Services.LiveDataEngine.PollingProfileProvider = () => Preferences.Default.Get("polling", "Balanced");

        // Start listening for (opted-in) data collection. A failure here must never stop the app.
        try { app.Services.GetRequiredService<CarScanner.Services.DataCollection.DataCollectionService>(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Data collection init failed: {ex.Message}"); }

        return app;
    }
}
