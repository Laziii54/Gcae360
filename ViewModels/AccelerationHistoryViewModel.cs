using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace CarScanner.ViewModels;

public partial class AccelerationHistoryViewModel : BaseViewModel
{
    private readonly AppDatabase _database;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;

    public ObservableCollection<AccelerationRunDisplay> Runs { get; } = new();

    [ObservableProperty] private string emptyMessage = "გაზომვების ისტორია ცარიელია.";
    [ObservableProperty] private string historyTitle = "0–100 km/h ისტორია";
    [ObservableProperty] private string bestResultText = string.Empty;
    [ObservableProperty] private bool hasRuns;

    public AccelerationHistoryViewModel(AppDatabase database, UnitSystemService units, LocalizationService localization)
    {
        _database = database;
        _units = units;
        _localization = localization;
        _units.UnitSystemChanged += (_, __) => MainThread.BeginInvokeOnMainThread(async () =>
        {
            RefreshUnitPresentation();
            await RefreshAsync();
        });
        RefreshUnitPresentation();
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            RefreshUnitPresentation();
            var savedRuns = await _database.GetAccelerationRunsAsync();
            Runs.Clear();
            foreach (var run in savedRuns)
                Runs.Add(ToDisplay(run));

            HasRuns = Runs.Count > 0;
            BestResultText = BuildBestText(savedRuns);
        }
        catch (Exception ex)
        {
            EmptyMessage = L("ისტორიის წაკითხვა ვერ მოხერხდა.", "Could not read the history.");
            System.Diagnostics.Debug.WriteLine($"Acceleration history failed: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteRun(AccelerationRunDisplay? item)
    {
        if (item is null)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            item.TestName,
            L("წაიშალოს ეს გაზომვა?", "Delete this measurement?"),
            L("წაშლა", "Delete"),
            L("გაუქმება", "Cancel"));
        if (!confirmed)
            return;

        await _database.DeleteAccelerationRunAsync(item.Id);
        Runs.Remove(item);
        HasRuns = Runs.Count > 0;
        BestResultText = BuildBestText(await _database.GetAccelerationRunsAsync());
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (Runs.Count == 0)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            HistoryTitle,
            L("წაიშალოს ყველა გაზომვა?", "Delete all measurements?"),
            L("წაშლა", "Delete"),
            L("გაუქმება", "Cancel"));
        if (!confirmed)
            return;

        await _database.ClearAccelerationRunsAsync();
        Runs.Clear();
        HasRuns = false;
        BestResultText = string.Empty;
    }

    private AccelerationRunDisplay ToDisplay(AccelerationRun run)
    {
        var testName = !string.IsNullOrWhiteSpace(run.TestName)
            ? run.TestName
            : run.Source.Replace("OBD · ", string.Empty).Replace("OBD", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(testName))
            testName = _units.PerformanceTestLabel;

        var details = new List<string>();
        if (run.MaxSpeedKph > 0)
            details.Add($"{L("მაქს.", "Max")} {_units.FormatSpeed(run.MaxSpeedKph)}");
        if (run.DistanceMeters > 0)
            details.Add($"{L("მანძილი", "Distance")} {_units.Format(run.DistanceMeters, "m", "0")}");
        details.Add($"{L("სიზუსტე", "Accuracy")}: {ConfidenceText(run.Confidence)}");

        return new AccelerationRunDisplay
        {
            Id = run.Id,
            TestName = testName,
            DateText = run.FinishedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            TimeText = $"{run.DurationSeconds:F2} {L("წმ", "s")}",
            SplitsText = BuildSplitsText(run),
            DetailsText = string.Join(" · ", details)
        };
    }

    private string BuildSplitsText(AccelerationRun run)
    {
        if (string.IsNullOrWhiteSpace(run.SplitsJson))
            return string.Empty;

        try
        {
            var splits = JsonSerializer.Deserialize<Dictionary<string, double>>(run.SplitsJson);
            if (splits is null || splits.Count == 0)
                return string.Empty;

            var start = _units.Convert(run.StartSpeedKph, "km/h").Value.ToString("0", CultureInfo.InvariantCulture);
            return string.Join("  ·  ", splits
                .Select(item => (Ok: int.TryParse(item.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kph), Kph: kph, Seconds: item.Value))
                .Where(item => item.Ok)
                .OrderBy(item => item.Kph)
                .Select(item =>
                {
                    var target = _units.Convert(item.Kph, "km/h").Value.ToString("0", CultureInfo.InvariantCulture);
                    return $"{start}–{target}: {item.Seconds:F2}";
                }));
        }
        catch
        {
            return string.Empty;
        }
    }

    private string BuildBestText(IReadOnlyCollection<AccelerationRun> runs)
    {
        var best = runs
            .Where(run => run.DurationSeconds > 0)
            .GroupBy(run => !string.IsNullOrWhiteSpace(run.TestName) ? run.TestName : run.Source.Replace("OBD · ", string.Empty).Trim())
            .Select(group => group.OrderBy(run => run.DurationSeconds).First())
            .OrderBy(run => run.TargetSpeedKph)
            .Take(4)
            .Select(run =>
            {
                var name = !string.IsNullOrWhiteSpace(run.TestName) ? run.TestName : run.Source.Replace("OBD · ", string.Empty).Trim();
                return $"{name}: {run.DurationSeconds:F2} {L("წმ", "s")}";
            })
            .ToArray();
        return string.Join("\n", best);
    }

    private string ConfidenceText(string? confidence) => confidence switch
    {
        "High" => L("მაღალი", "high"),
        "Medium" => L("საშუალო", "medium"),
        "Low" => L("დაბალი", "low"),
        _ => L("შეფასება", "estimate")
    };

    private void RefreshUnitPresentation()
    {
        HistoryTitle = _units.PerformanceTestLabel + L(" ისტორია", " history");
        Title = HistoryTitle;
        EmptyMessage = L("გაზომვების ისტორია ცარიელია.", "No measurements yet.");
    }
}

public sealed class AccelerationRunDisplay
{
    public int Id { get; init; }
    public string TestName { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public string SplitsText { get; init; } = string.Empty;
    public bool HasSplits => !string.IsNullOrWhiteSpace(SplitsText);
    public string DetailsText { get; init; } = string.Empty;
}
