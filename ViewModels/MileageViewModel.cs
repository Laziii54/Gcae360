using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Database;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.Mileage;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One module that returned a distance (Mileage page table).</summary>
public sealed class MileageModuleRow
{
    public string Name { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
    public string DiffText { get; init; } = string.Empty;
    public bool IsOff { get; init; }
    public Color DiffColor { get; init; } = Colors.Gray;
    public Color ValueColor => IsOff ? CarScanner.Services.Localization.ThemeService.Soft("#B3361F") : CarScanner.Services.Localization.ThemeService.Soft("#1B1D20");
    public Color RowBackground => IsOff ? CarScanner.Services.Localization.ThemeService.Soft("#FCEDEA") : Colors.Transparent;
}

/// <summary>One place the odometer finder found (Mileage page).</summary>
public sealed class OdometerMatchRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public OdometerMatch Match { get; init; } = null!;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    private bool _isSelected = true;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>
/// Mileage check: reads the distance stored in every module the car exposes
/// (engine ECU, cluster, transmission, ABS …) and compares them. A rolled-back
/// dashboard usually still leaves the original value in other modules.
/// Only values the car returned are shown; nothing is estimated.
/// </summary>
public class MileageViewModel : BaseViewModel
{
    private static Color Green => CarScanner.Services.Localization.ThemeService.Soft("#1F8A4C");
    private static Color Amber => CarScanner.Services.Localization.ThemeService.Soft("#B06A00");
    private static Color Grey => CarScanner.Services.Localization.ThemeService.Soft("#5D636B");

    private readonly ActualMileageService _mileage;
    private readonly CarScanner.Services.Maintenance.MaintenanceTrackerService _maintenance;
    private readonly VehicleInfoService _vehicle;
    private readonly ProService _pro;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private readonly AppDatabase _database;
    private readonly OdometerFinderService _finder;
    private CancellationTokenSource? _finderCts;
    private CarScanner.Models.VehicleInfo? _vehicleInfo;
    private double? _lastKnownMileageKm;
    private ActualMileageResult? _lastResult;
    private string _dashboardMileage = string.Empty;
    private bool _showLog;

    public string Vin { get; set; } = "—";
    public string VehicleTitle { get; private set; } = string.Empty;
    public bool IsIntro => !HasResult && !IsBusy;

    // Simple result card (title, number, one sentence)
    public string ResultTitle { get; private set; } = string.Empty;
    public Color ResultTitleColor { get; private set; } = Grey;
    public Color ResultBackground { get; private set; } = CarScanner.Services.Localization.ThemeService.Soft("#FFFFFF");

    // Result card
    public bool HasResult { get; private set; }
    public string ResultCaption { get; private set; } = string.Empty;
    public string ResultValue { get; private set; } = "—";
    public string VerdictText { get; private set; } = string.Empty;
    public bool HasVerdict => !string.IsNullOrWhiteSpace(VerdictText);
    public Color VerdictColor { get; private set; } = Grey;
    public Color VerdictBackground { get; private set; } = CarScanner.Services.Localization.ThemeService.Soft("#ECEEF0");
    public Color CardStroke { get; private set; } = CarScanner.Services.Localization.ThemeService.Soft("#DCDFE3");
    public string VerdictNote { get; private set; } = string.Empty;

    // Modules
    public ObservableCollection<MileageModuleRow> Modules { get; } = new();
    public bool HasModules => Modules.Count > 0;

    // Dashboard comparison
    public string DashboardMileage
    {
        get => _dashboardMileage;
        set
        {
            if (_dashboardMileage == value)
                return;
            _dashboardMileage = value;
            OnPropertyChanged();
            UpdateDashboardComparison();
        }
    }

    public string DashboardUnit => _units.DistanceUnit;
    public string DashboardResult { get; private set; } = string.Empty;
    public Color DashboardResultColor { get; private set; } = Grey;
    public bool HasDashboardResult => !string.IsNullOrWhiteSpace(DashboardResult);

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
    public string PageTitleText => L("გარბენის გადამოწმება", "Mileage check");
    public string ModulesCaption => L("მოდულები", "Modules");
    public string IntroTitle => L("შევამოწმოთ, ნამდვილია თუ არა გარბენი", "Let's check whether the mileage is real");
    public string IntroText => L(
        "აპი გარბენს რამდენიმე ბლოკიდან კითხულობს და ადარებს — გადახვევისას ისინი ერთმანეთს არ ემთხვევა.",
        "The app reads the distance from several modules and compares them — after a rollback they no longer match.");
    public string IntroHint => L("~30 წამი · ანთება ჩართული", "~30 seconds · ignition on");
    public string DashboardCaption => L("დაფაზე რას აჩვენებს?", "What does the dashboard show?");
    public string DashboardPlaceholder => L("მაგ. 125 690", "e.g. 125 690");
    public string CheckButtonText => (HasResult ? L("თავიდან შემოწმება", "Check again") : L("გარბენის შემოწმება", "Check mileage")) + ProGate.Suffix;
    public string LogToggleText => ShowLog ? L("ლოგის დამალვა", "Hide technical log") : L("ტექნიკური ლოგი ›", "Technical log ›");
    public string DisclaimerText => L(
        "ეს მინიშნებაა და არა საბოლოო მტკიცებულება: თუ გარბენი ყველა მოდულში შეცვალეს, აპი ამას ვერ დაინახავს. მოდულები, რომლებსაც მანქანა არ აძლევს, არ ჩანს.",
        "This is an indication, not final proof: if the distance was changed in every module, the app cannot see it. Modules the car does not expose are not shown.");

    // Odometer finder (when this model's odometer place is not known yet)
    public ObservableCollection<OdometerMatchRow> FinderMatches { get; } = new();
    public bool IsFinding { get; private set; }
    public double FinderProgress { get; private set; }
    public string FinderStatus { get; private set; } = string.Empty;
    public bool HasFinderStatus => !string.IsNullOrWhiteSpace(FinderStatus);
    public bool HasFinderMatches => FinderMatches.Count > 0;
    public bool ShowFinder => HasResult && !IsBusy && (_lastResult is null || _lastResult.FinalMileageKm is null ||
        _lastResult.Evidence.Select(e => e.Ecu).Distinct().Count() < 2);
    public string FinderTitle => _lastResult?.FinalMileageKm is null
        ? L("ამ მანქანაზე გარბენის ადგილი ჯერ უცნობია — მოვძებნოთ", "This car's odometer place is not known yet — let's find it")
        : L("მეტი მოდულის მოძებნა შესადარებლად", "Find more modules to compare");
    public string FinderText => L(
        "ჩაწერე ზემოთ დაფაზე ნაჩვენები გარბენი და დააჭირე „ძებნას“. აპი მოდულებში ამ რიცხვს ეძებს (მხოლოდ კითხულობს, 2–6 წუთი, ანთება ჩართული). ნაპოვნი ადგილი ამ მოდელზე ყველასთვის იმუშავებს — იგზავნება მხოლოდ ადგილი, არა შენი გარბენი.",
        "Type the distance shown on the dashboard above and press Search. The app looks for that number in the modules (read-only, 2–6 minutes, ignition on). The place it finds then works for everyone with this model — only the place is sent, never your distance.");
    public string FinderButtonText => IsFinding ? L("გაჩერება", "Stop") : L("ძებნა", "Search");
    public string SaveFoundText => L("შენახვა და გაზიარება", "Save and share");

    public IAsyncRelayCommand SearchOdometerCommand { get; }
    public IAsyncRelayCommand SaveFoundCommand { get; }

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
        CarScanner.Services.Maintenance.MaintenanceTrackerService maintenance,
        OdometerFinderService finder)
    {
        _database = database;
        _finder = finder;
        SearchOdometerCommand = new AsyncRelayCommand(SearchOdometerAsync);
        SaveFoundCommand = new AsyncRelayCommand(SaveFoundAsync);
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

        // The page is open for everyone; the check itself is PRO.
        if (!await ProGate.EnsureOrOpenAsync(_pro))
            return;

        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(IsIntro));
            StatusMessage = L("მანქანის ამოცნობა…", "Identifying the car…");
            var vehicle = await _vehicle.IdentifyConnectedVehicleAsync();
            _vehicleInfo = vehicle;
            FinderMatches.Clear();
            Vin = vehicle.VIN;
            VehicleTitle = Describe(vehicle);
            OnPropertyChanged(nameof(Vin));
            OnPropertyChanged(nameof(VehicleTitle));

            StatusMessage = L("ყველა მოდულის გარბენის კითხვა… (30 წამამდე)", "Reading the distance from every module… (up to 30 s)");
            _lastKnownMileageKm = await LoadLastKnownMileageAsync(vehicle.VIN);

            var scan = await _mileage.ScanAsync(vehicle);
            _lastResult = scan;

            // The Service page counts distances from the same odometer.
            if (scan.FinalMileageKm is { } finalKm && !scan.HasMismatch)
                _maintenance.SetCarOdometer(
                    CarScanner.Services.Maintenance.MaintenanceTrackerService.VehicleKey(vehicle),
                    finalKm);

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _lastResult = null;
            StatusMessage = L($"შემოწმება ვერ მოხერხდა: {ex.Message}", $"The check failed: {ex.Message}");
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

    /// <summary>The mileage check becomes the "mileage" part of the combined report.</summary>
    private void AddMileageToReport()
    {
        var result = _lastResult;
        if (result is null || (result.FinalMileageKm is null && result.Evidence.Count == 0))
            return;

        var section = new CarScanner.Services.Reports.ReportSectionItem
        {
            Kind = "mileage",
            Icon = "⏱",
            Title = L("რეალური გარბენი", "Real mileage"),
            // One source cannot be compared with anything: neutral information.
            Status = result.HasMismatch
                ? CarScanner.Services.Reports.ReportStatus.Bad
                : result.SourceCount >= 2
                    ? CarScanner.Services.Reports.ReportStatus.Ok
                    : CarScanner.Services.Reports.ReportStatus.Info,
            Summary = string.Join(" · ", new[] { ResultValue, ResultTitle, VerdictText }
                .Where(part => !string.IsNullOrWhiteSpace(part) && part != "—").Distinct())
        };

        foreach (var module in Modules)
            section.Lines.Add(new() { Label = module.Name, Value = string.Join(" · ", new[] { module.ValueText, module.DiffText }.Where(p => !string.IsNullOrWhiteSpace(p))) });
        if (!string.IsNullOrWhiteSpace(VerdictNote))
            section.Lines.Add(new() { Label = string.Empty, Value = VerdictNote });

        CarScanner.Services.Reports.VehicleReportService.Instance?.AddOrReplace(section);
    }

    // ------------------------------------------------------------------
    // Presentation
    // ------------------------------------------------------------------

    /// <summary>"Generic OBD-II" (PID A6) and the catalog's engine ECU are the same module.</summary>
    private static string ModuleKey(MileageEvidence evidence) =>
        evidence.Ecu is "Generic OBD-II" ? "Engine ECU" : evidence.Ecu;

    private string ModuleName(string ecu) => ecu switch
    {
        "Engine ECU" or "Generic OBD-II" => L("ძრავი", "Engine"),
        "Instrument cluster" => L("პანელი", "Instrument cluster"),
        "Transmission" => L("კოლოფი", "Transmission"),
        _ when ecu.StartsWith("ECU ", StringComparison.Ordinal) => L($"მოდული {ecu[4..]}", $"Module {ecu[4..]}"),
        _ => ecu
    };

    private void Present()
    {
        Title = PageTitleText;
        var result = _lastResult;
        Modules.Clear();

        if (result is null || (result.FinalMileageKm is null && result.Evidence.Count == 0))
        {
            HasResult = result is not null;
            ResultCaption = L("გარბენი", "Mileage");
            ResultValue = result is not null && _lastKnownMileageKm is { } known ? _units.FormatDistance(known) : "—";
            ResultTitle = L("გარბენი ვერ წავიკითხე", "No distance came back");
            ResultTitleColor = Grey;
            ResultBackground = CarScanner.Services.Localization.ThemeService.Soft("#FFFFFF");
            VerdictText = result is null ? string.Empty : ResultTitle;
            VerdictColor = Grey;
            VerdictBackground = CarScanner.Services.Localization.ThemeService.Soft("#ECEEF0");
            CardStroke = CarScanner.Services.Localization.ThemeService.Soft("#DCDFE3");
            VerdictNote = result is null
                ? string.Empty
                : L("შეამოწმე, რომ ანთება ჩართულია. ზოგი მანქანა გარბენს OBD-ით საერთოდ არ აძლევს." + (_lastKnownMileageKm is not null ? " ნაჩვენებია ბოლოს წაკითხული მნიშვნელობა." : string.Empty),
                    "Check that the ignition is on. Some cars do not give the distance over OBD at all." + (_lastKnownMileageKm is not null ? " The last value read before is shown." : string.Empty));
        }
        else
        {
            HasResult = true;
            var final = result.FinalMileageKm ?? result.Evidence.Max(e => e.ValueKm);
            var tolerance = result.ConsistencyToleranceKm ?? Math.Clamp(final * 0.001, 5, 250);

            // One row per module, the most trusted value of that module.
            var modules = result.Evidence
                .GroupBy(ModuleKey)
                .Select(group => group.OrderByDescending(e => e.Confidence).First())
                .OrderByDescending(e => e.ValueKm)
                .ToList();

            foreach (var evidence in modules)
            {
                var diff = evidence.ValueKm - final;
                var off = Math.Abs(diff) > tolerance;
                Modules.Add(new MileageModuleRow
                {
                    Name = ModuleName(evidence.Ecu),
                    ValueText = _units.FormatDistance(evidence.ValueKm),
                    SourceText = ShortSource(evidence),
                    DiffText = Math.Abs(diff) < 0.5 ? "—" : (diff > 0 ? "+" : "−") + _units.FormatDistance(Math.Abs(diff)),
                    IsOff = off,
                    DiffColor = off ? Amber : Green
                });
            }

            var moduleCount = modules.Count;
            if (modules.Count == 0)
            {
                // Only the standard odometer fallback answered.
                Modules.Add(new MileageModuleRow
                {
                    Name = ModuleName("Engine ECU"),
                    ValueText = _units.FormatDistance(final),
                    SourceText = "Standard OBD · 01 A6",
                    DiffText = "—",
                    DiffColor = Green
                });
                moduleCount = 1;
            }

            ResultValue = _units.FormatDistance(final);
            if (result.HasMismatch && modules.Count > 0)
            {
                var low = modules.Min(e => e.ValueKm);
                var high = modules.Max(e => e.ValueKm);
                // The module that disagrees with the majority (or the highest when there is no majority).
                var odd = modules.Where(e => Math.Abs(e.ValueKm - final) > tolerance).OrderByDescending(e => Math.Abs(e.ValueKm - final)).FirstOrDefault()
                          ?? modules.First(e => e.ValueKm == high);
                var gap = Math.Abs(odd.ValueKm - final);
                ResultCaption = L("ორიგინალი გარბენი, სავარაუდოდ", "Original mileage, likely");
                ResultTitle = L("⚠ გარბენი არ ემთხვევა", "⚠ The mileage does not match");
                ResultTitleColor = CarScanner.Services.Localization.ThemeService.Soft("#B3361F");
                ResultBackground = CarScanner.Services.Localization.ThemeService.Soft("#FCEDEA");
                ResultValue = $"{_units.FormatDistance(low)} / {_units.FormatDistance(high)}";
                VerdictText = ResultTitle;
                VerdictColor = Amber;
                VerdictBackground = CarScanner.Services.Localization.ThemeService.Soft("#FDF1D8");
                CardStroke = Color.FromArgb("#E0A530");
                VerdictNote = odd.ValueKm > final
                    ? L($"{ModuleName(odd.Ecu)}-ში {_units.FormatDistance(gap)}-ით მეტია. ასეთი სხვაობა გადახვევის ნიშანია — სანამ იყიდი, გადაამოწმე სერვისში.",
                        $"{ModuleName(odd.Ecu)} shows {_units.FormatDistance(gap)} more. A gap like this is a sign of a rollback — have it checked before you buy.")
                    : L($"{ModuleName(odd.Ecu)}-ში {_units.FormatDistance(gap)}-ით ნაკლებია. შესაძლოა ეს მოდული შეცვლილია ან გარბენი გადახვეულია — სანამ იყიდი, გადაამოწმე სერვისში.",
                        $"{ModuleName(odd.Ecu)} shows {_units.FormatDistance(gap)} less. The module may have been replaced, or the mileage rolled back — have it checked before you buy.");
            }
            else if (moduleCount >= 2)
            {
                ResultCaption = L("ორიგინალი გარბენი", "Original mileage");
                ResultTitle = L("✓ მოდულები თანხმდება", "✓ Modules agree");
                ResultTitleColor = Green;
                ResultBackground = CarScanner.Services.Localization.ThemeService.Soft("#E8F5EE");
                VerdictText = ResultTitle;
                VerdictColor = Green;
                VerdictBackground = CarScanner.Services.Localization.ThemeService.Soft("#E8F5EE");
                CardStroke = Color.FromArgb("#22C55E");
                VerdictNote = L(
                    $"{moduleCount} ბლოკი აჩვენებს ერთნაირს. მოდულები თანხმდება, მაგრამ ეს პროფესიონალურ გადახვევას სრულად არ გამორიცხავს — შეადარე სერვისის ჩანაწერებს / VIN ისტორიას.",
                    $"{moduleCount} modules show the same distance. Modules agree. This does not fully rule out a professional rollback — compare with service records/VIN history.");
            }
            else
            {
                ResultCaption = L("გარბენი", "Mileage");
                ResultTitle = L("გარბენი", "Mileage");
                ResultTitleColor = Grey;
                ResultBackground = CarScanner.Services.Localization.ThemeService.Soft("#FFFFFF");
                VerdictText = L("მხოლოდ 1 წყარო", "Only 1 source");
                VerdictColor = Grey;
                VerdictBackground = CarScanner.Services.Localization.ThemeService.Soft("#ECEEF0");
                CardStroke = CarScanner.Services.Localization.ThemeService.Soft("#DCDFE3");
                VerdictNote = L(
                    "ამ მანქანაზე გარბენს მხოლოდ ერთი ბლოკი აძლევს, ამიტომ შედარება შეუძლებელია. შეადარე პანელის ციფრს.",
                    "Only one module of this car gives the distance, so nothing can be compared. Compare it with the dashboard.");
            }
        }

        if (result is not null)
            Log = result.Log;
        CopyLogText = L("ლოგის კოპირება", "Copy log");

        // Everything on the page depends on the result and the language.
        OnPropertyChanged(string.Empty);
        UpdateDashboardComparison();
    }

    private static string ShortSource(MileageEvidence evidence)
    {
        if (evidence.Source.Contains("01 A6", StringComparison.Ordinal))
            return "Standard OBD · 01 A6";
        var detail = evidence.Details ?? string.Empty;
        var cut = detail.IndexOf(" · ", StringComparison.Ordinal);
        return cut > 0 ? detail[..cut] : evidence.Source;
    }

    private void UpdateDashboardComparison()
    {
        var dashboardKm = TryGetDashboardMileageKm();
        var final = _lastResult?.FinalMileageKm;
        if (dashboardKm is null)
        {
            DashboardResult = string.Empty;
        }
        else if (final is null)
        {
            DashboardResult = L("მანქანიდან გარბენი არ წაკითხულა — შესადარებელი არაფერია.", "No distance was read from the car, nothing to compare.");
            DashboardResultColor = Grey;
        }
        else
        {
            var tolerance = Math.Max(5d, final.Value * 0.002d);
            var diff = dashboardKm.Value - final.Value;
            if (Math.Abs(diff) <= tolerance)
            {
                DashboardResult = L("✓ დაფა ემთხვევა მოდულებს.", "✓ The dashboard matches the modules.");
                DashboardResultColor = Green;
            }
            else if (diff < 0)
            {
                DashboardResult = L(
                    $"⚠ დაფა {_units.FormatDistance(-diff)}-ით ნაკლებს აჩვენებს, ვიდრე მოდულებს ახსოვს.",
                    $"⚠ The dashboard shows {_units.FormatDistance(-diff)} less than the modules remember.");
                DashboardResultColor = Amber;
            }
            else
            {
                DashboardResult = L(
                    $"დაფა {_units.FormatDistance(diff)}-ით მეტს აჩვენებს. შეამოწმე ერთეული (km / mi) — მოდულები ზოგჯერ ძველ ციფრს ინახავს.",
                    $"The dashboard shows {_units.FormatDistance(diff)} more. Check the unit (km / mi); some modules keep an older value.");
                DashboardResultColor = Grey;
            }
        }

        OnPropertyChanged(nameof(DashboardResult));
        OnPropertyChanged(nameof(DashboardResultColor));
        OnPropertyChanged(nameof(HasDashboardResult));
    }

    // ------------------------------------------------------------------
    // Odometer finder
    // ------------------------------------------------------------------

    private void NotifyFinder()
    {
        foreach (var name in new[] { nameof(IsFinding), nameof(FinderProgress), nameof(FinderStatus), nameof(HasFinderStatus),
                     nameof(HasFinderMatches), nameof(ShowFinder), nameof(FinderButtonText), nameof(FinderTitle), nameof(FinderText), nameof(SaveFoundText) })
            OnPropertyChanged(name);
    }

    private async Task SearchOdometerAsync()
    {
        if (IsFinding)
        {
            _finderCts?.Cancel();
            return;
        }

        var dashboardKm = TryGetDashboardMileageKm();
        if (dashboardKm is null or < 100)
        {
            FinderStatus = L("ჯერ ჩაწერე დაფაზე ნაჩვენები გარბენი.", "Type the dashboard distance first.");
            NotifyFinder();
            return;
        }

        if (!await ProGate.EnsureOrOpenAsync(_pro))
            return;

        try
        {
            IsFinding = true;
            FinderMatches.Clear();
            FinderStatus = L("ძებნა დაიწყო…", "Search started…");
            NotifyFinder();
            _finderCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var vehicle = _vehicleInfo ?? await _vehicle.IdentifyConnectedVehicleAsync();
            _vehicleInfo = vehicle;

            var progress = new Progress<(double Fraction, string Text)>(p =>
            {
                FinderProgress = p.Fraction;
                if (!string.IsNullOrWhiteSpace(p.Text))
                    FinderStatus = p.Text;
                NotifyFinder();
            });
            var matches = await _finder.SearchAsync(vehicle, dashboardKm.Value, progress, _finderCts.Token);
            foreach (var match in matches)
            {
                FinderMatches.Add(new OdometerMatchRow
                {
                    Match = match,
                    Title = $"{match.Location.Module} — {_units.FormatDistance(match.ValueKm)}",
                    Detail = $"{match.Location.RequestText} · {L("ბაიტები", "bytes")} {match.Location.Offset}+{match.Location.Length}" +
                             (match.Location.Miles ? " · mi" : string.Empty),
                    IsSelected = Math.Abs(match.ValueKm - dashboardKm.Value) <= Math.Max(2, dashboardKm.Value * 0.002)
                });
            }

            FinderStatus = matches.Count == 0
                ? L("ვერ მოიძებნა: ამ მანქანის მოდულები გარბენს წაკითხვით არ აძლევს, ან სხვა ადგილას ინახავს. ციფრი გამოგონილი არ არის.",
                    "Not found: this car's modules do not give the distance on reading, or keep it elsewhere. Nothing is guessed.")
                : L($"ნაპოვნია {matches.Count} ადგილი. მონიშნე, რომლებიც სწორია, და შეინახე.", $"Found {matches.Count} place(s). Tick the correct ones and save.");
        }
        catch (OperationCanceledException)
        {
            FinderStatus = L("ძებნა გაჩერდა.", "Search stopped.");
        }
        catch (Exception ex)
        {
            FinderStatus = ex is InvalidOperationException ? ex.Message : L("ძებნა ვერ მოხერხდა.", "The search failed.");
            CommandErrors.Report(ex, "Mileage.SearchOdometer");
        }
        finally
        {
            IsFinding = false;
            FinderProgress = 0;
            NotifyFinder();
        }
    }

    private async Task SaveFoundAsync()
    {
        try
        {
            var chosen = FinderMatches.Where(row => row.IsSelected).Select(row => row.Match.Location).ToList();
            if (chosen.Count == 0 || _vehicleInfo is null)
                return;

            _finder.SaveLocal(_vehicleInfo, chosen);
            var shared = await _finder.ReportAsync(_vehicleInfo, chosen);
            FinderMatches.Clear();
            FinderStatus = shared
                ? L("შენახულია და გაზიარებულია ✓ ახლა გარბენი თავიდან შემოწმდება.", "Saved and shared ✓ Checking the mileage again now.")
                : L("შენახულია ამ ტელეფონზე ✓ (სერვერთან კავშირი ვერ მოხერხდა). ახლა გარბენი თავიდან შემოწმდება.", "Saved on this phone ✓ (could not reach the server). Checking the mileage again now.");
            NotifyFinder();
            await CheckAsync();
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Mileage.SaveFound");
        }
    }

    private async Task CopyLogAsync()
    {
        if (!HasLog) return;
        try
        {
            await Clipboard.Default.SetTextAsync($"GCar360 mileage log\nVIN {Vin}\n{Log}");
            CopyLogText = L("დაკოპირდა ✓", "Copied ✓");
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

    private double? TryGetDashboardMileageKm()
    {
        // Odometers are whole numbers: spaces, dots and commas are thousand separators.
        var digits = new string((DashboardMileage ?? string.Empty).Where(char.IsDigit).ToArray());
        return double.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value is > 0 and < 3_000_000
            ? _units.DistanceToKilometers(value)
            : null;
    }
}
