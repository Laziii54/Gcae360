using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Database;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.ViewModels;

public partial class HomeViewModel : BaseViewModel
{
    private readonly IBluetoothService _bluetooth;
    private readonly OBDService _obd;
    private readonly VehicleInfoService _vehicle;
    private readonly LoadingService _loading;
    private readonly ProService _pro;
    private readonly VehicleContextService _vehicleContext;
    private readonly VehicleBrandProfileService _brand;
    private readonly VehicleProfileResolverService _vehicleProfile;
    private readonly ServiceDataReaderService _serviceDataReader;
    private readonly DtcCatalogService _dtcCatalog;
    private readonly AccelerationMeasurementService _acceleration;
    private readonly LocalizationService _localization;
    private readonly UnitSystemService _units;
    private readonly AccountService _account;

    private const string AccelerationMiniHistoryKey = "acceleration_mini_history_v1";

    public ObservableCollection<string> AccelerationMiniHistory { get; } = new();

    [ObservableProperty]
    private string connectionStatus = "OBD2 არ არის დაკავშირებული";

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private Color statusColor = Colors.Gray;

    [ObservableProperty]
    private string connectedBrandImage = string.Empty;

    [ObservableProperty]
    private string vinNumber = "VIN უცნობია";

    /// <summary>The 17-character VIN inside <see cref="VinNumber"/>, or null while it is unknown.</summary>
    public string? CopyableVin =>
        System.Text.RegularExpressions.Regex.Match(VinNumber ?? string.Empty, @"\b[A-HJ-NPR-Z0-9]{17}\b") is { Success: true } match
            ? match.Value
            : null;

    public bool HasCopyableVin => CopyableVin is not null;

    partial void OnVinNumberChanged(string value)
    {
        OnPropertyChanged(nameof(CopyableVin));
        OnPropertyChanged(nameof(HasCopyableVin));
    }

    [ObservableProperty]
    private string vehicleName = "მანქანა უცნობია";

    [ObservableProperty]
    private string vehicleTrim = "კომპლექტაცია მიუწვდომელია";

    [ObservableProperty]
    private string vehicleLabel = "ტრანსპორტი";

    [ObservableProperty]
    private string vinLabel = "VIN";

    [ObservableProperty]
    private string trimLabel = "კომპლექტაცია";

    [ObservableProperty]
    private string fuelLabel = "საწვავი";

    [ObservableProperty]
    private string freezeFrameButtonText = "Freeze Frame · ხარვეზის მომენტი";

    [ObservableProperty]
    private string brandName = "აირჩიე მანქანა";

    [ObservableProperty]
    private string brandMark = "OBD";

    [ObservableProperty]
    private string brandWatermark = "OBD";

    [ObservableProperty]
    private string brandAccentColor = "#22C55E";

    [ObservableProperty]
    private Color brandHeroBackground = Color.FromArgb("#101827");

    [ObservableProperty]
    private string brandSupportNote = "Generic OBD-II engine diagnostics";

    [ObservableProperty]
    private string brandVerificationText = "აირჩიე მარკა დაკავშირებამდე";

    [ObservableProperty]
    private string protocol = "—";

    [ObservableProperty]
    private string connectedAdapterName = "OBD-II adapter";

    [ObservableProperty]
    private string protocolExplanation = "ჯერ არ არის დაკავშირებული.";

    [ObservableProperty]
    private string elmStatus = "OFF";

    [ObservableProperty]
    private Color elmStatusColor = Colors.Gray;

    [ObservableProperty]
    private string ecuName = "Unknown";

    [ObservableProperty]
    private string engine = "Unknown";

    [ObservableProperty]
    private string fuelType = "Unknown";

    [ObservableProperty]
    private string ecuStatus = "OFF";

    [ObservableProperty]
    private Color ecuStatusColor = Colors.Gray;

    // მანქანის რეჟიმი
    [ObservableProperty]
    private VehicleKind vehicleKind = VehicleKind.Car;

    [ObservableProperty]
    private bool isCarMode = true;

    [ObservableProperty]
    private string vehicleKindLabel = "ავტომობილი";

    [ObservableProperty]
    private string vehicleIcon = "";

    [ObservableProperty]
    private string accelerationResult = "ჯერ არ არის გაზომილი";

    [ObservableProperty]
    private string accelerationStatus = "გაზომვისთვის დააკავშირე OBD2";

    [ObservableProperty]
    private bool isAccelerationMeasuring;

    [ObservableProperty]
    private string accelerationSpeed = "—";

    [ObservableProperty] private string accelerationSpeedUnit = "km/h";

    // Big state line: stop → ready, go! → measuring → result.
    [ObservableProperty] private string accelerationPhaseText = string.Empty;
    [ObservableProperty] private string accelerationPhaseColor = "#94A3B8";
    [ObservableProperty] private Microsoft.Maui.Controls.Brush accelerationPhaseBrush = new Microsoft.Maui.Controls.SolidColorBrush(Color.FromArgb("#94A3B8"));

    // How many real ECU speed samples per second the adapter delivers.
    [ObservableProperty] private string accelerationRateText = "—";
    [ObservableProperty] private string accelerationRateColor = "#94A3B8";
    [ObservableProperty] private string accelerationShareText = "გაზიარება";

    private DateTime? _lastAccelerationCompletedAt;
    private double _lastAccelerationSeconds;
    [ObservableProperty] private string accelerationTestLabel = "0–100 km/h";
    [ObservableProperty] private string accelerationButtonText = "Start 0–100 km/h run";
    [ObservableProperty] private string accelerationScaleCenter = "50";
    [ObservableProperty] private string accelerationScaleMaximum = "100";

    [ObservableProperty]
    private string accelerationSpeedSource = "Vehicle Speed PID 01 0D-ის რეალურ პასუხს ველოდები…";

    [ObservableProperty]
    private string accelerationObdStatus = "Disconnected";

    [ObservableProperty]
    private Color accelerationObdStatusColor = Colors.Gray;

    // "—" (not "0") until the ECU has actually answered once, so a genuine
    // idle reading of 0 is never confused with "no data received yet".
    [ObservableProperty] private string accelerationRpm = "—";
    [ObservableProperty] private string accelerationThrottle = "—";
    [ObservableProperty] private string accelerationSplits = "0–20 — · 0–50 — · 0–80 — · 0–100 —";
    [ObservableProperty] private ObservableCollection<AccelerationSplitDisplay> accelerationSplitRows = new();
    [ObservableProperty] private string accelerationMetrics = "Peak/Avg acceleration გამოჩნდება გაზომვის დასრულების შემდეგ.";
    [ObservableProperty] private IReadOnlyList<ObdTelemetrySample> accelerationGraphSamples = Array.Empty<ObdTelemetrySample>();
    [ObservableProperty] private PerformanceTestDefinition? selectedPerformanceTest;
    [ObservableProperty] private string customStartSpeed = "0";
    [ObservableProperty] private string customTargetSpeed = "100";
    public ObservableCollection<PerformanceTestDefinition> PerformanceTests { get; } = new(CarScanner.Models.PerformanceTests.Standard);
    public IReadOnlyList<int> AccelerationGraphPids { get; } = new[] { 0x0D };

    [ObservableProperty]
    private string languageToggleFlag = "EN";

    // Compact service preview shown on Home when connected
    public ObservableCollection<CarScanner.Models.ServiceMetricDisplayItem> ServicePreview { get; } = new();

    [ObservableProperty] private bool showServicePreview = false;

    private CancellationTokenSource? _servicePreviewCts;

    // Service preview cache: one adapter read per VIN per minute at most.
    private static readonly TimeSpan ServicePreviewCacheLifetime = TimeSpan.FromSeconds(60);
    private ServiceDataReadResult? _servicePreviewCache;
    private string? _servicePreviewCacheVin;
    private DateTime _servicePreviewCacheAt = DateTime.MinValue;
    private bool _servicePreviewReading;

    /// <summary>True while HomePage is on screen (set by Start/StopServicePreview).</summary>
    public bool IsHomeVisible { get; private set; }

    /// <summary>Called from HomePage.OnAppearing: polls service data only while Home is visible.</summary>
    public void StartServicePreview()
    {
        IsHomeVisible = true;
        if (_bluetooth.IsConnected)
            _ = StartServicePreviewPolling();
    }

    /// <summary>Called from HomePage.OnDisappearing: frees the adapter for other pages.</summary>
    public void StopServicePreview()
    {
        IsHomeVisible = false;
        StopServicePreviewPolling(hidePreview: false);
    }


    /// <summary>
    /// True only for the server-confirmed admin account (listed e-mail AND a completed 2-step /admin sign-in —
    /// never a local e-mail match). Gates the "Research" tile so a regular account never even sees it.
    /// </summary>
    [ObservableProperty] private bool isAdmin;

    /// <summary>Called from HomePage.OnAppearing. Safe to call for every account: defaults to false and never throws.</summary>
    public async Task RefreshIsAdminAsync()
    {
        try { IsAdmin = await _account.IsAdminAsync(); }
        catch { IsAdmin = false; }
    }

    /// <summary>PRO marks on the home tiles: shown only while PRO is not active.</summary>
    public bool ShowProBadges => ProGate.IsLocked;

    /// <summary>Top bar badge text; the active state is not shown with a glyph.</summary>
    public string ProButtonText => "PRO";

    /// <summary>True while PRO is active (the AI send control then becomes an icon button).</summary>
    public bool HasProAccess => !ProGate.IsLocked;

    private void RefreshProState()
    {
        OnPropertyChanged(nameof(ShowProBadges));
        OnPropertyChanged(nameof(ProButtonText));
        OnPropertyChanged(nameof(HasProAccess));
    }

    // ---- AI question field on the home page ----

    [ObservableProperty] private string aiQuestion = string.Empty;

    [RelayCommand]
    private async Task AskAi(string? preset)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(preset))
                AiQuestion = LocalizationService.T(preset);

            if (!await RequireObdConnectionAsync())
                return;

            if (!await ProGate.EnsureOrOpenAsync(_pro))
                return;

            var question = AiQuestion?.Trim() ?? string.Empty;
            AiQuestion = string.Empty;

            await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.AiPage));

            if (question.Length > 0 &&
                IPlatformApplication.Current?.Services.GetService<AiDiagnosticViewModel>() is { } ai)
                _ = ai.AskFromHomeAsync(question);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.AskAi");
        }
    }

    public HomeViewModel(
        IBluetoothService bluetooth,
        OBDService obd,
        VehicleInfoService vehicle,
        LoadingService loading,
        ProService pro,
        VehicleContextService vehicleContext,
        VehicleBrandProfileService brand,
        VehicleProfileResolverService vehicleProfile,
        DtcCatalogService dtcCatalog,
        AccelerationMeasurementService acceleration,
        AppDatabase database,
        LocalizationService localization,
        UnitSystemService units,
        ServiceDataReaderService serviceDataReader,
        AccountService account)
    {
        _bluetooth = bluetooth;
        _obd = obd;
        _vehicle = vehicle;
        _loading = loading;
        _pro = pro;
        _pro.ProChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshProState);
        _vehicleContext = vehicleContext;
        _brand = brand;
        _vehicleProfile = vehicleProfile;
        _serviceDataReader = serviceDataReader;
        _dtcCatalog = dtcCatalog;
        _acceleration = acceleration;
        _localization = localization;
        _units = units;
        _account = account;

        SelectedPerformanceTest = CarScanner.Models.PerformanceTests.Default(_units);

        _acceleration.StateChanged += (_, __) =>
    MainThread.BeginInvokeOnMainThread(UpdateAccelerationState);

        _acceleration.Completed += (_, run) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _lastAccelerationCompletedAt = DateTime.UtcNow;
                _lastAccelerationSeconds = run.DurationSeconds;
                AccelerationResult =
                    $"{run.Source.Replace("OBD · ", string.Empty)}: {run.DurationSeconds:F2} წმ";
                // The car that made THIS run, not the car connected now.
                AccelerationResultVehicle = RunVehicleName(run);

                AddAccelerationMiniHistory(
                    run.Source,
                    run.DurationSeconds);

                UpdateAccelerationState();
            });

        _ = LoadLatestAccelerationAsync(database);

        LoadAccelerationMiniHistory();

        _localization.LanguageChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateAccelerationState();

                LanguageToggleFlag = _localization.CurrentBadge;

                UpdateHomeLanguageTexts();

                if (_bluetooth.IsConnected && _vehicle.CurrentVehicle is not null)
                    ApplyVehicleInfo(_vehicle.CurrentVehicle);
                else if (!_bluetooth.IsConnected)
                    ShowDisconnectedIdentity();
            });

        _units.UnitSystemChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(UpdateAccelerationState);

        LanguageToggleFlag = _localization.CurrentBadge;

        UpdateHomeLanguageTexts();
        UpdateAccelerationState();

        Title = "OBD360";

        _bluetooth.ConnectionChanged += OnConnectionChanged;
        _vehicleContext.Changed += OnVehicleContextChanged;
        _brand.Changed += OnBrandChanged;
        _vehicle.VehicleInfoUpdated += OnVehicleInfoUpdated;

        // The service preview is started by HomePage.OnAppearing
        // (StartServicePreview) so it never polls while Home is hidden.

        // მხოლოდ ავტომობილი
        ApplyVehicleContext(VehicleKind.Car);
        ApplyBrandProfile(_brand.Current);

        // A saved brand choice is a preference, not a connected vehicle: until this session identifies a car, say so.
        if (!_bluetooth.IsConnected)
            ShowDisconnectedIdentity();

        UpdateStatus(_bluetooth.IsConnected);

        if (_bluetooth.IsConnected &&
            _vehicle.CurrentVehicle is not null)
        {
            ApplyVehicleInfo(_vehicle.CurrentVehicle);
        }
    }

    private async Task LoadLatestAccelerationAsync(AppDatabase database)
    {
        var latest = await database.GetLatestAccelerationRunAsync();
        if (latest is not null)
        {
            AccelerationResult = $"{latest.Source.Replace("OBD · ", string.Empty)}: {latest.DurationSeconds:F2} წმ";
            AccelerationResultVehicle = RunVehicleName(latest);
        }
    }

    /// <summary>Car shown next to a saved run: stored with the run, never the currently connected car.</summary>
    [ObservableProperty] private string accelerationResultVehicle = string.Empty;

    private string RunVehicleName(AccelerationRun run)
    {
        if (!string.IsNullOrWhiteSpace(run.VehicleDisplayName))
            return run.VehicleDisplayName;
        if (!string.IsNullOrWhiteSpace(run.VIN))
            return "VIN " + run.VIN;
        return _localization.IsEnglish ? "Car not identified" : "მანქანა არ იყო ამოცნობილი";
    }

    private void UpdateAccelerationState()
    {
        var snapshot = _acceleration.GetSnapshot();
        IsAccelerationMeasuring = snapshot.IsArmed;
        AccelerationStatus = _localization.Translate(snapshot.Status);
        AccelerationSpeed = snapshot.HasCurrentSpeed
            ? _units.Convert(snapshot.CurrentSpeed, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : "—";
        AccelerationSpeedUnit = _units.SpeedUnit;
        AccelerationTestLabel = snapshot.CurrentTest.DisplayName;
        AccelerationButtonText = $"Start {snapshot.CurrentTest.DisplayName} run";
        var test = snapshot.CurrentTest;
        AccelerationScaleCenter = _units.Convert(test.DisplayTargetSpeedKph / 2d, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        AccelerationScaleMaximum = _units.Convert(test.DisplayTargetSpeedKph, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        AccelerationSpeedSource = _acceleration.GpsMode
            ? L("ტელეფონის GPS · მიახლოებითი, რეიტინგში არ შედის", "Phone GPS · approximate, not ranked")
            : snapshot.HasCurrentSpeed
                ? L($"რეალური ECU პასუხი · PID 01 0D · {snapshot.LastSpeedAt?.ToLocalTime():HH:mm:ss}", $"Real ECU answer · PID 01 0D · {snapshot.LastSpeedAt?.ToLocalTime():HH:mm:ss}")
                : L("PID 01 0D-ის პასუხს ველოდები…", "Waiting for PID 01 0D…");
        AccelerationObdStatus = _bluetooth.IsConnected ? "Connected" : "Disconnected";
        AccelerationObdStatusColor = _bluetooth.IsConnected ? Colors.LimeGreen : Colors.IndianRed;
        AccelerationRpm = snapshot.HasCurrentRpm
            ? snapshot.CurrentRpm.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "—";
        AccelerationThrottle = snapshot.HasCurrentThrottle
            ? $"{snapshot.CurrentThrottle}%"
            : "—";
        var s = snapshot.Splits;
        var startText = _units.Convert(test.StartSpeedKph, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        AccelerationSplits = string.Join(" · ", snapshot.SplitTargetsKph.Select(target =>
        {
            var targetText = _units.Convert(target, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            return s.TryGetValue(target, out var time) ? $"{startText}–{targetText}: {time:F2}s" : $"{startText}–{targetText}: —";
        }));

        // Every checkpoint (20/40/60/80 and the final target itself, e.g. 100)
        // gets its own row that fills in live, right where its speed label is,
        // instead of only appearing bundled into one summary sentence.
        AccelerationSplitRows.Clear();
        var checkpoints = test.IsDistanceTest
            ? snapshot.SplitTargetsKph.Distinct()
            : snapshot.SplitTargetsKph.Append((int)Math.Round(test.TargetSpeedKph)).Distinct();
        foreach (var target in checkpoints)
        {
            var targetText = _units.Convert(target, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            AccelerationSplitRows.Add(new AccelerationSplitDisplay
            {
                Label = $"{startText}–{targetText}",
                TimeText = s.TryGetValue(target, out var time) ? $"{time:F2}s" : "—"
            });
        }
        var hasRunData = snapshot.Splits.Count > 0 || snapshot.DistanceMeters > 0;
        AccelerationMetrics = !hasRunData
            ? "—"
            : _localization.IsEnglish
                ? $"Max speed: {_units.FormatSpeed(snapshot.MaximumSpeed)} · Max RPM: {snapshot.MaximumRpm}\nDistance: {_units.Format(snapshot.DistanceMeters, "m", "0")} · Peak acceleration: {snapshot.PeakAcceleration:F2} m/s²"
                : $"მაქს. სიჩქარე: {_units.FormatSpeed(snapshot.MaximumSpeed)} · მაქს. RPM: {snapshot.MaximumRpm}\nმანძილი: {_units.Format(snapshot.DistanceMeters, "m", "0")} · მაქს. აჩქარება: {snapshot.PeakAcceleration:F2} m/s²";
        AccelerationGraphSamples = snapshot.RunSamples;
        UpdateAccelerationPhaseAndRate(snapshot);
    }

    private void UpdateAccelerationPhaseAndRate(AccelerationMeasurementSnapshot snapshot)
    {
        const string ok = "#1F8A4C", warn = "#C98A1A", bad = "#B3361F", muted = "#94A3B8", blue = "#1D5BD6";
        var test = snapshot.CurrentTest;
        AccelerationShareText = L("გაზიარება", "Share");

        if (!_bluetooth.IsConnected)
        {
            (AccelerationPhaseText, AccelerationPhaseColor) = (L("დააკავშირე OBD ადაპტერი", "Connect the OBD adapter"), muted);
        }
        else if (_acceleration.IsTiming)
        {
            (AccelerationPhaseText, AccelerationPhaseColor) = (L("იზომება…", "Measuring…"), blue);
        }
        else if (snapshot.IsArmed && _acceleration.IsReadyToGo)
        {
            (AccelerationPhaseText, AccelerationPhaseColor) = (L("მზადაა — დაიძარი!", "Ready — go!"), ok);
        }
        else if (snapshot.IsArmed)
        {
            var start = _units.Convert(test.StartSpeedKph, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            AccelerationPhaseText = test.RequiresStationaryStart
                ? L("გაჩერდი — გაზომვა 0-დან იწყება", "Stop — the run starts from 0")
                : L($"ჩამოდი {start} {_units.SpeedUnit}-მდე", $"Slow down to {start} {_units.SpeedUnit}");
            AccelerationPhaseColor = warn;
        }
        else if (_lastAccelerationCompletedAt is { } done && DateTime.UtcNow - done < TimeSpan.FromSeconds(20))
        {
            AccelerationPhaseText = L($"დასრულდა: {_lastAccelerationSeconds:F2} წმ", $"Done: {_lastAccelerationSeconds:F2} s");
            AccelerationPhaseColor = ok;
        }
        else
        {
            (AccelerationPhaseText, AccelerationPhaseColor) = (L("დააჭირე ღილაკს გაზომვის დასაწყებად", "Press the button to arm a run"), muted);
        }

        AccelerationPhaseBrush = new Microsoft.Maui.Controls.SolidColorBrush(Color.FromArgb(AccelerationPhaseColor));

        var rate = _acceleration.SpeedSampleRate;
        if (!snapshot.HasCurrentSpeed || rate <= 0)
        {
            (AccelerationRateText, AccelerationRateColor) = ("—", muted);
            return;
        }

        var quality = rate >= 8 ? L("ზუსტი", "accurate") : rate >= 4 ? L("საშუალო", "fair") : L("ნელი ადაპტერი", "slow adapter");
        AccelerationRateText = L(
            $"{rate:0.#} გაზომვა/წმ · {quality}",
            $"{rate:0.#} samples/s · {quality}");
        AccelerationRateColor = rate >= 8 ? ok : rate >= 4 ? warn : bad;
    }

    partial void OnSelectedPerformanceTestChanged(PerformanceTestDefinition? value)
    {
        if (value is not null && _acceleration.SetTest(value))
            UpdateAccelerationState();
    }

    [RelayCommand]
    private void ApplyCustomPerformanceTest()
    {
        if (!TryParseSpeed(CustomStartSpeed, out var start) || !TryParseSpeed(CustomTargetSpeed, out var target))
        {
            AccelerationStatus = "Custom ტესტისთვის ჩაწერე სწორი საწყისი და საბოლოო სიჩქარე.";
            return;
        }

        var factor = _units.IsImperial ? 1.609344d : 1d;
        start *= factor;
        target *= factor;
        if (target <= start + 1d || target > 300d)
        {
            AccelerationStatus = "საბოლოო სიჩქარე საწყისზე მეტია აუცილებელი და 300 km/h-ს არ უნდა აჭარბებდეს.";
            return;
        }

        var displayStart = _units.Convert(start, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        var displayTarget = _units.Convert(target, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        var custom = new PerformanceTestDefinition(
            "custom",
            $"{displayStart}–{displayTarget} {_units.SpeedUnit}",
            start,
            target,
            BuildCustomSplits(start, target));
        var prior = PerformanceTests.FirstOrDefault(item => item.Id == "custom");
        if (prior is not null)
            PerformanceTests.Remove(prior);
        PerformanceTests.Add(custom);
        SelectedPerformanceTest = custom;
    }

    private static IReadOnlyList<int> BuildCustomSplits(double start, double target)
    {
        var span = target - start;
        return Enumerable.Range(1, 3)
            .Select(part => (int)Math.Round(start + span * part / 4d))
            .Where(speed => speed > start && speed < target)
            .Distinct()
            .ToArray();
    }

    private static bool TryParseSpeed(string text, out double speed) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speed) ||
        double.TryParse(text, System.Globalization.NumberStyles.Float, new System.Globalization.CultureInfo("ka-GE"), out speed);

    /// <summary>Called by Performance Lab on entry to display only real ECU speed samples.</summary>
    public async Task StartAccelerationPreviewAsync()
    {
        if (!_bluetooth.IsConnected)
        {
            AccelerationSpeed = "—";
            AccelerationSpeedSource = L("ადაპტერი არ არის · შეგიძლია ტელეფონის GPS-ით გაზომო", "No adapter · you can measure with the phone GPS");
            AccelerationObdStatus = "Disconnected";
            AccelerationObdStatusColor = Colors.IndianRed;
            AccelerationStatus = L("ზუსტი დროისთვის დააკავშირე OBD2 ადაპტერი, ან დააჭირე ARM და აირჩიე GPS.",
                "Connect an OBD2 adapter for an exact time, or tap ARM and choose GPS.");
            return;
        }

        try
        {
            await _acceleration.StartPreviewAsync();
            UpdateAccelerationState();
        }
        catch (Exception ex)
        {
            UpdateAccelerationState();
            AccelerationSpeed = "—";
            AccelerationSpeedSource = L("ECU-მ სიჩქარე (PID 01 0D) არ დააბრუნა", "The ECU did not return the speed (PID 01 0D)");
            System.Diagnostics.Debug.WriteLine($"Acceleration preview failed: {ex}");
            AccelerationStatus = ex is InvalidOperationException or TimeoutException
                ? UserError.Describe(ex)
                : L("სიჩქარის წაკითხვა ვერ დაიწყო. შეამოწმე OBD კავშირი, ან გაზომე GPS-ით.", "Could not start reading the speed. Check the OBD connection, or measure with GPS.");
        }
    }

    /// <summary>Restores normal shared live-data polling when leaving Performance Lab.</summary>
    public async Task StopAccelerationPreviewAsync()
    {
        await _acceleration.StopPreviewAsync();
        UpdateAccelerationState();
    }

    [RelayCommand]
    private async Task StartAcceleration()
    {
        try
        {
        if (IsBusy || IsAccelerationMeasuring)
            return;

        // No adapter: offer timing from the phone GPS (works on any car,
        // also on cars without OBD speed, but it is an estimate and not ranked).
        if (!_bluetooth.IsConnected)
        {
            var useGps = await Shell.Current.DisplayAlertLocalizedAsync(
                L("ადაპტერი არ არის დაკავშირებული", "No adapter connected"),
                L("გავზომო ტელეფონის GPS-ით? GPS წამში დაახლოებით ერთხელ ახლდება, ამიტომ შედეგი მიახლოებითია და რეიტინგში არ შედის. ზუსტი დროისთვის დააკავშირე OBD2 ადაპტერი.",
                  "Measure with the phone GPS? GPS updates about once a second, so the result is approximate and not ranked. For an exact time, connect an OBD2 adapter."),
                L("GPS-ით გაზომვა", "Use GPS"),
                L("გაუქმება", "Cancel"));
            if (!useGps)
                return;

            try
            {
                IsBusy = true;
                await _acceleration.StartGpsAsync();
                UpdateAccelerationState();
            }
            catch (Exception ex)
            {
                AccelerationStatus = UserError.Describe(ex);
            }
            finally
            {
                IsBusy = false;
            }

            return;
        }

        if (!await RequireObdConnectionAsync())
            return;

        try
        {
            IsBusy = true;
            await _acceleration.StartAsync();
            UpdateAccelerationState();
        }
        catch (Exception ex)
        {
            AccelerationStatus = UserError.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.StartAcceleration");
        }
    }

    [RelayCommand]
    private void CancelAcceleration()
    {
        _acceleration.Cancel();
        UpdateAccelerationState();
    }

    private void LoadAccelerationMiniHistory()
    {
        AccelerationMiniHistory.Clear();

        try
        {
            var json = Preferences.Default.Get(
                AccelerationMiniHistoryKey,
                "[]");

            var items =
                JsonSerializer.Deserialize<List<string>>(json)
                ?? new List<string>();

            foreach (var item in items)
                AccelerationMiniHistory.Add(item);
        }
        catch
        {
            AccelerationMiniHistory.Clear();
        }
    }

    private void AddAccelerationMiniHistory(
        string source,
        double durationSeconds)
    {
        var testName = (source ?? string.Empty)
            .Replace("OBD · ", string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(testName))
            testName = AccelerationTestLabel;

        var entry =
            $"{testName} — {durationSeconds:F2} წმ · {DateTime.Now:dd.MM HH:mm}";

        AccelerationMiniHistory.Insert(0, entry);

        // პატარა ისტორია — ბოლო 6 გაზომვა
        while (AccelerationMiniHistory.Count > 6)
            AccelerationMiniHistory.RemoveAt(
                AccelerationMiniHistory.Count - 1);

        SaveAccelerationMiniHistory();
    }

    private void SaveAccelerationMiniHistory()
    {
        var json = JsonSerializer.Serialize(
            AccelerationMiniHistory.ToArray());

        Preferences.Default.Set(
            AccelerationMiniHistoryKey,
            json);
    }

    [RelayCommand]
    private void ClearAccelerationMiniHistory()
    {
        AccelerationMiniHistory.Clear();

        Preferences.Default.Remove(
            AccelerationMiniHistoryKey);
    }

    [RelayCommand]
    private Task GoToAccelerationHistory()
        => CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.AccelerationHistoryPage));

    [RelayCommand]
    private Task GoToAccelerationLeaderboard()
        => CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.AccelerationLeaderboardPage));

    [RelayCommand]
    private async Task GoToAcceleration()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.AccelerationPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToAcceleration");
        }
    }

    /// <summary>Opens the language window (all languages); the tapped language is stored and applied.</summary>
    [RelayCommand]
    private async Task ToggleLanguage()
    {
        try
        {
            var picked = await Views.LanguagePickerPage.ShowAsync(_localization.Translate("ენა"));
            if (picked is not null && picked.Code != _localization.CurrentLanguageCode)
                _localization.SetLanguage(picked.DisplayName);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.ToggleLanguage");
        }
    }


    private void OnConnectionChanged(
        object? sender,
        bool connected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (connected)
            {
                ApplyBrandProfile(_brand.Current);

                // Start reading VIN / vehicle / trim / fuel right away. The
                // read runs in the shared service, independent of which page
                // is open, and each field is pushed to Home as soon as it is
                // known (see OnVehicleInfoUpdated).
                var identityRead = _vehicle.StartConnectedIdentityReadAsync(forceVinRefresh: true);
                if (_vehicle.CurrentVehicle is not null)
                    ApplyVehicleInfo(_vehicle.CurrentVehicle);
                else
                    ShowIdentityLoading();
                _ = ObserveIdentityReadAsync(identityRead);
                if (IsHomeVisible)
                    _ = StartServicePreviewPolling();
            }
            else
            {
                StopServicePreviewPolling();
                _servicePreviewCache = null;
                _servicePreviewCacheVin = null;
                _vehicle.ClearConnectedVehicleState();
                ShowDisconnectedIdentity();
            }

            UpdateStatus(connected);
        });
    }


    private void OnVehicleInfoUpdated(
        object? sender,
        VehicleInfo vehicle)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // A late update from a previous connection must not repaint Home
            // after the adapter has been disconnected.
            if (!_bluetooth.IsConnected)
                return;

            ApplyVehicleInfo(vehicle);
            UpdateStatus(_bluetooth.IsConnected);
            // When vehicle identity becomes available, refresh preview immediately
            if (_bluetooth.IsConnected && IsHomeVisible && _servicePreviewCts is { } previewCts)
                _ = RefreshServicePreviewOnceAsync(previewCts.Token);
        });
    }

    private async Task RefreshServicePreviewOnceAsync(CancellationToken token)
    {
        if (_servicePreviewReading)
            return;

        try
        {
            _servicePreviewReading = true;

            var vehicle = _vehicle.CurrentVehicle;
            if (vehicle is null || token.IsCancellationRequested)
                return;

            var vin = vehicle.VIN ?? string.Empty;
            ServiceDataReadResult result;
            if (_servicePreviewCache is not null &&
                string.Equals(_servicePreviewCacheVin, vin, StringComparison.OrdinalIgnoreCase) &&
                DateTime.UtcNow - _servicePreviewCacheAt < ServicePreviewCacheLifetime)
            {
                result = _servicePreviewCache;
            }
            else
            {
                result = await _serviceDataReader.ReadAsync(vehicle, token);
                if (token.IsCancellationRequested)
                    return;

                _servicePreviewCache = result;
                _servicePreviewCacheVin = vin;
                _servicePreviewCacheAt = DateTime.UtcNow;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                ServicePreview.Clear();
                foreach (var r in result.Values.Take(4))
                    ServicePreview.Add(new CarScanner.Models.ServiceMetricDisplayItem
                    {
                        Id = r.Id,
                        Kind = r.Kind,
                        IconText = GetServiceMetricIcon(r.Kind),
                        Name = LocalizeServiceMetricName(r),
                        ValueText = FormatServiceMetricValue(r),
                        ValueColor = GetServiceMetricValueColor(r),
                        Detail = r.Detail,
                        SourceText = r.SourceCatalog
                    });

                ShowServicePreview = ServicePreview.Count > 0;
            });
        }
        catch
        {
            // ignore preview failures (including cancellation)
        }
        finally
        {
            _servicePreviewReading = false;
        }
    }

    private void StopServicePreviewPolling(bool hidePreview = true)
    {
        try
        {
            _servicePreviewCts?.Cancel();
            _servicePreviewCts?.Dispose();
            _servicePreviewCts = null;
            if (hidePreview)
                ShowServicePreview = false;
        }
        catch { }
    }

    private async Task StartServicePreviewPolling()
    {
        StopServicePreviewPolling(hidePreview: false);
        var cts = new CancellationTokenSource();
        _servicePreviewCts = cts;
        var token = cts.Token;

        // Only while Home is visible and connected. Reads are cached per VIN
        // for 60 s, so this loop touches the adapter at most once a minute.
        while (!token.IsCancellationRequested && _bluetooth.IsConnected && IsHomeVisible)
        {
            await RefreshServicePreviewOnceAsync(token);
            try { await Task.Delay(ServicePreviewCacheLifetime, token); } catch { break; }
        }
    }

    // Short, user-readable tile words (no technical abbreviations).
    private string GetServiceMetricIcon(ServiceMetricKind kind) => kind switch
    {
        ServiceMetricKind.OilLifePercent => L("ზეთი", "Oil"),
        ServiceMetricKind.OilLevel => L("დონე", "Level"),
        ServiceMetricKind.DistanceToNextService => L("კმ", "km"),
        ServiceMetricKind.DaysToNextService => L("დღე", "Days"),
        ServiceMetricKind.DistanceSinceOilChange => L("კმ", "km"),
        ServiceMetricKind.OilChangeInterval => L("ციკლი", "Cycle"),
        ServiceMetricKind.InspectionDistance => L("შემოწმ.", "Check"),
        ServiceMetricKind.InspectionDays => L("დღე", "Days"),
        ServiceMetricKind.MaintenanceCounter => L("მთვლელი", "Count"),
        ServiceMetricKind.ServiceStatus => L("სერვისი", "Service"),
        _ => L("ინფო", "Info")
    };

    private string LocalizeServiceMetricName(ServiceMetricReading reading) => reading.Kind switch
    {
        ServiceMetricKind.OilLifePercent => L("ზეთის დარჩენილი რესურსი", "Oil Life Remaining"),
        ServiceMetricKind.OilLevel when
            reading.SourceName.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
            reading.SourceName.Contains("warning", StringComparison.OrdinalIgnoreCase)
                => L("ზეთის დონის ზღვარი", "Oil level threshold"),
        ServiceMetricKind.OilLevel when
            reading.SourceName.Contains("max", StringComparison.OrdinalIgnoreCase)
                => L("ზეთის დონის მაქსიმუმი", "Oil level maximum"),
        ServiceMetricKind.OilLevel when
            reading.SourceName.Contains("min", StringComparison.OrdinalIgnoreCase)
                => L("ზეთის დონის მინიმუმი", "Oil level minimum"),
        ServiceMetricKind.OilLevel => L("ზეთის დონე", "Oil level"),
        ServiceMetricKind.DistanceToNextService => L("მანძილი შემდეგ სერვისამდე", "Distance to next service"),
        ServiceMetricKind.DaysToNextService => L("დღეები შემდეგ სერვისამდე", "Days to next service"),
        ServiceMetricKind.DistanceSinceOilChange => L("მანძილი ზეთის ბოლო შეცვლის შემდეგ", "Distance since last oil change"),
        ServiceMetricKind.OilChangeInterval => L("ზეთის_changed_interval", "Oil change interval"),
        ServiceMetricKind.InspectionDistance => L("მანძილი ინსპექციამდე", "Distance to inspection"),
        ServiceMetricKind.InspectionDays => L("დღეები ინსპექციამდე", "Days to inspection"),
        ServiceMetricKind.MaintenanceCounter => reading.SourceName,
        ServiceMetricKind.ServiceStatus => L("სერვისის სტატუსი", "Service status"),
        _ => reading.SourceName
    };

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    /// <summary>
    /// The ranked test of the unit system the person chose: 0–100 km/h for metric, 0–60 mph for imperial. The page
    /// heading and the ranking row follow it instead of a fixed "0–100", and a change of language never changes it.
    /// </summary>
    public string AccelerationHeadline => _units.PerformanceTestLabel;
    public string AccelerationRankingRowText => L($"{_units.PerformanceTestLabel} რეიტინგი", $"{_units.PerformanceTestLabel} ranking");

    private string FormatServiceMetricValue(ServiceMetricReading reading)
    {
        var unit = (reading.Unit ?? string.Empty).Trim().ToLowerInvariant();
        return unit switch
        {
            "percent" or "%" => $"{reading.NumericValue:0.#}%",
            "kilometers" or "kilometer" or "km" => $"{reading.NumericValue:0} km",
            "days" or "day" => L($"{reading.NumericValue:0} დღე", $"{reading.NumericValue:0} days"),
            "centimeters" or "centimeter" or "cm" => $"{reading.NumericValue:0.##} cm",
            "offon" => reading.NumericValue >= 0.5 ? L("ჩართული", "ON") : L("გამორთული", "OFF"),
            "scalar" or "unknown" or "" => $"{reading.NumericValue:0.##}",
            _ => $"{reading.NumericValue:0.##} {reading.Unit}"
        };
    }

    private string GetServiceMetricValueColor(ServiceMetricReading reading)
    {
        return reading.Kind switch
        {
            ServiceMetricKind.OilLifePercent when reading.NumericValue <= 10 => "#EF4444",
            ServiceMetricKind.OilLifePercent when reading.NumericValue <= 25 => "#F59E0B",
            ServiceMetricKind.OilLifePercent => "#4ADE80",
            ServiceMetricKind.DistanceToNextService when reading.NumericValue <= 0 => "#EF4444",
            ServiceMetricKind.DistanceToNextService when reading.NumericValue <= 1000 => "#F59E0B",
            ServiceMetricKind.DistanceToNextService => "#4ADE80",
            ServiceMetricKind.DaysToNextService when reading.NumericValue <= 0 => "#EF4444",
            ServiceMetricKind.DaysToNextService when reading.NumericValue <= 30 => "#F59E0B",
            ServiceMetricKind.DaysToNextService => "#4ADE80",
            ServiceMetricKind.InspectionDistance when reading.NumericValue <= 0 => "#EF4444",
            ServiceMetricKind.InspectionDistance when reading.NumericValue <= 1000 => "#F59E0B",
            ServiceMetricKind.InspectionDistance => "#4ADE80",
            ServiceMetricKind.InspectionDays when reading.NumericValue <= 0 => "#EF4444",
            ServiceMetricKind.InspectionDays when reading.NumericValue <= 30 => "#F59E0B",
            ServiceMetricKind.InspectionDays => "#4ADE80",
            _ => "#4ADE80"
        };
    }

    private void OnBrandChanged(object? sender, VehicleBrandProfile profile)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplyBrandProfile(profile));
    }

    private void ApplyBrandProfile(VehicleBrandProfile profile)
    {
        BrandName = profile.DisplayName;
        BrandMark = profile.Mark;
        BrandWatermark = profile.Mark;
        BrandAccentColor = profile.AccentColor;
        BrandHeroBackground = CreateBrandHeroBackground(profile.AccentColor);

        ConnectedBrandImage = _bluetooth.IsConnected
        ? GetConnectedBrandImage(profile.DisplayName)
        : string.Empty;

        BrandSupportNote = profile.GenericSupportNote;
        BrandVerificationText = _brand.GetVerificationMessage(null);

        // The selected brand is known before the ECU replies.  Show that
        // immediately, but keep VIN/year explicitly marked as pending so the
        // app never presents guessed vehicle data as an ECU result.
        if (_bluetooth.IsConnected)
        {
            if (VinNumber is "VIN უცნობია" or "VIN მიუწვდომელია ამ ECU-ზე")
                VinNumber = "VIN ECU-დან იტვირთება…";
        }
    }

    private static Color CreateBrandHeroBackground(string accentColor)
    {
        try
        {
            var accent = Color.FromArgb(accentColor);
            return new Color(
                0.055f + accent.Red * 0.15f,
                0.075f + accent.Green * 0.13f,
                0.115f + accent.Blue * 0.18f,
                1f);
        }
        catch
        {
            return Color.FromArgb("#101827");
        }
    }

    private static string GetConnectedBrandImage(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
            return string.Empty;

        return brand.Trim().ToLowerInvariant() switch
        {
            "acura" => "acura.png",

            "alfa romeo" => "alfa_romeo.png",
            "alfa-romeo" => "alfa_romeo.png",

            "audi" => "audi.png",
            "bentley" => "bentley.png",
            "bmw" => "bmw.png",
            "byd" => "byd.png",
            "cadillac" => "cadillac.png",
            "chery" => "chery.png",
            "chevrolet" => "chevrolet.png",
            "chrysler" => "chrysler.png",

            "citroen" => "citroen.png",
            "citroën" => "citroen.png",

            "dacia" => "dacia.png",
            "daewoo" => "daewoo.png",
            "dodge" => "dodge.png",
            "dongfeng" => "dongfeng.png",

            "ds" => "ds.png",
            "ds automobiles" => "ds.png",

            "ferrari" => "ferrari.png",
            "fiat" => "fiat.png",
            "ford" => "ford.png",
            "geely" => "geely.png",

            "genesis" => "genesis.jpg",

            "gmc" => "gmc.png",
            "haval" => "haval.png",
            "honda" => "honda.png",
            "hongqi" => "hongqi.png",
            "hyundai" => "hyundai.png",
            "infiniti" => "infiniti.png",
            "isuzu" => "isuzu.png",
            "jac" => "jac.png",
            "jaguar" => "jaguar.png",
            "jeep" => "jeep.png",
            "kia" => "kia.png",

            "lada" => "lada.jpg",

            "lamborghini" => "lamborghini.png",
            "lancia" => "lancia.png",

            "land rover" => "land_rover.png",
            "land-rover" => "land_rover.png",

            "lexus" => "lexus.png",
            "lincoln" => "lincoln.png",
            "lucid" => "lucid.png",
            "maserati" => "maserati.png",
            "mazda" => "mazda.png",

            "mclaren" => "mclaren.png",

            "mercedes" => "mercedes.png",
            "mercedes-benz" => "mercedes.png",
            "mercedes benz" => "mercedes.png",

            "mg" => "mg.png",
            "mini" => "mini.png",
            "mitsubishi" => "mitsubishi.png",
            "nio" => "nio.png",
            "nissan" => "nissan.png",
            "opel" => "opel.png",
            "peugeot" => "peugeot.png",
            "polestar" => "polestar.png",
            "porsche" => "porsche.png",
            "ram" => "ram.png",
            "renault" => "renault.png",
            "rivian" => "rivian.png",

            "rolls royce" => "rolls_royce.png",
            "rolls-royce" => "rolls_royce.png",

            "saab" => "saab.png",
            "seat" => "seat.png",

            "skoda" => "skoda.png",
            "škoda" => "skoda.png",

            "smart" => "smart.png",

            "ssangyong" => "ssangyong.png",
            "ssang yong" => "ssangyong.png",

            "subaru" => "subaru.png",
            "suzuki" => "suzuki.png",
            "tesla" => "tesla.png",
            "toyota" => "toyota.png",
            "uaz" => "uaz.png",
            "volkswagen" => "volkswagen.png",
            "vw" => "volkswagen.png",
            "volvo" => "volvo.png",
            "zeekr" => "zeekr.png",

            _ => string.Empty
        };
    }

    private void OnVehicleContextChanged(
        object? sender,
        VehicleKind kind)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // პროექტი მხოლოდ მანქანაზეა
            ApplyVehicleContext(VehicleKind.Car);
        });
    }


    private void ApplyVehicleContext(
        VehicleKind kind)
    {
        VehicleKind = VehicleKind.Car;
        IsCarMode = true;

        VehicleKindLabel = "ავტომობილი";
        VehicleIcon = "";
    }


    private void UpdateStatus(bool connected)
    {
        IsConnected = connected;

        ConnectionStatus =
            connected
                ? "დაკავშირებულია"
                : "არ არის დაკავშირებული";

        StatusColor =
            connected
                ? Color.FromArgb("#22C55E")
                : Color.FromArgb("#64748B");

        ElmStatus =
            connected
                ? "ON"
                : "OFF";

        ElmStatusColor = StatusColor;

        EcuStatus =
            _obd.IsInitialized
                ? "READY"
                : connected
                    ? "CONNECTED"
                    : "OFF";

        EcuStatusColor =
            _obd.IsInitialized
                ? Color.FromArgb("#22C55E")
                : connected
                    ? Color.FromArgb("#F59E0B")
                    : Color.FromArgb("#64748B");

        Protocol =
            _obd.CurrentProtocolDescription;

        ProtocolExplanation =
            ExplainProtocol(Protocol);

        ConnectedAdapterName =
            string.IsNullOrWhiteSpace(
                _bluetooth.ConnectedDeviceName)
                ? "OBD-II adapter"
                : _bluetooth.ConnectedDeviceName;
    }


    [RelayCommand]
    private async Task Connect()
    {
        try
        {
        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.VehicleBrandSelectionPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.Connect");
        }
    }


    [RelayCommand]
    private async Task GoToBluetooth()
    {
        try
        {
        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.VehicleBrandSelectionPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToBluetooth");
        }
    }

    [RelayCommand]
    private Task BeginConnection()
        => CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.VehicleBrandSelectionPage));




    // ============================================================
    // მანქანის დიაგნოსტიკა
    // ============================================================

    [RelayCommand]
    private async Task StartScan()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.ModuleScanPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.StartScan");
        }
    }


    [RelayCommand]
    private async Task GoToTroubleCodes()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.ModuleScanPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToTroubleCodes");
        }
    }


    /// <summary>
    /// Coding is switched off for users for now: the tile stays dimmed, connected or not, and a tap only says
    /// "Coming soon" at the bottom of the screen. The Coding page itself is untouched.
    /// </summary>
    [RelayCommand]
    private void GoToCoding()
    {
        try
        {
            CarScanner.Services.Reports.ReportToast.Show(_localization.Translate(L("მალე დაემატება", "Coming soon")));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToCoding");
        }
    }

    /// <summary>Graph Studio opens without an adapter too (saved recordings).</summary>
    [RelayCommand]
    private async Task GoToGraphStudio()
    {
        try
        {
            if (!_bluetooth.IsConnected) return;
            await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.GraphStudioPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToGraphStudio");
        }
    }

    [RelayCommand]
    private async Task GoToLiveData()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.LiveDataPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToLiveData");
        }
    }


    [RelayCommand]
    private async Task GoToSensors()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.SensorsPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToSensors");
        }
    }


    [RelayCommand]
    private async Task GoToHistory()
    {
        try
        {
        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.HistoryPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToHistory");
        }
    }


    [RelayCommand]
    private async Task GoToSettings()
    {
        try
        {
        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.SettingsPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToSettings");
        }
    }


    [RelayCommand]
    private async Task GoToEmissions()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.EmissionsPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToEmissions");
        }
    }

    [RelayCommand]
    private async Task GoToTechnicalInspection()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.TechnicalInspectionPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToTechnicalInspection");
        }
    }

    [RelayCommand]
    private async Task GoToVehicleHealth()
    {
        try
        {
        if (!await RequireObdConnectionAsync()) return;
        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.VehicleHealthPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToVehicleHealth");
        }
    }

    [RelayCommand]
    private async Task GoToBattery()
    {
        try
        {
        if (!await RequireObdConnectionAsync()) return;
        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.BatteryPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToBattery");
        }
    }

    [RelayCommand]
    private async Task GoToHybridHealth()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;
        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.HybridHealthPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToHybridHealth");
        }
    }
    [RelayCommand] private async Task GoToAdapterCenter() {
        try
        {
            // The adapter page is where the connection is made, so it must open without one.
            await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.AdapterCenterPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToAdapterCenter");
        } }


    [RelayCommand]
    private async Task GoToEcu()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.EcuIdentifiersPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToEcu");
        }
    }

    [RelayCommand]
    private async Task GoToCapabilities()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.CapabilityCenterPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToCapabilities");
        }
    }


    [RelayCommand]
    private async Task GoToMileage()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.MileagePage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToMileage");
        }
    }

    // Freeze Frame is free (not PRO); it only needs an OBD connection.
    [RelayCommand]
    private async Task GoToFreezeFrame()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.FreezeFramePage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToFreezeFrame");
        }
    }

    [RelayCommand]
    private async Task GoToDiagnosticReport()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        // One combined report: every check of this car, exported together.
        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.VehicleReportPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToDiagnosticReport");
        }
    }


    [RelayCommand]
    private async Task GoToAi()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.AiPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToAi");
        }
    }


    [RelayCommand]
    private async Task GoToFuel()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.FuelPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToFuel");
        }
    }


    private async Task<bool> RequireProAsync(
        string feature)
    {
        if (await _pro.EnsureProAsync())
            return true;

        var openActivation = await Shell.Current.DisplayAlertLocalizedAsync(
            CarScanner.Services.Localization.LocalizationService.T("PRO ფუნქცია"),
            CarScanner.Services.Localization.LocalizationService.T($"{feature} ხელმისაწვდომია მხოლოდ აქტიური PRO ვერსიით."),
            CarScanner.Services.Localization.LocalizationService.T("გააქტიურება"),
            CarScanner.Services.Localization.LocalizationService.T("ახლა არა"));

        if (openActivation)
            await GoToPro();

        return false;
    }


    [RelayCommand]
    private async Task GoToService()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.ServicePage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToService");
        }
    }


    [RelayCommand]
    private async Task GoToModules()
    {
        try
        {
        if (!await RequireObdConnectionAsync())
            return;

        // "Module diagnostics" tile = live values of the modules (ABS, steering,
        // battery …). The all-module trouble-code scan stays on the DTC page.
        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.ModuleLiveDataPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToModules");
        }
    }


    [RelayCommand]
    private async Task GoToPro()
    {
        try
        {
        await CarScanner.Services.SafeNavigation.GoToAsync(
            nameof(Views.ProPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.GoToPro");
        }
    }

    private bool _connectPromptOpen;

    /// <summary>For tiles handled in code-behind: true when connected, otherwise the "connect an adapter" explanation is shown.</summary>
    public Task<bool> EnsureObdConnectionAsync() => RequireObdConnectionAsync();

    /// <summary>
    /// Every tile stays visible and tappable. Features that need the car are muted until the adapter is connected, and a tap
    /// on one explains what to do (once at a time) instead of silently doing nothing.
    /// </summary>
    private async Task<bool> RequireObdConnectionAsync()
    {
        if (_bluetooth.IsConnected)
            return true;

        if (_connectPromptOpen)
            return false;

        _connectPromptOpen = true;
        try
        {
            if (Shell.Current is { } shell)
                await CarScanner.Services.PageDialogExtensions.DisplayAlertLocalizedAsync(
                    shell,
                    L("OBD ადაპტერი საჭიროა", "OBD adapter needed"),
                    L("ეს ფუნქცია მუშაობს დაკავშირებულ OBD ადაპტერთან. გახსენი „ადაპტერი“, დააკავშირე და სცადე თავიდან.",
                      "This feature needs a connected OBD adapter. Open Adapter, connect it, and try again."),
                    "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Connect prompt failed: {ex.Message}");
        }
        finally
        {
            _connectPromptOpen = false;
        }

        return false;
    }

    // ============================================================
    // OBD PROTOCOL EXPLANATION
    // ============================================================

    private static string ExplainProtocol(
        string protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol)
            || protocol == "—"
            || protocol == "Unknown")
        {
            return "პროტოკოლი ჯერ არ არის განსაზღვრული.";
        }

        if (protocol.Contains(
                "ISO15765",
                StringComparison.OrdinalIgnoreCase)
            || protocol.Contains(
                "ISO 15765",
                StringComparison.OrdinalIgnoreCase)
            || protocol.Contains(
                "CAN",
                StringComparison.OrdinalIgnoreCase))
        {
            return "CAN — მანქანის ECU-ებთან მონაცემების გაცვლის თანამედროვე და ყველაზე გავრცელებული გზა.";
        }

        if (protocol.Contains(
                "ISO 9141",
                StringComparison.OrdinalIgnoreCase))
        {
            return "ISO 9141 — ძველი OBD კავშირის სტანდარტი, რომელიც ზოგ მანქანაზე გამოიყენება.";
        }

        if (protocol.Contains(
                "KWP",
                StringComparison.OrdinalIgnoreCase))
        {
            return "KWP — OBD კავშირის ძველი სტანდარტი, რომელიც ძირითადად ძველ ავტომობილებში გვხვდება.";
        }

        if (protocol.Contains(
                "J1850",
                StringComparison.OrdinalIgnoreCase))
        {
            return "J1850 — ძველი OBD სტანდარტი, რომელიც ძირითადად ამერიკულ ავტომობილებში გვხვდება.";
        }

        return "ეს არის მანქანის ECU-სთან კავშირის ტექნიკური პროტოკოლი.";
    }


    // ============================================================
    // VEHICLE INFORMATION
    // ============================================================

    public async Task RefreshConnectionSummaryAsync(bool forceVinRefresh = false)
    {
        RefreshProState();
        var connected = _bluetooth.IsConnected;
        UpdateStatus(connected);

        if (!connected)
            return;

        ApplyBrandProfile(_brand.Current);

        // Always paint the latest shared state immediately. This keeps Home
        // stable when navigating away and back, even before any refresh runs.
        if (_vehicle.CurrentVehicle is not null)
            ApplyVehicleInfo(_vehicle.CurrentVehicle);

        // Join the shared identity read (or start one if a field is still
        // missing). Already confirmed values are kept and not re-queried on
        // every page visit. No page-level timeout: partial results arrive via
        // VehicleInfoUpdated while the read continues.
        var identityRead = _vehicle.StartConnectedIdentityReadAsync(forceVinRefresh: false);
        if (_vehicle.CurrentVehicle is null && !identityRead.IsCompleted)
            ShowIdentityLoading();

        await ObserveIdentityReadAsync(identityRead);
    }

    private async Task ObserveIdentityReadAsync(Task<VehicleInfo> identityRead)
    {
        try
        {
            var latest = await identityRead;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_bluetooth.IsConnected)
                    ApplyVehicleInfo(_vehicle.CurrentVehicle ?? latest);
            });
        }
        catch
        {
            // Identity read is supplementary. Never clear already confirmed
            // vehicle data just because it failed; show final state instead.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_bluetooth.IsConnected)
                    ApplyVehicleInfo(_vehicle.CurrentVehicle ?? new VehicleInfo());
            });
        }
    }

    /// <summary>
    /// "განახლება" on the vehicle card: forgets the current identity and
    /// reads VIN / vehicle / trim / fuel from the ECU again.
    /// </summary>
    [RelayCommand]
    private async Task RefreshVehicleIdentity()
    {
        try
        {
        if (!_bluetooth.IsConnected)
        {
            await RequireObdConnectionAsync();
            return;
        }

        _vehicle.ClearConnectedVehicleState();
        ShowIdentityLoading();
        var identityRead = _vehicle.StartConnectedIdentityReadAsync(forceVinRefresh: true);
        await ObserveIdentityReadAsync(identityRead);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HomeViewModel.RefreshVehicleIdentity");
        }
    }

    /// <summary>The vehicle card while no adapter is connected: nothing is claimed about a vehicle.</summary>
    private void ShowDisconnectedIdentity()
    {
        var lines = VehicleIdentityText.Disconnected(_localization.IsEnglish);
        ConnectedBrandImage = string.Empty;
        VinNumber = lines.Vin;
        VehicleName = lines.Name;
        VehicleTrim = lines.Trim;
        FuelType = lines.Fuel;
    }

    private void ShowIdentityLoading()
    {
        var loading = _localization.IsEnglish ? "Reading…" : "იკითხება…";
        VinNumber = loading;
        VehicleName = loading;
        VehicleTrim = loading;
        FuelType = loading;
    }

    private void UpdateHomeLanguageTexts()
    {
        if (_localization.IsEnglish)
        {
            VehicleLabel = "Vehicle";
            VinLabel = "VIN";
            TrimLabel = "Trim";
            FuelLabel = "Fuel";
            FreezeFrameButtonText = "Freeze Frame · Fault snapshot";
        }
        else
        {
            VehicleLabel = "ტრანსპორტი";
            VinLabel = "VIN";
            TrimLabel = "კომპლექტაცია";
            FuelLabel = "საწვავი";
            FreezeFrameButtonText = "Freeze Frame · ხარვეზის მომენტი";
        }
    }


    private void ApplyVehicleInfo(VehicleInfo vehicle)
    {
        var resolved = _vehicleProfile.Resolve(vehicle);
        var reading = _bluetooth.IsConnected && _vehicle.IsReadingIdentity;
        var loadingText = _localization.IsEnglish ? "Reading…" : "იკითხება…";

        if (_bluetooth.IsConnected &&
        !string.IsNullOrWhiteSpace(vehicle.Manufacturer) &&
        !vehicle.Manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            ConnectedBrandImage = GetConnectedBrandImage(vehicle.Manufacturer);
        }

        // VIN
        VinNumber =
            string.IsNullOrWhiteSpace(vehicle.VIN) ||
            vehicle.VIN.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                ? (reading
                    ? loadingText
                    : _localization.IsEnglish
                        ? "VIN unavailable"
                        : "VIN მიუწვდომელია")
                : vehicle.VIN;

        // Chevrolet Volt 2013
        VehicleName = string.Join(
            " ",
            new[]
            {
            vehicle.Manufacturer,
            vehicle.Model,
            vehicle.Year
            }
            .Where(value =>
                !string.IsNullOrWhiteSpace(value) &&
                !value.Equals(
                    "Unknown",
                    StringComparison.OrdinalIgnoreCase)));

        // Nothing obtained in this session: "Reading…" while the read runs, otherwise "unknown". The brand the person picked
        // earlier (resolved.DisplayName) is a preference and is deliberately not shown as the vehicle.
        if (string.IsNullOrWhiteSpace(VehicleName))
            VehicleName = reading ? loadingText : VehicleIdentityText.Name(null, _localization.IsEnglish);

        // კომპლექტაცია
        VehicleTrim = reading && IsMissing(vehicle.Trim)
            ? loadingText
            : GetVehicleTrim(vehicle);

        // ძრავი
        Engine =
            string.IsNullOrWhiteSpace(vehicle.Engine) ||
            vehicle.Engine.Equals(
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
                ? "Unknown"
                : vehicle.Engine;

        // საწვავი
        FuelType = reading && IsMissing(vehicle.FuelType)
            ? loadingText
            : GetFuelDisplayName(vehicle.FuelType);

        EcuName = vehicle.ECUName;
        Protocol = vehicle.Protocol;
        ProtocolExplanation = ExplainProtocol(Protocol);
        BrandVerificationText = resolved.VerificationMessage;
    }

    private static bool IsMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private string GetVehicleTrim(VehicleInfo vehicle)
    {
        string[] possibleProperties =
        {
        "Trim",
        "TrimLevel",
        "Edition",
        "Grade"
    };

        foreach (var propertyName in possibleProperties)
        {
            var property =
                vehicle.GetType().GetProperty(propertyName);

            if (property is null)
                continue;

            var value =
                property.GetValue(vehicle)?.ToString();

            if (!string.IsNullOrWhiteSpace(value) &&
                !value.Equals(
                    "Unknown",
                    StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return _localization.IsEnglish
            ? "Trim unavailable"
            : "კომპლექტაცია მიუწვდომელია";
    }

    private string GetFuelDisplayName(string? fuel)
    {
        if (string.IsNullOrWhiteSpace(fuel) ||
            fuel.Equals(
                "Unknown",
                StringComparison.OrdinalIgnoreCase))
        {
            return _localization.IsEnglish
                ? "Fuel unavailable"
                : "საწვავი მიუწვდომელია";
        }

        if (_localization.IsEnglish)
            return fuel;

        var normalized = fuel.Trim().ToLowerInvariant();
        if (normalized.Contains("flexible fuel") || normalized.Contains("ffv"))
            return "Flex Fuel";
        if (normalized.Contains("plug-in") || normalized.Contains("phev"))
            return "Plug-in ჰიბრიდი";
        if (normalized.Contains("electric") && normalized.Contains("gasoline"))
            return "ბენზინი / ელექტრო";
        if (normalized.Contains("compressed natural gas"))
            return "CNG";
        if (normalized.Contains("liquefied petroleum gas"))
            return "LPG";

        return normalized switch
        {
            "gasoline" => "ბენზინი",
            "petrol" => "ბენზინი",
            "diesel" => "დიზელი",
            "electric" => "ელექტრო",
            "ev" => "ელექტრო",
            "hybrid" => "ჰიბრიდი",
            "plug-in hybrid" => "Plug-in ჰიბრიდი",
            "phev" => "Plug-in ჰიბრიდი",
            "lpg" => "LPG",
            "cng" => "CNG",
            _ => fuel
        };
    }
}
