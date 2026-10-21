using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// Simple Service page: the oil card with one reset button, the reminders,
/// the odometer line and a short "Help" list (menu guide · search · recorder).
/// </summary>
public partial class ServiceViewModel
{
    [ObservableProperty] private bool isPullRefreshing;

    /// <summary>Pull-to-refresh: the same read as the Refresh button, never a second one while one is running.</summary>
    [RelayCommand]
    private Task PullRefresh() => PullRefreshRunner.RunAsync(
        Refresh,
        () => IsBusy || IsOilResetRunning || IsDiscoveryRunning,
        value => IsPullRefreshing = value,
        CarScanner.Helpers.RefreshHaptics.Completed);

    [ObservableProperty] private bool showSearch;
    [ObservableProperty] private bool showMenuGuide;
    [ObservableProperty] private string menuGuideText = string.Empty;

    public string OilCardTitle => L("ზეთი", "Oil");
    public string OilSourceText => L("მანქანიდან", "from the car");
    public string ResetOilButtonText => L("ზეთი შევცვალე — განულება", "Oil changed — reset");
    public string RemindersTitle => L("შეხსენებები", "Reminders");
    public string HelpTitle => L("დახმარება", "Help");
    public string ChangeOdometerText => L("შეცვლა", "Change");
    public string OdometerLineText => L($"გარბენი: {OdometerValueText}", $"Odometer: {OdometerValueText}");

    public string HelpMenuTitle => L("როგორ განვაულო მენიუდან", "How to reset from the menu");
    public string HelpMenuText => L("ნაბიჯ-ნაბიჯ ინსტრუქცია ამ მანქანისთვის", "Step-by-step guide for this car");
    public string HelpSearchTitle => L("ზეთის % არ ჩანს?", "Oil % not shown?");
    public string HelpSearchText => L("ძებნა — აპი ყველა ცნობილ გზას სცდის", "Search — the app tries every known way");
    public string ManualSearchCaption => L("ხელით შეყვანა", "Enter manually");
    public string ManualSearchButtonText => L("ძებნა", "Search");
    public string ManualSearchHint => L(
        "ზუსტი ციფრი არ არის საჭირო: ახლო მნიშვნელობებიც ჩაითვლება (ზეთი ±2%) — მაგ. 84 იპოვის 82–86-საც. 100-ზე მეტი ციფრი კილომეტრად ითვლება.",
        "The exact number is not needed: close values count (oil ±2 %), so 84 also finds 82 to 86. A number above 100 is read as a distance.");
    public double ManualSearchChevronRotation => ShowSearch ? 180 : 0;

    partial void OnShowSearchChanged(bool value) => OnPropertyChanged(nameof(ManualSearchChevronRotation));

    public string HelpRecorderTitle => L("რესეტი აპიდან ჯერ არ არის?", "No reset in the app yet?");
    public string HelpRecorderText => L("დაგვეხმარე — ჩაწერე, როგორ ნულდება", "Help us — record how it resets");

    /// <summary>"≈ 3 200 km to the next service", when the car reports it.</summary>
    public string OilDueText
    {
        get
        {
            var distance = _lastServiceReadings.FirstOrDefault(reading => reading.Kind == ServiceMetricKind.DistanceToNextService);
            if (distance is null || distance.NumericValue <= 0)
                return string.Empty;
            var km = distance.Unit.Trim().ToLowerInvariant() is "mi" or "mile" or "miles"
                ? distance.NumericValue * MaintenanceText.KmPerMile
                : distance.NumericValue;
            return L($"≈ {MaintenanceText.Km(km)} შემდეგ სერვისამდე", $"≈ {MaintenanceText.Km(km)} to the next service");
        }
    }

    public bool HasOilDue => !string.IsNullOrWhiteSpace(OilDueText);

    /// <summary>
    /// These are plain get-only properties (computed from <c>L(ka, en)</c> on every access), not
    /// <c>[ObservableProperty]</c> fields, so switching language while this page is open would otherwise leave
    /// them showing the old language: nothing tells the binding to re-read them. Called from
    /// <c>RefreshLanguageAsync</c> (the existing LanguageChanged handler) alongside <c>ApplyLanguageTexts()</c>.
    /// </summary>
    private void NotifySimplePageTextsChanged()
    {
        OnPropertyChanged(nameof(OilCardTitle));
        OnPropertyChanged(nameof(OilSourceText));
        OnPropertyChanged(nameof(ResetOilButtonText));
        OnPropertyChanged(nameof(RemindersTitle));
        OnPropertyChanged(nameof(HelpTitle));
        OnPropertyChanged(nameof(ChangeOdometerText));
        OnPropertyChanged(nameof(OdometerLineText));
        OnPropertyChanged(nameof(HelpMenuTitle));
        OnPropertyChanged(nameof(HelpMenuText));
        OnPropertyChanged(nameof(ManualSearchCaption));
        OnPropertyChanged(nameof(ManualSearchButtonText));
        OnPropertyChanged(nameof(ManualSearchHint));
        OnPropertyChanged(nameof(HelpSearchTitle));
        OnPropertyChanged(nameof(HelpSearchText));
        OnPropertyChanged(nameof(HelpRecorderTitle));
        OnPropertyChanged(nameof(HelpRecorderText));
        OnPropertyChanged(nameof(OilDueText));
        OnPropertyChanged(nameof(HasOilDue));

        // Same issue in the other two ServiceViewModel partials (Parts.cs, Procedures.cs): plain get-only
        // L(ka, en) properties with no backing field, so they need the same explicit notification.
        OnPropertyChanged(nameof(OdometerCaptionText));
        OnPropertyChanged(nameof(PartsSetupText));
        OnPropertyChanged(nameof(PartsNoteText));
        OnPropertyChanged(nameof(ProceduresTitleText));
        OnPropertyChanged(nameof(ProcedureSearchPlaceholder));
        OnPropertyChanged(nameof(ProcedureAllText));
    }

    partial void OnOdometerValueTextChanged(string value) => OnPropertyChanged(nameof(OdometerLineText));

    partial void OnOilLifeValueTextChanged(string value)
    {
        OnPropertyChanged(nameof(OilDueText));
        OnPropertyChanged(nameof(HasOilDue));
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        ShowSearch = !ShowSearch;
        if (ShowSearch)
            ShowMenuGuide = false;
    }

    [RelayCommand]
    private void ToggleMenuGuide()
    {
        if (!ShowMenuGuide)
            MenuGuideText = ManualResetGuide();
        ShowMenuGuide = !ShowMenuGuide;
        if (ShowMenuGuide)
            ShowSearch = false;
    }
}
