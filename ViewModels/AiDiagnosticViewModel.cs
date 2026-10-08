using CarScanner.AI;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace CarScanner.ViewModels;

/// <summary>
/// Builds an AI request exclusively from the current read-only OBD scan.
/// The cloud response is a diagnostic opinion, never a replacement for the
/// measurements shown in the evidence card.
/// </summary>
public partial class AiDiagnosticViewModel : BaseViewModel
{
    private const string RepairBaselinePreferenceKey = "ai_repair_baseline_v1";
    private readonly AiDiagnosticService _ai;
    private readonly VehicleInfoService _vehicleService;
    private readonly DiagnosticService _diagnostic;
    private readonly OBDService _obd;
    private readonly ProService _pro;
    private readonly LocalizationService _localization;
    private readonly IBluetoothService _connection;

    /// <summary>A restored or completed scan older than this is re-read before answering.</summary>
    private static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(24);

    private VehicleInfo? _vehicle;
    private DateTime? _scanTime;
    private bool _scannedThisConnection;
    private List<ErrorCode> _codes = new();
    private List<SensorValue> _sensors = new();
    private ReadinessInfo? _lastReadiness;

    public ObservableCollection<string> ChatMessages { get; } = new();

    [ObservableProperty] private string connectionStatus = "ECU ჯერ არ შემოწმებულა";
    [ObservableProperty] private string vehicleSummary = "ავტომობილი: —";
    [ObservableProperty] private string vinText = "VIN: —";
    [ObservableProperty] private string moduleSummary = "ECU მოდულები: —";
    [ObservableProperty] private string dtcSummary = "აქტიური DTC: —";
    [ObservableProperty] private string liveDataSummary = "Live Data: —";
    [ObservableProperty] private string emissionsSummary = "Emissions: —";
    [ObservableProperty] private string lastScanText = "ბოლო AI სკანი: —";
    [ObservableProperty] private string scanStage = "დააჭირე „Start AI Diagnostic“-ს.";
    [ObservableProperty] private int scanProgress;
    [ObservableProperty] private double scanProgressValue;
    [ObservableProperty] private string result = "AI პასუხი გამოჩნდება მას შემდეგ, რაც რეალური OBD სკანი დასრულდება.";
    [ObservableProperty] private string evidenceSummary = "მტკიცებულება: ჯერ არ არის წაკითხული.";
    [ObservableProperty] private string freezeFrameSummary = "Freeze Frame: —";
    [ObservableProperty] private string guidedTestResult = "Guided Test: ჯერ არ შესრულებულა.";
    [ObservableProperty] private string repairVerificationSummary = "Before/After შემოწმებისთვის ჯერ შეინახე რეალური სკანი როგორც „Before Repair“ baseline.";
    [ObservableProperty] private string chatInput = string.Empty;
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private bool isAiThinking;
    [ObservableProperty] private string aiThinkingText = "AI აანალიზებს...";

    /// <summary>Start button — marked PRO until PRO is active (the page is open to everyone).</summary>
    public string AnalyzeButtonText => CarScanner.Services.Localization.LocalizationService.T("AI დიაგნოსტიკის დაწყება") + ProGate.Suffix;

    public AiDiagnosticViewModel(
        AiDiagnosticService ai,
        VehicleInfoService vehicleService,
        DiagnosticService diagnostic,
        OBDService obd,
        ProService pro,
        LocalizationService localization,
        IBluetoothService connection)
    {
        _ai = ai;
        _vehicleService = vehicleService;
        _diagnostic = diagnostic;
        _obd = obd;
        _pro = pro;
        _pro.ProChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(AnalyzeButtonText)));
        _localization = localization;
        _connection = connection;
        // A new connection may be a different car: the next question must
        // not be answered from the previous connection's scan.
        _connection.ConnectionChanged += (_, _) => _scannedThisConnection = false;
        AiThinkingText = _localization.IsEnglish ? "AI is analyzing..." : "AI აანალიზებს...";
        _localization.LanguageChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(() =>
                AiThinkingText = _localization.IsEnglish ? "AI is analyzing..." : "AI აანალიზებს...");
        Title = "AI Diagnostic";
        RestoreSession();
    }

    [RelayCommand]
    private async Task Analyze()
    {
        try
        {
        if (IsBusy) return;
        if (!await EnsureProAsync()) return;

        try
        {
            IsBusy = true;
            IsAnalyzing = true;
            Result = "AI-სთვის რეალური მონაცემები მზადდება…";
            ChatMessages.Clear();
            ResetScanPresentation();
            _scanTime = null;
            _scannedThisConnection = false;

            SetStage(5, "1/7 OBD adapter და ECU კავშირის შემოწმება…");
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            ConnectionStatus = "ECU Connected";
            ModuleSummary = $"ECU მონაცემთა წყარო: Generic OBD-II · {_obd.CurrentProtocolDescription}";

            SetStage(18, "2/7 VIN და ავტომობილის ინფორმაციის წაკითხვა…");
            // Quick identity intentionally avoids a long Mode 09 sequence on
            // inexpensive adapters. It returns only values confirmed by ECU
            // or the exact VIN's existing local profile.
            _vehicle = await _vehicleService.IdentifyConnectedVehicleAsync();
            VehicleSummary = FormatVehicle(_vehicle);
            VinText = $"VIN: {Known(_vehicle.VIN)}";

            SetStage(34, "3/7 შეცდომის კოდების წაკითხვა…");
            _codes = await _diagnostic.ScanForErrorsAsync();
            DtcSummary = _codes.Count == 0
                ? "აქტიური DTC: 0 — Generic OBD სკანზე კოდი არ აღმოჩნდა"
                : $"აქტიური DTC: {_codes.Count} — ნახე AI-ის სავარაუდო მიზეზები ქვემოთ";

            SetStage(50, "4/7 Freeze Frame-ის შემოწმება…");
            var freezeFrame = _codes.Count == 0
                ? null
                : await _obd.ReadFreezeFrameQuickAsync(_codes[0].Code);
            FreezeFrameSummary = freezeFrame is null || freezeFrame.Values.Count == 0
                ? "Freeze Frame: ECU-ში ამ DTC-ის snapshot არ აღმოჩნდა."
                : $"Freeze Frame: {freezeFrame.Values.Count} რეალური მნიშვნელობა · Trigger: {Known(freezeFrame.TriggerCode)}";

            SetStage(64, "5/7 სწრაფი Live Data snapshot-ის წაკითხვა…");
            _sensors = (await _obd.ReadAllSupportedValuesAsync()).ToList();
            LiveDataSummary = _sensors.Count == 0
                ? "Live Data: ECU-მ snapshot მნიშვნელობა არ დააბრუნა"
                : $"Live Data: {_sensors.Count} რეალური PID snapshot-დან";

            SetStage(77, "6/7 Emissions / Readiness-ის შემოწმება…");
            ReadinessInfo? readiness = null;
            try { readiness = await _obd.ReadReadinessAsync(); }
            catch { /* unavailable is a valid ECU result, not a fault-free result */ }
            _lastReadiness = readiness;
            EmissionsSummary = readiness is null
                ? "Emissions: Readiness მონაცემი მიუწვდომელია"
                : $"Emissions: MIL {(readiness.MilOn ? "ON" : "OFF")} · ECU DTC count: {readiness.StoredDtcCount}";

            EvidenceSummary = BuildEvidenceSummary(_codes, _sensors, readiness, freezeFrame);
            _scanTime = DateTime.Now;
            _scannedThisConnection = true;
            LastScanText = $"ბოლო AI სკანი: {_scanTime:dd.MM.yyyy HH:mm}";

            SetStage(90, "7/7 AI აანალიზებს მიღებულ მტკიცებულებას…");
            try
            {
                Result = await _ai.AnalyzeAsync(
                    _vehicle,
                    _codes,
                    _sensors);
                AddAiToReport(Result);

                ChatMessages.Add(_localization.IsEnglish
                    ? "AI: The scan is complete. Ask me anything about this data."
                    : "AI: სკანი დასრულდა. დამისვი კითხვა ამ კონკრეტული მონაცემების შესახებ.");
                SetStage(100, "AI დიაგნოსტიკა მზადაა — დასკვნები არის სავარაუდო, არა საბოლოო რემონტის დადასტურება.");
            }
            catch (Exception ex)
            {
                // The OBD scan above completed successfully. An AI/network
                // outage must not be presented as an ECU disconnection and
                // must not discard the confirmed evidence already on screen.
                Result = $"OBD სკანი წარმატებით დასრულდა, მაგრამ AI პასუხი ამჯერად ვერ მივიღეთ. {UserError.Describe(ex)}";
                ChatMessages.Add("AI: OBD მონაცემები შენარჩუნებულია. AI სერვისის აღდგენის შემდეგ სცადე თავიდან.");
                SetStage(100, "OBD სკანი დასრულდა — AI პასუხი დროებით მიუწვდომელია.");
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = "ECU მონაცემი მიუწვდომელია";
            Result = "AI დიაგნოსტიკა ვერ დასრულდა. შეამოწმე OBD კავშირი და ინტერნეტი, შემდეგ სცადე თავიდან.";
            EvidenceSummary = $"მიზეზი: {UserError.Describe(ex)}";
            // A half-finished scan (e.g. VIN read, codes not) must not be
            // used later as if it described this car completely.
            if (_scanTime is null || !_scannedThisConnection)
            {
                _vehicle = null;
                _codes = new();
                _sensors = new();
                _lastReadiness = null;
            }
            SetStage(100, "სკანი შეწყდა — რეალური მონაცემები ვერ მივიღეთ.");
        }
        finally
        {
            IsAnalyzing = false;
            IsBusy = false;
            SaveSession();
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AiDiagnosticViewModel.Analyze");
        }
    }

    /// <summary>The AI conclusion becomes the "ai" part of the combined report.</summary>
    private void AddAiToReport(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var english = _localization.IsEnglish;
        var paragraphs = text.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('*', '#', ' '))
            .Where(line => line.Length > 0)
            .ToList();

        var section = new CarScanner.Services.Reports.ReportSectionItem
        {
            Kind = "ai",
            Icon = "AI",
            Title = english ? "AI conclusion" : "AI დასკვნა",
            Status = CarScanner.Services.Reports.ReportStatus.Info,
            Summary = paragraphs.FirstOrDefault() is { } first ? (first.Length > 160 ? first[..160] + "…" : first) : string.Empty
        };

        var total = 0;
        foreach (var paragraph in paragraphs.Skip(1))
        {
            if ((total += paragraph.Length) > 2500)
                break;
            section.Lines.Add(new() { Label = string.Empty, Value = paragraph });
        }

        CarScanner.Services.Reports.VehicleReportService.Instance?.AddOrReplace(section);
    }

    /// <summary>
    /// A question typed on the home page. <see cref="SendMessage"/> reads the
    /// connected car first when there is no scan of it yet (or the saved scan
    /// belongs to another car or is older than 24 hours).
    /// </summary>
    public async Task AskFromHomeAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || IsBusy)
            return;

        try
        {
            ChatInput = question.Trim();
            await SendMessage();
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "AiDiagnosticViewModel.AskFromHome");
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        try
        {
        var question = ChatInput?.Trim();
        if (string.IsNullOrWhiteSpace(question) || IsBusy) return;
        if (!await EnsureProAsync()) return;

        var connected = _connection.IsConnected;
        if (connected && NeedsFreshScan())
        {
            // The saved scan is of another car, of an unknown car or too old:
            // read the connected car first and drop the old conversation.
            DiscardSession();
            await Analyze();
            if (_vehicle is null || !_scannedThisConnection)
            {
                ChatMessages.Add(L(
                    "AI: დაკავშირებული მანქანის სკანი ვერ დასრულდა, ამიტომ კითხვაზე პასუხს ძველ მონაცემებზე არ დავაფუძნებ. შეამოწმე კავშირი და სცადე თავიდან.",
                    "AI: The scan of the connected car did not finish, so I won't answer from old data. Check the connection and try again."));
                return;
            }
        }

        if (_vehicle is null)
        {
            ChatMessages.Add(L(
                "AI: ჯერ გაუშვი Start AI Diagnostic, რომ პასუხი ამ მანქანის რეალურ OBD მონაცემებს დაეყრდნოს.",
                "AI: Run Start AI Diagnostic first so the answer is based on this car's real OBD data."));
            return;
        }

        // Not connected: the answer can only come from the saved scan — say so.
        var sourceNote = connected ? null : SavedScanNote();

        ChatMessages.Add(AiDiagnosticService.AnswerInEnglish(question) ? $"You: {question}" : $"თქვენ: {question}");
        ChatInput = string.Empty;
        try
        {
            IsBusy = true;
            IsAiThinking = true;
            AiThinkingText = _localization.IsEnglish ? "AI is analyzing..." : "AI აანალიზებს...";

            // The question was just added to the chat and is sent separately,
            // so it must not be repeated inside the history.
            var history = ChatMessages.Take(Math.Max(0, ChatMessages.Count - 1)).ToList();

            var reply = await _ai.ChatAsync(
                _vehicle,
                _codes,
                _sensors,
                history,
                question);

            ChatMessages.Add(sourceNote is null ? $"AI: {reply}" : $"AI: {sourceNote}\n{reply}");
        }
        catch (Exception ex)
        {
            ChatMessages.Add(L("AI: პასუხი ვერ მივიღე — ", "AI: Could not get an answer — ") + UserError.Describe(ex));
        }
        finally
        {
            IsAiThinking = false;
            IsBusy = false;
            SaveSession();
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AiDiagnosticViewModel.SendMessage");
        }
    }

    // =========================================================
    // WHICH CAR THE SAVED SCAN BELONGS TO
    // =========================================================

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    /// <summary>
    /// Called by the AI page when it appears. A saved scan of a different (or
    /// unidentified) car, or one older than 24 hours, is discarded while a car
    /// is connected, so the chat never mixes two cars. Without a connection
    /// the saved scan stays visible and answers are marked as coming from it.
    /// </summary>
    public Task EnsureCurrentVehicleAsync()
    {
        try
        {
            if (IsBusy || _vehicle is null || !_connection.IsConnected || !NeedsFreshScan())
                return Task.CompletedTask;

            DiscardSession();
            Result = L(
                "წინა AI სკანი სხვა ან ამოუცნობ მანქანას ეკუთვნის, ან 24 საათზე ძველია. დაიწყე ახალი AI დიაგნოსტიკა — ან უბრალოდ დასვი კითხვა და ჯერ მანქანას წავიკითხავ.",
                "The previous AI scan belongs to another or unidentified car, or is older than 24 hours. Start a new AI diagnostic — or just ask a question and the car will be read first.");
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "AiDiagnosticViewModel.EnsureCurrentVehicle");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// True when the data in memory must not be used for the connected car:
    /// no scan, scan older than 24 h, or a VIN that differs from (or cannot be
    /// matched to) the connected car's VIN. Only meaningful while connected.
    /// </summary>
    private bool NeedsFreshScan()
    {
        if (_vehicle is null || _scanTime is not { } scannedAt)
            return true;

        if (DateTime.Now - scannedAt > MaxSessionAge)
            return true;

        // Scanned during this connection: it is this car, even when the ECU
        // does not report a VIN.
        if (_scannedThisConnection)
            return false;

        var connectedVin = _vehicleService.CurrentVehicle?.VIN;
        if (IsUnknownVin(connectedVin) || IsUnknownVin(_vehicle.VIN))
            return true;

        return !string.Equals(connectedVin!.Trim(), _vehicle.VIN.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Forgets the saved scan and its chat (memory and file).</summary>
    private void DiscardSession()
    {
        _vehicle = null;
        _codes = new();
        _sensors = new();
        _lastReadiness = null;
        _scanTime = null;
        _scannedThisConnection = false;
        ChatMessages.Clear();

        VehicleSummary = L("ავტომობილი: —", "Vehicle: —");
        VinText = "VIN: —";
        ModuleSummary = L("ECU მოდულები: —", "ECU modules: —");
        DtcSummary = L("აქტიური DTC: —", "Active DTC: —");
        LiveDataSummary = "Live Data: —";
        EmissionsSummary = "Emissions: —";
        FreezeFrameSummary = "Freeze Frame: —";
        EvidenceSummary = L("მტკიცებულება: ჯერ არ არის წაკითხული.", "Evidence: not read yet.");
        LastScanText = L("ბოლო AI სკანი: —", "Last AI scan: —");
        ConnectionStatus = L("ECU ჯერ არ შემოწმებულა", "ECU not checked yet");
        ScanProgress = 0;
        ScanProgressValue = 0;
        ScanStage = L("დააჭირე „Start AI Diagnostic“-ს.", "Tap \u201cStart AI Diagnostic\u201d.");

        try
        {
            if (File.Exists(SessionPath))
                File.Delete(SessionPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AI chat session delete failed: {ex.Message}");
        }
    }

    /// <summary>"Based on the scan of Toyota Camry 2018 from 01.10.2026 14:20".</summary>
    private string SavedScanNote()
    {
        var vehicle = _vehicle;
        var name = vehicle is null
            ? string.Empty
            : string.Join(" ", new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
                .Where(part => !string.IsNullOrWhiteSpace(part) && !part.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));
        if (string.IsNullOrWhiteSpace(name))
            name = L("მანქანის", "the car");
        var when = _scanTime is { } time ? time.ToString("dd.MM.yyyy HH:mm") : "—";
        return L(
            $"(მანქანა არ არის დაკავშირებული — პასუხი ეყრდნობა {name} სკანს {when}-დან.)",
            $"(No car connected — based on the scan of {name} from {when}.)");
    }

    // =========================================================
    // CHAT SESSION (kept when leaving the page and after a restart)
    // =========================================================

    private static string SessionPath =>
        Path.Combine(FileSystem.AppDataDirectory, "ai_chat_session.json");

    /// <summary>The last scan and its chat; replaced only by a new "Start AI Diagnostic".</summary>
    private sealed class AiChatSession
    {
        public DateTime SavedAt { get; set; }
        /// <summary>When the OBD scan itself was made (SavedAt changes with every chat message).</summary>
        public DateTime? ScannedAt { get; set; }
        public VehicleInfo? Vehicle { get; set; }
        public List<ErrorCode> Codes { get; set; } = new();
        public List<SensorValue> Sensors { get; set; } = new();
        public List<string> Messages { get; set; } = new();
        public string Result { get; set; } = string.Empty;
        public string ConnectionStatus { get; set; } = string.Empty;
        public string VehicleSummary { get; set; } = string.Empty;
        public string VinText { get; set; } = string.Empty;
        public string ModuleSummary { get; set; } = string.Empty;
        public string DtcSummary { get; set; } = string.Empty;
        public string LiveDataSummary { get; set; } = string.Empty;
        public string EmissionsSummary { get; set; } = string.Empty;
        public string LastScanText { get; set; } = string.Empty;
        public string EvidenceSummary { get; set; } = string.Empty;
        public string FreezeFrameSummary { get; set; } = string.Empty;
        public string ScanStage { get; set; } = string.Empty;
    }

    private void SaveSession()
    {
        if (_vehicle is null)
            return;

        try
        {
            var session = new AiChatSession
            {
                SavedAt = DateTime.Now,
                ScannedAt = _scanTime,
                Vehicle = _vehicle,
                Codes = _codes.Select(code => new ErrorCode
                {
                    Code = code.Code,
                    TitleKa = code.TitleKa,
                    TitleEn = code.TitleEn,
                    DescriptionKa = code.DescriptionKa,
                    DescriptionEn = code.DescriptionEn,
                    PossibleCausesKa = code.PossibleCausesKa,
                    PossibleCausesEn = code.PossibleCausesEn,
                    SymptomsKa = code.SymptomsKa,
                    SymptomsEn = code.SymptomsEn,
                    ChecksKa = code.ChecksKa,
                    Severity = code.Severity,
                    Status = code.Status
                }).ToList(),
                Sensors = _sensors.ToList(),
                Messages = ChatMessages.TakeLast(60).ToList(),
                Result = Result,
                ConnectionStatus = ConnectionStatus,
                VehicleSummary = VehicleSummary,
                VinText = VinText,
                ModuleSummary = ModuleSummary,
                DtcSummary = DtcSummary,
                LiveDataSummary = LiveDataSummary,
                EmissionsSummary = EmissionsSummary,
                LastScanText = LastScanText,
                EvidenceSummary = EvidenceSummary,
                FreezeFrameSummary = FreezeFrameSummary,
                ScanStage = ScanStage
            };

            File.WriteAllText(SessionPath, JsonSerializer.Serialize(session, SessionJson));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AI chat save failed: {ex.Message}");
        }
    }

    private void RestoreSession()
    {
        try
        {
            if (!File.Exists(SessionPath))
                return;

            var session = JsonSerializer.Deserialize<AiChatSession>(File.ReadAllText(SessionPath), SessionJson);
            if (session?.Vehicle is null || DateTime.Now - session.SavedAt > TimeSpan.FromDays(30))
                return;

            _vehicle = session.Vehicle;
            _scanTime = session.ScannedAt ?? session.SavedAt;
            _scannedThisConnection = false;
            _codes = session.Codes;
            _sensors = session.Sensors;
            foreach (var message in session.Messages)
                ChatMessages.Add(message);

            Result = session.Result;
            ConnectionStatus = session.ConnectionStatus;
            VehicleSummary = session.VehicleSummary;
            VinText = session.VinText;
            ModuleSummary = session.ModuleSummary;
            DtcSummary = session.DtcSummary;
            LiveDataSummary = session.LiveDataSummary;
            EmissionsSummary = session.EmissionsSummary;
            LastScanText = session.LastScanText;
            EvidenceSummary = session.EvidenceSummary;
            FreezeFrameSummary = session.FreezeFrameSummary;
            ScanStage = session.ScanStage;
        }
        catch (Exception ex)
        {
            // A damaged file must never block the page; start empty instead.
            System.Diagnostics.Debug.WriteLine($"AI chat restore failed: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions SessionJson = new()
    {
        IgnoreReadOnlyProperties = true
    };

    [RelayCommand]
    private Task OpenLiveData() => Shell.Current.GoToAsync(nameof(Views.LiveDataPage));

    [RelayCommand]
    private async Task RunGuidedFuelTrimTest()
    {
        try
        {
        if (IsBusy) return;
        if (!await EnsureProAsync()) return;

        try
        {
            IsBusy = true;
            GuidedTestResult = "უსაფრთხოების შემოწმება მიმდინარეობს…";
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            if (!_obd.IsPidSupported(0x06) && !_obd.IsPidSupported(0x07))
            {
                GuidedTestResult = "Guided Test მიუწვდომელია: ამ ECU-მ Fuel Trim Bank 1 PID არ დაადასტურა.";
                return;
            }

            var continueAtIdle = await Shell.Current.DisplayAlertAsync(
                CarScanner.Services.Localization.LocalizationService.T("Guided Fuel Trim Test"),
                CarScanner.Services.Localization.LocalizationService.T("უსაფრთხო ადგილზე გააჩერე მანქანა, ჩართე Parking brake და დატოვე ძრავა idle-ზე. ტესტი მხოლოდ გაჩერებულ მანქანაზე შეასრულე."),
                CarScanner.Services.Localization.LocalizationService.T("Idle-ის წაკითხვა"),
                CarScanner.Services.Localization.LocalizationService.T("გაუქმება"));
            if (!continueAtIdle)
            {
                GuidedTestResult = "Guided Test გაუქმდა მომხმარებლის მიერ.";
                return;
            }

            GuidedTestResult = "Idle Fuel Trim იკითხება…";
            var idle = await ReadFuelTrimAsync();
            if (idle is null)
            {
                GuidedTestResult = "Idle Fuel Trim ECU-მ არ დააბრუნა — შედარება ვერ შესრულდება.";
                return;
            }

            var continueAtRpm = await Shell.Current.DisplayAlertAsync(
                CarScanner.Services.Localization.LocalizationService.T("Guided Fuel Trim Test"),
                CarScanner.Services.Localization.LocalizationService.T("მანქანა ისევ გაჩერებული უნდა იყოს. ნეიტრალში/P-ზე დაიჭირე დაახლოებით 2500 RPM 10 წამით. არ შეასრულო ეს ტესტი გზაზე ან დახურულ, უვენტილაციო სივრცეში."),
                CarScanner.Services.Localization.LocalizationService.T("10 წამის შემდეგ წაკითხვა"),
                CarScanner.Services.Localization.LocalizationService.T("გაუქმება"));
            if (!continueAtRpm)
            {
                GuidedTestResult = $"Idle შედეგი: {idle.Name} {idle.Value} {idle.Unit}. მეორე წაკითხვა გაუქმდა.";
                return;
            }

            GuidedTestResult = "2500 RPM-ზე 10-წამიანი სტაბილიზაცია და წაკითხვა მიმდინარეობს…";
            await Task.Delay(TimeSpan.FromSeconds(10));
            var elevated = await ReadFuelTrimAsync();
            if (elevated is null)
            {
                GuidedTestResult = $"Idle: {idle.Name} {idle.Value} {idle.Unit}. 2500 RPM-ზე ECU-მ მნიშვნელობა არ დააბრუნა.";
                return;
            }

            GuidedTestResult = BuildGuidedFuelTrimConclusion(idle, elevated);
        }
        catch (Exception ex)
        {
            GuidedTestResult = $"Guided Test ვერ შესრულდა: {UserError.Describe(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AiChatSession.RunGuidedFuelTrimTest");
        }
    }

    [RelayCommand]
    private async Task SaveBeforeRepairBaseline()
    {
        try
        {
        if (!await EnsureProAsync()) return;
        if (_vehicle is null || IsUnknownVin(_vehicle.VIN))
        {
            RepairVerificationSummary = "Before Repair baseline ვერ შეინახება: მიმდინარე სკანის VIN მიუწვდომელია. ჯერ გაუშვი AI Diagnostic დაკავშირებულ მანქანაზე.";
            return;
        }

        var baseline = new AiRepairBaseline
        {
            Vin = _vehicle.VIN,
            CapturedAt = DateTime.Now,
            DtcCodes = _codes.Select(code => code.Code).Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MilOn = _lastReadiness?.MilOn,
            Sensors = _sensors.Select(sensor => new AiRepairSensor
            {
                Pid = sensor.Pid,
                Name = sensor.Name,
                Value = sensor.Value,
                Unit = sensor.Unit
            }).ToList()
        };

        Preferences.Default.Set(RepairBaselinePreferenceKey, JsonSerializer.Serialize(baseline));
        RepairVerificationSummary = $"Before Repair baseline შენახულია: {baseline.CapturedAt:dd.MM.yyyy HH:mm} · DTC: {baseline.DtcCodes.Count} · PID: {baseline.Sensors.Count}.";
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AiChatSession.SaveBeforeRepairBaseline");
        }
    }

    [RelayCommand]
    private async Task VerifyRepair()
    {
        try
        {
        if (IsBusy) return;
        if (!await EnsureProAsync()) return;

        var baseline = LoadBaseline();
        if (baseline is null)
        {
            RepairVerificationSummary = "შენახული Before Repair baseline ვერ მოიძებნა. რემონტამდე გაუშვი AI Diagnostic და დააჭირე „Save Before Repair“.";
            return;
        }

        try
        {
            IsBusy = true;
            RepairVerificationSummary = "After Repair რეალური OBD მონაცემები იკითხება…";
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            var currentVehicle = await _vehicleService.IdentifyConnectedVehicleAsync();
            if (IsUnknownVin(currentVehicle.VIN))
            {
                RepairVerificationSummary = "VIN ვერ წავიკითხეთ, ამიტომ სხვა მანქანის მონაცემებთან უსაფრთხო შედარება შეუძლებელია.";
                return;
            }
            if (!string.Equals(baseline.Vin, currentVehicle.VIN, StringComparison.OrdinalIgnoreCase))
            {
                RepairVerificationSummary = "შენახული Before Repair baseline სხვა VIN-ს ეკუთვნის. შედარება შეჩერდა, რათა არასწორი დასკვნა არ მივიღოთ.";
                return;
            }

            var currentCodes = await _diagnostic.ScanForErrorsAsync();
            var currentSensors = (await _obd.ReadAllSupportedValuesAsync()).ToList();
            ReadinessInfo? currentReadiness = null;
            try { currentReadiness = await _obd.ReadReadinessAsync(); } catch { }

            RepairVerificationSummary = BuildRepairVerification(baseline, currentCodes, currentSensors, currentReadiness);
        }
        catch (Exception ex)
        {
            RepairVerificationSummary = $"Repair Verification ვერ დასრულდა: {UserError.Describe(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AiChatSession.VerifyRepair");
        }
    }

    private async Task<bool> EnsureProAsync()
    {
        return await ProGate.EnsureOrOpenAsync(_pro);
    }

    private void ResetScanPresentation()
    {
        ScanProgress = 0;
        ScanProgressValue = 0;
        ConnectionStatus = "კავშირი მოწმდება…";
        VehicleSummary = "ავტომობილი: იკითხება…";
        VinText = "VIN: იკითხება…";
        ModuleSummary = "ECU მოდულები: იკითხება…";
        DtcSummary = "აქტიური DTC: იკითხება…";
        LiveDataSummary = "Live Data: იკითხება…";
        EmissionsSummary = "Emissions: იკითხება…";
        FreezeFrameSummary = "Freeze Frame: იკითხება…";
    }

    private void SetStage(int percent, string text)
    {
        ScanProgress = percent;
        ScanProgressValue = percent / 100d;
        ScanStage = text;
    }

    private static string FormatVehicle(VehicleInfo vehicle)
    {
        var make = Known(vehicle.Manufacturer);
        var model = Known(vehicle.Model);
        var year = Known(vehicle.Year);
        return $"ავტომობილი: {make} {model} · {year} · საწვავი: {Known(vehicle.FuelType)}";
    }

    private static string BuildEvidenceSummary(
        IReadOnlyCollection<ErrorCode> codes,
        IReadOnlyCollection<SensorValue> sensors,
        ReadinessInfo? readiness,
        FreezeFrame? freezeFrame)
    {
        var attentionSensors = sensors.Where(sensor => sensor.IsProblem).Select(sensor => sensor.Name).Take(4).ToArray();
        var parts = new List<string>
        {
            $"DTC: {codes.Count}",
            $"Live PID: {sensors.Count}",
            readiness is null ? "Readiness: მიუწვდომელია" : $"MIL: {(readiness.MilOn ? "ON" : "OFF")}",
            freezeFrame is { Values.Count: > 0 } ? $"Freeze Frame: {freezeFrame.Values.Count}" : "Freeze Frame: არ არის"
        };
        if (attentionSensors.Length > 0)
            parts.Add($"ყურადღების PID: {string.Join(", ", attentionSensors)}");
        return string.Join(" · ", parts);
    }

    private static string Known(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? "მიუწვდომელია"
            : value;

    private async Task<SensorValue?> ReadFuelTrimAsync()
    {
        var longTerm = await _obd.ReadSensorAsync(0x07);
        return longTerm ?? await _obd.ReadSensorAsync(0x06);
    }

    private static string BuildGuidedFuelTrimConclusion(SensorValue idle, SensorValue elevated)
    {
        var header = $"რეალური Fuel Trim · Idle: {idle.Value} {idle.Unit} · ~2500 RPM: {elevated.Value} {elevated.Unit}. ";
        if (!TryNumber(idle.Value, out var idleValue) || !TryNumber(elevated.Value, out var elevatedValue))
            return header + "რიცხვითი შედარება ვერ გაკეთდა; ნახე ეს მნიშვნელობები Live Data-ში.";

        if (idleValue >= 10 && elevatedValue <= idleValue - 7)
            return header + "ეს შაბლონი შესაძლოა უფრო შეესაბამებოდეს vacuum/intake leak-ს idle-ზე. დაათვალიერე ჰაერის შლანგები და დაადასტურე სხვა ტესტებით.";
        if (Math.Abs(idleValue) >= 10 && Math.Abs(elevatedValue) >= 10)
            return header + "Fuel Trim ორივე რეჟიმში მაღალია. ეს შეიძლება მიუთითებდეს ჰაერის/MAF-ის ან საწვავის მიწოდების საკითხზე; მხოლოდ ამ ტესტით ნაწილი ვერ დადასტურდება.";
        return header + "მნიშვნელობების განსხვავება ამ მოკლე ტესტში საკმარისი არ არის კონკრეტული მიზეზის დასასკვნელად.";
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);

    private static bool IsUnknownVin(string? vin) =>
        string.IsNullOrWhiteSpace(vin) || vin.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || vin.Length != 17;

    private static AiRepairBaseline? LoadBaseline()
    {
        try
        {
            var json = Preferences.Default.Get(RepairBaselinePreferenceKey, string.Empty);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AiRepairBaseline>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildRepairVerification(
        AiRepairBaseline baseline,
        IReadOnlyCollection<ErrorCode> currentCodes,
        IReadOnlyCollection<SensorValue> currentSensors,
        ReadinessInfo? currentReadiness)
    {
        var lines = new List<string>
        {
            $"Before ({baseline.CapturedAt:dd.MM.yyyy HH:mm}): DTC {baseline.DtcCodes.Count} · MIL {FormatMil(baseline.MilOn)}",
            $"After: DTC {currentCodes.Count} · MIL {FormatMil(currentReadiness?.MilOn)}"
        };

        var currentCodeSet = currentCodes.Select(code => code.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cleared = baseline.DtcCodes.Where(code => !currentCodeSet.Contains(code)).ToArray();
        var remaining = baseline.DtcCodes.Where(currentCodeSet.Contains).ToArray();
        if (cleared.Length > 0) lines.Add($"აღარ იკითხება: {string.Join(", ", cleared)}");
        if (remaining.Length > 0) lines.Add($"ჯერ ისევ იკითხება: {string.Join(", ", remaining)}");

        var currentByPid = currentSensors.ToDictionary(sensor => sensor.Pid);
        var comparisons = baseline.Sensors
            .Where(sensor => sensor.Pid is 0x06 or 0x07 or 0x05 or 0x0B or 0x10 or 0x42)
            .Where(sensor => currentByPid.ContainsKey(sensor.Pid))
            .Select(sensor =>
            {
                var after = currentByPid[sensor.Pid];
                return $"{sensor.Name}: ადრე {sensor.Value} {sensor.Unit} → ახლა {after.Value} {after.Unit}";
            })
            .Take(6)
            .ToArray();
        if (comparisons.Length > 0)
        {
            lines.Add("რეალური PID შედარება:");
            lines.AddRange(comparisons);
        }

        if (baseline.DtcCodes.Count > 0 && currentCodes.Count < baseline.DtcCodes.Count && currentReadiness?.MilOn == false)
            lines.Add("ECU ახლა ნაკლებ DTC-ს აჩვენებს და MIL გამორთულია. შედეგი დადებითია, მაგრამ drive cycle-ისა და Readiness-ის დასრულებამდე რემონტი საბოლოოდ დადასტურებულად არ ჩაითვალოს.");
        else
            lines.Add("შედარება აჩვენებს მხოლოდ ECU-ს ამჟამინდელ პასუხს. საბოლოო დასკვნამდე გადაამოწმე დარჩენილი DTC-ები და Readiness drive cycle-ის შემდეგ.");

        return string.Join("\n", lines);
    }

    private static string FormatMil(bool? milOn) => milOn is null ? "მიუწვდომელია" : milOn.Value ? "ON" : "OFF";
}
