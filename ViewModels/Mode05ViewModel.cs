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

    private static string L(string ka, string en) => ObdLang.L(ka, en);

    public ObservableCollection<Mode05MonitorResult> Tests { get; } = new();

    [ObservableProperty] private string scanState = L("ჯერ არ შემოწმებულა", "Not checked yet");
    [ObservableProperty] private string scanDetail = L(
        "Mode 05 ჟანგბადის (O₂) სენსორების არჩევითი ტესტია; ბევრ თანამედროვე ECU-ს ის არ აქვს.",
        "Mode 05 is an optional O₂ sensor test service; many modern ECUs don't support it.");
    [ObservableProperty] private string scanColor = "#94A3B8";
    [ObservableProperty] private int scanPercent;
    [ObservableProperty] private string rawStatus = L("ECU-ს პასუხი ჯერ არ არის.", "No ECU response yet.");
    public double ScanProgress => ScanPercent / 100d;

    // Page texts (both languages, so the page never mixes them)
    public string PageHeadingText => L("MODE 05 — O₂ ტესტები", "MODE 05 — O₂ TESTS");
    public string PageSubtitleText => L(
        "ჟანგბადის სენსორების ტესტები · სტანდარტული OBD-II",
        "Optional O₂ sensor test data · generic OBD-II");
    public string RefreshButtonText => L("განახლება", "Refresh");
    public string ResultsCaptionText => L("ECU-ს დაბრუნებული ტესტები", "Tests returned by the ECU");
    public string EmptyText => L(
        "Mode 05 შედეგები შემოწმების შემდეგ გამოჩნდება.",
        "Mode 05 results appear after the test.");
    public string FooterText => L(
        "Mode 05 მხოლოდ კითხულობს ძველი ტიპის O₂ ტესტებს. თანამედროვე ECU-ების უმეტესობა მას არ აბრუნებს — ასეთ შემთხვევაში გამოიყენე Mode 06 ან ცოცხალ მონაცემებში O₂/ლამბდა პარამეტრები.",
        "Mode 05 is a read-only legacy O₂ monitor service. Most modern ECUs do not return it; use Mode 06 or live O₂/Lambda PIDs instead.");

    public Mode05ViewModel(OBDService obd)
    {
        _obd = obd;
        Title = "Mode 05 O₂ Tests";
    }

    partial void OnScanPercentChanged(int value) => OnPropertyChanged(nameof(ScanProgress));

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            Tests.Clear();
            ScanColor = "#1D5BD6";
            ScanPercent = 15;
            ScanState = L("MODE 05 შემოწმება…", "Checking MODE 05…");
            ScanDetail = L(
                "ECU-ს ეგზავნება ერთი მოკლე მოთხოვნა (მხოლოდ კითხვა).",
                "Sending one short, read-only Mode 05 request to the ECU.");

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            ScanPercent = 55;
            var raw = await _obd.ReadMode05QuickAsync();
            if (ObdResponseParser.IsNegative(raw) || raw.Contains("NO DATA", StringComparison.OrdinalIgnoreCase))
            {
                ScanPercent = 100;
                ScanColor = "#C98A1A";
                ScanState = L("MODE 05 მიუწვდომელია", "MODE 05 unavailable");
                ScanDetail = L(
                    "ამ ECU-მ Mode 05 მონაცემი არ დააბრუნა. ეს გაუმართაობას არ ნიშნავს — თანამედროვე მანქანებში ხშირად Mode 06 გამოიყენება.",
                    "This ECU returned no Mode 05 O₂ test data. This is not a fault — modern cars often use Mode 06.");
                RawStatus = L("ECU-ს პასუხი: მონაცემი არ არის.", "ECU response: No Data / negative response.");
                return;
            }

            ScanPercent = 80;
            var results = AdvancedModeParser.ParseMode05Detailed(raw);
            foreach (var result in results)
                Tests.Add(result);

            ScanPercent = 100;
            if (results.Count == 0)
            {
                ScanColor = "#C98A1A";
                ScanState = L("MODE 05 მონაცემი ვერ გაიშიფრა", "MODE 05 data could not be decoded");
                ScanDetail = L(
                    "ECU-მ პასუხი დააბრუნა, მაგრამ მასში გასაშიფრი Mode 05 ტესტი ვერ მოიძებნა.",
                    "The ECU responded, but no safely decodable Mode 05 item was found for this format.");
                RawStatus = L(
                    "პასუხი მივიღეთ, მაგრამ სტანდარტული ტესტი ვერ მოიძებნა.",
                    "A response arrived, but no confirmed standard item was found.");
                return;
            }

            var flagged = results.Count(item => item.IsFlagged);
            ScanColor = flagged == 0 ? "#1F8A4C" : "#B3361F";
            ScanState = flagged == 0
                ? L("MODE 05 — ყველა ტესტი ნორმაშია", "MODE 05 — no item is flagged")
                : L($"MODE 05 — {flagged} ტესტი ნორმაში არ არის", $"MODE 05 — {flagged} item(s) flagged");
            ScanDetail = L(
                "ეს არის ECU-ის მიერ დაბრუნებული Mode 05 სტატუსი. „ნორმაშია“ არ ნიშნავს ყველა O₂ სისტემის სრულ დიაგნოზს.",
                "This is the ECU's raw Mode 05 status. NOT FLAGGED is not an absolute diagnosis of every O₂ system.");
            RawStatus = L(
                $"ECU-მ {results.Count} ტესტი დააბრუნა.",
                $"The ECU returned {results.Count} Mode 05 item(s).");
        }
        catch (Exception ex)
        {
            ScanPercent = 100;
            ScanColor = "#B3361F";
            ScanState = L("MODE 05 ვერ წაიკითხა", "MODE 05 could not be read");
            ScanDetail = L(
                "კავშირი ან ECU-ს პასუხი ვერ დადასტურდა; მონაცემი გამოგონილი არ არის.",
                "The connection or ECU response could not be confirmed; no data is invented.");
            RawStatus = string.IsNullOrWhiteSpace(ex.Message) ? L("კავშირის შეცდომა.", "Connection error.") : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "Mode05ViewModel.Refresh");
        }
    }
}
