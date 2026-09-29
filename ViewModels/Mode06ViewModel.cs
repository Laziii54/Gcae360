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

    public ObservableCollection<Mode06MonitorResult> Tests { get; } = new();

    [ObservableProperty] private string scanState = "ჯერ არ შემოწმებულა";
    [ObservableProperty] private string scanDetail = "Mode 06 optional სტანდარტული OBD-II სერვისია.";
    [ObservableProperty] private string scanColor = "#94A3B8";
    [ObservableProperty] private int scanPercent;
    [ObservableProperty] private string rawStatus = "Raw პასუხი ჯერ არ არის.";

    public double ScanProgress => ScanPercent / 100d;

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
            ScanState = "MODE 06 მიუწვდომელია";
            ScanDetail = "ამ ECU-მ Mode 06 მონიტორების სია არ დააბრუნა. ეს თავისთავად გაუმართაობას არ ნიშნავს.";
            RawStatus = "0600: No Data";
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
        RawStatus = $"მონიტორები: {supported.Count} · ტესტები: {all.Count}";
        if (all.Count == 0)
        {
            ScanColor = "#C98A1A";
            ScanState = "MODE 06 მონაცემი ვერ წაიკითხა";
            ScanDetail = "მონიტორების სია მოვიდა, მაგრამ ტესტების შედეგები არა. სცადე ძრავის ჩართვით.";
            return;
        }

        var failed = all.Count(test => !test.Passed);
        ScanColor = failed == 0 ? "#1F8A4C" : "#B3361F";
        ScanState = failed == 0 ? $"MODE 06: {all.Count} ტესტი, ყველა PASSED" : $"MODE 06: {failed} ტესტი FAILED";
        ScanDetail = "მნიშვნელობა, მინიმუმი და მაქსიმუმი ECU-მ დააბრუნა; ერთეულები SAE J1979 სტანდარტით არის გადაყვანილი.";
    }

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
            ScanState = "MODE 06 შემოწმება…";
            ScanDetail = "ECU-ს ვუგზავნით ერთ მოკლე, read-only Mode 06 მოთხოვნას.";

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
                ScanColor = "#F59E0B";
                ScanState = "MODE 06 მიუწვდომელია";
                ScanDetail = "ამ ECU-მ Mode 06 მონაცემი არ დააბრუნა. ეს თავისთავად გაუმართაობას არ ნიშნავს.";
                RawStatus = "ECU პასუხი: No Data / negative response.";
                return;
            }

            ScanPercent = 80;
            var results = AdvancedModeParser.ParseMode06Detailed(raw);
            RawStatus = results.Count == 0
                ? "ECU-მ პასუხი დააბრუნა, მაგრამ ამ პასუხში დეკოდირებადი სტანდარტული Mode 06 item ვერ მოიძებნა."
                : $"ECU-მ {results.Count} რეალური Mode 06 item დააბრუნა.";

            foreach (var result in results)
                Tests.Add(result);

            ScanPercent = 100;
            if (results.Count == 0)
            {
                ScanColor = "#F59E0B";
                ScanState = "MODE 06 მონაცემი ვერ გაიშიფრა";
                ScanDetail = "მონაცემი გამოგონილი არ არის: ამ ECU/adapter-ის პასუხისთვის საჭიროა დამატებითი, დადასტურებული definition.";
            }
            else
            {
                var failed = results.Count(result => !result.Passed);
                ScanColor = failed == 0 ? "#22C55E" : "#EF4444";
                ScanState = failed == 0 ? "MODE 06 — ყველა წაკითხული ტესტი ზღვარშია" : $"MODE 06 — {failed} item ზღვარს გარეთაა";
                ScanDetail = "შედეგი ეფუძნება ECU-ის მიერ დაბრუნებულ raw minimum/maximum საზღვრებს. ზუსტი OEM მონიტორის სახელები მოგვიანებით დაემატება დადასტურებული definition pack-ებით.";
            }
        }
        catch (Exception ex)
        {
            ScanPercent = 100;
            ScanColor = "#EF4444";
            ScanState = "MODE 06 ვერ წაიკითხა";
            ScanDetail = "კავშირი ან ECU პასუხი ვერ დადასტურდა; მონაცემი არ არის გამოგონილი.";
            RawStatus = string.IsNullOrWhiteSpace(ex.Message) ? "კავშირის შეცდომა." : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
