using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CarScanner.ViewModels;

/// <summary>One option inside the filter sheet.</summary>
public sealed partial class LeaderboardFilterOption : ObservableObject
{
    public LeaderboardFilterOption(string key, string text, Action<LeaderboardFilterOption> onTap)
    {
        Key = key;
        Text = text;
        TapCommand = new RelayCommand(() => onTap(this));
    }

    public string Key { get; }
    public string Text { get; }
    public IRelayCommand TapCommand { get; }

    [ObservableProperty] private bool isSelected;
}

public partial class AccelerationLeaderboardViewModel : BaseViewModel
{
    private readonly AccelerationLeaderboardService _leaderboard;
    private readonly VehicleProfileResolverService _profiles;
    private readonly LocalizationService _localization;
    private readonly UnitSystemService _units;
    private string _testId;
    private IReadOnlyList<AccelerationLeaderboardEntry> _all = [];
    private string _vehicleFilter = "all";
    private string _period = "all";
    private HashSet<string> _mySubmissionIds = new(StringComparer.OrdinalIgnoreCase);
    private AccelerationRun? _myBest;

    /// <summary>Places 4 and below (1–3 are on the podium when there are at least three).</summary>
    public ObservableCollection<AccelerationLeaderboardRow> Rows { get; } = new();

    public ObservableCollection<LeaderboardFilterOption> TestOptions { get; } = new();
    public ObservableCollection<LeaderboardFilterOption> VehicleOptions { get; } = new();
    public ObservableCollection<LeaderboardFilterOption> PeriodOptions { get; } = new();

    // My result is "under review": send the video recorded with the run.
    private AccelerationRun? _waitingForVideo;
    [ObservableProperty] private bool hasVideoToSend;
    [ObservableProperty] private bool needsVideo;
    [ObservableProperty] private string sendVideoText = string.Empty;
    [ObservableProperty] private string videoHintText = string.Empty;
    [ObservableProperty] private bool hasRows;
    [ObservableProperty] private bool hasAnyResult;
    [ObservableProperty] private string heading = string.Empty;
    [ObservableProperty] private string activeFilterText = string.Empty;
    [ObservableProperty] private string publishText = string.Empty;
    [ObservableProperty] private string emptyText = string.Empty;
    [ObservableProperty] private string rulesText = string.Empty;
    [ObservableProperty] private string rulesTitle = string.Empty;
    [ObservableProperty] private bool isRulesOpen;
    [ObservableProperty] private string resultsCountText = string.Empty;

    // Podium (places 1–3).
    [ObservableProperty] private bool hasPodium;
    [ObservableProperty] private AccelerationLeaderboardRow? first;
    [ObservableProperty] private AccelerationLeaderboardRow? second;
    [ObservableProperty] private AccelerationLeaderboardRow? third;

    // "My place" card, always visible above the list.
    [ObservableProperty] private bool hasMyPlace;
    [ObservableProperty] private string myPlaceCaption = string.Empty;
    [ObservableProperty] private string myPlaceRank = string.Empty;
    [ObservableProperty] private string myPlaceTime = string.Empty;
    [ObservableProperty] private string myPlaceDetail = string.Empty;

    // Filter sheet (opened with the ☰ button).
    [ObservableProperty] private bool isFilterOpen;
    [ObservableProperty] private bool verifiedOnly;
    [ObservableProperty] private int activeFilterCount;
    [ObservableProperty] private bool hasActiveFilters;
    [ObservableProperty] private string filterTitle = string.Empty;
    [ObservableProperty] private string testSectionText = string.Empty;
    [ObservableProperty] private string vehicleSectionText = string.Empty;
    [ObservableProperty] private string periodSectionText = string.Empty;
    [ObservableProperty] private string verifiedOnlyText = string.Empty;
    [ObservableProperty] private string verifiedOnlyHint = string.Empty;
    [ObservableProperty] private string resetText = string.Empty;
    [ObservableProperty] private string showResultsText = string.Empty;

    partial void OnVerifiedOnlyChanged(bool value) => ApplyFilter();

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
        BuildOptions();
        UpdateTexts();
        _localization.LanguageChanged += (_, _) => { BuildOptions(); UpdateTexts(); ApplyFilter(); };
    }

    private bool En => _localization.IsEnglish;
    private string L(string ka, string en) => En ? en : ka;

    // ------------------------------------------------------------------
    // Filter sheet
    // ------------------------------------------------------------------

    private void BuildOptions()
    {
        TestOptions.Clear();
        TestOptions.Add(new("0-100", "0–100 km/h", o => _ = SelectTest(o.Key)));
        TestOptions.Add(new("0-60mph", "0–60 mph", o => _ = SelectTest(o.Key)));
        TestOptions.Add(new("quarter-mile", L("¼ მილი", "¼ mile"), o => _ = SelectTest(o.Key)));

        VehicleOptions.Clear();
        VehicleOptions.Add(new("all", L("ყველა მანქანა", "All cars"), o => { _vehicleFilter = o.Key; ApplyFilter(); }));
        VehicleOptions.Add(new("model", L("ჩემი მოდელი", "My model"), o => { _vehicleFilter = o.Key; ApplyFilter(); }));

        PeriodOptions.Clear();
        PeriodOptions.Add(new("all", L("ყველა დრო", "All time"), o => { _period = o.Key; ApplyFilter(); }));
        PeriodOptions.Add(new("month", L("ამ თვეში", "This month"), o => { _period = o.Key; ApplyFilter(); }));
        PeriodOptions.Add(new("year", L("ამ წელს", "This year"), o => { _period = o.Key; ApplyFilter(); }));

        MarkSelected();
    }

    private void MarkSelected()
    {
        foreach (var o in TestOptions) o.IsSelected = o.Key == _testId;
        foreach (var o in VehicleOptions) o.IsSelected = o.Key == _vehicleFilter;
        foreach (var o in PeriodOptions) o.IsSelected = o.Key == _period;
        ActiveFilterCount = (_vehicleFilter != "all" ? 1 : 0) + (_period != "all" ? 1 : 0) + (VerifiedOnly ? 1 : 0);
        HasActiveFilters = ActiveFilterCount > 0;
    }

    [RelayCommand] private void OpenFilters() => IsFilterOpen = true;
    [RelayCommand] private void CloseFilters() => IsFilterOpen = false;
    [RelayCommand] private void ToggleRules() => IsRulesOpen = !IsRulesOpen;

    [RelayCommand]
    private void ResetFilters()
    {
        _vehicleFilter = "all";
        _period = "all";
        VerifiedOnly = false;
        ApplyFilter();
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    [RelayCommand]
    public async Task Load()
    {
        try
        {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = L("რეიტინგი იტვირთება…", "Loading ranking…");
            _all = await _leaderboard.GetAsync(_testId);
            (_mySubmissionIds, _myBest) = await _leaderboard.GetMineAsync(_testId);
            ApplyFilter();
            await RefreshVideoStateAsync();
            StatusMessage = string.Empty;
        }
        finally { IsBusy = false; }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationLeaderboardViewModel.Load");
        }
    }

    private async Task RefreshVideoStateAsync()
    {
        _waitingForVideo = await _leaderboard.GetRunWaitingForVideoAsync(_testId);
        HasVideoToSend = _waitingForVideo is not null && AccelerationLeaderboardService.HasLocalVideo(_waitingForVideo);
        NeedsVideo = _waitingForVideo is not null && !HasVideoToSend;
        SendVideoText = L("ვიდეოს გაგზავნა შესამოწმებლად", "SEND VIDEO FOR REVIEW");
        VideoHintText = L(
            "შენი შედეგი მოწმდება. ტოპ ადგილს ვიდეო სჭირდება: გახსენი Performance Video, გაზომე ხელახლა ჩაწერით და ვიდეო ავტომატურად გაიგზავნება.",
            "Your result is under review. Top places need a video: open Performance Video, measure the run again while recording, and the video is sent automatically.");
    }

    [RelayCommand]
    private async Task SendVideo()
    {
        try
        {
        if (IsBusy || _waitingForVideo is null) return;
        IsBusy = true;
        try
        {
            StatusMessage = L("ვიდეო იგზავნება…", "Sending the video…");
            var result = await _leaderboard.UploadVideoAsync(_waitingForVideo);
            StatusMessage = result.Message;
            await RefreshVideoStateAsync();
        }
        finally { IsBusy = false; }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationLeaderboardViewModel.SendVideo");
        }
    }

    private async Task SelectTest(string testId)
    {
        if (_testId == testId || IsBusy)
            return;
        _testId = testId;
        UpdateTexts();
        _all = [];
        ApplyFilter();
        await Load();
    }

    private string TestName => _testId switch
    {
        "0-60mph" => "0–60 mph",
        "quarter-mile" => L("¼ მილი", "¼ mile"),
        _ => "0–100 km/h"
    };

    [RelayCommand]
    private async Task PublishLatest()
    {
        try
        {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _leaderboard.PublishLatestAsync(_testId);
            StatusMessage = En ? TranslateStatus(result.Message) : result.Message;
            if (result.Success)
            {
                _all = await _leaderboard.GetAsync(_testId);
                (_mySubmissionIds, _myBest) = await _leaderboard.GetMineAsync(_testId);
                ApplyFilter();
                await RefreshVideoStateAsync();
            }
            else
            {
                // Rejected / not accepted: the local run's state was updated —
                // show it instead of a stale "added" state.
                (_mySubmissionIds, _myBest) = await _leaderboard.GetMineAsync(_testId);
                ApplyFilter();
                await RefreshVideoStateAsync();
            }
        }
        finally { IsBusy = false; }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationLeaderboardViewModel.PublishLatest");
        }
    }

    /// <summary>The model for "My model": the connected car, otherwise the car of my best run.</summary>
    private string? MyModel =>
        !string.IsNullOrWhiteSpace(_profiles.Current?.DisplayName)
            ? _profiles.Current!.DisplayName
            : string.IsNullOrWhiteSpace(_myBest?.VehicleDisplayName) ? null : _myBest!.VehicleDisplayName;

    private void ApplyFilter()
    {
        MarkSelected();

        var source = _all.AsEnumerable();
        var now = DateTime.Now;
        if (_period == "month")
            source = source.Where(x => x.FinishedAt.ToLocalTime().Year == now.Year && x.FinishedAt.ToLocalTime().Month == now.Month);
        else if (_period == "year")
            source = source.Where(x => x.FinishedAt.ToLocalTime().Year == now.Year);

        var myModel = MyModel;
        if (_vehicleFilter == "model")
        {
            source = string.IsNullOrWhiteSpace(myModel)
                ? Enumerable.Empty<AccelerationLeaderboardEntry>()
                : source.Where(x => x.VehicleModel.Equals(myModel, StringComparison.OrdinalIgnoreCase));
        }

        if (VerifiedOnly)
            source = source.Where(x => x.Status == "Verified" || x.HasVerifiedVideo);

        var ordered = source.OrderBy(x => x.DurationSeconds).ThenBy(x => x.FinishedAt).ToList();
        var all = new List<AccelerationLeaderboardRow>();
        var myRank = 0;
        AccelerationLeaderboardEntry? myEntry = null;
        for (var i = 0; i < ordered.Count; i++)
        {
            var entry = ordered[i];
            var isMine = !entry.IsDemo && !string.IsNullOrWhiteSpace(entry.SubmissionId) && _mySubmissionIds.Contains(entry.SubmissionId);
            if (isMine && myEntry is null)
            {
                myEntry = entry;
                myRank = i + 1;
            }
            all.Add(ToRow(entry, i + 1, isMine));
        }

        HasPodium = all.Count >= 3;
        First = all.ElementAtOrDefault(0);
        Second = all.ElementAtOrDefault(1);
        Third = all.ElementAtOrDefault(2);

        Rows.Clear();
        foreach (var row in HasPodium ? all.Skip(3) : all)
            Rows.Add(row);

        HasRows = Rows.Count > 0;
        HasAnyResult = all.Count > 0;
        ResultsCountText = all.Count == 0 ? string.Empty : L($"{all.Count} შედეგი", all.Count == 1 ? "1 result" : $"{all.Count} results");
        EmptyText = _vehicleFilter == "model" && string.IsNullOrWhiteSpace(myModel)
            ? L("„ჩემი მოდელი“-სთვის დააკავშირე მანქანა ან გაზომე შედეგი.", "For “My model”, connect your car or measure a run first.")
            : L("ამ ფილტრით შედეგი არ მოიძებნა.", "No results for these filters.");

        // Short line under the title: what is shown now.
        var parts = new List<string> { TestName };
        parts.Add(_vehicleFilter == "model" && !string.IsNullOrWhiteSpace(myModel) ? myModel! : L("ყველა მანქანა", "All cars"));
        if (_period == "month") parts.Add(L("ამ თვეში", "this month"));
        if (_period == "year") parts.Add(L("ამ წელს", "this year"));
        if (VerifiedOnly) parts.Add(L("დადასტურებული", "verified"));
        ActiveFilterText = string.Join(" · ", parts);

        UpdateMyPlace(myEntry, myRank, all.Count);
    }

    private AccelerationLeaderboardRow ToRow(AccelerationLeaderboardEntry entry, int rank, bool isMine)
    {
        var name = string.IsNullOrWhiteSpace(entry.UserName) ? "GCar360 Driver" : entry.UserName.Trim();
        return new AccelerationLeaderboardRow
        {
            IsMine = isMine,
            Rank = $"{rank}",
            UserName = name,
            Initial = name[..1].ToUpperInvariant(),
            // ¼ mile: the speed at the finish line (trap speed) next to the car.
            VehicleModel = _testId == "quarter-mile" && entry.MaxSpeedKph > 0
                ? $"{entry.VehicleModel} · {_units.FormatSpeed(entry.MaxSpeedKph)}"
                : entry.VehicleModel,
            Seconds = entry.DurationSeconds,
            TimeText = entry.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture),
            SecondsUnit = L("წმ", "s"),
            DateText = entry.FinishedAt.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            VerificationText = entry.IsDemo ? "DEMO"
                : entry.Status == "Verified" ? L("✓ დადასტურებული", "✓ VERIFIED")
                : entry.Status == "Pending" ? L("მოწმდება", "UNDER REVIEW")
                : string.Empty,
            VerificationColor = entry.IsDemo ? "#C98A1A"
                : entry.Status == "Verified" ? "#1F8A4C"
                : entry.Status == "Pending" ? "#B7791F"
                : "#1D5BD6",
            HasVerification = entry.IsDemo || entry.Status is "Verified" or "Pending",
            MedalColor = rank switch { 1 => "#E8B323", 2 => "#A7B0BA", 3 => "#C7834A", _ => "#1D5BD6" }
        };
    }

    private void UpdateMyPlace(AccelerationLeaderboardEntry? mine, int rank, int total)
    {
        var sec = L("წმ", "s");
        MyPlaceCaption = L("ჩემი ადგილი", "MY PLACE");
        if (mine is not null)
        {
            MyPlaceRank = $"#{rank}";
            MyPlaceTime = $"{mine.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)} {sec}";
            MyPlaceDetail = L($"{mine.VehicleModel} · {total}-დან", $"{mine.VehicleModel} · of {total}");
        }
        else if (_myBest is not null)
        {
            // Not in the ranking yet: show where the best local run would land.
            var wouldBe = _all.Count(x => x.DurationSeconds < _myBest.DurationSeconds) + 1;
            MyPlaceRank = $"#{wouldBe}";
            MyPlaceTime = $"{_myBest.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)} {sec}";
            var car = string.IsNullOrWhiteSpace(_myBest.VehicleDisplayName) ? string.Empty : _myBest.VehicleDisplayName + " · ";
            MyPlaceDetail = L(
                $"{car}შენი საუკეთესო შედეგი ჯერ რეიტინგში არ არის — დაამატე ქვემოთ.",
                $"{car}Your best run is not in the ranking yet — add it below.");
        }
        else
        {
            MyPlaceRank = "—";
            MyPlaceTime = string.Empty;
            MyPlaceDetail = L($"ადგილის მისაღებად გაზომე {TestName} Performance Lab-ში.", $"Measure {TestName} in Performance Lab to get a place.");
        }
        HasMyPlace = true;
    }

    private void UpdateTexts()
    {
        Title = L($"{TestName} რეიტინგი", $"{TestName} Ranking");
        Heading = L("რეიტინგი", "Ranking");
        FilterTitle = L("ფილტრი", "Filters");
        TestSectionText = L("გაზომვა", "Measurement");
        VehicleSectionText = L("მანქანები", "Cars");
        PeriodSectionText = L("პერიოდი", "Period");
        VerifiedOnlyText = L("მხოლოდ დადასტურებული", "Verified only");
        VerifiedOnlyHint = L("ვიდეოთი ან გუნდის მიერ შემოწმებული შედეგები", "Results checked by video or by our team");
        ResetText = L("გასუფთავება", "Reset");
        ShowResultsText = L("შედეგების ჩვენება", "Show results");
        PublishText = L("ჩემი ბოლო შედეგის დამატება", "ADD MY LATEST RESULT");
        RulesTitle = L("როგორ მოწმდება შედეგები", "How results are checked");
        RulesText = L(
            "მიიღება მხოლოდ ECU Vehicle Speed PID 01 0D-ით, ადგილიდან გაზომილი შედეგი: 0–100 km/h, 0–60 mph და ¼ მილი (მანძილი ECU სიჩქარით ითვლება, ამიტომ შეფასებაა). მოდელი და წელი VIN-ით უნდა იყოს დადასტურებული. შეუძლებელი ან არასაკმარისი გაზომვა უარყოფილია, საუკეთესო შედეგებს კი ჩვენი გუნდი ამოწმებს.",
            "Only runs measured from ECU Vehicle Speed (PID 01 0D) from a standstill are accepted: 0–100 km/h, 0–60 mph and ¼ mile (distance from ECU speed, so it is an estimate). The model and year must be VIN-confirmed. Implausible or insufficient measurements are rejected, and top results may be checked by our team.");
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
    public string Initial { get; init; } = "G";
    public string VehicleModel { get; init; } = string.Empty;
    public double Seconds { get; init; }
    public string TimeText { get; init; } = string.Empty;
    public string SecondsUnit { get; init; } = "s";
    public string DateText { get; init; } = string.Empty;
    public string VerificationText { get; init; } = string.Empty;
    public string VerificationColor { get; init; } = "#38BDF8";
    public bool HasVerification { get; init; }
    public string MedalColor { get; init; } = "#1D5BD6";
}
