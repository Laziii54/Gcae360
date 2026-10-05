using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One bar of the block / cell chart.</summary>
public sealed record HybridBlockBar(
    int Number,
    string Label,
    double BarHeight,
    double BarWidth,
    string Color,
    bool ShowLabel,
    string ValueText);

/// <summary>One row of the chart.</summary>
public sealed record HybridBarRow(IReadOnlyList<HybridBlockBar> Bars);

public partial class HybridHealthViewModel : BaseViewModel
{
    private const int LoadSeconds = 30;
    private const double ChartHeight = 92;
    private const double ChartWidth = 280;

    private readonly HybridHealthService _health;
    private readonly VehicleInfoService _vehicles;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _loadStop;
    private HybridHealthResult? _result;

    [ObservableProperty] private string vehicleName = "";
    [ObservableProperty] private string packLabel = "";
    [ObservableProperty] private bool hasPack;
    [ObservableProperty] private string scoreText = "—";
    [ObservableProperty] private string ratingText = "";
    [ObservableProperty] private string scoreColor = "#94A3B8";
    [ObservableProperty] private string summaryText = "";
    [ObservableProperty] private string scanStage = "";
    [ObservableProperty] private bool hasResult;

    // Chart
    [ObservableProperty] private string blocksTitle = "";
    [ObservableProperty] private string blocksCaption = "";
    [ObservableProperty] private string selectedBlockText = "";

    // Detail rows
    [ObservableProperty] private string spreadTitle = "";
    [ObservableProperty] private string spreadText = "—";
    [ObservableProperty] private string spreadCaption = "";
    [ObservableProperty] private string temperatureText = "—";
    [ObservableProperty] private string temperatureCaption = "";
    [ObservableProperty] private bool hasTemperature;
    [ObservableProperty] private string socText = "—";
    [ObservableProperty] private string socCaption = "";
    [ObservableProperty] private bool hasSoc;
    [ObservableProperty] private string resistanceText = "—";
    [ObservableProperty] private string resistanceCaption = "";
    [ObservableProperty] private bool hasResistance;
    [ObservableProperty] private string sohText = "—";
    [ObservableProperty] private string sohCaption = "";
    [ObservableProperty] private bool hasCapacity;
    [ObservableProperty] private string capacityText = "—";
    [ObservableProperty] private bool isLoadTestSupported = true;
    [ObservableProperty] private bool hasBars;
    [ObservableProperty] private string packNote = "";
    [ObservableProperty] private bool hasPackNote;
    [ObservableProperty] private bool hasSoh;
    [ObservableProperty] private string remainingLifeText = "—";
    [ObservableProperty] private string remainingLifeCaption = "";
    [ObservableProperty] private bool hasRemainingLife;

    // Load test
    [ObservableProperty] private bool isLoadTesting;
    [ObservableProperty] private double loadProgress;
    [ObservableProperty] private string loadLiveText = "";
    [ObservableProperty] private string loadResultTitle = "";
    [ObservableProperty] private string loadResultText = "";
    [ObservableProperty] private string loadResultColor = "#1F8A4C";
    [ObservableProperty] private bool hasLoadResult;

    // Technical
    [ObservableProperty] private bool showTechnical;
    [ObservableProperty] private string validationStatus = "";
    [ObservableProperty] private string evidenceText = "";
    [ObservableProperty] private string rawLog = "";
    [ObservableProperty] private bool hasRawLog;
    [ObservableProperty] private string copyLogText = "";

    /// <summary>Chart rows (one row up to 24 bars, otherwise rows of up to 32).</summary>
    public ObservableCollection<HybridBarRow> BarRows { get; } = new();

    public HybridHealthViewModel(HybridHealthService health, VehicleInfoService vehicles, LocalizationService localization)
    {
        _health = health;
        _vehicles = vehicles;
        _localization = localization;
        Title = L("ჰიბრიდის ბატარეა", "Hybrid battery");
        VehicleName = L("ავტომობილი ჯერ არ არის ამოცნობილი", "Vehicle not identified yet");
        ScanStage = L("ჩართე ანთება (READY) და დააჭირე „შემოწმებას“.", "Switch to READY and tap “Check”.");
        CopyLogText = L("ჩანაწერის კოპირება", "Copy log");
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    // ---------------------------------------------------------------- texts

    public string PageTitle => L("ჰიბრიდის ბატარეა", "Hybrid battery");
    public string ScanButtonText => L("შემოწმება", "Check");
    public string ScoreCaption => L("ბატარეის მდგომარეობა", "Battery condition");
    public string LoadTitle => L("ტესტი დატვირთვით · 30 წმ", "Load test · 30 s");
    public string LoadIntro => L(
        "სუსტი ბლოკი ყველაზე კარგად დატვირთვისას ჩანს. ტესტს რთავს მგზავრი, მძღოლი კი 2–3-ჯერ მკვეთრად აჩქარებს უსაფრთხო გზაზე. მძღოლი ტელეფონს არ ეხება.",
        "A weak block shows best under load. A passenger starts the test while the driver accelerates hard 2–3 times on a safe road. The driver does not touch the phone.");
    public string LoadStartText => L("ტესტის დაწყება", "Start test");
    public string LoadStopText => L("დასრულება", "Finish");
    public string DetailsCaption => L("დეტალები", "Details");
    public string TemperatureTitle => L("ბატარეის ტემპერატურა", "Battery temperature");
    public string SocTitle => L("დამუხტვა (SOC)", "Charge (SOC)");
    public string SohTitle => L("ბატარეის ჯანმრთელობა (SOH)", "Battery health (SOH)");
    public string CapacityTitle => L("ტევადობა", "Capacity");
    public string CapacityCaption => L(
        "ბატარეის კონტროლერის შეფასება. ბერდებასთან ერთად მცირდება — შეადარე იმავე წლის სხვა მანქანას.",
        "The battery controller's estimate. It drops as the pack ages — compare with another car of the same year.");
    public string LoadNotSupportedText => _result?.PackKind is HvPackKind.AudiEtron or HvPackKind.HavalHev or HvPackKind.VwEGolf or HvPackKind.PorscheTaycan or HvPackKind.MgZsEv or HvPackKind.JaguarIPace
        ? L("ამ მანქანაზე უჯრედები რამდენიმე ცალკე მოთხოვნით იკითხება, ამიტომ დატვირთვის ტესტი აქ სწორ შედეგს ვერ მოგცემს. შეამოწმე დგომისას — SOH და უჯრედების სხვაობა საკმარისი სურათია.",
            "On this car the cells come from several separate requests, so a load test would not give a fair result. Check at rest — SOH and the cell spread give a good picture.")
        : _result?.PackKind == HvPackKind.GmVoltBolt
        ? L("Volt/Bolt-ზე 96 უჯრედი სათითაოდ იკითხება (10–20 წამი), ამიტომ დატვირთვის ტესტი აქ სწორ შედეგს ვერ მოგცემს. ბატარეა შეამოწმე დგომისას, READY რეჟიმში — უჯრედების სხვაობა და ტემპერატურა საკმარისი სურათია.",
            "On Volt/Bolt the 96 cells are read one by one (10–20 s), so a load test would not give a fair result. Check the pack at rest in READY — the cell spread and temperature give a good picture.")
        : L("ამ მანქანამ ცალკეული უჯრედები არ დააბრუნა, ამიტომ ტესტი დატვირთვით აქ ვერ ჩატარდება.",
            "This car did not return single cell values, so the load test cannot run here.");
    public string ResistanceTitle => L("შიდა წინაღობა", "Internal resistance");
    public string RemainingLifeTitle => L("ECU-ს შეფასება", "ECU estimate");
    public string HowToTitle => L("როგორ წავიკითხო", "How to read it");
    public string HowToText => _result?.PackKind is { } howKind && HvPackProtocol.IsCellLevel(howKind)
        ? L("უჯრედები ძალიან თანაბარი უნდა იყოს: სხვაობა 40 mV-მდე კარგია, 80 mV-ზე მეტი — ყურადღება, 150 mV-ზე მეტი — სუსტი უჯრედი. SOH ბატარეის საკუთარი შეფასებაა: ახალი = 100%, 80%-ზე ქვემოთ შესამჩნევად დაბერებულია. Hyundai/Kia ძაბვას 20 mV-იანი ნაბიჯით ზომავს.",
            "Cells should be very even: a spread up to 40 mV is good, above 80 mV needs attention, above 150 mV means a weak cell. SOH is the battery's own estimate: new = 100%, below 80% is noticeably aged. Hyundai/Kia measure in 20 mV steps.")
        : L("ჯანმრთელ ბატარეაში ბლოკები ერთმანეთს მისდევს. დგომისას სხვაობა 0.3 V-მდე კარგია, 0.5 V-ზე მეტი — ყურადღება, 1 V-ზე მეტი — სუსტი ბლოკი. დატვირთვისას სხვაობა ბუნებრივად იზრდება; 1.2 V-ზე მეტი ჩვეულებრივ P0A80 შეცდომამდე მიდის. ეს მიმართულებაა და არა ტევადობის ტესტი.",
            "In a healthy pack the blocks track each other. At rest a spread up to 0.3 V is good, above 0.5 V needs attention, above 1 V means a weak block. Under load the spread grows naturally; above 1.2 V usually leads to P0A80. This is a guide, not a capacity test.");
    public string TechnicalToggleText => ShowTechnical ? L("ტექნიკური დეტალების დამალვა", "Hide technical details") : L("ტექნიკური დეტალები", "Technical details");
    public string SafetyText => L(
        "იგზავნება მხოლოდ წაკითხვის მოთხოვნები. აპი ბატარეას არ ანულებს და არაფერს წერს.",
        "Only read requests are sent. The app does not reset the battery or write anything.");

    partial void OnShowTechnicalChanged(bool value) => OnPropertyChanged(nameof(TechnicalToggleText));

    public bool CanLoadTest => HasPack && IsLoadTestSupported && !IsBusy && !IsLoadTesting;
    public bool ShowLoadStart => IsLoadTestSupported && !IsLoadTesting;
    partial void OnIsLoadTestSupportedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoadTest));
        OnPropertyChanged(nameof(ShowLoadStart));
    }
    partial void OnHasPackChanged(bool value) => OnPropertyChanged(nameof(CanLoadTest));
    partial void OnIsLoadTestingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoadTest));
        OnPropertyChanged(nameof(ShowLoadStart));
    }

    // ------------------------------------------------------------ commands

    [RelayCommand]
    private void ToggleTechnical() => ShowTechnical = !ShowTechnical;

    [RelayCommand]
    private void SelectBlock(HybridBlockBar? bar)
    {
        if (bar is not null)
            SelectedBlockText = bar.ValueText;
    }

    [RelayCommand]
    private async Task CopyLog()
    {
        if (string.IsNullOrWhiteSpace(RawLog)) return;
        try
        {
            await Clipboard.Default.SetTextAsync($"GCar360 HV battery log\n{VehicleName}\n{RawLog}");
            CopyLogText = L("დაკოპირდა ✓", "Copied ✓");
        }
        catch
        {
            CopyLogText = L("კოპირება ვერ მოხერხდა", "Could not copy");
        }
    }

    [RelayCommand]
    private async Task Scan()
    {
        if (IsBusy || IsLoadTesting) return;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(CanLoadTest));
            HasResult = false;
            HasLoadResult = false;
            ScanStage = L("ავტომობილის იდენტიფიკაცია…", "Identifying the vehicle…");
            var vehicle = _vehicles.CurrentVehicle;
            if (vehicle is null)
            {
                try { vehicle = await _vehicles.IdentifyConnectedVehicleAsync(_scanCancellation.Token); }
                catch { }
            }

            if (vehicle is null)
            {
                ScanStage = L("მოდელი ვერ დადასტურდა. ჯერ დაუკავშირდი მანქანას.", "The model could not be confirmed. Connect to the car first.");
                return;
            }

            var progress = new Progress<string>(message => ScanStage = message);
            Apply(await _health.ScanAsync(vehicle, progress, _scanCancellation.Token));
        }
        catch (OperationCanceledException)
        {
            ScanStage = L("შემოწმება შეჩერდა.", "Check stopped.");
        }
        catch (Exception ex)
        {
            ScanStage = L($"შემოწმება ვერ დასრულდა: {ex.Message}", $"Check failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanLoadTest));
        }
    }

    [RelayCommand]
    private async Task StartLoadTest()
    {
        if (!CanLoadTest || _result?.PackKind is not { } kind) return;
        var lithium = HvPackProtocol.IsCellLevel(kind);
        _loadStop?.Dispose();
        _loadStop = new CancellationTokenSource();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(LoadSeconds + 30));
        IsLoadTesting = true;
        HasLoadResult = false;
        LoadProgress = 0;
        LoadLiveText = L("ვკითხულობ…", "Reading…");

        try
        {
            var progress = new Progress<HybridLoadProgress>(state =>
            {
                LoadProgress = state.TotalSeconds == 0 ? 0 : (double)state.Seconds / state.TotalSeconds;
                var current = state.CurrentA is { } a ? L($" · დენი {a:0} A", $" · current {a:0} A") : "";
                LoadLiveText = L(
                    $"სხვაობა ახლა {Volts(state.Spread, lithium)} · მაქს. {Volts(state.MaxSpread, lithium)}{current}",
                    $"Spread now {Volts(state.Spread, lithium)} · max {Volts(state.MaxSpread, lithium)}{current}");
            });

            var outcome = await _health.RunLoadTestAsync(TimeSpan.FromSeconds(LoadSeconds), progress, _loadStop.Token, _scanCancellation.Token);
            ApplyLoad(outcome, lithium);
        }
        catch (OperationCanceledException)
        {
            LoadResultColor = "#5D636B";
            LoadResultTitle = L("ტესტი შეწყდა", "Test cancelled");
            LoadResultText = "";
            HasLoadResult = true;
        }
        catch (Exception ex)
        {
            LoadResultColor = "#B3361F";
            LoadResultTitle = L("ტესტი ვერ შესრულდა", "Test failed");
            LoadResultText = ex.Message;
            HasLoadResult = true;
        }
        finally
        {
            IsLoadTesting = false;
            LoadLiveText = "";
        }
    }

    [RelayCommand]
    private void StopLoadTest() => _loadStop?.Cancel();

    public void CancelScan()
    {
        _scanCancellation?.Cancel();
        _loadStop?.Cancel();
    }

    // ------------------------------------------------------------- results

    private static string Volts(double value, bool lithium) => lithium ? $"{value * 1000:0} mV" : $"{value:0.00} V";

    private static string ColorFor(int? score) => score switch
    {
        >= 80 => "#1F8A4C",
        >= 60 => "#C98A1A",
        not null => "#B3361F",
        _ => "#94A3B8"
    };

    private string RatingFor(int? score) => score switch
    {
        null => L("ვერ შეფასდა", "Not rated"),
        >= 80 => L("კარგია", "Good"),
        >= 60 => L("საჭიროებს ყურადღებას", "Needs attention"),
        _ => L("სუსტია — შესამოწმებელია", "Weak — have it checked")
    };

    private void Apply(HybridHealthResult result)
    {
        _result = result;
        OnPropertyChanged(nameof(HowToText));
        OnPropertyChanged(nameof(LoadNotSupportedText));
        VehicleName = string.IsNullOrWhiteSpace(result.VehicleName) ? L("ამოუცნობი ავტომობილი", "Unknown vehicle") : result.VehicleName;
        ScoreText = result.Score?.ToString() ?? "—";
        ScoreColor = ColorFor(result.Score);
        RatingText = RatingFor(result.Score);
        HasPack = result.PackKind.HasValue;
        PackNote = result.PackNote;
        HasPackNote = !string.IsNullOrWhiteSpace(result.PackNote);
        HasBars = result.Blocks is { Count: > 0 };
        SelectedBlockText = "";

        var lithium = result.PackKind is { } packKind && HvPackProtocol.IsCellLevel(packKind);
        // Generic BMS cells are single cells too (millivolt scale).
        var millivolts = lithium || (result.PackKind is null && !result.IsBlockVoltage);
        PackLabel = result.PackKind switch
        {
            HvPackKind.ToyotaGen2NiMh => L($"Toyota 2004–2009 · NiMH · {result.CellReadingCount} ბლოკი", $"Toyota 2004–2009 · NiMH · {result.CellReadingCount} blocks"),
            HvPackKind.ToyotaGen3NiMh => L($"Toyota/Lexus · NiMH · {result.CellReadingCount} ბლოკი", $"Toyota/Lexus · NiMH · {result.CellReadingCount} blocks"),
            HvPackKind.ToyotaCells => L($"Toyota/Lexus 2017+ · {result.CellReadingCount} უჯრედი", $"Toyota/Lexus 2017+ · {result.CellReadingCount} cells"),
            HvPackKind.HyundaiKia => L($"Hyundai/Kia · ლითიუმ-იონი · {result.CellReadingCount} უჯრედი", $"Hyundai/Kia · lithium-ion · {result.CellReadingCount} cells"),
            HvPackKind.GmVoltBolt => L($"Chevrolet Volt/Bolt · ლითიუმ-იონი · {result.CellReadingCount} უჯრედი", $"Chevrolet Volt/Bolt · lithium-ion · {result.CellReadingCount} cells"),
            HvPackKind.NissanLeaf => result.Blocks is { Count: > 0 }
                ? L($"Nissan Leaf · ლითიუმ-იონი · {result.Blocks.Count} უჯრედი", $"Nissan Leaf · lithium-ion · {result.Blocks.Count} cells")
                : L("Nissan Leaf · ლითიუმ-იონი", "Nissan Leaf · lithium-ion"),
            HvPackKind.BmwI3 => L("BMW i3 · ლითიუმ-იონი · 96 უჯრედი", "BMW i3 · lithium-ion · 96 cells"),
            HvPackKind.FordHybrid => L("Ford · ლითიუმ-იონი", "Ford · lithium-ion"),
            HvPackKind.AudiEtron => result.Blocks is { Count: > 0 }
                ? L($"Audi e-tron · ლითიუმ-იონი · {result.Blocks.Count} უჯრედი", $"Audi e-tron · lithium-ion · {result.Blocks.Count} cells")
                : L("Audi e-tron · ლითიუმ-იონი", "Audi e-tron · lithium-ion"),
            HvPackKind.HavalHev => L("Haval ჰიბრიდი · ლითიუმ-იონი", "Haval hybrid · lithium-ion"),
            HvPackKind.HondaHybrid => L($"Honda ჰიბრიდი · ლითიუმ-იონი · {result.CellReadingCount} უჯრედი", $"Honda hybrid · lithium-ion · {result.CellReadingCount} cells"),
            HvPackKind.JaguarIPace => L("Jaguar I-PACE · ლითიუმ-იონი", "Jaguar I-PACE · lithium-ion"),
            HvPackKind.PorscheTaycan => result.Blocks is { Count: > 0 }
                ? L($"Porsche Taycan · ლითიუმ-იონი · {result.Blocks.Count} უჯრედი", $"Porsche Taycan · lithium-ion · {result.Blocks.Count} cells")
                : L("Porsche Taycan · ლითიუმ-იონი", "Porsche Taycan · lithium-ion"),
            HvPackKind.MgZsEv => L("MG ZS EV · ლითიუმ-იონი", "MG ZS EV · lithium-ion"),
            HvPackKind.VwEGolf => L("VW e-Golf · ლითიუმ-იონი", "VW e-Golf · lithium-ion"),
            _ => ""
        };

        IsLoadTestSupported = result.LoadTestSupported;
        HasCapacity = result.CapacityAh.HasValue;
        CapacityText = result.CapacityAh is { } capacityAh ? $"{capacityAh:0.#} Ah" : "—";

        HasSoh = result.SohPercent.HasValue;
        SohText = result.SohPercent is { } soh ? $"{soh:0.#}%" : "—";
        SohCaption = result.SohPercent is { } sohValue
            ? sohValue >= 90
                ? L("ბატარეის მართვის სისტემის შეფასება · ახალი = 100% · კარგია", "The battery system's own estimate · new = 100% · good")
                : sohValue >= 80
                    ? L("ბატარეის მართვის სისტემის შეფასება · ახალი = 100% · ნორმალური ცვეთა", "The battery system's own estimate · new = 100% · normal wear")
                    : L("ბატარეის მართვის სისტემის შეფასება · ახალი = 100% · შესამჩნევად დაბერებულია", "The battery system's own estimate · new = 100% · noticeably aged")
            : "";

        SpreadTitle = millivolts ? L("უჯრედების სხვაობა", "Cell spread") : L("ბლოკების სხვაობა", "Block spread");
        SpreadText = result.CellVoltageDelta is { } spread ? Volts(spread, millivolts) : "—";
        SpreadCaption = result.MinimumCellVoltage is { } min && result.MaximumCellVoltage is { } max
            ? result.Blocks is not { Count: > 0 }
                ? L($"{min:0.000}–{max:0.000} V · ყველაზე დაბალი და მაღალი უჯრედი", $"{min:0.000}–{max:0.000} V · lowest and highest cell")
                : L($"{min:0.00}–{max:0.00} V · {result.CellReadingCount} მნიშვნელობა", $"{min:0.00}–{max:0.00} V · {result.CellReadingCount} values")
            : L("ძაბვები ECU-მ არ დააბრუნა", "The ECU returned no voltages");

        HasTemperature = result.MaximumTemperatureC.HasValue;
        TemperatureText = result.MaximumTemperatureC is { } tMax ? $"{tMax:0} °C" : "—";
        TemperatureCaption = result.MinimumTemperatureC is { } tMin && result.MaximumTemperatureC is { } tTop
            ? L($"{(Math.Round(tMin) == Math.Round(tTop) ? "" : $"{tMin:0}–{tTop:0} °C · ")}{result.TemperatureReadingCount} სენსორი{(tTop > 45 ? " · ცხელია — შეამოწმე ვენტილატორი და ფილტრი" : "")}",
                $"{(Math.Round(tMin) == Math.Round(tTop) ? "" : $"{tMin:0}–{tTop:0} °C · ")}{result.TemperatureReadingCount} sensors{(tTop > 45 ? " · hot — check the fan and filter" : "")}")
            : "";

        HasSoc = result.SocPercent.HasValue;
        SocText = result.SocPercent is { } soc ? $"{soc:0}%" : "—";
        SocCaption = string.Join(" · ", new[]
        {
            result.CurrentA is { } current ? L($"დენი {current:0.#} A", $"current {current:0.#} A") : "",
            result.PackVoltage is { } volts ? L($"ბატარეა {volts:0} V", $"pack {volts:0} V") : "",
            L("ჰიბრიდზე ჩვეულებრივ 40–80%", "usually 40–80% on a hybrid")
        }.Where(text => text.Length > 0));

        var resistances = result.Resistances ?? Array.Empty<int>();
        HasResistance = resistances.Count > 0;
        if (HasResistance)
        {
            var high = HvPackAssessment.HighResistanceBlocks(resistances);
            ResistanceText = $"{resistances.Min()}–{resistances.Max()} mΩ";
            ResistanceCaption = high.Length == 0
                ? L("ყველა ბლოკი მსგავსია — კარგი ნიშანია", "All blocks are similar — a good sign")
                : L($"მაღალია: ბლოკი {string.Join(", ", high)} — ბერდება ან კონტაქტი აქვს ცუდი", $"High: block {string.Join(", ", high)} — ageing or a poor contact");
        }

        HasRemainingLife = result.EstimatedRemainingLifePercent.HasValue && (result.RemainingLifeFromEcu || !HasPack);
        RemainingLifeText = result.EstimatedRemainingLifePercent is { } life ? $"≈ {life:0}%" : "—";
        RemainingLifeCaption = result.RemainingLifeFromEcu
            ? L("მანქანის კომპიუტერის შეფასება (PID 5B)", "The car's own estimate (PID 5B)")
            : L("მიახლოებით, ძაბვისა და ტემპერატურის მიხედვით — ტევადობის ტესტი არ არის", "Approximate, from voltages and temperature — not a capacity test");

        BuildBars(result.Blocks ?? Array.Empty<double>(), lithium, result.WeakestBlock);
        SummaryText = Summary(result, lithium);

        ScanStage = result.Status;
        ValidationStatus = result.ValidationStatus;
        EvidenceText = result.Evidence;
        RawLog = result.RawLog.Trim();
        HasRawLog = !string.IsNullOrWhiteSpace(RawLog);
        CopyLogText = L("ჩანაწერის კოპირება", "Copy log");
        HasResult = true;
    }

    private string Summary(HybridHealthResult result, bool lithium)
    {
        if (!HasPack)
            return result.Score is null
                ? L("ამ მანქანიდან ბატარეის საკმარისი მონაცემი ვერ მივიღე.", "Not enough battery data came back from this car.")
                : L("შეფასება ზოგადი BMS მონაცემებითაა.", "The rating uses general BMS data.");

        var health = result.SohPercent is { } soh
            ? L($"ბატარეის სისტემის შეფასებით ჯანმრთელობა {soh:0.#}%-ია. ", $"The battery system rates its health at {soh:0.#}%. ")
            : "";
        var next = result.LoadTestSupported
            ? L(" გაუშვი ტესტი დატვირთვით.", " Run the load test.")
            : L(" შეამოწმე ხელახლა სხვა დღეს, სხვა დამუხტვის დონეზე.", " Check again another day at a different charge level.");
        var unit = lithium ? L("უჯრედი", "cell") : L("ბლოკი", "block");
        var spread = Volts(result.CellVoltageDelta ?? 0, lithium);
        if (result.Blocks is not { Count: > 0 })
            return health + (result.CellVoltageDelta.HasValue
                ? L($"ყველაზე მაღალ და დაბალ უჯრედს შორის სხვაობა {spread}-ია.", $"The highest and lowest cell differ by {spread}.")
                : L("ცალკეული უჯრედები ამ მანქანამ არ დააბრუნა.", "This car did not return single cell values."));
        if (result.WeakestBlock is { } weakPosition && Number(weakPosition) is var weak)
            return health + L($"{unit} {weak} დანარჩენზე {Volts(result.WeakestDeviation, lithium)}-ით დაბალია — სუსტი მოდულის ტიპური ნიშანი.",
                              $"{Capital(unit)} {weak} sits {Volts(result.WeakestDeviation, lithium)} below the others — a typical sign of a weak module.") + next;

        var verdict = HvPackAssessment.Judge(result.CellVoltageDelta ?? 0, lithium, underLoad: false);
        return health + (verdict == HvPackAssessment.Verdict.Good
            ? L($"ყველა {result.CellReadingCount} {unit} თანაბარია (სხვაობა {spread}).", $"All {result.CellReadingCount} {unit}s are even (spread {spread}).")
              + (result.LoadTestSupported ? L(" დარწმუნებისთვის გაუშვი ტესტი დატვირთვით.", " Run the load test to be sure.") : "")
            : L($"სხვაობა {spread} — ნორმაზე მეტია.", $"Spread {spread} — above normal.") + next);
    }

    /// <summary>Real block / cell number for a 1-based position (some GM cells may be missing).</summary>
    private int Number(int position) =>
        _result?.BlockNumbers is { } numbers && position >= 1 && position <= numbers.Count ? numbers[position - 1] : position;

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void BuildBars(IReadOnlyList<double> blocks, bool lithium, int? weakest, bool underLoad = false)
    {
        BarRows.Clear();
        if (blocks.Count == 0)
        {
            BlocksTitle = BlocksCaption = "";
            return;
        }

        var (good, watch, _) = HvPackAssessment.Limits(lithium, underLoad);
        var min = blocks.Min();
        var max = blocks.Max();
        var spread = max - min;
        BlocksTitle = lithium ? L("უჯრედების ძაბვა", "Cell voltages") : L("ბლოკების ძაბვა", "Block voltages");
        BlocksCaption = L($"სხვაობა {Volts(spread, lithium)} · ნორმა < {Volts(good, lithium)} · შეეხე სვეტს",
                          $"spread {Volts(spread, lithium)} · normal < {Volts(good, lithium)} · tap a bar");

        // The visible range is never smaller than the "watch" limit, so a
        // healthy pack looks calm and a weak block stands out.
        var range = Math.Max(spread, watch) * 1.25;
        var bottom = max - range;
        var rows = blocks.Count <= 24 ? 1 : (int)Math.Ceiling(blocks.Count / 32.0);
        var perRow = (int)Math.Ceiling(blocks.Count / (double)rows);
        var width = Math.Max(4, Math.Floor(ChartWidth / perRow) - 4);
        var height = rows == 1 ? ChartHeight : 54;
        var median = HvPackAssessment.Median(blocks);
        var unit = lithium ? L("უჯრედი", "Cell") : L("ბლოკი", "Block");

        var bars = new List<HybridBlockBar>(blocks.Count);
        for (var i = 0; i < blocks.Count; i++)
        {
            var value = blocks[i];
            var deviation = median - value;
            var color = weakest == i + 1 ? "#D64545" : deviation >= good * 0.6 ? "#E0A030" : "#3B82F6";
            var barHeight = Math.Round(10 + (value - bottom) / range * (height - 10), 1);
            var number = Number(i + 1);
            bars.Add(new HybridBlockBar(
                number,
                number.ToString(),
                Math.Clamp(barHeight, 6, height),
                width,
                color,
                rows == 1,
                lithium
                    ? $"{unit} {number}: {value:0.000} V ({(value - median) * 1000:+0;-0;0} mV)"
                    : $"{unit} {number}: {value:0.00} V ({value - median:+0.00;-0.00;0.00} V)"));
        }

        foreach (var chunk in bars.Chunk(perRow))
            BarRows.Add(new HybridBarRow(chunk));
    }

    private void ApplyLoad(HybridLoadOutcome outcome, bool lithium)
    {
        HasLoadResult = true;
        if (outcome.Samples < 3)
        {
            LoadResultColor = "#5D636B";
            LoadResultTitle = L("საკმარისი მონაცემი ვერ შევაგროვე", "Not enough readings");
            LoadResultText = L("ბატარეის ECU-მ ტესტის დროს იშვიათად უპასუხა. სცადე თავიდან.", "The battery ECU rarely answered during the test. Try again.");
            return;
        }

        var verdict = HvPackAssessment.Judge(outcome.MaxSpread, lithium, underLoad: true);
        LoadResultColor = ColorFor(HvPackAssessment.Score(outcome.MaxSpread, lithium, underLoad: true));
        LoadResultTitle = verdict switch
        {
            HvPackAssessment.Verdict.Good => L("✓ დატვირთვაზე ბლოკები თანაბრად მუშაობს", "✓ Blocks stay even under load"),
            HvPackAssessment.Verdict.Watch => L("დატვირთვაზე სხვაობა გაიზარდა — დააკვირდი", "The spread grows under load — keep an eye on it"),
            HvPackAssessment.Verdict.Weak => L("სუსტი ბლოკი დატვირთვაზე", "Weak block under load"),
            _ => L("ბატარეა დატვირთვას ვერ უძლებს", "The pack does not hold up under load")
        };

        var lines = new List<string>
        {
            L($"მაქსიმალური სხვაობა: {Volts(outcome.MaxSpread, lithium)} · {outcome.Samples} წაკითხვა",
              $"Largest spread: {Volts(outcome.MaxSpread, lithium)} · {outcome.Samples} readings")
        };
        if (outcome.WeakestBlock is { } weakPosition && Number(weakPosition) is var weak)
            lines.Add(L($"ყველაზე მეტად ეცემა: {(lithium ? "უჯრედი" : "ბლოკი")} {weak} (საშუალოდ {Volts(outcome.WeakestDeviation, lithium)}-ით დაბლა)",
                        $"Sags most: {(lithium ? "cell" : "block")} {weak} ({Volts(outcome.WeakestDeviation, lithium)} below on average)"));
        if (outcome.MaxDischargeA is { } amps)
            lines.Add(amps < 30
                ? L($"მაქს. დენი მხოლოდ {amps:0} A — დატვირთვა სუსტი იყო, შედეგი ნაკლებად სანდოა. სცადე უფრო მკვეთრი აჩქარება.",
                    $"Peak current only {amps:0} A — the load was light, so the result is less reliable. Try a harder acceleration.")
                : L($"მაქს. დენი: {amps:0} A", $"Peak current: {amps:0} A"));
        LoadResultText = string.Join("\n", lines);

        if (outcome.WorstBlocks.Count >= 3)
        {
            var title = lithium ? L("უჯრედების ძაბვა", "Cell voltages") : L("ბლოკების ძაბვა", "Block voltages");
            BuildBars(outcome.WorstBlocks, lithium, outcome.WeakestBlock, underLoad: true);
            BlocksTitle = L($"{title} · დატვირთვის პიკზე", $"{title} · at peak load");
        }
    }
}
