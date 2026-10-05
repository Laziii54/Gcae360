using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.OemDiagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

/// <summary>
/// Technical inspection self-check: the user answers the nine official categories
/// question by question, the app reads the OBD part itself, then a combined verdict is shown.
/// </summary>
public partial class TechnicalInspectionViewModel : BaseViewModel
{
    private const string Ok = "#1F8A4C";
    private const string Bad = "#B3361F";
    private const string Warn = "#C98A1A";
    private const string Muted = "#94A3B8";

    private readonly TechnicalInspectionService _inspection;
    private readonly LocalizationService _localization;
    private readonly ProService _pro;
    private readonly ModuleDtcScanService _modules;
    private readonly Dictionary<string, InspectionAnswer> _answers;
    private TechnicalInspectionResult? _lastResult;
    private bool _obdFailed;

    private IReadOnlyList<InspectionQuestion> _currentQuestions = Array.Empty<InspectionQuestion>();
    private int _currentCategory;
    private int _questionIndex;

    public ObservableCollection<TechnicalInspectionIssueDisplay> Issues { get; } = new();
    public ObservableCollection<InspectionCategoryRow> Categories { get; } = new();

    // ---------- verdict / OBD (existing bindings) ----------
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool hasIssues;
    [ObservableProperty] private string pageHeading = string.Empty;
    [ObservableProperty] private string pageSubtitle = string.Empty;
    [ObservableProperty] private string checkButtonText = string.Empty;
    [ObservableProperty] private string resultAnswer = "—";
    [ObservableProperty] private string resultTitle = string.Empty;
    [ObservableProperty] private string resultExplanation = string.Empty;
    [ObservableProperty] private string resultColor = Muted;
    [ObservableProperty] private string issuesHeading = string.Empty;
    [ObservableProperty] private string physicalHeading = string.Empty;
    [ObservableProperty] private string physicalText = string.Empty;
    [ObservableProperty] private string disclaimerText = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;

    // ---------- hub ----------
    [ObservableProperty] private bool isHubMode = true;
    [ObservableProperty] private string overallProgressText = string.Empty;
    [ObservableProperty] private double overallProgress;
    [ObservableProperty] private string categoriesCaption = string.Empty;
    [ObservableProperty] private string obdRowTitle = string.Empty;
    [ObservableProperty] private string obdRowStatus = string.Empty;
    [ObservableProperty] private string obdRowColor = Muted;
    [ObservableProperty] private string continueButtonText = string.Empty;
    [ObservableProperty] private string resetButtonText = string.Empty;
    [ObservableProperty] private string resultCaption = string.Empty;
    [ObservableProperty] private string moduleRowTitle = string.Empty;
    [ObservableProperty] private string moduleRowStatus = string.Empty;
    [ObservableProperty] private string moduleRowColor = Muted;

    // ---------- question ----------
    [ObservableProperty] private bool isQuestionMode;
    [ObservableProperty] private string backToListText = string.Empty;
    [ObservableProperty] private string questionCategoryTitle = string.Empty;
    [ObservableProperty] private string questionProgressText = string.Empty;
    [ObservableProperty] private double questionProgress;
    [ObservableProperty] private string questionText = string.Empty;
    [ObservableProperty] private string severityText = string.Empty;
    [ObservableProperty] private string severityColor = Bad;
    [ObservableProperty] private string howLabel = string.Empty;
    [ObservableProperty] private string howText = string.Empty;
    [ObservableProperty] private string answerOkText = string.Empty;
    [ObservableProperty] private string answerProblemText = string.Empty;
    [ObservableProperty] private string answerUnknownText = string.Empty;
    [ObservableProperty] private string previousText = string.Empty;
    [ObservableProperty] private bool canGoBack;
    [ObservableProperty] private bool isOkSelected;
    [ObservableProperty] private bool isProblemSelected;
    [ObservableProperty] private bool isUnknownSelected;

    /// <summary>Raised when the page should scroll to the top (new question or back to the list).</summary>
    public event EventHandler? ScrollToTopRequested;

    public TechnicalInspectionViewModel(
        TechnicalInspectionService inspection,
        LocalizationService localization,
        ProService pro,
        ModuleDtcScanService modules)
    {
        _inspection = inspection;
        _localization = localization;
        _pro = pro;
        _modules = modules;
        _answers = InspectionAnswerStore.Load();
        _localization.LanguageChanged += OnLanguageChanged;
        UpdateLanguage();
        RebuildAll();
    }

    private bool En => _localization.IsEnglish;
    private string L(string ka, string en) => En ? en : ka;

    // =====================================================================
    // OBD part
    // =====================================================================

    [RelayCommand]
    private async Task Check()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            _obdFailed = false;
            ObdRowStatus = L("მოწმდება…", "Checking…");
            ObdRowColor = Muted;
            StatusText = L(
                "მოწმდება Check Engine, შეცდომის კოდები და მზადყოფნის მონიტორები…",
                "Reading MIL, trouble codes and readiness monitors…");

            _lastResult = await _inspection.CheckAsync();
        }
        catch (Exception ex)
        {
            _lastResult = null;
            _obdFailed = true;
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RebuildAll();
        }
    }

    // =====================================================================
    // ABS / airbag modules (PRO). Without PRO this step is skipped and
    // reported at the end as one item left to check.
    // =====================================================================

    private enum ModuleState { NotPro, NotScanned, NotFound, Clean, Faults }

    private ModuleState _moduleState = ModuleState.NotPro;
    private List<(ModuleDtcReport Module, ModuleDtc Code)> _moduleFaults = new();

    private static bool IsSafetyModule(ModuleDtcReport m) =>
        m.ModuleId.Equals("ABS", StringComparison.OrdinalIgnoreCase) ||
        m.ModuleId.Equals("SRS", StringComparison.OrdinalIgnoreCase);

    private void EvaluateModules()
    {
        _moduleFaults = new();
        if (!_pro.HasObd)
        {
            _moduleState = ModuleState.NotPro;
            return;
        }

        if (_modules.LastScanAt is null)
        {
            _moduleState = ModuleState.NotScanned;
            return;
        }

        var safety = _modules.LastModules.Where(m => m.Responded && IsSafetyModule(m)).ToList();
        if (safety.Count == 0)
        {
            _moduleState = ModuleState.NotFound;
            return;
        }

        _moduleFaults = safety.SelectMany(m => m.Codes.Select(c => (m, c))).ToList();
        _moduleState = _moduleFaults.Count > 0 ? ModuleState.Faults : ModuleState.Clean;
    }

    /// <summary>Called when the page appears (e.g. after returning from the module scan).</summary>
    public void RefreshModules()
    {
        if (IsQuestionMode)
            return;
        RebuildAll();
    }

    [RelayCommand]
    private async Task OpenModules()
    {
        try
        {
            var route = _pro.HasObd ? nameof(Views.ModuleScanPage) : nameof(Views.ProPage);
            await Shell.Current.GoToAsync(route);
        }
        catch
        {
            // Navigation is optional here.
        }
    }

    private void RebuildModuleRow()
    {
        (ModuleRowStatus, ModuleRowColor) = _moduleState switch
        {
            ModuleState.NotPro => ("PRO", Warn),
            ModuleState.NotScanned => (L("დასაწყები", "Not run"), Muted),
            ModuleState.NotFound => (L("ვერ მოიძებნა", "Not found"), Warn),
            ModuleState.Clean => (L("რიგზეა", "OK"), Ok),
            _ => (L($"{_moduleFaults.Count} შეცდომა", $"{_moduleFaults.Count} fault(s)"), Bad)
        };
    }

    private TechnicalInspectionIssue ModulesNotCheckedIssue() => new()
    {
        TitleKa = "ABS და აირბეგის მოდულები ვერ შემოწმდა",
        TitleEn = "ABS and airbag modules were not checked",
        CategoryKa = "2. სამუხრუჭე სისტემა · 8. უსაფრთხოება",
        CategoryEn = "2. Brakes · 8. Safety equipment",
        SeverityKa = "შესამოწმებელი",
        SeverityEn = "To verify",
        ImpactKa = _moduleState == ModuleState.NotPro
            ? "ABS-ის და აირბეგის მოდულების შეცდომები PRO ვერსიით იკითხება, ამიტომ ეს ნაბიჯი გამოტოვდა."
            : _moduleState == ModuleState.NotFound
                ? "მოდულების დიაგნოსტიკამ ABS ან აირბეგის მოდული ვერ იპოვა."
                : "მოდულების დიაგნოსტიკა ჯერ არ გაშვებულა.",
        ImpactEn = _moduleState == ModuleState.NotPro
            ? "ABS and airbag module faults are read with PRO, so this step was skipped."
            : _moduleState == ModuleState.NotFound
                ? "The module scan did not find an ABS or airbag module."
                : "The module scan has not been run yet.",
        CheckKa = "დარწმუნდი, რომ ABS-ის და აირბეგის ნათურები ძრავის ჩართვის შემდეგ ქრება (კითხვები 2 და 8).",
        CheckEn = "Make sure the ABS and airbag lights go out after the engine starts (questions in 2 and 8).",
        FixKa = _moduleState == ModuleState.NotPro
            ? "ზუსტი შემოწმებისთვის გამოიყენე PRO მოდულების დიაგნოსტიკა."
            : "გაუშვი მოდულების დიაგნოსტიკა და დაბრუნდი ამ გვერდზე.",
        FixEn = _moduleState == ModuleState.NotPro
            ? "Use PRO module diagnostics for an exact check."
            : "Run the module scan and come back to this page.",
        Color = Warn
    };

    private TechnicalInspectionIssue ModuleFaultIssue(ModuleDtcReport module, ModuleDtc code)
    {
        var airbag = module.ModuleId.Equals("SRS", StringComparison.OrdinalIgnoreCase);
        return new TechnicalInspectionIssue
        {
            Code = code.DisplayCode,
            TitleKa = string.IsNullOrWhiteSpace(code.TitleKa) ? $"{module.NameKa}: შეცდომა {code.Code}" : code.TitleKa,
            TitleEn = string.IsNullOrWhiteSpace(code.TitleEn) ? $"{module.NameEn}: fault {code.Code}" : code.TitleEn,
            CategoryKa = airbag ? $"8. უსაფრთხოება · {module.NameKa}" : $"2. სამუხრუჭე სისტემა · {module.NameKa}",
            CategoryEn = airbag ? $"8. Safety equipment · {module.NameEn}" : $"2. Brakes · {module.NameEn}",
            SeverityKa = "მნიშვნელოვანი",
            SeverityEn = "Major",
            ImpactKa = airbag
                ? "აირბეგის შეცდომით გამაფრთხილებელი ნათურა ანთია — ტექდათვალიერებას ვერ გაივლის."
                : "ABS-ის შეცდომით გამაფრთხილებელი ნათურა ანთია — ტექდათვალიერებას ვერ გაივლის.",
            ImpactEn = airbag
                ? "An airbag fault keeps the warning light on — the car will not pass."
                : "An ABS fault keeps the warning light on — the car will not pass.",
            CheckKa = "გახსენი მოდულების დიაგნოსტიკა და ნახე კოდის დეტალები.",
            CheckEn = "Open module diagnostics to see the code details.",
            FixKa = "შეაკეთე მიზეზი და შემდეგ წაშალე კოდი.",
            FixEn = "Repair the cause, then clear the code.",
            Color = Bad
        };
    }

    // =====================================================================
    // Question flow
    // =====================================================================

    [RelayCommand]
    private void OpenCategory(InspectionCategoryRow? row)
    {
        if (row is null)
            return;

        var questions = InspectionChecklist.ForCategory(row.Index);
        if (questions.Count == 0)
            return;

        _currentCategory = row.Index;
        _currentQuestions = questions;
        var firstOpen = questions.ToList().FindIndex(q => AnswerOf(q) == InspectionAnswer.None);
        _questionIndex = firstOpen < 0 ? 0 : firstOpen;
        IsHubMode = false;
        IsQuestionMode = true;
        ShowQuestion();
    }

    [RelayCommand]
    private void Continue()
    {
        // Next category that still has unanswered questions; otherwise the OBD part; otherwise the result.
        var next = Categories.FirstOrDefault(c => c.Answered < c.Total);
        if (next is not null)
        {
            OpenCategory(next);
            return;
        }

        if (_lastResult is null && !IsBusy)
        {
            CheckCommand.Execute(null);
            return;
        }

        ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand] private void AnswerOk() => Answer(InspectionAnswer.Ok);
    [RelayCommand] private void AnswerProblem() => Answer(InspectionAnswer.Problem);
    [RelayCommand] private void AnswerUnknown() => Answer(InspectionAnswer.Unknown);

    private void Answer(InspectionAnswer answer)
    {
        if (_questionIndex < 0 || _questionIndex >= _currentQuestions.Count)
            return;

        _answers[_currentQuestions[_questionIndex].Id] = answer;
        InspectionAnswerStore.Save(_answers);

        if (_questionIndex < _currentQuestions.Count - 1)
        {
            _questionIndex++;
            ShowQuestion();
            return;
        }

        // Category finished: move straight on to the next unfinished one.
        RebuildAll();
        var next = Categories.FirstOrDefault(c => c.Answered < c.Total && c.Index != _currentCategory);
        if (next is not null)
        {
            OpenCategory(next);
            return;
        }

        CloseQuestion();
    }

    [RelayCommand]
    private void PreviousQuestion()
    {
        if (_questionIndex <= 0)
            return;
        _questionIndex--;
        ShowQuestion();
    }

    [RelayCommand]
    private void CloseQuestion()
    {
        IsQuestionMode = false;
        IsHubMode = true;
        RebuildAll();
        ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ResetAnswers()
    {
        _answers.Clear();
        InspectionAnswerStore.Clear();
        _lastResult = null;
        _obdFailed = false;
        RebuildAll();
    }

    private InspectionAnswer AnswerOf(InspectionQuestion q) =>
        _answers.TryGetValue(q.Id, out var a) ? a : InspectionAnswer.None;

    private void ShowQuestion()
    {
        var q = _currentQuestions[_questionIndex];
        var category = InspectionChecklist.Categories.First(c => c.Index == _currentCategory);
        QuestionCategoryTitle = $"{category.Index}. {(En ? category.En : category.Ka)}";
        QuestionProgressText = L(
            $"კითხვა {_questionIndex + 1} / {_currentQuestions.Count}",
            $"Question {_questionIndex + 1} / {_currentQuestions.Count}");
        QuestionProgress = (_questionIndex + 1) / (double)_currentQuestions.Count;
        QuestionText = En ? q.En : q.Ka;
        HowText = En ? q.HowEn : q.HowKa;
        (SeverityText, SeverityColor) = SeverityLabel(q.Severity);
        CanGoBack = _questionIndex > 0;

        var answer = AnswerOf(q);
        IsOkSelected = answer == InspectionAnswer.Ok;
        IsProblemSelected = answer == InspectionAnswer.Problem;
        IsUnknownSelected = answer == InspectionAnswer.Unknown;

        ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
    }

    private (string Text, string Color) SeverityLabel(InspectionSeverity severity) => severity switch
    {
        InspectionSeverity.Dangerous => (L("სახიფათო", "Dangerous"), Bad),
        InspectionSeverity.Major => (L("მნიშვნელოვანი", "Major"), Bad),
        _ => (L("მცირე", "Minor"), Warn)
    };

    // =====================================================================
    // Hub + verdict
    // =====================================================================

    private void RebuildAll()
    {
        EvaluateModules();
        RebuildModuleRow();
        RebuildCategories();
        RebuildObdRow();
        RebuildVerdict();
    }

    private void RebuildCategories()
    {
        Categories.Clear();
        foreach (var category in InspectionChecklist.Categories)
        {
            var questions = InspectionChecklist.ForCategory(category.Index);
            var answered = questions.Count(q => AnswerOf(q) != InspectionAnswer.None);
            var problems = questions.Count(q => AnswerOf(q) == InspectionAnswer.Problem);
            var unknown = questions.Count(q => AnswerOf(q) == InspectionAnswer.Unknown);

            string status, color;
            if (answered == 0)
            {
                (status, color) = ("—", Muted);
            }
            else if (problems > 0)
            {
                status = L($"{problems} პრობლემა", problems == 1 ? "1 problem" : $"{problems} problems");
                color = Bad;
            }
            else if (answered < questions.Count)
            {
                (status, color) = ($"{answered} / {questions.Count}", Muted);
            }
            else if (unknown > 0)
            {
                (status, color) = (L("გადასამოწმებელი", "To verify"), Warn);
            }
            else
            {
                (status, color) = (L("რიგზეა", "OK"), Ok);
            }

            Categories.Add(new InspectionCategoryRow(
                category.Index,
                $"{category.Index}. {(En ? category.En : category.Ka)}",
                category.IconPath,
                status,
                color,
                answered,
                questions.Count));
        }

        var modulesDone = _moduleState is ModuleState.NotPro or ModuleState.Clean or ModuleState.Faults or ModuleState.NotFound;
        var doneCategories = Categories.Count(c => c.Answered == c.Total) + (_lastResult is not null ? 1 : 0) + (modulesDone ? 1 : 0);
        var steps = Categories.Count + 2;
        OverallProgress = doneCategories / (double)steps;
        OverallProgressText = L($"შევსებულია {doneCategories} / {steps}", $"Completed {doneCategories} / {steps}");

        var allAnswered = Categories.All(c => c.Answered == c.Total);
        ContinueButtonText = !allAnswered
            ? (Categories.Any(c => c.Answered > 0) ? L("გაგრძელება", "Continue") : L("დაწყება", "Start"))
            : _lastResult is null
                ? L("OBD შემოწმება", "Run OBD check")
                : L("დასკვნის ნახვა", "See the result");
    }

    private void RebuildObdRow()
    {
        if (IsBusy)
            return;

        if (_obdFailed)
        {
            (ObdRowStatus, ObdRowColor) = (L("ვერ წაიკითხა", "Failed"), Warn);
            return;
        }

        (ObdRowStatus, ObdRowColor) = _lastResult?.Verdict switch
        {
            null => (L("დასაწყები", "Not run"), Muted),
            TechnicalInspectionVerdict.Pass => (L("რიგზეა", "OK"), Ok),
            TechnicalInspectionVerdict.Unavailable => (L("ვერ წაიკითხა", "No data"), Warn),
            TechnicalInspectionVerdict.Dangerous => (L("სახიფათო", "Dangerous"), Bad),
            _ => (L($"{_lastResult!.Issues.Count} პრობლემა", $"{_lastResult!.Issues.Count} problem(s)"), Bad)
        };

        if (_lastResult is not null && _lastResult.SupportedMonitorCount > 0)
        {
            StatusText = L(
                $"OBD: მზადყოფნის მონიტორები {_lastResult.CompleteMonitorCount}/{_lastResult.SupportedMonitorCount} დასრულებულია",
                $"OBD: readiness {_lastResult.CompleteMonitorCount}/{_lastResult.SupportedMonitorCount} complete");
        }
    }

    private void RebuildVerdict()
    {
        Issues.Clear();

        var problems = InspectionChecklist.Questions.Where(q => AnswerOf(q) == InspectionAnswer.Problem).ToList();
        var unknown = InspectionChecklist.Questions.Where(q => AnswerOf(q) == InspectionAnswer.Unknown).ToList();
        var unanswered = InspectionChecklist.Questions.Count(q => AnswerOf(q) == InspectionAnswer.None);

        // Problems first (dangerous → major → minor), then OBD findings, then items to verify.
        foreach (var q in problems.OrderByDescending(q => q.Severity))
            Issues.Add(new TechnicalInspectionIssueDisplay(ChecklistIssue(q, false), En));
        if (_lastResult is not null)
        {
            foreach (var issue in _lastResult.Issues)
                Issues.Add(new TechnicalInspectionIssueDisplay(issue, En));
        }
        foreach (var (module, code) in _moduleFaults)
            Issues.Add(new TechnicalInspectionIssueDisplay(ModuleFaultIssue(module, code), En));
        var modulesUnchecked = _moduleState is ModuleState.NotPro or ModuleState.NotScanned or ModuleState.NotFound;
        foreach (var q in unknown)
            Issues.Add(new TechnicalInspectionIssueDisplay(ChecklistIssue(q, true), En));
        if (modulesUnchecked)
            Issues.Add(new TechnicalInspectionIssueDisplay(ModulesNotCheckedIssue(), En));
        HasIssues = Issues.Count > 0;

        var anyActivity = _answers.Count > 0 || _lastResult is not null || _obdFailed;
        HasResult = anyActivity;
        if (!anyActivity)
            return;

        var dangerous = problems.Any(q => q.Severity == InspectionSeverity.Dangerous) ||
                        _lastResult?.Verdict == TechnicalInspectionVerdict.Dangerous;
        var fails = problems.Any(q => q.Severity >= InspectionSeverity.Major) ||
                    _lastResult?.Verdict == TechnicalInspectionVerdict.Fail ||
                    _moduleFaults.Count > 0;
        var majorCount = problems.Count(q => q.Severity >= InspectionSeverity.Major) +
                         (_lastResult?.Verdict is TechnicalInspectionVerdict.Fail or TechnicalInspectionVerdict.Dangerous
                             ? _lastResult!.Issues.Count
                             : 0) +
                         _moduleFaults.Count;
        var okCount = InspectionChecklist.Questions.Count(q => AnswerOf(q) == InspectionAnswer.Ok);
        var obdMissing = _lastResult is null || _lastResult.Verdict == TechnicalInspectionVerdict.Unavailable;

        if (dangerous)
        {
            (ResultAnswer, ResultColor) = (L("არა", "NO"), Bad);
            ResultTitle = L("სახიფათო ხარვეზი", "DANGEROUS FAULT");
            ResultExplanation = L(
                "ტექდათვალიერებას ვერ გაივლის. სახიფათო ხარვეზით მოძრაობა არ არის რეკომენდებული — ჯერ შეაკეთე.",
                "It will not pass. Driving with a dangerous fault is not recommended — repair it first.");
        }
        else if (fails)
        {
            (ResultAnswer, ResultColor) = (L("არა", "NO"), Bad);
            ResultTitle = L("სავარაუდოდ ვერ გაივლის", "UNLIKELY TO PASS");
            ResultExplanation = L(
                $"{majorCount} მნიშვნელოვანი ხარვეზი. გამოასწორე ქვემოთ ჩამოთვლილი და შემოწმება გაიმეორე.",
                $"{majorCount} major defect(s). Fix the items below and check again.");
        }
        else if (unanswered > 0 || unknown.Count > 0 || obdMissing)
        {
            (ResultAnswer, ResultColor) = ("?", Warn);
            ResultTitle = L("ჯერ არ დასრულებულა", "NOT FINISHED YET");
            var parts = new List<string>();
            if (unanswered > 0) parts.Add(L($"უპასუხო კითხვა: {unanswered}", $"unanswered: {unanswered}"));
            if (unknown.Count > 0) parts.Add(L($"გადასამოწმებელი: {unknown.Count}", $"to verify: {unknown.Count}"));
            if (obdMissing) parts.Add(L("OBD შემოწმება", "OBD check"));
            ResultExplanation = L(
                $"ჯერჯერობით ხარვეზი არ ჩანს. დარჩა — {string.Join(", ", parts)}.",
                $"No defect so far. Remaining — {string.Join(", ", parts)}.");
        }
        else
        {
            (ResultAnswer, ResultColor) = (L("კი", "YES"), Ok);
            ResultTitle = L("სავარაუდოდ გაივლის", "LIKELY TO PASS");
            ResultExplanation = L(
                $"{okCount} პუნქტი რიგზეა და OBD სუფთაა." +
                (problems.Count > 0 ? $" მცირე შენიშვნა: {problems.Count}." : string.Empty) +
                (modulesUnchecked ? " შესამოწმებელი დარჩა 1: ABS და აირბეგის მოდულები." : string.Empty),
                $"{okCount} items are OK and the OBD check is clean." +
                (problems.Count > 0 ? $" Minor notes: {problems.Count}." : string.Empty) +
                (modulesUnchecked ? " 1 item left to check: ABS and airbag modules." : string.Empty));
        }
    }

    private TechnicalInspectionIssue ChecklistIssue(InspectionQuestion q, bool unknown)
    {
        var category = InspectionChecklist.Categories.First(c => c.Index == q.Category);
        var (sevKa, sevEn, color) = unknown
            ? ("შესამოწმებელი", "To verify", Warn)
            : q.Severity switch
            {
                InspectionSeverity.Dangerous => ("სახიფათო", "Dangerous", Bad),
                InspectionSeverity.Major => ("მნიშვნელოვანი", "Major", Bad),
                _ => ("მცირე", "Minor", Warn)
            };

        return new TechnicalInspectionIssue
        {
            TitleKa = unknown ? q.Ka : q.ShortKa,
            TitleEn = unknown ? q.En : q.ShortEn,
            CategoryKa = $"{category.Index}. {category.Ka}",
            CategoryEn = $"{category.Index}. {category.En}",
            SeverityKa = sevKa,
            SeverityEn = sevEn,
            ImpactKa = unknown
                ? "ეს პუნქტი ვერ შემოწმდა, ტექცენტრში კი აუცილებლად შეამოწმებენ."
                : q.Severity switch
                {
                    InspectionSeverity.Dangerous => "სახიფათო ხარვეზი: ტექდათვალიერებას ვერ გაივლის და მოძრაობა სარისკოა.",
                    InspectionSeverity.Major => "მნიშვნელოვანი ხარვეზი: ამით ტექდათვალიერებას ვერ გაივლის.",
                    _ => "მცირე ხარვეზი: ჩაიწერება, მაგრამ გავლას, როგორც წესი, ხელს არ უშლის."
                },
            ImpactEn = unknown
                ? "This item could not be checked, but the center will check it."
                : q.Severity switch
                {
                    InspectionSeverity.Dangerous => "Dangerous defect: the car fails and driving is risky.",
                    InspectionSeverity.Major => "Major defect: the car will not pass with it.",
                    _ => "Minor defect: noted, but usually does not stop a pass."
                },
            CheckKa = q.HowKa,
            CheckEn = q.HowEn,
            FixKa = string.IsNullOrWhiteSpace(q.FixKa) ? "გამოასწორე და ხელახლა შეამოწმე." : q.FixKa,
            FixEn = string.IsNullOrWhiteSpace(q.FixEn) ? "Fix it and check again." : q.FixEn,
            Color = color
        };
    }

    // =====================================================================
    // Language
    // =====================================================================

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLanguage();
        RebuildAll();
        if (IsQuestionMode && _currentQuestions.Count > 0)
            ShowQuestion();
    }

    private void UpdateLanguage()
    {
        Title = L("ტექდათვალიერების შემოწმება", "Technical inspection check");
        PageHeading = L("ტექდათვალიერების შემოწმება", "TECHNICAL INSPECTION CHECK");
        PageSubtitle = L(
            "შეავსე 9 კატეგორია, აპი OBD-ს თავად შეამოწმებს და გეტყვის, გაივლის თუ არა",
            "Answer 9 categories, the app checks the OBD part and tells you if the car will pass");
        CheckButtonText = L("OBD შემოწმება", "Run OBD check");
        IssuesHeading = L("რას სჭირდება ყურადღება", "WHAT NEEDS ATTENTION");
        PhysicalHeading = L("საჭიროა ფიზიკური შემოწმება", "PHYSICAL INSPECTION IS REQUIRED");
        PhysicalText = string.Empty;
        DisclaimerText = L(
            "ეს წინასწარი შეფასებაა და არა 100%-იანი გარანტია. საბოლოო გადაწყვეტილებას ოფიციალური ტექინსპექტირების ცენტრი იღებს.",
            "This is a preliminary assessment, not a 100% guarantee. The official inspection center makes the final decision.");
        StatusText = L("დააკავშირე OBD ადაპტერი OBD ნაწილისთვის.", "Connect the OBD adapter for the OBD part.");

        CategoriesCaption = L("კატეგორიები", "CATEGORIES");
        ObdRowTitle = L("OBD შემოწმება (ავტომატური)", "OBD check (automatic)");
        ResetButtonText = L("თავიდან დაწყება", "Start over");
        ResultCaption = L("დასკვნა", "RESULT");
        ModuleRowTitle = L("ABS და აირბეგის მოდულები", "ABS and airbag modules");

        BackToListText = L("‹ კატეგორიები", "‹ Categories");
        HowLabel = L("როგორ შევამოწმო", "How to check");
        AnswerOkText = L("რიგზეა", "OK");
        AnswerProblemText = L("არის პრობლემა", "There is a problem");
        AnswerUnknownText = L("ვერ შევამოწმე", "Couldn't check");
        PreviousText = L("‹ წინა კითხვა", "‹ Previous question");
    }
}

public sealed class InspectionCategoryRow
{
    public InspectionCategoryRow(int index, string title, string iconPath, string status, string statusColor, int answered, int total)
    {
        Index = index;
        Title = title;
        IconPath = iconPath;
        Status = status;
        StatusColor = statusColor;
        Answered = answered;
        Total = total;
    }

    public int Index { get; }
    public string Title { get; }
    public string IconPath { get; }
    public Microsoft.Maui.Controls.Shapes.Geometry? Icon =>
        new Microsoft.Maui.Controls.Shapes.PathGeometryConverter().ConvertFromInvariantString(IconPath) as Microsoft.Maui.Controls.Shapes.Geometry;
    public string Status { get; }
    public string StatusColor { get; }
    public int Answered { get; }
    public int Total { get; }
}

public sealed class TechnicalInspectionIssueDisplay
{
    public TechnicalInspectionIssueDisplay(TechnicalInspectionIssue issue, bool english)
    {
        Code = issue.Code;
        HasCode = !string.IsNullOrWhiteSpace(issue.Code);
        Title = english ? issue.TitleEn : issue.TitleKa;
        Category = english ? issue.CategoryEn : issue.CategoryKa;
        Severity = english ? issue.SeverityEn : issue.SeverityKa;
        Impact = english ? issue.ImpactEn : issue.ImpactKa;
        Check = english ? issue.CheckEn : issue.CheckKa;
        Fix = english ? issue.FixEn : issue.FixKa;
        ImpactLabel = english ? "Impact" : "გავლენა";
        CheckLabel = english ? "How to check" : "როგორ შეამოწმო";
        FixLabel = english ? "Recommended action" : "რეკომენდაცია";
        Color = issue.Color switch
        {
            "#EF4444" or "#DC2626" => "#B3361F",
            "#F59E0B" => "#C98A1A",
            _ => issue.Color
        };
    }

    public string Code { get; }
    public bool HasCode { get; }
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
