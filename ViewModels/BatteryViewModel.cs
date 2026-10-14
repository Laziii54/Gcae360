using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CarScanner.ViewModels;

public partial class BatteryViewModel : BaseViewModel
{
    private const int HistoryLimit = 30;

    private readonly OBDService _obd;
    private readonly List<double> _samples = new();

    public ObservableCollection<string> VoltageHistory { get; } = new();

    [ObservableProperty] private string voltage = "— V";
    [ObservableProperty] private string minimum = "— V";
    [ObservableProperty] private string maximum = "— V";
    [ObservableProperty] private string chargingStatus = "12V სისტემა ჯერ არ შემოწმებულა";
    [ObservableProperty] private string measurementState = "NOT CHECKED";
    [ObservableProperty] private string measurementStateColor = "#94A3B8";
    [ObservableProperty] private string lastUpdated = "ბოლო განახლება: ჯერ არ არის";
    [ObservableProperty] private int selectedTypeIndex;
    [ObservableProperty] private string batteryTypeHint = "";

    private const string TypePreference = "battery12v_type_v1";
    [ObservableProperty] private bool isPullRefreshing;

    /// <summary>Pull-to-refresh: the same read as the Refresh button, never a second one while one is running.</summary>
    [RelayCommand]
    private Task PullRefresh() => PullRefreshRunner.RunAsync(
        Refresh,
        () => IsBusy,
        value => IsPullRefreshing = value,
        CarScanner.Helpers.RefreshHaptics.Completed);

    private double? _lastValue;
    private double? _lastRpm;

    public string BatteryTypeTitle => L("12 V აკუმულატორის ტიპი", "12 V battery type");
    public string BatteryTypeNote => L("მანქანიდან არ იკითხება — აირჩიე შენი აკუმულატორის ტიპი.", "Not read from the car — choose your 12 V battery type.");

    public IReadOnlyList<string> BatteryTypeOptions { get; } = new[]
    {
        L("ჩვეულებრივი (ტყვია-მჟავა)", "Standard (lead-acid)"),
        L("AGM / EFB (start-stop)", "AGM / EFB (start-stop)"),
        L("ჰიბრიდი / EV — 12 V დამხმარე", "Hybrid / EV — 12 V auxiliary"),
        L("ლითიუმი 12 V", "Lithium 12 V")
    };

    public BatteryViewModel(OBDService obd)
    {
        _obd = obd;
        Title = "Battery / Charging";
        int saved;
        try { saved = Preferences.Default.Get(TypePreference, 0); }
        catch { saved = 0; }
        selectedTypeIndex = Enum.IsDefined(typeof(Battery12VType), saved) ? saved : 0;
        BatteryTypeHint = BatteryVoltageGuide.Hint((Battery12VType)selectedTypeIndex);
    }

    /// <summary>The verdict depends on the battery type, so a change re-judges the last reading without a new request.</summary>
    partial void OnSelectedTypeIndexChanged(int value)
    {
        if (!Enum.IsDefined(typeof(Battery12VType), value))
            return;
        var type = (Battery12VType)value;
        try { Preferences.Default.Set(TypePreference, value); } catch { /* a missing preference store must not break the page */ }
        BatteryTypeHint = BatteryVoltageGuide.Hint(type);
        if (_lastValue is { } last)
        {
            var verdict = BatteryVoltageGuide.Describe(last, _lastRpm, type);
            ChargingStatus = verdict.Text;
            MeasurementStateColor = ColorOf(verdict.Level);
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            MeasurementState = "LOADING";
            MeasurementStateColor = "#60A5FA";
            ChargingStatus = _samples.Count == 0
                ? "ECU-დან 12V Control Module Voltage (PID 0142) იკითხება…"
                : "ახალი 12V გაზომვა იკითხება; ეკრანზე ბოლო წარმატებული მონაცემია.";

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            // PID 0142 is optional. Use the bounded live reader so an ECU
            // that advertises it incorrectly cannot keep this page loading.
            var sensor = (await _obd.ReadLiveValuesAsync(new[] { 0x42 })).FirstOrDefault();
            if (sensor is null || !double.TryParse(
                    sensor.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                SetUnavailableState("ECU-მ Control Module Voltage (PID 0142) მნიშვნელობა არ დააბრუნა.");
                return;
            }

            // Engine state decides which range is normal: resting 12.4–12.7 V
            // means nothing when the alternator should be charging.
            var rpm = await TryReadRpmAsync();
            ApplySuccessfulMeasurement(value, rpm, DateTime.Now);
        }
        catch (Exception ex)
        {
            SetErrorState(ex);
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "BatteryViewModel.Refresh");
        }
    }

    private async Task<double?> TryReadRpmAsync()
    {
        try
        {
            var rpm = (await _obd.ReadLiveValuesAsync(new[] { 0x0C })).FirstOrDefault();
            return rpm is not null && double.TryParse(rpm.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery RPM read failed: {ex.Message}");
            return null;
        }
    }

    private void ApplySuccessfulMeasurement(double value, double? rpm, DateTime timestamp)
    {
        _samples.Add(value);
        _lastValue = value;
        _lastRpm = rpm;
        if (_samples.Count > HistoryLimit)
            _samples.RemoveAt(0);

        Voltage = $"{value:F2} V";
        Minimum = $"{_samples.Min():F2} V";
        Maximum = $"{_samples.Max():F2} V";
        var verdict = BatteryVoltageGuide.Describe(value, rpm, (Battery12VType)SelectedTypeIndex);
        var color = ColorOf(verdict.Level);
        ChargingStatus = verdict.Text;
        LastUpdated = $"ბოლო განახლება: {timestamp:HH:mm:ss}";
        MeasurementState = "AVAILABLE";
        MeasurementStateColor = color;

        VoltageHistory.Insert(0, $"{timestamp:HH:mm:ss}  ·  {value:F2} V");
        if (VoltageHistory.Count > HistoryLimit)
            VoltageHistory.RemoveAt(VoltageHistory.Count - 1);
    }

    private void SetUnavailableState(string reason)
    {
        MeasurementState = _samples.Count == 0 ? "NOT AVAILABLE" : "LAST VALUE";
        MeasurementStateColor = "#F59E0B";
        ChargingStatus = _samples.Count == 0
            ? $"12V სისტემა ვერ შემოწმდა — {reason}"
            : $"ახალი გაზომვა მიუწვდომელია — {reason} ბოლო წარმატებული გაზომვა ზემოთ რჩება.";
    }

    private void SetErrorState(Exception exception)
    {
        MeasurementState = _samples.Count == 0 ? "ERROR" : "LAST VALUE";
        MeasurementStateColor = "#F87171";
        ChargingStatus = _samples.Count == 0
            ? "12V სისტემის გაზომვა ვერ შესრულდა. შეამოწმე OBD კავშირი."
            : "ახალი 12V გაზომვა ვერ შესრულდა; ბოლო წარმატებული მონაცემი ზემოთ რჩება.";
        LastUpdated = _samples.Count == 0 ? "ბოლო განახლება: შეცდომა" : LastUpdated;
        System.Diagnostics.Debug.WriteLine($"Battery voltage refresh failed: {exception.Message}");
    }

    private static string L(string ka, string en) => CarScanner.Services.OBD.ObdLang.IsEnglish ? en : ka;

    private static string ColorOf(VoltageLevel level) => level switch
    {
        VoltageLevel.Good => "#4ADE80",
        VoltageLevel.Attention => "#F59E0B",
        VoltageLevel.Problem => "#F87171",
        _ => "#94A3B8"
    };
}
