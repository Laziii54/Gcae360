using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

/// <summary>Displays standard Mode 06 data without inventing OEM test labels.</summary>
public partial class Mode06ViewModel : BaseViewModel
{
    private readonly OBDService _obd;

    private static string L(string ka, string en) => ObdLang.L(ka, en);

    public ObservableCollection<Mode06MonitorResult> Tests { get; } = new();

    [ObservableProperty] private string scanState = L("ჯერ არ შემოწმებულა", "Not checked yet");
    [ObservableProperty] private string scanDetail = L(
        "Mode 06 სტანდარტული OBD-II სერვისია: ECU-ის თვითშემოწმების შედეგები.",
        "Mode 06 is an optional standard OBD-II service: the ECU's self-test results.");
    [ObservableProperty] private string scanColor = "#94A3B8";
    [ObservableProperty] private int scanPercent;
    [ObservableProperty] private string rawStatus = L("ECU-ს პასუხი ჯერ არ არის.", "No ECU response yet.");

    public double ScanProgress => ScanPercent / 100d;

    // Page texts (both languages, so the page never mixes them)
    public string PageHeadingText => L("MODE 06 — მონიტორები", "MODE 06 MONITORS");
    public string PageSubtitleText => L(
        "გამონაბოლქვის სისტემების თვითშემოწმება · სტანდარტული OBD-II",
        "Emission system self-test results · generic OBD-II");
    public string RefreshButtonText => L("განახლება", "Refresh");
    public string ResultsCaptionText => L("აღმოჩენილი ტესტები", "Tests found");
    public string EmptyText => L(
        "Mode 06 შედეგები შემოწმების შემდეგ გამოჩნდება.",
        "Mode 06 results appear after the test.");
    public string FooterText => L(
        "Mode 06 მხოლოდ კითხულობს ECU-ის თვითშემოწმების შედეგებს და ზღვრებს. ეს გვერდი მანქანაში არაფერს ცვლის.",
        "Mode 06 only reads the ECU's self-test results and limits. This page changes nothing in the vehicle.");

    public Mode06ViewModel(OBDService obd)
    {
        _obd = obd;
        Title = "Mode 06 Monitors";
    }

    partial void OnScanPercentChanged(int value) => OnPropertyChanged(nameof(ScanProgress));

    /// <summary>
    /// CAN cars (ISO 15765-4): ask which monitors exist (0600, 0620, 0640 …),
    /// then read each monitor and decode every test with its unit and limits.
    /// </summary>
    private async Task RefreshCanAsync()
    {
        var supported = new List<byte>();
        for (var baseMid = 0x00; baseMid <= 0xE0; baseMid += 0x20)
        {
            var raw = await _obd.ReadMode06MidAsync((byte)baseMid);
            var mids = Mode06Definitions.ParseSupported(raw, (byte)baseMid);
            if (mids.Count == 0)
                break;
            supported.AddRange(mids.Where(mid => mid % 0x20 != 0));
            if (!mids.Contains((byte)(baseMid + 0x20)))
                break;
        }

        if (supported.Count == 0)
        {
            ScanPercent = 100;
            ScanColor = "#C98A1A";
            ScanState = L("MODE 06 მიუწვდომელია", "MODE 06 unavailable");
            ScanDetail = L(
                "ამ ECU-მ Mode 06 მონიტორების სია არ დააბრუნა. ეს თავისთავად გაუმართაობას არ ნიშნავს.",
                "This ECU returned no Mode 06 monitor list. This alone is not a fault.");
            RawStatus = L("0600: მონაცემი არ არის", "0600: No data");
            return;
        }

        var all = new List<Mode06MonitorResult>();
        for (var index = 0; index < supported.Count; index++)
        {
            var mid = supported[index];
            ScanPercent = 20 + (int)(75.0 * (index + 1) / supported.Count);
            ScanState = $"MODE 06: {Mode06Definitions.MonitorName(mid)}";
            try
            {
                var raw = await _obd.ReadMode06MidAsync(mid);
                foreach (var test in Mode06Definitions.ParseMid(raw, mid))
                {
                    all.Add(test);
                    Tests.Add(test);
                }
            }
            catch
            {
                // One monitor that does not answer does not stop the others.
            }
        }

        ScanPercent = 100;
        RawStatus = L(
            $"მონიტორები: {supported.Count} · ტესტები: {all.Count}",
            $"Monitors: {supported.Count} · Tests: {all.Count}");
        if (all.Count == 0)
        {
            ScanColor = "#C98A1A";
            ScanState = L("MODE 06 მონაცემი ვერ წაიკითხა", "MODE 06 data could not be read");
            ScanDetail = L(
                "მონიტორების სია მოვიდა, მაგრამ ტესტების შედეგები არა. სცადე ძრავის ჩართვით.",
                "The monitor list arrived, but no test results. Try again with the engine running.");
            return;
        }

        var failed = all.Count(test => !test.Passed);
        ScanColor = failed == 0 ? "#1F8A4C" : "#B3361F";
        ScanState = failed == 0
            ? L($"MODE 06: {all.Count} ტესტი, ყველა ნორმაშია", $"MODE 06: {all.Count} tests, all passed")
            : L($"MODE 06: {failed} ტესტი ნორმაში არ არის", $"MODE 06: {failed} test(s) failed");
        ScanDetail = L(
            "მნიშვნელობა, მინიმუმი და მაქსიმუმი ECU-მ დააბრუნა; ერთეულები SAE J1979 სტანდარტით არის გადაყვანილი.",
            "Value, minimum and maximum come from the ECU; units are converted per SAE J1979.");
    }

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
            ScanState = L("MODE 06 შემოწმება…", "Checking MODE 06…");
            ScanDetail = L(
                "ECU-ს ვუგზავნით მოკლე მოთხოვნას (მხოლოდ კითხვა).",
                "Sending a short, read-only Mode 06 request to the ECU.");

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            if (_obd.IsCanProtocol)
            {
                await RefreshCanAsync();
                return;
            }

            ScanPercent = 55;
            var raw = await _obd.ReadMode06QuickAsync();
            if (ObdResponseParser.IsNegative(raw))
            {
                ScanPercent = 100;
                ScanColor = "#C98A1A";
                ScanState = L("MODE 06 მიუწვდომელია", "MODE 06 unavailable");
                ScanDetail = L(
                    "ამ ECU-მ Mode 06 მონაცემი არ დააბრუნა. ეს თავისთავად გაუმართაობას არ ნიშნავს.",
                    "This ECU returned no Mode 06 data. This alone is not a fault.");
                RawStatus = L("ECU-ს პასუხი: მონაცემი არ არის.", "ECU response: No Data / negative response.");
                return;
            }

            ScanPercent = 80;
            var results = AdvancedModeParser.ParseMode06Detailed(raw);
            RawStatus = results.Count == 0
                ? L("ECU-მ პასუხი დააბრუნა, მაგრამ მასში გასაშიფრი Mode 06 ტესტი ვერ მოიძებნა.",
                    "The ECU responded, but no decodable standard Mode 06 item was found in this response.")
                : L($"ECU-მ {results.Count} ტესტი დააბრუნა.", $"The ECU returned {results.Count} Mode 06 item(s).");

            foreach (var result in results)
                Tests.Add(result);

            ScanPercent = 100;
            if (results.Count == 0)
            {
                ScanColor = "#C98A1A";
                ScanState = L("MODE 06 მონაცემი ვერ გაიშიფრა", "MODE 06 data could not be decoded");
                ScanDetail = L(
                    "მონაცემი გამოგონილი არ არის: ამ ECU-ს პასუხისთვის საჭიროა მწარმოებლის დადასტურებული აღწერა.",
                    "No data is invented: this ECU/adapter response needs a verified manufacturer definition.");
            }
            else
            {
                var failed = results.Count(result => !result.Passed);
                ScanColor = failed == 0 ? "#1F8A4C" : "#B3361F";
                ScanState = failed == 0
                    ? L("MODE 06 — ყველა ტესტი ნორმაშია", "MODE 06 — all tests are within limits")
                    : L($"MODE 06 — {failed} ტესტი ნორმაში არ არის", $"MODE 06 — {failed} item(s) out of limits");
                ScanDetail = L(
                    "შედეგი ეფუძნება ECU-ის მიერ დაბრუნებულ მინიმუმ/მაქსიმუმ ზღვრებს. ტესტების ზუსტი სახელები მწარმოებლის დადასტურებული აღწერით დაემატება.",
                    "The result is based on the minimum/maximum limits returned by the ECU. Exact test names will come with verified manufacturer definitions.");
            }
        }
        catch (Exception ex)
        {
            ScanPercent = 100;
            ScanColor = "#B3361F";
            ScanState = L("MODE 06 ვერ წაიკითხა", "MODE 06 could not be read");
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
            CarScanner.Services.CommandErrors.Report(commandException, "Mode06ViewModel.Refresh");
        }
    }
}
