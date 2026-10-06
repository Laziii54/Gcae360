using System.Collections.ObjectModel;
using CarScanner.AI;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// AI consultation after a pre-purchase check. The AI already has the full
/// check result and writes its assessment as soon as the page opens.
/// </summary>
public partial class PrePurchaseAiViewModel : BaseViewModel
{
    private readonly PrePurchaseCheckService _check;
    private readonly AiDiagnosticService _ai;
    private readonly LocalizationService _localization;
    private PrePurchaseResult? _started;

    public ObservableCollection<PrePurchaseChatMessage> Messages { get; } = new();

    [ObservableProperty] private string input = string.Empty;
    [ObservableProperty] private bool isThinking;
    [ObservableProperty] private string thinkingText = "AI აანალიზებს…";
    [ObservableProperty] private string pageTitle = "AI კონსულტაცია";
    [ObservableProperty] private string subtitle = "AI-მ უკვე იცის ამ შემოწმების ყველა შედეგი";
    [ObservableProperty] private string placeholder = "დაწერე კითხვა…";
    [ObservableProperty] private string sendText = "გაგზავნა";

    public PrePurchaseAiViewModel(PrePurchaseCheckService check, AiDiagnosticService ai, LocalizationService localization)
    {
        _check = check;
        _ai = ai;
        _localization = localization;
        Title = "AI consultation";
        ApplyLanguage();
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyLanguage);
    }

    private bool English => _localization.IsEnglish;

    private void ApplyLanguage()
    {
        PageTitle = English ? "AI consultation" : "AI კონსულტაცია";
        Subtitle = English ? "The AI already knows every result of this check" : "AI-მ უკვე იცის ამ შემოწმების ყველა შედეგი";
        Placeholder = English ? "Type a question…" : "დაწერე კითხვა…";
        SendText = English ? "Send" : "გაგზავნა";
        ThinkingText = English ? "AI is analyzing…" : "AI აანალიზებს…";
    }

    /// <summary>Writes the first assessment automatically (once per check).</summary>
    public async Task StartAsync()
    {
        var result = _check.LastResult;
        if (result is null || ReferenceEquals(result, _started))
            return;

        _started = result;
        Messages.Clear();
        var question = English
            ? "Give a short, clear assessment of this car for someone who is about to buy it: is its technical condition good, what does each problem found mean, how serious is it, and what should a mechanic check before buying."
            : "მომეცი მოკლე, გასაგები შეფასება ამ მანქანის შესახებ ადამიანისთვის, რომელიც მის ყიდვას აპირებს: როგორია ტექნიკური მდგომარეობა, რას ნიშნავს თითოეული ნაპოვნი პრობლემა, რამდენად სერიოზულია და რა შეამოწმოს ხელოსანმა ყიდვამდე.";
        await AskAsync(result, question, showQuestion: false);
    }

    [RelayCommand]
    private async Task Send()
    {
        try
        {
        var text = Input?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsThinking)
            return;

        var result = _check.LastResult;
        if (result is null)
            return;

        Input = string.Empty;
        await AskAsync(result, text, showQuestion: true);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "PrePurchaseAiViewModel.Send");
        }
    }

    private async Task AskAsync(PrePurchaseResult result, string question, bool showQuestion)
    {
        if (showQuestion)
            Messages.Add(new PrePurchaseChatMessage { Text = question, IsUser = true });

        var history = Messages
            .Take(Math.Max(0, Messages.Count - (showQuestion ? 1 : 0)))
            .Select(message => (message.IsUser ? "User: " : "AI: ") + message.Text)
            .ToList();

        var prompt = result.AiContext + "\n" + Rules(English) + "\n\nQUESTION:\n" + question;

        try
        {
            IsThinking = true;
            var reply = await _ai.ChatAsync(
                result.Report.Vehicle,
                BuildCodes(result.Report),
                result.Report.Sensors,
                history,
                prompt);

            Messages.Add(new PrePurchaseChatMessage
            {
                Text = string.IsNullOrWhiteSpace(reply)
                    ? (English ? "The AI did not return an answer. Try again." : "AI-მ პასუხი არ დააბრუნა. სცადე თავიდან.")
                    : reply.Trim()
            });
        }
        catch
        {
            Messages.Add(new PrePurchaseChatMessage
            {
                Text = English
                    ? "Could not get the AI answer. Check the internet connection and try again."
                    : "AI პასუხი ვერ მივიღეთ. შეამოწმე ინტერნეტი და სცადე თავიდან."
            });
            if (!showQuestion)
                _started = null; // allow the automatic assessment again next time
        }
        finally
        {
            IsThinking = false;
        }
    }

    private static string Rules(bool english) => $"""
PRE-PURCHASE CONSULTATION RULES:
- The user is thinking about buying this car. Base every statement only on the check result above.
- Do NOT talk about the car's price, its market value, bargaining or negotiation, and do not give repair cost estimates,
  unless the user explicitly asks about price or cost in their question. Only then answer that part.
- Do not invent faults. If a part of the check was not available, say it could not be checked.
- Keep it short and practical, in plain words for an ordinary driver.
- Answer in {(english ? "English" : "Georgian")}.
""";

    private static List<ErrorCode> BuildCodes(ObdScanReport report)
    {
        var codes = new List<ErrorCode>();
        foreach (var (code, status) in report.StoredCodes.Select(code => (code, "Stored"))
                     .Concat(report.PendingCodes.Select(code => (code, "Pending")))
                     .Concat(report.PermanentCodes.Select(code => (code, "Permanent"))))
        {
            codes.Add(new ErrorCode
            {
                Code = code.Code,
                TitleEn = code.Description,
                DescriptionEn = code.Description,
                Status = status
            });
        }

        foreach (var module in report.Modules.Where(module => module.Responded))
        {
            foreach (var code in module.Codes)
            {
                codes.Add(new ErrorCode
                {
                    Code = code.Code,
                    TitleKa = code.TitleKa,
                    TitleEn = string.IsNullOrWhiteSpace(code.TitleEn) ? $"{module.NameEn} module fault" : $"{module.NameEn}: {code.TitleEn}",
                    DescriptionEn = $"Module: {module.NameEn}",
                    Status = code.Active ? "Active" : code.Pending ? "Pending" : "History"
                });
            }
        }

        return codes;
    }
}
