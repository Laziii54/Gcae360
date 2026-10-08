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

    public ObservableCollection<O2TestRow> Tests { get; } = new();

    [ObservableProperty] private string scanState = L("ჯერ არ შემოწმებულა", "Not checked yet");
    [ObservableProperty] private string scanDetail = L(
        "ჟანგბადის (O₂) სენსორების ტესტები: რამდენად სწრაფად და სწორად მუშაობს თითო სენსორი.",
        "Oxygen (O₂) sensor tests: how fast and how well each sensor works.");
    [ObservableProperty] private string scanColor = "#94A3B8";
    [ObservableProperty] private int scanPercent;
    [ObservableProperty] private string rawStatus = L("ECU-ს პასუხი ჯერ არ არის.", "No ECU response yet.");
    [ObservableProperty] private string sourceNote = string.Empty;
    [ObservableProperty] private bool hasSourceNote;
    public double ScanProgress => ScanPercent / 100d;

    // Page texts (both languages, so the page never mixes them)
    public string PageHeadingText => L("MODE 05 — O₂ ტესტები", "MODE 05 — O₂ TESTS");
    public string PageSubtitleText => L(
        "ჟანგბადის სენსორების ტესტები · სტანდარტული OBD-II",
        "Oxygen sensor tests · generic OBD-II");
    public string RefreshButtonText => L("შემოწმება", "Check");
    public string ResultsCaptionText => L("სენსორების ტესტები", "Sensor tests");
    public string EmptyText => L(
        "შედეგები შემოწმების შემდეგ გამოჩნდება. ძრავი გაათბეთ და რამდენიმე წუთი იარეთ — ტესტები მოძრაობისას სრულდება.",
        "Results appear after the check. Warm up the engine and drive a few minutes — the tests run while driving.");
    public string FooterText => L(
        "მხოლოდ კითხულობს. 2008 წლის შემდეგ (CAN) მანქანებში Mode 05 სტანდარტით აღარ არსებობს — იგივე ტესტებს ECU Mode 06-ში ინახავს და აპი მათ ავტომატურად იქიდან კითხულობს.",
        "Read-only. Cars with CAN (most from 2008) no longer have Mode 05 by standard — the ECU keeps the same tests in Mode 06 and the app reads them from there automatically.");

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
            HasSourceNote = false;
            ScanColor = "#1D5BD6";
            ScanPercent = 5;
            ScanState = L("O₂ ტესტების წაკითხვა…", "Reading O₂ tests…");
            ScanDetail = L("ECU-ს ეგზავნება მხოლოდ წაკითხვის მოთხოვნები.", "Only read requests are sent to the ECU.");

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            var progress = new Progress<(int Percent, string Text)>(p =>
            {
                ScanPercent = Math.Clamp(p.Percent, 0, 99);
                RawStatus = p.Text;
            });
            var result = await O2TestReader.ReadAsync(_obd, progress, CancellationToken.None);
            foreach (var row in result.Rows)
                Tests.Add(row);

            ScanPercent = 100;
            SourceNote = result.FromMode06
                ? L("ეს მანქანა CAN-ს იყენებს: Mode 05 სტანდარტით მასში არ არის, ამიტომ იგივე O₂ ტესტები Mode 06-დან წავიკითხეთ.",
                    "This car uses CAN: it has no Mode 05 by standard, so the same O₂ tests were read from Mode 06.")
                : L("ძველი ტიპის (არა CAN) მანქანა: ტესტები Mode 05-ით წავიკითხეთ.",
                    "Older (non-CAN) car: tests were read with Mode 05.");
            HasSourceNote = true;

            if (result.Rows.Count == 0)
            {
                ScanColor = "#C98A1A";
                ScanState = L("O₂ ტესტების შედეგი ჯერ არ არის", "No O₂ test results yet");
                ScanDetail = L(
                    "ECU-მ შედეგი არ დააბრუნა. ჩვეულებრივ ეს ნიშნავს, რომ ტესტი ჯერ არ შესრულებულა (მაგ. შეცდომების წაშლის შემდეგ) — გაათბეთ ძრავი, იარეთ 10–15 წუთი და სცადეთ თავიდან.",
                    "The ECU returned no results. Usually the tests have not run yet (e.g. after clearing codes) — warm up, drive 10–15 minutes and try again.");
                RawStatus = L($"შემოწმებული სენსორები: {result.SensorsChecked}", $"Sensors checked: {result.SensorsChecked}");
                return;
            }

            var failed = result.Rows.Count(row => !row.Passed);
            ScanColor = failed == 0 ? "#1F8A4C" : "#B3361F";
            ScanState = failed == 0
                ? L($"{result.Rows.Count} ტესტი — ყველა ნორმაშია", $"{result.Rows.Count} tests — all passed")
                : L($"{failed} ტესტი ნორმაში არ არის", $"{failed} test(s) failed");
            ScanDetail = failed == 0
                ? L("ჟანგბადის სენსორები ECU-ის ზღვრებში მუშაობს.", "The oxygen sensors work within the ECU's limits.")
                : L("წითლად მონიშნულ ტესტს ქვემოთ ახსნა აქვს — რას ამოწმებს და რას ნიშნავს.", "Each red test below explains what it checks and what it means.");
            RawStatus = L($"მნიშვნელობები და ზღვრები ECU-მ დააბრუნა · ტესტები: {result.Rows.Count}", $"Values and limits come from the ECU · tests: {result.Rows.Count}");
        }
        catch (Exception ex)
        {
            ScanPercent = 100;
            ScanColor = "#B3361F";
            ScanState = L("O₂ ტესტები ვერ წაიკითხა", "O₂ tests could not be read");
            ScanDetail = L(
                "შეამოწმე ადაპტერთან კავშირი და ანთება, შემდეგ სცადე თავიდან.",
                "Check the adapter connection and ignition, then try again.");
            RawStatus = string.IsNullOrWhiteSpace(ex.Message) ? L("კავშირის შეცდომა.", "Connection error.") : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
