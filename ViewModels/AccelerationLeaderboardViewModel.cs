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
    private readonly UnitSystemService _units;
    private string _testId;
    private IReadOnlyList<AccelerationLeaderboardEntry> _all = [];
    private string _filter = "all";
    private HashSet<string> _mySubmissionIds = new(StringComparer.OrdinalIgnoreCase);
    private AccelerationRun? _myBest;

    public ObservableCollection<AccelerationLeaderboardRow> Rows { get; } = new();

    // My result is "under review": send the video recorded with the run.
    private AccelerationRun? _waitingForVideo;
    [ObservableProperty] private bool hasVideoToSend;
    [ObservableProperty] private bool needsVideo;
    [ObservableProperty] private string sendVideoText = string.Empty;
    [ObservableProperty] private string videoHintText = string.Empty;
    [ObservableProperty] private bool hasRows;
    [ObservableProperty] private string heading = string.Empty;
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string allText = string.Empty;
    [ObservableProperty] private string myModelText = string.Empty;
    [ObservableProperty] private string thisMonthText = string.Empty;
    [ObservableProperty] private string publishText = string.Empty;
    [ObservableProperty] private string emptyText = string.Empty;
    [ObservableProperty] private string rulesText = string.Empty;

    // "My place" card, always visible above the list.
    [ObservableProperty] private bool hasMyPlace;
    [ObservableProperty] private string myPlaceCaption = string.Empty;
    [ObservableProperty] private string myPlaceRank = string.Empty;
    [ObservableProperty] private string myPlaceTime = string.Empty;
    [ObservableProperty] private string myPlaceDetail = string.Empty;

    // Selected filter chip.
    [ObservableProperty] private bool isAllSelected = true;
    [ObservableProperty] private bool isMyModelSelected;
    [ObservableProperty] private bool isThisMonthSelected;

    // Ranked test: 0–100 km/h, 0–60 mph or ¼ mile.
    [ObservableProperty] private bool isZeroHundredSelected;
    [ObservableProperty] private bool isZeroSixtySelected;
    [ObservableProperty] private bool isQuarterMileSelected;
    public string ZeroHundredText => "0–100 km/h";
    public string ZeroSixtyText => "0–60 mph";
    public string QuarterMileText => _localization.IsEnglish ? "¼ mile" : "¼ მილი";

    public AccelerationLeaderboardViewModel(
        AccelerationLeaderboardService leaderboard,
        VehicleProfileResolverService profiles,
        LocalizationService localization,
        UnitSystemService units)
    {
        _leaderboard = leaderboard;
        _profiles = profiles;
        _localization = localization;
        _units = units;
        _testId = units.IsImperial ? "0-60mph" : "0-100";
        SelectTestFlags();
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
            _all = await _leaderboard.GetAsync(_testId);
            (_mySubmissionIds, _myBest) = await _leaderboard.GetMineAsync(_testId);
            ApplyFilter();
            await RefreshVideoStateAsync();
            StatusMessage = string.Empty;
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshVideoStateAsync()
    {
        var en = _localization.IsEnglish;
        _waitingForVideo = await _leaderboard.GetRunWaitingForVideoAsync(_testId);
        HasVideoToSend = _waitingForVideo is not null && AccelerationLeaderboardService.HasLocalVideo(_waitingForVideo);
        NeedsVideo = _waitingForVideo is not null && !HasVideoToSend;
        SendVideoText = en ? "SEND VIDEO FOR REVIEW" : "ვიდეოს გაგზავნა შესამოწმებლად";
        VideoHintText = en
            ? "Your result is under review. Top places need a video: open Performance Video, measure the run again while recording, and the video is sent automatically."
            : "შენი შედეგი მოწმდება. ტოპ ადგილს ვიდეო სჭირდება: გახსენი Performance Video, გაზომე ხელახლა ჩაწერით და ვიდეო ავტომატურად გაიგზავნება.";
    }

    [RelayCommand]
    private async Task SendVideo()
    {
        if (IsBusy || _waitingForVideo is null) return;
        IsBusy = true;
        try
        {
            StatusMessage = _localization.IsEnglish ? "Sending the video…" : "ვიდეო იგზავნება…";
            var result = await _leaderboard.UploadVideoAsync(_waitingForVideo);
            StatusMessage = result.Message;
            await RefreshVideoStateAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task ShowZeroHundred() => SelectTest("0-100");
    [RelayCommand] private Task ShowZeroSixty() => SelectTest("0-60mph");
    [RelayCommand] private Task ShowQuarterMile() => SelectTest("quarter-mile");

    private async Task SelectTest(string testId)
    {
        if (_testId == testId || IsBusy)
            return;
        _testId = testId;
        SelectTestFlags();
        UpdateTexts();
        _all = [];
        ApplyFilter();
        await Load();
    }

    private void SelectTestFlags()
    {
        IsZeroHundredSelected = _testId == "0-100";
        IsZeroSixtySelected = _testId == "0-60mph";
        IsQuarterMileSelected = _testId == "quarter-mile";
    }

    private string TestName => _testId switch
    {
        "0-60mph" => "0–60 mph",
        "quarter-mile" => _localization.IsEnglish ? "¼ mile" : "¼ მილი",
        _ => "0–100 km/h"
    };

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
            var result = await _leaderboard.PublishLatestAsync(_testId);
            StatusMessage = _localization.IsEnglish ? TranslateStatus(result.Message) : result.Message;
            if (result.Success)
            {
                _all = await _leaderboard.GetAsync(_testId);
                (_mySubmissionIds, _myBest) = await _leaderboard.GetMineAsync(_testId);
                ApplyFilter();
                await RefreshVideoStateAsync();
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

        IsAllSelected = _filter == "all";
        IsMyModelSelected = _filter == "model";
        IsThisMonthSelected = _filter == "month";

        Rows.Clear();
        var rank = 1;
        var myRank = 0;
        AccelerationLeaderboardEntry? myEntry = null;
        foreach (var entry in source.OrderBy(x => x.DurationSeconds).ThenBy(x => x.FinishedAt))
        {
            var isMine = !entry.IsDemo && !string.IsNullOrWhiteSpace(entry.SubmissionId) && _mySubmissionIds.Contains(entry.SubmissionId);
            if (isMine && myEntry is null)
            {
                myEntry = entry;
                myRank = rank;
            }

            Rows.Add(new AccelerationLeaderboardRow
            {
                IsMine = isMine,
                Rank = $"#{rank++}",
                UserName = entry.UserName,
                // ¼ mile: the speed at the finish line (trap speed) next to the car.
                VehicleModel = _testId == "quarter-mile" && entry.MaxSpeedKph > 0
                    ? $"{entry.VehicleModel} · {_units.FormatSpeed(entry.MaxSpeedKph)}"
                    : entry.VehicleModel,
                TimeText = $"{entry.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)} {(_localization.IsEnglish ? "s" : "წმ")}",
                DateText = entry.FinishedAt.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                VerificationText = entry.IsDemo ? "DEMO"
                    : entry.Status == "Verified" ? (_localization.IsEnglish ? "✓ VERIFIED" : "✓ დადასტურებული")
                    : entry.Status == "Pending" ? (_localization.IsEnglish ? "UNDER REVIEW" : "მოწმდება")
                    : string.Empty,
                VerificationColor = entry.IsDemo ? "#C98A1A"
                    : entry.Status == "Verified" ? "#1F8A4C"
                    : entry.Status == "Pending" ? "#B7791F"
                    : "#1D5BD6"
            });
        }
        HasRows = Rows.Count > 0;
        UpdateMyPlace(myEntry, myRank);
    }

    private void UpdateMyPlace(AccelerationLeaderboardEntry? mine, int rank)
    {
        var en = _localization.IsEnglish;
        var sec = en ? "s" : "წმ";
        MyPlaceCaption = en ? "MY PLACE" : "ჩემი ადგილი";
        if (mine is not null)
        {
            MyPlaceRank = $"#{rank}";
            MyPlaceTime = $"{mine.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)} {sec}";
            MyPlaceDetail = en
                ? $"{mine.VehicleModel} · of {Rows.Count}"
                : $"{mine.VehicleModel} · {Rows.Count}-დან";
            HasMyPlace = true;
        }
        else if (_myBest is not null)
        {
            // Not in the ranking yet: show where the best local run would land.
            var wouldBe = Rows.Count(r => double.TryParse(r.TimeText.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t < _myBest.DurationSeconds) + 1;
            MyPlaceRank = $"#{wouldBe}";
            MyPlaceTime = $"{_myBest.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)} {sec}";
            MyPlaceDetail = en
                ? "Your best run is not in the ranking yet — add it below."
                : "შენი საუკეთესო შედეგი ჯერ რეიტინგში არ არის — დაამატე ქვემოთ.";
            HasMyPlace = true;
        }
        else
        {
            MyPlaceRank = "—";
            MyPlaceTime = string.Empty;
            MyPlaceDetail = en
                ? $"Measure {TestName} in Performance Lab to get a place."
                : $"ადგილის მისაღებად გაზომე {TestName} Performance Lab-ში.";
            HasMyPlace = true;
        }
    }

    private void UpdateTexts()
    {
        if (_localization.IsEnglish)
        {
            Title = $"{TestName} Ranking"; Heading = $"{TestName} RANKING".ToUpperInvariant();
            Subtitle = "Results measured from the vehicle computer";
            AllText = "All cars"; MyModelText = "My model"; ThisMonthText = "This month";
            PublishText = "ADD MY LATEST RESULT"; EmptyText = "No qualifying results for this filter.";
            RulesText = "Only runs measured from ECU Vehicle Speed (PID 01 0D) from a standstill are accepted: 0–100 km/h, 0–60 mph and ¼ mile (distance from ECU speed, so it is an estimate). The model and year must be VIN-confirmed. Implausible or insufficient measurements are rejected, and top results may be checked by our team.";
        }
        else
        {
            Title = $"{TestName} რეიტინგი"; Heading = $"{TestName} რეიტინგი";
            Subtitle = "მანქანის კომპიუტერიდან გაზომილი შედეგები";
            AllText = "ყველა მანქანა"; MyModelText = "ჩემი მოდელი"; ThisMonthText = "ამ თვეში";
            PublishText = "ჩემი ბოლო შედეგის დამატება"; EmptyText = "ამ ფილტრით შესაბამისი შედეგი არ მოიძებნა.";
            RulesText = "მიიღება მხოლოდ ECU Vehicle Speed PID 01 0D-ით, ადგილიდან გაზომილი შედეგი: 0–100 km/h, 0–60 mph და ¼ მილი (მანძილი ECU სიჩქარით ითვლება, ამიტომ შეფასებაა). მოდელი და წელი VIN-ით უნდა იყოს დადასტურებული. შეუძლებელი ან არასაკმარისი გაზომვა უარყოფილია, საუკეთესო შედეგებს კი ჩვენი გუნდი ამოწმებს.";
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
    public bool IsMine { get; init; }
    public string Rank { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string VehicleModel { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string VerificationText { get; init; } = string.Empty;
    public string VerificationColor { get; init; } = "#38BDF8";
}
