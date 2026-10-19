using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Database;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.Mileage;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One reading the car returned (Mileage page).</summary>
public sealed class MileageReadingRow
{
    public string Title { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string ConvertedText { get; init; } = string.Empty;
    public bool HasConverted => ConvertedText.Length > 0;
    public string MeasurementText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string MethodText { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;

    /// <summary>Set only when the catalog still flags the definition behind the reading as under test.</summary>
    public string DefinitionText { get; init; } = string.Empty;
    public bool HasDefinitionText => DefinitionText.Length > 0;
    public bool IsHighlighted { get; init; }
    public Color ValueColor { get; init; } = Colors.Gray;
    public Color RowBackground => IsHighlighted ? ThemeService.Soft("#FDF1D8") : Colors.Transparent;
}

public sealed class MileageConflictRow
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>One observation about the stored records (Mileage page). An observation, never a verdict.</summary>
public sealed class MileageFindingRow
{
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public Color TitleColor { get; init; } = Colors.Gray;
}

/// <summary>One source that was tried (or deliberately not tried) and what came of it.</summary>
public sealed class MileageAttemptRow
{
    public string Title { get; init; } = string.Empty;
    public string OutcomeText { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
    public bool HasDetail => DetailText.Length > 0;
    public Color OutcomeColor { get; init; } = Colors.Gray;
}

/// <summary>
/// Mileage page: shows what each module / standard PID REPORTED, keeps different measurements apart, and compares
/// only total-odometer readings. It never says which reading is the "original" one and never accuses anyone of
/// tampering: agreement and disagreement are reported as such, with the reminder that diagnostic data cannot establish
/// the historically authentic mileage. Only values the car returned are shown; nothing is estimated.
/// </summary>
public class MileageViewModel : BaseViewModel
{
    private static Color Green => ThemeService.Soft("#1F8A4C");
    private static Color Amber => ThemeService.Soft("#B06A00");
    private static Color Grey => ThemeService.Soft("#5D636B");
    private static Color Blue => ThemeService.Soft("#1D5BD6");

    private readonly ActualMileageService _mileage;
    private readonly CarScanner.Services.Maintenance.MaintenanceTrackerService _maintenance;
    private readonly VehicleInfoService _vehicle;
    private readonly ProService _pro;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private readonly AppDatabase _database;
    private CarScanner.Models.VehicleInfo? _vehicleInfo;
    private double? _lastKnownMileageKm;
    private ActualMileageResult? _lastResult;
    private bool _showLog;

    public string Vin { get; set; } = "—";
    public string VehicleTitle { get; private set; } = string.Empty;
    public bool IsIntro => !HasResult && !IsBusy;

    // Summary card
    public bool HasResult { get; private set; }
    public string ResultTitle { get; private set; } = string.Empty;
    public Color ResultTitleColor { get; private set; } = Grey;
    public Color ResultBackground { get; private set; } = ThemeService.Soft("#FFFFFF");
    public Color CardStroke { get; private set; } = ThemeService.Soft("#DCDFE3");
    public string ResultValue { get; private set; } = "—";
    public bool HasResultValue => ResultValue.Length > 0 && ResultValue != "—";
    public string ResultSource { get; private set; } = string.Empty;
    public bool HasResultSource => ResultSource.Length > 0;

    // Lists
    public ObservableCollection<MileageReadingRow> Readings { get; } = new();
    public ObservableCollection<MileageConflictRow> Conflicts { get; } = new();
    public ObservableCollection<MileageReadingRow> Flagged { get; } = new();
    public ObservableCollection<MileageReadingRow> ContextReadings { get; } = new();
    public ObservableCollection<MileageAttemptRow> Attempts { get; } = new();

    // Records the modules keep of the past, and what they show next to the current readings
    public ObservableCollection<MileageReadingRow> HistoryReadings { get; } = new();
    public ObservableCollection<MileageFindingRow> Findings { get; } = new();
    public bool HasHistory => HistoryReadings.Count > 0;
    public bool HasFindings => Findings.Count > 0;
    public bool HasReadings => Readings.Count > 0;
    public bool HasConflicts => Conflicts.Count > 0;
    public bool HasFlagged => Flagged.Count > 0;
    public bool HasContext => ContextReadings.Count > 0;
    public bool HasAttempts => Attempts.Count > 0;

    // Log
    public string Log { get; set; } = string.Empty;
    public bool HasLog => !string.IsNullOrWhiteSpace(Log);
    public bool ShowLog
    {
        get => _showLog;
        set { _showLog = value; OnPropertyChanged(); OnPropertyChanged(nameof(LogToggleText)); }
    }

    public string CopyLogText { get; set; } = string.Empty;

    // Static texts
    public string PageTitleText => L("გარბენის მაჩვენებლები", "Mileage readings");
    public string IntroTitle => L("რას აცხადებს მანქანა გარბენის შესახებ", "What the car reports about its distance");
    public string IntroText => L(
        "აპი კითხულობს მხოლოდ იმ მაჩვენებლებს, რომლებსაც მანქანის მოდულები აცხადებენ, ინახავს მათ ცალ-ცალკე და ადარებს მხოლოდ ჯამურ ოდომეტრს. არაფერი იწერება მანქანაში და არაფერი ფასდება.",
        "The app reads only what the car's modules report, keeps each reading separate, and compares only total-odometer readings. Nothing is written to the car and nothing is estimated.");
    public string IntroHint => L("~30 წამი · ანთება ჩართული", "~30 seconds · ignition on");
    public string DisclaimerText => MileageWording.Disclaimer(_localization.IsEnglish);
    public string ReadingsCaption => L("ოდომეტრის მაჩვენებლები", "Odometer readings");
    public string ConflictsCaption => L("სხვაობა მაჩვენებლებს შორის", "Differences between readings");
    public string FlaggedCaption => L("გადასამოწმებელი მნიშვნელობები (არ გამოიყენება)", "Values to review (not used)");
    public string FindingsCaption => L("რას აჩვენებს შენახული ჩანაწერები", "What the stored records show");
    public string HistoryCaption => L("მოდულების წარსული ჩანაწერები", "Records the modules keep of the past");
    public string HistoryNote => L(
        "ეს წარსულის ჩანაწერებია და არა მიმდინარე მანძილი. ისინი მხოლოდ მიმდინარე ოდომეტრის მაჩვენებლებს ედარება და ერთმანეთს — არა.",
        "These are records of the past, not the current distance. They are compared only with the current odometer readings, never with each other.");
    public string ContextCaption => L("სხვა მანძილის და დროის მნიშვნელობები", "Other distance and time values");
    public string ContextNote => MileageWording.ContextNote(_localization.IsEnglish);
    public string AttemptsCaption => L("წყაროების სტატუსი", "Source status");
    public string CheckButtonText => (HasResult ? L("თავიდან წაკითხვა", "Read again") : L("მაჩვენებლების წაკითხვა", "Read mileage values"));
    public string LogToggleText => ShowLog ? L("ლოგის დამალვა", "Hide technical log") : L("ტექნიკური ლოგი ›", "Technical log ›");

    public IAsyncRelayCommand CheckCommand { get; }
    public IAsyncRelayCommand CopyLogCommand { get; }
    public IRelayCommand ToggleLogCommand { get; }

    public MileageViewModel(
        ActualMileageService mileage,
        VehicleInfoService vehicle,
        ProService pro,
        UnitSystemService units,
        LocalizationService localization,
        AppDatabase database,
        CarScanner.Services.Maintenance.MaintenanceTrackerService maintenance)
    {
        _database = database;
        _maintenance = maintenance;
        _mileage = mileage;
        _vehicle = vehicle;
        _pro = pro;
        _units = units;
        _localization = localization;
        Attach();

        CheckCommand = new AsyncRelayCommand(CheckAsync);
        CopyLogCommand = new AsyncRelayCommand(CopyLogAsync);
        ToggleLogCommand = new RelayCommand(() => ShowLog = !ShowLog);
        Present();
    }

    // Detached by the page when it goes away (no leak through singleton services).
    private bool _attached;
    private bool _refreshOnAttach;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _pro.ProChanged += HandleProChanged;
        _units.UnitSystemChanged += HandleUnitSystemChanged;
        _localization.LanguageChanged += HandleLanguageChanged;

        // Texts may have changed while the page was hidden (e.g. Settings on top).
        if (_refreshOnAttach)
        {
            _refreshOnAttach = false;
            HandleLanguageChanged(this, EventArgs.Empty);
            HandleProChanged(this, false);
        }
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _pro.ProChanged -= HandleProChanged;
        _units.UnitSystemChanged -= HandleUnitSystemChanged;
        _localization.LanguageChanged -= HandleLanguageChanged;
        _refreshOnAttach = true;
    }

    private void HandleProChanged(object? sender, bool isPro) =>
        MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(CheckButtonText)));

    private void HandleUnitSystemChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(Present);

    private void HandleLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(Present);

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    public async Task LoadVehicleContextAsync()
    {
        var vehicle = _vehicle.CurrentVehicle;
        if (vehicle?.VIN is not { Length: 17 })
        {
            try { vehicle = await _vehicle.IdentifyConnectedVehicleAsync(); }
            catch { }
        }

        Vin = vehicle?.VIN is { Length: 17 } knownVin ? knownVin : L("VIN მიუწვდომელია", "VIN unavailable");
        VehicleTitle = Describe(vehicle);
        OnPropertyChanged(nameof(Vin));
        OnPropertyChanged(nameof(VehicleTitle));
    }

    private string Describe(CarScanner.Models.VehicleInfo? vehicle)
    {
        var parts = new[] { vehicle?.Manufacturer, vehicle?.Model, vehicle?.Year }
            .Where(part => !string.IsNullOrWhiteSpace(part) && !part.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return parts.Length == 0 ? L("მანქანა", "Car") : string.Join(" ", parts);
    }

    private async Task CheckAsync()
    {
        if (IsBusy)
            return;

        // The mileage check is free for everyone (no PRO needed).
        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(IsIntro));
            StatusMessage = L("მანქანის ამოცნობა…", "Identifying the car…");
            var vehicle = await _vehicle.IdentifyConnectedVehicleAsync();
            _vehicleInfo = vehicle;
            Vin = vehicle.VIN;
            VehicleTitle = Describe(vehicle);
            OnPropertyChanged(nameof(Vin));
            OnPropertyChanged(nameof(VehicleTitle));

            StatusMessage = L("მოდულების მაჩვენებლების წაკითხვა… (30 წამამდე)", "Reading the values the modules report… (up to 30 s)");
            _lastKnownMileageKm = await LoadLastKnownMileageAsync(vehicle.VIN);

            var scan = await _mileage.ScanAsync(vehicle);
            _lastResult = scan;

            // The Service page counts distances from the same odometer — only when there is one unconflicted reading.
            if (scan.FinalMileageKm is { } finalKm && !scan.HasMismatch)
                _maintenance.SetCarOdometer(
                    CarScanner.Services.Maintenance.MaintenanceTrackerService.VehicleKey(vehicle),
                    finalKm);

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _lastResult = null;
            StatusMessage = L($"წაკითხვა ვერ მოხერხდა: {UserError.Describe(ex)}", $"Reading failed: {UserError.Describe(ex)}");
            Log = MileageTrace.Snapshot();
        }
        finally
        {
            IsBusy = false;
            Present();
            OnPropertyChanged(nameof(IsIntro));
            AddMileageToReport();
        }
    }

    /// <summary>The readings become the "mileage" part of the combined report — worded as reported values.</summary>
    private void AddMileageToReport()
    {
        var result = _lastResult;
        if (result is null || (result.Analysis.Odometer.Count == 0 && result.Analysis.Flagged.Count == 0))
            return;

        var analysis = result.Analysis;
        var section = new CarScanner.Services.Reports.ReportSectionItem
        {
            Kind = "mileage",
            Title = L("გარბენი — მოდულების მიერ მოწოდებული", "Mileage — as reported by the car"),
            Status = analysis.Outcome == MileageOutcome.Conflicting
                ? CarScanner.Services.Reports.ReportStatus.Warning
                : CarScanner.Services.Reports.ReportStatus.Info,
            Summary = string.Join(" · ", new[] { ResultValue, ResultTitle }
                .Where(part => !string.IsNullOrWhiteSpace(part) && part != "—").Distinct())
        };

        foreach (var reading in analysis.Odometer)
            section.Lines.Add(new() { Label = ReadingTitle(reading), Value = MileageWording.FormatValue(reading, _localization.IsEnglish) });
        section.Lines.Add(new() { Label = string.Empty, Value = MileageWording.Disclaimer(_localization.IsEnglish) });

        CarScanner.Services.Reports.VehicleReportService.Instance?.AddOrReplace(section);
    }

    // ------------------------------------------------------------------
    // Presentation
    // ------------------------------------------------------------------

    private string ReadingTitle(MileageEvidence evidence) => evidence.Ecu switch
    {
        "OBD-II" => L("OBD-II ინტერფეისი (მოპასუხე ECU ამოცნობილი არ არის)", "OBD-II interface (responding ECU not identified)"),
        "Engine ECU" => L("ძრავის ბლოკი", "Engine ECU"),
        "Instrument cluster" => L("ხელსაწყოთა პანელი", "Instrument cluster"),
        "Transmission" => L("კოლოფის ბლოკი", "Transmission"),
        var ecu when ecu.StartsWith("ECU ", StringComparison.Ordinal) => L($"მოდული {ecu[4..]}", $"Module {ecu[4..]}"),
        var ecu when ecu.StartsWith("CAN broadcast", StringComparison.Ordinal) => L("CAN თვითნაკადი შეტყობინება (პანელი, საჯარო აღწერით)", "CAN broadcast (instrument-cluster message per public definition)"),
        var ecu => ecu
    };

    private MileageReadingRow ToRow(MileageEvidence evidence, bool highlighted)
    {
        var en = _localization.IsEnglish;
        var converted = string.Empty;
        if (evidence.IsDistance)
        {
            var userUnitIsMiles = _units.DistanceUnit == "mi";
            if (evidence.Unit == "km" && userUnitIsMiles || evidence.Unit == "mi" && !userUnitIsMiles)
                converted = "≈ " + _units.FormatDistance(evidence.ValueKm, "0");
        }

        return new MileageReadingRow
        {
            Title = ReadingTitle(evidence),
            ValueText = MileageWording.FormatValue(evidence, en),
            ConvertedText = converted,
            MeasurementText = evidence.Measurement switch
            {
                MileageMeasurement.OdometerAtEvent => $"{MileageWording.EventKind(evidence.EventKind, en)}\n{evidence.SignalName}",
                MileageMeasurement.ModuleInstallRecord => $"{MileageWording.Measurement(evidence.Measurement, en)}\n{evidence.SignalName}",
                _ => MileageWording.Measurement(evidence.Measurement, en)
            },
            StatusText = evidence.Status == MileageReadStatus.Ok ? string.Empty : MileageWording.ReadStatus(evidence.Status, en),
            MethodText = $"{evidence.Method} · {evidence.RetrievedAt.ToLocalTime():HH:mm:ss}" +
                         (evidence.RawHex.Length > 0 ? $"\nraw {evidence.RawHex}" : string.Empty),
            SourceText = MileageWording.SourceKind(evidence.SourceKind, en),
            DefinitionText = evidence.DefinitionUnderTest ? MileageWording.DefinitionUnderTest(en) : string.Empty,
            IsHighlighted = highlighted,
            ValueColor = highlighted ? Amber : ThemeService.Soft("#1B1D20")
        };
    }

    private void Present()
    {
        Title = PageTitleText;
        var en = _localization.IsEnglish;
        var result = _lastResult;

        Readings.Clear();
        Conflicts.Clear();
        Flagged.Clear();
        ContextReadings.Clear();
        Attempts.Clear();
        HistoryReadings.Clear();
        Findings.Clear();

        HasResult = result is not null;
        if (result is null)
        {
            ResultTitle = string.Empty;
            ResultValue = "—";
            ResultSource = string.Empty;
        }
        else
        {
            var analysis = result.Analysis;
            var conflicted = new HashSet<MileageEvidence>(analysis.Conflicts.SelectMany(c => new[] { c.First, c.Second }));

            foreach (var reading in analysis.Odometer)
                Readings.Add(ToRow(reading, conflicted.Contains(reading)));
            foreach (var reading in analysis.Flagged)
                Flagged.Add(ToRow(reading, true));
            foreach (var reading in analysis.Context.Where(c => c.Status == MileageReadStatus.Ok))
                ContextReadings.Add(ToRow(reading, false));

            foreach (var conflict in analysis.Conflicts)
            {
                var sameUnit = conflict.First.Unit == conflict.Second.Unit;
                var difference = sameUnit ? $"{_units.FormatDistance(conflict.DifferenceKm, "0.#")}" : $"{_units.FormatDistance(conflict.DifferenceKm, "0.#")} ({L("გადათვლილი", "converted")})";
                Conflicts.Add(new MileageConflictRow
                {
                    Text = $"{ReadingTitle(conflict.First)} {MileageWording.FormatValue(conflict.First, en)} {ReadingTitle(conflict.Second)} {MileageWording.FormatValue(conflict.Second, en)}  =  {difference}"
                });
            }

            // Records of the past: each row says which module stored it and when; a row that a finding points at is highlighted.
            var pointedAt = new HashSet<MileageEvidence>(analysis.Findings
                .Where(f => f.Code is MileageFindingCode.HistoricalExceedsCurrent or MileageFindingCode.ModuleRecordExceedsCurrent)
                .SelectMany(f => f.Evidence.Where(e => e.Measurement != MileageMeasurement.TotalOdometer)));
            foreach (var record in analysis.History.Concat(analysis.InstallRecords))
                HistoryReadings.Add(ToRow(record, pointedAt.Contains(record)));

            foreach (var finding in analysis.Findings.Where(f => f.Code is
                         MileageFindingCode.HistoricalExceedsCurrent or MileageFindingCode.ModuleRecordExceedsCurrent or
                         MileageFindingCode.HistoryNotAboveCurrent or MileageFindingCode.NoCurrentOdometer))
            {
                // The English template goes through the translation table first, then the amount is put in.
                var template = MileageWording.FindingTemplate(finding.Code, en);
                var amount = finding.DifferenceKm is { } difference ? _units.FormatDistance(difference, "0.#") : string.Empty;
                Findings.Add(new MileageFindingRow
                {
                    Title = en ? LocalizationService.T(MileageWording.FindingTitle(finding.Code, true)) : MileageWording.FindingTitle(finding.Code, false),
                    Text = string.Format(CultureInfo.InvariantCulture, en ? LocalizationService.T(template) : template, amount),
                    TitleColor = finding.Level switch
                    {
                        MileageFindingLevel.Attention => Amber,
                        MileageFindingLevel.Info => Blue,
                        _ => Grey
                    }
                });
            }

            foreach (var attempt in result.Attempts)
                Attempts.Add(ToAttemptRow(attempt, en));

            ResultTitle = analysis.Outcome == MileageOutcome.NoReading
                ? L("ოდომეტრი ვერ მოიძებნა", "Odometer not found")
                : L("ოდომეტრი:", "Odometer:");

            switch (analysis.Outcome)
            {
                case MileageOutcome.Consistent:
                case MileageOutcome.SingleSource:
                    var primary = analysis.Primary!;
                    ResultValue = MileageWording.FormatValue(primary, en);
                    ResultSource = $"{ReadingTitle(primary)} · {primary.Method}";
                    ResultTitleColor = analysis.Outcome == MileageOutcome.Consistent ? Blue : Grey;
                    ResultBackground = ThemeService.Soft(analysis.Outcome == MileageOutcome.Consistent ? "#EAF1FD" : "#FFFFFF");
                    CardStroke = analysis.Outcome == MileageOutcome.Consistent ? Blue : ThemeService.Soft("#DCDFE3");
                    break;

                case MileageOutcome.Conflicting:
                    // No reading is promoted: the range is shown, every reading sits in the list below.
                    ResultValue = $"{_units.FormatDistance(analysis.Odometer.Min(r => r.ValueKm), "0")} – {_units.FormatDistance(analysis.Odometer.Max(r => r.ValueKm), "0")}";
                    ResultSource = L($"სხვაობა: {_units.FormatDistance(analysis.SpreadKm ?? 0, "0.#")} · დასაშვები: {_units.FormatDistance(analysis.ToleranceKm, "0.#")}",
                        $"Difference: {_units.FormatDistance(analysis.SpreadKm ?? 0, "0.#")} · tolerance: {_units.FormatDistance(analysis.ToleranceKm, "0.#")}");
                    ResultTitleColor = Amber;
                    ResultBackground = ThemeService.Soft("#FDF1D8");
                    CardStroke = Color.FromArgb("#E0A530");
                    break;

                default:
                    // An older stored value is never shown as this scan's result.
                    ResultValue = "—";
                    ResultSource = string.Empty;
                    ResultTitleColor = Grey;
                    ResultBackground = ThemeService.Soft("#FFFFFF");
                    CardStroke = ThemeService.Soft("#DCDFE3");
                    break;
            }
        }

        if (result is not null)
            Log = result.Log;
        CopyLogText = L("ლოგის კოპირება", "Copy log");

        // Everything on the page depends on the result and the language.
        OnPropertyChanged(string.Empty);
    }

    private MileageAttemptRow ToAttemptRow(MileageAttempt attempt, bool en)
    {
        var outcome = attempt.Outcome switch
        {
            MileageAttemptOutcome.Read => (en ? "Read" : "წაკითხულია", Green),
            MileageAttemptOutcome.NotTried => (en ? "Not requested" : "არ მოუთხოვიათ", Grey),
            MileageAttemptOutcome.ModuleNotResponding => (en ? "Module not responding" : "მოდული არ პასუხობს", Amber),
            MileageAttemptOutcome.ReadingUnavailable => (en ? "Reading unavailable" : "მაჩვენებელი მიუწვდომელია", Amber),
            _ => (en ? "Invalid or unverified reading" : "არასწორი ან დაუდასტურებელი მაჩვენებელი", ThemeService.Soft("#B3361F"))
        };

        var detail = new List<string>();
        if (attempt.Compatibility is { } compatibility)
            detail.Add(MileageWording.SourceStatus(compatibility, en));
        if (attempt.Reason is { } reason)
            detail.Add(MileageWording.SourceReason(reason, en));

        return new MileageAttemptRow
        {
            Title = string.IsNullOrWhiteSpace(attempt.Method) ? attempt.Source : $"{attempt.Source} · {attempt.Method}",
            OutcomeText = outcome.Item1,
            OutcomeColor = outcome.Item2,
            DetailText = string.Join(" — ", detail)
        };
    }

    private async Task CopyLogAsync()
    {
        if (!HasLog) return;
        try
        {
            // The log holds requests and answers, not the VIN.
            await Clipboard.Default.SetTextAsync($"OBD360 mileage log\n{Log}");
            CopyLogText = L("დაკოპირდა", "Copied");
        }
        catch
        {
            CopyLogText = L("კოპირება ვერ მოხერხდა", "Could not copy");
        }
        OnPropertyChanged(nameof(CopyLogText));
    }

    private async Task<double?> LoadLastKnownMileageAsync(string? vin)
    {
        if (string.IsNullOrWhiteSpace(vin))
            return null;

        try
        {
            foreach (var record in await _database.GetMileageAsync(vin))
            {
                var text = (record.Value ?? string.Empty).Replace("km", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(',', '.').Trim();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var km) && km is > 0 and < 2_000_000)
                    return km;
            }
        }
        catch
        {
        }

        return null;
    }
}
