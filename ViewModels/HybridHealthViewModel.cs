using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class HybridHealthViewModel : BaseViewModel
{
    private readonly HybridHealthService _health;
    private readonly VehicleInfoService _vehicles;
    private CancellationTokenSource? _scanCancellation;

    [ObservableProperty] private string vehicleName = "ავტომობილი ჯერ არ არის ამოცნობილი";
    [ObservableProperty] private string scoreText = "—/100";
    [ObservableProperty] private string ratingText = "ჯერ არ შემოწმებულა";
    [ObservableProperty] private string scoreColor = "#94A3B8";
    [ObservableProperty] private string cellDeltaText = "—";
    [ObservableProperty] private string cellRangeText = "რეალური მონაცემი ჯერ არ არის";
    [ObservableProperty] private string temperatureText = "—";
    [ObservableProperty] private string temperatureRangeText = "რეალური მონაცემი ჯერ არ არის";
    [ObservableProperty] private string remainingLifeText = "—";
    [ObservableProperty] private string remainingLifeSource = "რეალური მონაცემი ჯერ არ არის";
    [ObservableProperty] private string scanStage = "დააკავშირე OBD და დაიწყე შემოწმება";
    [ObservableProperty] private string validationStatus = "Prius → Volt → Ioniq · თითოეულ პროფილს ნამდვილ მანქანაზე ვალიდაცია სჭირდება.";
    [ObservableProperty] private string evidenceText = string.Empty;
    [ObservableProperty] private bool hasResult;

    public HybridHealthViewModel(HybridHealthService health, VehicleInfoService vehicles)
    {
        _health = health;
        _vehicles = vehicles;
        Title = "ელექტრო / ჰიბრიდის ჯანმრთელობა";
    }

    [RelayCommand]
    private async Task Scan()
    {
        if (IsBusy) return;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            HasResult = false;
            ScanStage = "ავტომობილის იდენტიფიკაცია…";
            var vehicle = _vehicles.CurrentVehicle;
            if (vehicle is null)
            {
                try { vehicle = await _vehicles.IdentifyConnectedVehicleAsync(_scanCancellation.Token); }
                catch { }
            }

            if (vehicle is null)
            {
                ScanStage = "ავტომობილის მოდელი ვერ დადასტურდა. ჯერ შეასრულე დაკავშირება/სკანირება.";
                return;
            }

            var progress = new Progress<string>(message => ScanStage = message);
            var result = await _health.ScanAsync(vehicle, progress, _scanCancellation.Token);
            Apply(result);
        }
        catch (OperationCanceledException)
        {
            ScanStage = "შემოწმება შეჩერდა.";
        }
        catch (Exception ex)
        {
            ScanStage = $"შემოწმება ვერ დასრულდა: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    private void Apply(HybridHealthResult result)
    {
        VehicleName = string.IsNullOrWhiteSpace(result.VehicleName) ? "ამოუცნობი ავტომობილი" : result.VehicleName;
        ScoreText = result.Score.HasValue ? $"{result.Score}/100" : "—/100";
        RatingText = result.Rating;
        ScoreColor = result.Score switch
        {
            >= 80 => "#22C55E",
            >= 60 => "#F59E0B",
            not null => "#EF4444",
            _ => "#94A3B8"
        };
        CellDeltaText = result.CellVoltageDelta.HasValue ? $"{result.CellVoltageDelta.Value:0.000} V" : "—";
        CellRangeText = result.MinimumCellVoltage.HasValue && result.MaximumCellVoltage.HasValue
            ? $"{result.MinimumCellVoltage.Value:0.000}–{result.MaximumCellVoltage.Value:0.000} V · {result.CellReadingCount} მნიშვნელობა"
            : "უჯრედების/მოდულების ძაბვა ECU-მ არ დააბრუნა";
        TemperatureText = result.MaximumTemperatureC.HasValue ? $"{result.MaximumTemperatureC.Value:0.#} °C" : "—";
        TemperatureRangeText = result.MinimumTemperatureC.HasValue && result.MaximumTemperatureC.HasValue
            ? $"{result.MinimumTemperatureC.Value:0.#}–{result.MaximumTemperatureC.Value:0.#} °C · {result.TemperatureReadingCount} სენსორი"
            : "HV ბატარეის ტემპერატურა ECU-მ არ დააბრუნა";
        RemainingLifeText = result.EstimatedRemainingLifePercent.HasValue
            ? $"≈ {result.EstimatedRemainingLifePercent.Value:0}%"
            : "—";
        RemainingLifeSource = result.EstimatedRemainingLifePercent.HasValue
            ? result.RemainingLifeFromEcu
                ? "ECU-reported Hybrid Battery Remaining Life (PID 015B)"
                : "სავარაუდო ინდიკატორი მიღებული ძაბვის/ტემპერატურის მიხედვით — capacity ტესტი არ არის"
            : "დარჩენილი სიცოცხლისთვის საკმარისი რეალური მონაცემი არ დაბრუნდა";
        ScanStage = result.Status;
        ValidationStatus = result.ValidationStatus;
        EvidenceText = result.Evidence;
        HasResult = true;
    }
}
