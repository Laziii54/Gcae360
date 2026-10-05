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
    [ObservableProperty] private string accelerationButtonText = "ARM 0–100 TEST";
    [ObservableProperty] private string accelerationScaleCenter = "50";
    [ObservableProperty] private string accelerationScaleMaximum = "100";

    [ObservableProperty]
    private string accelerationSpeedSource = "Vehicle Speed PID 01 0D-ის რეალურ პასუხს ველოდები…";

    [ObservableProperty]
    private string accelerationObdStatus = "● Disconnected";

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
        ServiceDataReaderService serviceDataReader)
    {
        _bluetooth = bluetooth;
        _obd = obd;
        _vehicle = vehicle;
        _loading = loading;
        _pro = pro;
        _vehicleContext = vehicleContext;
        _brand = brand;
        _vehicleProfile = vehicleProfile;
        _serviceDataReader = serviceDataReader;
        _dtcCatalog = dtcCatalog;
        _acceleration = acceleration;
        _localization = localization;
        _units = units;

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

                LanguageToggleFlag =
                    _localization.IsEnglish ? "KA" : "EN";

                UpdateHomeLanguageTexts();

                if (_vehicle.CurrentVehicle is not null)
                    ApplyVehicleInfo(_vehicle.CurrentVehicle);
            });

        _units.UnitSystemChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(UpdateAccelerationState);

        LanguageToggleFlag =
            _localization.IsEnglish ? "KA" : "EN";

        UpdateHomeLanguageTexts();
        UpdateAccelerationState();

        Title = "Georgian Car Scanner";

        _bluetooth.ConnectionChanged += OnConnectionChanged;
        _vehicleContext.Changed += OnVehicleContextChanged;
        _brand.Changed += OnBrandChanged;
        _vehicle.VehicleInfoUpdated += OnVehicleInfoUpdated;

        // Start/stop service preview based on connection state
        if (_bluetooth.IsConnected)
            _ = StartServicePreviewPolling();

        // მხოლოდ ავტომობილი
        ApplyVehicleContext(VehicleKind.Car);
        ApplyBrandProfile(_brand.Current);

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
            AccelerationResult = $"{latest.Source.Replace("OBD · ", string.Empty)}: {latest.DurationSeconds:F2} წმ";
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
        AccelerationButtonText = $"ARM {snapshot.CurrentTest.DisplayName.ToUpperInvariant()} TEST";
        var test = snapshot.CurrentTest;
        AccelerationScaleCenter = _units.Convert(test.DisplayTargetSpeedKph / 2d, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        AccelerationScaleMaximum = _units.Convert(test.DisplayTargetSpeedKph, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        AccelerationSpeedSource = _acceleration.GpsMode
            ? L("ტელეფონის GPS · მიახლოებითი, რეიტინგში არ შედის", "Phone GPS · approximate, not ranked")
            : snapshot.HasCurrentSpeed
                ? L($"რეალური ECU პასუხი · PID 01 0D · {snapshot.LastSpeedAt?.ToLocalTime():HH:mm:ss}", $"Real ECU answer · PID 01 0D · {snapshot.LastSpeedAt?.ToLocalTime():HH:mm:ss}")
                : L("PID 01 0D-ის პასუხს ველოდები…", "Waiting for PID 01 0D…");
        AccelerationObdStatus = _bluetooth.IsConnected ? "● Connected" : "● Disconnected";
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
            AccelerationObdStatus = "● Disconnected";
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
            AccelerationStatus = string.IsNullOrWhiteSpace(ex.Message)
                ? L("სიჩქარის წაკითხვა ვერ დაიწყო. შეამოწმე OBD კავშირი, ან გაზომე GPS-ით.", "Could not start reading the speed. Check the OBD connection, or measure with GPS.")
                : ex.Message;
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
        if (IsBusy || IsAccelerationMeasuring)
            return;

        // No adapter: offer timing from the phone GPS (works on any car,
        // also on cars without OBD speed, but it is an estimate and not ranked).
        if (!_bluetooth.IsConnected)
        {
            var useGps = await Shell.Current.DisplayAlertAsync(
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
                AccelerationStatus = ex.Message;
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
            AccelerationStatus = ex.Message;
        }
        finally
        {
            IsBusy = false;
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
        => Shell.Current.GoToAsync(nameof(Views.AccelerationHistoryPage));

    [RelayCommand]
    private Task GoToAccelerationLeaderboard()
        => Shell.Current.GoToAsync(nameof(Views.AccelerationLeaderboardPage));

    [RelayCommand]
    private async Task GoToAcceleration()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(nameof(Views.AccelerationPage));
    }

    [RelayCommand]
    private void ToggleLanguage()
        => _localization.SetLanguage(_localization.IsEnglish ? "ქართული" : "English");


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
                _ = StartServicePreviewPolling();
            }
            else
            {
                StopServicePreviewPolling();
                _vehicle.ClearConnectedVehicleState();
                ConnectedBrandImage = string.Empty;

                VinNumber = _localization.IsEnglish
                     ? "VIN unknown"
                     : "VIN უცნობია";

                VehicleName = _localization.IsEnglish
                    ? "Vehicle unknown"
                    : "მანქანა უცნობია";

                VehicleTrim = _localization.IsEnglish
                    ? "Trim unavailable"
                    : "კომპლექტაცია მიუწვდომელია";

                FuelType = _localization.IsEnglish
                    ? "Fuel unavailable"
                    : "საწვავი მიუწვდომელია";
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
            if (_bluetooth.IsConnected)
                _ = RefreshServicePreviewOnceAsync();
        });
    }

    private async Task RefreshServicePreviewOnceAsync()
    {
        try
        {
            var vehicle = _vehicle.CurrentVehicle;
            if (vehicle is null)
                return;

            var result = await _serviceDataReader.ReadAsync(vehicle, CancellationToken.None);
            MainThread.BeginInvokeOnMainThread(() =>
            {
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
            // ignore preview failures
        }
    }

    private void StopServicePreviewPolling()
    {
        try
        {
            _servicePreviewCts?.Cancel();
            _servicePreviewCts?.Dispose();
            _servicePreviewCts = null;
            ShowServicePreview = false;
        }
        catch { }
    }

    private async Task StartServicePreviewPolling()
    {
        StopServicePreviewPolling();
        _servicePreviewCts = new CancellationTokenSource();
        var token = _servicePreviewCts.Token;

        // Poll every 6 seconds while connected
        while (!token.IsCancellationRequested && _bluetooth.IsConnected)
        {
            await RefreshServicePreviewOnceAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(6), token); } catch { break; }
        }
    }

    private static string GetServiceMetricIcon(ServiceMetricKind kind) => kind switch
    {
        ServiceMetricKind.OilLifePercent => "OIL",
        ServiceMetricKind.OilLevel => "LVL",
        ServiceMetricKind.DistanceToNextService => "KM",
        ServiceMetricKind.DaysToNextService => "DAY",
        ServiceMetricKind.DistanceSinceOilChange => "KM",
        ServiceMetricKind.OilChangeInterval => "INT",
        ServiceMetricKind.InspectionDistance => "INSP",
        ServiceMetricKind.InspectionDays => "DAY",
        ServiceMetricKind.MaintenanceCounter => "CNT",
        ServiceMetricKind.ServiceStatus => "SRV",
        _ => "DATA"
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

        if (VehicleName is "მანქანა უცნობია" or "მანქანის ინფორმაცია მიუწვდომელია")
            VehicleName = profile.DisplayName;

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
        await Shell.Current.GoToAsync(
            nameof(Views.VehicleBrandSelectionPage));
    }


    [RelayCommand]
    private async Task GoToBluetooth()
    {
        await Shell.Current.GoToAsync(
            nameof(Views.VehicleBrandSelectionPage));
    }

    [RelayCommand]
    private Task BeginConnection()
        => Shell.Current.GoToAsync(nameof(Views.VehicleBrandSelectionPage));




    // ============================================================
    // მანქანის დიაგნოსტიკა
    // ============================================================

    [RelayCommand]
    private async Task StartScan()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.DiagnosticsPage));
    }


    [RelayCommand]
    private async Task GoToTroubleCodes()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.DiagnosticsPage));
    }


    [RelayCommand]
    private async Task GoToLiveData()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.LiveDataPage));
    }


    [RelayCommand]
    private async Task GoToSensors()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.SensorsPage));
    }


    [RelayCommand]
    private async Task GoToHistory()
    {
        await Shell.Current.GoToAsync(
            nameof(Views.HistoryPage));
    }


    [RelayCommand]
    private async Task GoToSettings()
    {
        await Shell.Current.GoToAsync(
            nameof(Views.SettingsPage));
    }


    [RelayCommand]
    private async Task GoToEmissions()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.EmissionsPage));
    }

    [RelayCommand]
    private async Task GoToTechnicalInspection()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.TechnicalInspectionPage));
    }

    [RelayCommand]
    private async Task GoToVehicleHealth()
    {
        if (!await RequireObdConnectionAsync()) return;
        await Shell.Current.GoToAsync(nameof(Views.VehicleHealthPage));
    }

    [RelayCommand]
    private async Task GoToBattery()
    {
        if (!await RequireObdConnectionAsync()) return;
        await Shell.Current.GoToAsync(nameof(Views.BatteryPage));
    }

    [RelayCommand]
    private async Task GoToHybridHealth()
    {
        if (!await RequireObdConnectionAsync())
            return;
        await Shell.Current.GoToAsync(nameof(Views.HybridHealthPage));
    }
    [RelayCommand] private async Task GoToAdapterCenter() { if (!await RequireObdConnectionAsync()) return; await Shell.Current.GoToAsync(nameof(Views.AdapterCenterPage)); }


    [RelayCommand]
    private async Task GoToEcu()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.EcuIdentifiersPage));
    }

    [RelayCommand]
    private async Task GoToCapabilities()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(nameof(Views.CapabilityCenterPage));
    }


    [RelayCommand]
    private async Task GoToMileage()
    {
        if (!await RequireObdConnectionAsync())
            return;

        if (!await RequireProAsync("გარბენის გადამოწმება"))
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.MileagePage));
    }

    // Freeze Frame is free (not PRO); it only needs an OBD connection.
    [RelayCommand]
    private async Task GoToFreezeFrame()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(nameof(Views.FreezeFramePage));
    }

    [RelayCommand]
    private async Task GoToDiagnosticReport()
    {
        if (!await RequireProAsync("დიაგნოსტიკური ანგარიში"))
            return;

        await Shell.Current.GoToAsync(nameof(Views.DiagnosticReportPage));
    }


    [RelayCommand]
    private async Task GoToAi()
    {
        if (!await RequireObdConnectionAsync())
            return;

        if (!await RequireProAsync("AI Diagnostic"))
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.AiPage));
    }


    [RelayCommand]
    private async Task GoToFuel()
    {
        if (!await RequireObdConnectionAsync())
            return;

        if (!await RequireProAsync("Fuel Consumption"))
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.FuelPage));
    }


    private async Task<bool> RequireProAsync(
        string feature)
    {
        if (await _pro.EnsureProAsync())
            return true;

        var openActivation = await Shell.Current.DisplayAlertAsync(
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
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.ServicePage));
    }

    [RelayCommand]
    private async Task GoToCalendar()
    {
        await Shell.Current.GoToAsync(nameof(Views.CalendarPage));
    }


    [RelayCommand]
    private async Task GoToModules()
    {
        if (!await RequireObdConnectionAsync())
            return;

        await Shell.Current.GoToAsync(
            nameof(Views.ModuleScanPage));
    }


    [RelayCommand]
    private async Task GoToPro()
    {
        await Shell.Current.GoToAsync(
            nameof(Views.ProPage));
    }

    private async Task<bool> RequireObdConnectionAsync()
    {
        if (_bluetooth.IsConnected)
            return true;

        await Shell.Current.GoToAsync(nameof(Views.VehicleBrandSelectionPage));
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
    /// "↻ განახლება" on the vehicle card: forgets the current identity and
    /// reads VIN / vehicle / trim / fuel from the ECU again.
    /// </summary>
    [RelayCommand]
    private async Task RefreshVehicleIdentity()
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

        if (string.IsNullOrWhiteSpace(VehicleName))
        {
            VehicleName = resolved.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(VehicleName) && reading)
            VehicleName = loadingText;

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
