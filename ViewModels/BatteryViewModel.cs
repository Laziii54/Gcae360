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
    [ObservableProperty] private string highVoltageStatus = "HV Battery/BECM მონაცემები ჯერ არ შემოწმებულა.";
    [ObservableProperty] private string highVoltageState = "NOT CHECKED";
    [ObservableProperty] private string highVoltageStateColor = "#94A3B8";

    public BatteryViewModel(OBDService obd)
    {
        _obd = obd;
        Title = "Battery / Charging";
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

            await RefreshHighVoltageStatusAsync();

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

            ApplySuccessfulMeasurement(value, DateTime.Now);
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

    private void ApplySuccessfulMeasurement(double value, DateTime timestamp)
    {
        _samples.Add(value);
        if (_samples.Count > HistoryLimit)
            _samples.RemoveAt(0);

        Voltage = $"{value:F2} V";
        Minimum = $"{_samples.Min():F2} V";
        Maximum = $"{_samples.Max():F2} V";
        ChargingStatus = DescribeVoltage(value);
        LastUpdated = $"ბოლო განახლება: {timestamp:HH:mm:ss}";
        MeasurementState = "AVAILABLE";
        MeasurementStateColor = "#4ADE80";

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

    private async Task RefreshHighVoltageStatusAsync()
    {
        if (!_obd.IsPidSupported(0x5B))
        {
            HighVoltageState = "NOT AVAILABLE";
            HighVoltageStateColor = "#94A3B8";
            HighVoltageStatus = "Generic OBD-II-მა Hybrid Battery Remaining Life (PID 015B) არ დაადასტურა. Pack voltage, current, SOC და cell data-სთვის საჭიროა დადასტურებული OEM/BMS პროფილი.";
            return;
        }

        try
        {
            var reading = (await _obd.ReadLiveValuesAsync(new[] { 0x5B })).FirstOrDefault();
            if (reading is null || !double.TryParse(reading.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                HighVoltageState = "NOT AVAILABLE";
                HighVoltageStateColor = "#F59E0B";
                HighVoltageStatus = "ECU-მ PID 015B მხარდაჭერად გამოაცხადა, მაგრამ დეკოდირებადი მნიშვნელობა არ დააბრუნა. სხვა HV მონაცემი არ არის ნაჩვენები.";
                return;
            }

            HighVoltageState = "AVAILABLE";
            HighVoltageStateColor = "#4ADE80";
            HighVoltageStatus = $"ECU-reported Hybrid Battery Remaining Life: {value:0.##}%. ეს არ არის ავტომატურად Battery SOC, pack voltage ან cell მდგომარეობა.";
        }
        catch (Exception ex)
        {
            HighVoltageState = "ERROR";
            HighVoltageStateColor = "#F87171";
            HighVoltageStatus = "Hybrid Battery PID ვერ განახლდა; მონაცემი არ არის გამოგონილი.";
            System.Diagnostics.Debug.WriteLine($"Hybrid battery refresh failed: {ex.Message}");
        }
    }

    private static string DescribeVoltage(double value) => value switch
    {
        >= 13.3 and <= 15.1 => "Charging: ნორმალური დამუხტვის ძაბვა",
        >= 12.4 and < 13.3 => "12V: ნორმალური მოსვენების ძაბვა",
        < 12.0 => "12V: დაბალი ძაბვა — შეამოწმე აკუმულატორი და დამუხტვა",
        > 15.1 => "Charging: მაღალი ძაბვა — შეამოწმე დამუხტვის სისტემა",
        _ => "12V: შუალედური ძაბვა — გაზომვა მიღებულია"
    };
}
