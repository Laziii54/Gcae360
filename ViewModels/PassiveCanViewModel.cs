using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.Passive;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// Passive CAN listening. The adapter only listens; nothing is transmitted. What is heard is compared with the
/// public opendbc databases, and every value shown is labelled as an unverified database match.
/// </summary>
public partial class PassiveCanViewModel(PassiveCanService passive, LocalizationService localization) : BaseViewModel
{
    private CancellationTokenSource? _run;

    [ObservableProperty] private bool isListening;
    [ObservableProperty] private string resultTitle = "";
    [ObservableProperty] private string resultText = "";
    [ObservableProperty] private bool hasResult;

    public ObservableCollection<string> Signals { get; } = new();
    public bool HasSignals => Signals.Count > 0;

    private string L(string ka, string en) => localization.IsEnglish ? en : ka;

    public string PageTitle => L("CAN მოსმენა (მხოლოდ კითხვა)", "CAN listening (read-only)");
    public string IntroTitle => L("მანქანის საკუთარი შეტყობინებები", "The car's own messages");
    public string IntroText => L(
        "აპი მხოლოდ უსმენს, რას გადასცემს მანქანა თვითონ — არაფერს აგზავნის. შედეგი შეადარდება საჯარო opendbc ბაზებს. ბევრ მანქანაზე OBD-სოკეტიდან ეს შეტყობინებები არ ჩანს (გეიტვეი).",
        "The app only listens to what the car broadcasts itself — it sends nothing. The result is compared with the public opendbc databases. On many cars these messages are not visible on the OBD socket (gateway).");
    public string StartText => IsListening ? L("გაუქმება", "Cancel") : L("მოსმენის დაწყება", "Start listening");
    public string NoticeText => L(
        "ყველა მნიშვნელობა არის ბაზასთან შესაძლო დამთხვევა და არ არის დადასტურებული.",
        "Every value is a possible database match and is not verified.");

    partial void OnIsListeningChanged(bool value) => OnPropertyChanged(nameof(StartText));

    [RelayCommand]
    private async Task Start()
    {
        if (IsListening)
        {
            _run?.Cancel();
            return;
        }

        _run = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        IsListening = true;
        HasResult = false;
        Signals.Clear();
        OnPropertyChanged(nameof(HasSignals));
        StatusMessage = L("ვუსმენთ მანქანას…", "Listening to the car…");

        try
        {
            var report = await passive.ListenAsync(null, _run.Token);
            Show(report);
        }
        catch (OperationCanceledException)
        {
            ResultTitle = L("შეჩერდა", "Stopped");
            ResultText = "";
            HasResult = true;
        }
        catch (NotSupportedException)
        {
            ResultTitle = L("ამ მანქანაზე მიუწვდომელია", "Not available on this car");
            ResultText = L("საჭიროა CAN-ზე მომუშავე მანქანა (2008+).", "A CAN car (2008+) is required.");
            HasResult = true;
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "PassiveCanViewModel.Start");
            ResultTitle = L("მოსმენა ვერ შესრულდა", "Listening failed");
            ResultText = L("შეამოწმე ადაპტერის კავშირი და სცადე ხელახლა.", "Check the adapter connection and try again.");
            HasResult = true;
        }
        finally
        {
            IsListening = false;
            StatusMessage = "";
        }
    }

    public void Stop() => _run?.Cancel();

    private void Show(PassiveCanReport report)
    {
        Signals.Clear();
        switch (report.Status)
        {
            case PassiveCanStatus.NoTraffic:
                ResultTitle = L("შეტყობინებები ვერ დავიჭირეთ", "No messages heard");
                ResultText = L("ამ მანქანის OBD-სოკეტზე თვითნაკადი შეტყობინებები არ ჩანს.", "This car does not show its own messages on the OBD socket.");
                break;
            case PassiveCanStatus.OnlyDiagnosticTraffic:
                ResultTitle = L("მხოლოდ დიაგნოსტიკური ტრაფიკია", "Only diagnostic traffic");
                ResultText = L("დახურული გეიტვეის გამო სხვა შეტყობინებები არ ჩანს.", "Other messages are hidden behind a gateway.");
                break;
            case PassiveCanStatus.NoDatabaseMatch:
                ResultTitle = L("შესაბამისი ბაზა არ არის", "No matching database");
                ResultText = string.Format(CultureInfo.InvariantCulture,
                    L("მოვისმინეთ {0} კადრი, {1} სხვადასხვა ID. საჯარო ბაზებში საკმარისი დამთხვევა ვერ ვიპოვეთ — არაფერს ვაჩვენებთ.",
                      "Heard {0} frames, {1} different IDs. No public database matches well enough — nothing is shown."),
                    report.FrameCount, report.DistinctIds);
                break;
            default:
                ResultTitle = L("შესაძლო დამთხვევა (დაუდასტურებელი)", "Possible match (unverified)");
                ResultText = string.Format(CultureInfo.InvariantCulture,
                    L("ბაზა {0}: დაემთხვა {1} ID. ეს არ ნიშნავს, რომ ბაზა ამ მანქანას ეკუთვნის.",
                      "Database {0}: {1} IDs agree. This does not mean the database belongs to this car."),
                    report.Best?.DbcFile, report.Best?.MatchedIds);
                foreach (var signal in report.Signals.Take(80))
                {
                    Signals.Add(string.Format(CultureInfo.InvariantCulture, "0x{0:X} {1}: {2:0.##} {3}",
                        signal.MessageId, signal.Signal, signal.Value, signal.Unit).TrimEnd());
                }
                break;
        }

        OnPropertyChanged(nameof(HasSignals));
        HasResult = true;
    }
}
