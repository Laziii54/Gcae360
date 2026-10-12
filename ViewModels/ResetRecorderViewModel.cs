using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.OemDiagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// "Help us with the reset" — a three-step wizard:
///  1. how do you reset (car menu / workshop tool),
///  2. the app listens while you reset, then you tap Done,
///  3. thank you (or what to try next).
/// The app only listens; nothing is ever sent to the car from this page.
/// </summary>
public partial class ResetRecorderViewModel(
    ResetRecorderService recorder,
    ServiceDataReaderService serviceData,
    VehicleInfoService vehicles,
    LocalizationService localization) : BaseViewModel
{
    private CancellationTokenSource? _stop;
    private CancellationTokenSource? _run;
    private ResetRecorderService.Mode _mode = ResetRecorderService.Mode.MenuReset;

    // Wizard steps
    [ObservableProperty] private bool isChoosing = true;
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool isFinished;

    // Step 2
    [ObservableProperty] private bool isBaseline;
    [ObservableProperty] private string timerText = "0:00";
    [ObservableProperty] private string listeningTitle = "";
    [ObservableProperty] private string instructionTitle = "";
    [ObservableProperty] private string instructionText = "";

    // Step 3
    [ObservableProperty] private bool isSuccess;
    [ObservableProperty] private string resultTitle = "";
    [ObservableProperty] private string resultText = "";
    [ObservableProperty] private string resultColor = "#1F8A4C";
    [ObservableProperty] private string resultBackground = "#E8F5EE";
    [ObservableProperty] private string oilChangeText = "";
    [ObservableProperty] private bool hasOilChange;
    [ObservableProperty] private bool canTryTool;
    [ObservableProperty] private bool showTechnical;

    /// <summary>What is being reset (kept for the server; "Oil" unless set by the caller).</summary>
    [ObservableProperty] private string itemName = "";

    public ObservableCollection<string> Candidates { get; } = new();
    public bool HasCandidates => Candidates.Count > 0;

    private string L(string ka, string en) => localization.IsEnglish ? en : ka;

    // ---------------------------------------------------------------- texts

    public string PageTitle => L("დაგვეხმარე რესეტში", "Help us with the reset");
    public string IntroTitle => L("ამ მანქანაზე რესეტი აპიდან ჯერ არ მუშაობს.", "Reset from the app does not work on this car yet.");
    public string IntroText => L(
        "თუ ერთხელ მენიუდან განულებ, აპი „მოისმენს“, რა ბრძანება გაიარა, და შემდეგ თვითონ შეძლებს. მანქანაში არაფერი იწერება.",
        "Reset it once from the menu and the app “listens” to which command went through — then it can do it itself. Nothing is written to the car.");
    public string HowTitle => L("როგორ ანულებ?", "How do you reset it?");
    public string MenuTitle => L("მანქანის მენიუდან ან ღილაკით", "From the car's menu or a button");
    public string MenuText => L("ჩვეულებრივად, როგორც ყოველთვის", "The usual way, as always");
    public string ToolTitle => L("ხელოსნის აპარატით", "With a workshop tool");
    public string ToolText => L("სერვისში, Y-გადამყვანით (VW, BMW, Mercedes)", "At a workshop, with a Y-splitter (VW, BMW, Mercedes)");
    public string DoneText => L("დავასრულე", "Done");
    public string CancelText => L("გაუქმება", "Cancel");
    public string CloseText => L("დახურვა", "Close");
    public string TryToolText => L("სცადე ხელოსნის აპარატით", "Try with a workshop tool");
    public string OilLabel => L("ზეთი", "Oil");
    public string TechnicalToggleText => ShowTechnical ? L("დამალვა", "Hide") : L("რა ჩაიწერა (ტექნიკური)", "What was recorded (technical)");

    partial void OnShowTechnicalChanged(bool value) => OnPropertyChanged(nameof(TechnicalToggleText));

    // ------------------------------------------------------------ commands

    [RelayCommand]
    private Task StartMenu() => RunAsync(ResetRecorderService.Mode.MenuReset);

    [RelayCommand]
    private Task StartTool() => RunAsync(ResetRecorderService.Mode.ToolSession);

    [RelayCommand]
    private void Done() => _stop?.Cancel();

    [RelayCommand]
    private void Cancel()
    {
        _run?.Cancel();
        _stop?.Cancel();
    }

    [RelayCommand]
    private void ToggleTechnical() => ShowTechnical = !ShowTechnical;

    [RelayCommand]
    private async Task Close()
    {
        try { await CarScanner.Services.SafeNavigation.GoToAsync(".."); }
        catch { }
    }

    public void Stop() => Cancel();

    // ------------------------------------------------------------- flow

    private void ShowStep(int step)
    {
        IsChoosing = step == 1;
        IsRecording = step == 2;
        IsFinished = step == 3;
    }

    private async Task RunAsync(ResetRecorderService.Mode mode)
    {
        if (IsRecording)
            return;

        _mode = mode;
        var vehicle = vehicles.CurrentVehicle ?? new VehicleInfo();
        _stop = new CancellationTokenSource();
        _run = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        Candidates.Clear();
        OnPropertyChanged(nameof(HasCandidates));
        ShowTechnical = false;
        TimerText = "0:00";
        IsBaseline = true;
        ListeningTitle = L("ვემზადებით…", "Getting ready…");
        InstructionTitle = L("ჯერ არაფერს შეეხო", "Touch nothing yet");
        InstructionText = L("რამდენიმე წამით ვუსმენთ მანქანის ჩვეულებრივ „ხმაურს“.", "We listen to the car's normal traffic for a few seconds.");
        ShowStep(2);

        try
        {
            var before = await ReadOilAsync(vehicle, _run.Token);

            var progress = new Progress<ResetRecorderService.Status>(status =>
            {
                TimerText = $"{status.Seconds / 60}:{status.Seconds % 60:00}";
                IsBaseline = status.Phase == "baseline";
                if (status.Phase == "baseline")
                {
                    ListeningTitle = L("ვუსმენთ მანქანას…", "Listening to the car…");
                    InstructionTitle = L("ჯერ არაფერს შეეხო", "Touch nothing yet");
                    InstructionText = L("რამდენიმე წამით ვუსმენთ მანქანის ჩვეულებრივ „ხმაურს“.", "We listen to the car's normal traffic for a few seconds.");
                }
                else if (status.Phase == "record")
                {
                    ListeningTitle = L("ვუსმენთ მანქანას…", "Listening to the car…");
                    InstructionTitle = mode == ResetRecorderService.Mode.MenuReset
                        ? L("ახლა გააკეთე რესეტი მენიუდან", "Now reset from the menu")
                        : L("ახლა გააკეთე რესეტი აპარატით", "Now reset with the tool");
                    InstructionText = L("როცა პანელზე ზეთი განულდება, დააჭირე „დავასრულე“.", "When the dashboard shows the reset, tap “Done”.");
                }
                else
                {
                    ListeningTitle = L("ვამოწმებთ ჩანაწერს…", "Checking the recording…");
                }
            });

            var recording = await recorder.RecordAsync(mode, _stop.Token, progress, _run.Token);
            ListeningTitle = L("ვამოწმებთ ჩანაწერს…", "Checking the recording…");
            var after = await ReadOilAsync(vehicle, _run.Token);

            foreach (var candidate in recording.Candidates)
                Candidates.Add($"{candidate.Id.ToString(recording.Extended29Bit ? "X8" : "X3")}  {candidate.Data}  · {candidate.Kind} · {candidate.Note}");
            OnPropertyChanged(nameof(HasCandidates));

            var item = string.IsNullOrWhiteSpace(ItemName) ? "Oil" : ItemName.Trim();
            var sent = await recorder.UploadAsync(vehicle, recording, item, before, after, _run.Token);

            HasOilChange = before is not null || after is not null;
            OilChangeText = $"{Fmt(before)} → {Fmt(after)}";

            var captured = recording.Candidates.Count > 0;
            IsSuccess = captured && sent;
            CanTryTool = !captured && mode == ResetRecorderService.Mode.MenuReset;
            if (IsSuccess)
            {
                SetResult("#1F8A4C", "#E8F5EE",
                    L("მადლობა!", "Thank you!"),
                    L("ჩანაწერი გაიგზავნა. როცა შევამოწმებთ, „განულება“ ღილაკი ამ მოდელზე ყველასთვის ჩაირთვება.",
                      "The recording was sent. Once we have checked it, the Reset button will be switched on for everyone with this model."));
            }
            else if (!captured)
            {
                SetResult("#B06A00", "#FDF4E3",
                    L("ამჯერად ვერაფერი დავიჭირეთ", "Nothing was caught this time"),
                    mode == ResetRecorderService.Mode.MenuReset
                        ? L("ამ მანქანაზე მენიუს ბრძანება OBD-ში არ ჩანს (ხშირია VW, BMW, Mercedes-ზე). სცადე სერვისში, ხელოსნის აპარატით.",
                            "On this car the menu command is not visible on the OBD socket (common on VW, BMW, Mercedes). Try at a workshop, with their tool.")
                        : L("შეამოწმე, რომ Y-გადამყვანი სწორადაა შეერთებული და აპარატმა რესეტი მართლა გააკეთა.",
                            "Check that the Y-splitter is connected correctly and the tool really did the reset."));
            }
            else
            {
                SetResult("#B06A00", "#FDF4E3",
                    L("ჩაიწერა, მაგრამ ვერ გაიგზავნა", "Recorded, but not sent"),
                    L("შეამოწმე ინტერნეტი და გაიმეორე.", "Check the internet and try again."));
            }

            if (recording.BufferOverflow)
                ResultText += L("\nადაპტერი ყველაფერს ვერ ასწრებდა — უკეთესი ადაპტერით (OBDLink, vLinker) შედეგი უფრო ზუსტია.",
                                "\nThe adapter could not keep up — a better adapter (OBDLink, vLinker) gives a more exact result.");
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user: back to the first step.
            ShowStep(1);
            return;
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            CanTryTool = false;
            HasOilChange = false;
            SetResult("#B3361F", "#FCEDEA", L("ჩაწერა ვერ შესრულდა", "Recording failed"), ex.Message);
        }
        finally
        {
            IsBaseline = false;
        }

        ShowStep(3);
    }

    private void SetResult(string color, string background, string title, string text)
    {
        ResultColor = CarScanner.Services.Localization.ThemeService.SoftHex(color);
        ResultBackground = CarScanner.Services.Localization.ThemeService.SoftHex(background);
        ResultTitle = title;
        ResultText = text;
    }

    private static string Fmt(double? value) => value is { } v ? $"{v:0.#}%" : "—";

    /// <summary>Oil life before / after (read-only, best effort).</summary>
    private async Task<double?> ReadOilAsync(VehicleInfo vehicle, CancellationToken token)
    {
        try
        {
            var read = await serviceData.ReadAsync(vehicle, token);
            return read.Values.FirstOrDefault(r => r.Kind == ServiceMetricKind.OilLifePercent)?.NumericValue;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
