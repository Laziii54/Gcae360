using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CarScanner.ViewModels;

public partial class AccelerationLeaderboardViewModel : BaseViewModel
{
    private readonly AccelerationLeaderboardService _leaderboard;
    private readonly VehicleProfileResolverService _profiles;
    private readonly LocalizationService _localization;
    private IReadOnlyList<AccelerationLeaderboardEntry> _all = [];
    private string _filter = "all";

    public ObservableCollection<AccelerationLeaderboardRow> Rows { get; } = new();
    [ObservableProperty] private bool hasRows;
    [ObservableProperty] private string heading = string.Empty;
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string allText = string.Empty;
    [ObservableProperty] private string myModelText = string.Empty;
    [ObservableProperty] private string thisMonthText = string.Empty;
    [ObservableProperty] private string publishText = string.Empty;
    [ObservableProperty] private string emptyText = string.Empty;
    [ObservableProperty] private string rulesText = string.Empty;

    public AccelerationLeaderboardViewModel(
        AccelerationLeaderboardService leaderboard,
        VehicleProfileResolverService profiles,
        LocalizationService localization)
    {
        _leaderboard = leaderboard;
        _profiles = profiles;
        _localization = localization;
        UpdateTexts();
        _localization.LanguageChanged += (_, _) => { UpdateTexts(); ApplyFilter(); };
    }

    [RelayCommand]
    public async Task Load()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = _localization.IsEnglish ? "Loading ranking…" : "რეიტინგი იტვირთება…";
            _all = await _leaderboard.GetAsync();
            ApplyFilter();
            StatusMessage = string.Empty;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void ShowAll() { _filter = "all"; ApplyFilter(); }
    [RelayCommand] private void ShowMyModel() { _filter = "model"; ApplyFilter(); }
    [RelayCommand] private void ShowThisMonth() { _filter = "month"; ApplyFilter(); }

    [RelayCommand]
    private async Task PublishLatest()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _leaderboard.PublishLatestAsync();
            StatusMessage = _localization.IsEnglish ? TranslateStatus(result.Message) : result.Message;
            if (result.Success)
            {
                _all = await _leaderboard.GetAsync();
                ApplyFilter();
            }
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        var source = _all.AsEnumerable();
        if (_filter == "month")
        {
            var now = DateTime.Now;
            source = source.Where(x => x.FinishedAt.ToLocalTime().Year == now.Year && x.FinishedAt.ToLocalTime().Month == now.Month);
        }
        else if (_filter == "model")
        {
            var model = _profiles.Current?.DisplayName;
            source = string.IsNullOrWhiteSpace(model)
                ? Enumerable.Empty<AccelerationLeaderboardEntry>()
                : source.Where(x => x.VehicleModel.Equals(model, StringComparison.OrdinalIgnoreCase));
        }

        Rows.Clear();
        var rank = 1;
        foreach (var entry in source.OrderBy(x => x.DurationSeconds).ThenBy(x => x.FinishedAt))
        {
            Rows.Add(new AccelerationLeaderboardRow
            {
                Rank = $"#{rank++}",
                UserName = entry.UserName,
                VehicleModel = entry.VehicleModel,
                TimeText = $"{entry.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)} {(_localization.IsEnglish ? "s" : "წმ")}",
                DateText = entry.FinishedAt.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                VerificationText = entry.IsDemo
                    ? (_localization.IsEnglish ? "DEMO RESULT" : "DEMO შედეგი")
                    : entry.HasVerifiedVideo
                        ? (_localization.IsEnglish ? "✓ VIDEO VERIFIED" : "✓ ვიდეოთი დამოწმებული")
                        : (_localization.IsEnglish ? "VIN + ECU VERIFIED" : "VIN + ECU დამოწმებული"),
                VerificationColor = entry.IsDemo ? "#F59E0B" : entry.HasVerifiedVideo ? "#22C55E" : "#38BDF8"
            });
        }
        HasRows = Rows.Count > 0;
    }

    private void UpdateTexts()
    {
        if (_localization.IsEnglish)
        {
            Title = "0–100 Ranking"; Heading = "0–100 RANKING";
            Subtitle = "Results measured from the vehicle computer";
            AllText = "All cars"; MyModelText = "My model"; ThisMonthText = "This month";
            PublishText = "ADD MY LATEST RESULT"; EmptyText = "No qualifying results for this filter.";
            RulesText = "Only 0–100 km/h runs measured from ECU Vehicle Speed (PID 01 0D) are accepted. The model and year must be VIN-confirmed. Implausible or insufficient measurements are rejected.";
        }
        else
        {
            Title = "0–100 რეიტინგი"; Heading = "0–100 რეიტინგი";
            Subtitle = "მანქანის კომპიუტერიდან გაზომილი შედეგები";
            AllText = "ყველა მანქანა"; MyModelText = "ჩემი მოდელი"; ThisMonthText = "ამ თვეში";
            PublishText = "ჩემი ბოლო შედეგის დამატება"; EmptyText = "ამ ფილტრით შესაბამისი შედეგი არ მოიძებნა.";
            RulesText = "მიიღება მხოლოდ ECU Vehicle Speed PID 01 0D-ით გაზომილი 0–100 km/h შედეგი. მოდელი და წელი VIN-ით უნდა იყოს დადასტურებული. შეუძლებელი ან არასაკმარისი გაზომვა ავტომატურად უარყოფილია.";
        }
    }

    private static string TranslateStatus(string value) => value switch
    {
        "ჯერ 0–100 km/h გაზომვა არ არის." => "No 0–100 km/h measurement yet.",
        "რეიტინგში შედეგის დასამატებლად შედი ანგარიშზე." => "Sign in before adding a result to the ranking.",
        "შედეგი რეიტინგში დაემატა." => "Result added to the ranking.",
        "შედეგი გაიგზავნა და ადმინისტრატორის დამოწმებას ელოდება." => "Result submitted and waiting for administrator approval.",
        "რეიტინგის სერვერთან დაკავშირება ვერ მოხერხდა." => "Could not connect to the ranking server.",
        _ => value
    };
}

public sealed class AccelerationLeaderboardRow
{
    public string Rank { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string VehicleModel { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string VerificationText { get; init; } = string.Empty;
    public string VerificationColor { get; init; } = "#38BDF8";
}
