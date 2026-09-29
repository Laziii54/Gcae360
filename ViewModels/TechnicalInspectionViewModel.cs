using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class TechnicalInspectionViewModel : BaseViewModel
{
    private readonly TechnicalInspectionService _inspection;
    private readonly LocalizationService _localization;
    private TechnicalInspectionResult? _lastResult;

    public ObservableCollection<TechnicalInspectionIssueDisplay> Issues { get; } = new();

    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool hasIssues;
    [ObservableProperty] private string pageHeading = string.Empty;
    [ObservableProperty] private string pageSubtitle = string.Empty;
    [ObservableProperty] private string checkButtonText = string.Empty;
    [ObservableProperty] private string resultAnswer = "—";
    [ObservableProperty] private string resultTitle = string.Empty;
    [ObservableProperty] private string resultExplanation = string.Empty;
    [ObservableProperty] private string resultColor = "#94A3B8";
    [ObservableProperty] private string issuesHeading = string.Empty;
    [ObservableProperty] private string physicalHeading = string.Empty;
    [ObservableProperty] private string physicalText = string.Empty;
    [ObservableProperty] private string disclaimerText = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;

    public TechnicalInspectionViewModel(
        TechnicalInspectionService inspection,
        LocalizationService localization)
    {
        _inspection = inspection;
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
        UpdateLanguage();
    }

    [RelayCommand]
    private async Task Check()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            HasResult = false;
            Issues.Clear();
            HasIssues = false;
            StatusText = _localization.IsEnglish
                ? "Reading MIL, trouble codes and readiness monitors…"
                : "მოწმდება MIL, შეცდომის კოდები და readiness მონიტორები…";

            _lastResult = await _inspection.CheckAsync();
            ApplyResult();
        }
        catch (Exception ex)
        {
            ResultAnswer = "?";
            ResultColor = "#F59E0B";
            ResultTitle = _localization.IsEnglish ? "CHECK REQUIRED" : "საჭიროა შემოწმება";
            ResultExplanation = _localization.IsEnglish
                ? "The OBD assessment could not be completed. Check the adapter and try again."
                : "OBD შეფასება ვერ დასრულდა. შეამოწმე ადაპტერი და სცადე თავიდან.";
            StatusText = ex.Message;
            HasResult = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResult()
    {
        if (_lastResult is null)
            return;

        var english = _localization.IsEnglish;
        (ResultAnswer, ResultTitle, ResultExplanation, ResultColor) = _lastResult.Verdict switch
        {
            TechnicalInspectionVerdict.Pass => english
                ? ("YES", "LIKELY PASSES THE OBD PORTION", "No OBD issue that would prevent passing was found.", "#22C55E")
                : ("კი", "OBD ნაწილს სავარაუდოდ გაივლის", "OBD-ით შემაფერხებელი პრობლემა არ დაფიქსირდა.", "#22C55E"),
            TechnicalInspectionVerdict.Dangerous => english
                ? ("NO", "DANGEROUS FAULT", "Stop safely and diagnose the fault before continuing to drive.", "#DC2626")
                : ("არა", "სახიფათო გაუმართაობა", "გაჩერდი უსაფრთხო ადგილას და მოძრაობის გაგრძელებამდე შეამოწმე პრობლემა.", "#DC2626"),
            TechnicalInspectionVerdict.Fail => english
                ? ("NO", "UNLIKELY TO PASS THE OBD PORTION", "Fix the items below and run the check again.", "#EF4444")
                : ("არა", "OBD ნაწილს სავარაუდოდ ვერ გაივლის", "აღმოფხვერი ქვემოთ ჩამოთვლილი მიზეზები და შემოწმება გაიმეორე.", "#EF4444"),
            _ => english
                ? ("?", "CHECK REQUIRED", "There is not enough ECU data for an OBD verdict.", "#F59E0B")
                : ("?", "საჭიროა შემოწმება", "OBD შეფასებისთვის ECU-ის მონაცემები საკმარისი არ არის.", "#F59E0B")
        };

        Issues.Clear();
        foreach (var issue in _lastResult.Issues)
            Issues.Add(new TechnicalInspectionIssueDisplay(issue, english));

        HasIssues = Issues.Count > 0;
        StatusText = english
            ? $"Readiness: {_lastResult.CompleteMonitorCount}/{_lastResult.SupportedMonitorCount} complete"
            : $"Readiness: {_lastResult.CompleteMonitorCount}/{_lastResult.SupportedMonitorCount} დასრულებულია";
        HasResult = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLanguage();
        ApplyResult();
    }

    private void UpdateLanguage()
    {
        if (_localization.IsEnglish)
        {
            Title = "Technical inspection check";
            PageHeading = "TECHNICAL INSPECTION CHECK";
            PageSubtitle = "Preliminary OBD check before visiting an inspection center";
            CheckButtonText = "WILL IT PASS INSPECTION?";
            IssuesHeading = "WHAT NEEDS ATTENTION";
            PhysicalHeading = "PHYSICAL INSPECTION IS REQUIRED";
            PhysicalText = "OBD cannot assess brakes and brake-force balance, steering play, suspension wear, tires, windshield and visibility, wipers/washer, lights and their aim, mirrors, seat belts, body/chassis corrosion, smoke, fuel or fluid leaks, or vehicle identification.";
            DisclaimerText = "This is a preliminary OBD assessment, not a 100% guarantee. The official inspection center makes the final decision.";
            StatusText = "Connect the OBD adapter and start the check.";
        }
        else
        {
            Title = "ტექდათვალიერების შემოწმება";
            PageHeading = "ტექდათვალიერების შემოწმება";
            PageSubtitle = "წინასწარი OBD შემოწმება ტექდათვალიერების ცენტრში მისვლამდე";
            CheckButtonText = "გაივლის ტექდათვალიერებას?";
            IssuesHeading = "რას სჭირდება ყურადღება";
            PhysicalHeading = "საჭიროა ფიზიკური შემოწმება";
            PhysicalText = "OBD ვერ ამოწმებს მუხრუჭებსა და დამუხრუჭების ძალის სხვაობას, საჭის თავისუფალ სვლას, დაკიდების მექანიკურ ცვეთას, საბურავებს, საქარე მინასა და ხილვადობას, საწმენდებს/სარეცხს, განათებასა და ფარების მიმართულებას, სარკეებს, უსაფრთხოების ღვედებს, ძარას/შასის ჟანგს, რეალურ კვამლს, საწვავის ან სხვა სითხის გაჟონვას და ავტომობილის იდენტიფიკაციას.";
            DisclaimerText = "ეს არის მხოლოდ წინასწარი OBD შეფასება და არა 100%-იანი გარანტია. საბოლოო გადაწყვეტილებას ოფიციალური ტექდათვალიერების ცენტრი იღებს.";
            StatusText = "დააკავშირე OBD ადაპტერი და დაიწყე შემოწმება.";
        }
    }
}

public sealed class TechnicalInspectionIssueDisplay
{
    public TechnicalInspectionIssueDisplay(TechnicalInspectionIssue issue, bool english)
    {
        Code = issue.Code;
        Title = english ? issue.TitleEn : issue.TitleKa;
        Category = english ? issue.CategoryEn : issue.CategoryKa;
        Severity = english ? issue.SeverityEn : issue.SeverityKa;
        Impact = english ? issue.ImpactEn : issue.ImpactKa;
        Check = english ? issue.CheckEn : issue.CheckKa;
        Fix = english ? issue.FixEn : issue.FixKa;
        ImpactLabel = english ? "Impact" : "გავლენა";
        CheckLabel = english ? "How to check" : "როგორ შეამოწმო";
        FixLabel = english ? "Recommended action" : "რეკომენდაცია";
        Color = issue.Color;
    }

    public string Code { get; }
    public string Title { get; }
    public string Category { get; }
    public string Severity { get; }
    public string Impact { get; }
    public string Check { get; }
    public string Fix { get; }
    public string ImpactLabel { get; }
    public string CheckLabel { get; }
    public string FixLabel { get; }
    public string Color { get; }
}
