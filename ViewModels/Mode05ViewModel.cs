using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>Shows only raw, ECU-returned Mode 05 monitor data.</summary>
public partial class Mode05ViewModel : BaseViewModel
{
    private readonly OBDService _obd;

    public ObservableCollection<Mode05MonitorResult> Tests { get; } = new();

    [ObservableProperty] private string scanState = "ჯერ არ შემოწმებულა";
    [ObservableProperty] private string scanDetail = "Mode 05 არის optional O₂ monitor service; ბევრ თანამედროვე ECU-ს ის არ აქვს.";
    [ObservableProperty] private string scanColor = "#94A3B8";
    [ObservableProperty] private int scanPercent;
    [ObservableProperty] private string rawStatus = "Raw პასუხი ჯერ არ არის.";
    public double ScanProgress => ScanPercent / 100d;

    public Mode05ViewModel(OBDService obd)
    {
        _obd = obd;
        Title = "Mode 05 O₂ Tests";
    }

    partial void OnScanPercentChanged(int value) => OnPropertyChanged(nameof(ScanProgress));

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            Tests.Clear();
            ScanColor = "#60A5FA";
            ScanPercent = 15;
            ScanState = "MODE 05 შემოწმება…";
            ScanDetail = "ECU-ს ეგზავნება ერთი მოკლე, read-only Mode 05 მოთხოვნა.";

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            ScanPercent = 55;
            var raw = await _obd.ReadMode05QuickAsync();
            if (ObdResponseParser.IsNegative(raw) || raw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase))
            {
                ScanPercent = 100;
                ScanColor = "#F59E0B";
                ScanState = "MODE 05 მიუწვდომელია";
                ScanDetail = "ამ ECU-მ Mode 05 O₂ test მონაცემი არ დააბრუნა. ეს გაუმართაობას არ ნიშნავს — თანამედროვე მანქანებში ხშირად Mode 06 გამოიყენება.";
                RawStatus = "ECU პასუხი: No Data / negative response.";
                return;
            }

            ScanPercent = 80;
            var results = AdvancedModeParser.ParseMode05Detailed(raw);
            foreach (var result in results)
                Tests.Add(result);

            ScanPercent = 100;
            if (results.Count == 0)
            {
                ScanColor = "#F59E0B";
                ScanState = "MODE 05 მონაცემი ვერ გაიშიფრა";
                ScanDetail = "ECU-მ პასუხი დააბრუნა, თუმცა ამ ფორმატისთვის უსაფრთხოდ დეკოდირებადი Mode 05 item ვერ მოიძებნა.";
                RawStatus = "Raw პასუხი მივიღეთ, მაგრამ დადასტურებული სტანდარტული item ვერ მოიძებნა.";
                return;
            }

            var flagged = results.Count(item => item.IsFlagged);
            ScanColor = flagged == 0 ? "#22C55E" : "#EF4444";
            ScanState = flagged == 0
                ? "MODE 05 — არცერთი წაკითხული item არ არის flagged"
                : $"MODE 05 — {flagged} item flagged";
            ScanDetail = "ეს არის ECU-ის raw Mode 05 სტატუსი. NOT FLAGGED არ ნიშნავს ყველა O₂ სისტემის აბსოლუტურ დიაგნოზს.";
            RawStatus = $"ECU-მ {results.Count} რეალური Mode 05 item დააბრუნა.";
        }
        catch (Exception ex)
        {
            ScanPercent = 100;
            ScanColor = "#EF4444";
            ScanState = "MODE 05 ვერ წაიკითხა";
            ScanDetail = "კავშირი ან ECU პასუხი ვერ დადასტურდა; მონაცემი არ არის გამოგონილი.";
            RawStatus = string.IsNullOrWhiteSpace(ex.Message) ? "კავშირის შეცდომა." : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
