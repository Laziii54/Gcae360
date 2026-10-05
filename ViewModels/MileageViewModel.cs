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
    public Color ValueColor => IsOff ? Color.FromArgb("#B3361F") : Color.FromArgb("#1B1D20");
    public Color RowBackground => IsOff ? Color.FromArgb("#FCEDEA") : Colors.Transparent;
}

/// <summary>
/// Mileage check: reads the distance stored in every module the car exposes
/// (engine ECU, cluster, transmission, ABS …) and compares them. A rolled-back
/// dashboard usually still leaves the original value in other modules.
/// Only values the car returned are shown; nothing is estimated.
/// </summary>
public class MileageViewModel : BaseViewModel
{
    private static readonly Color Green = Color.FromArgb("#1F8A4C");
    private static readonly Color Amber = Color.FromArgb("#B06A00");
    private static readonly Color Grey = Color.FromArgb("#5D636B");

    private readonly ActualMileageService _mileage;
    private readonly CarScanner.Services.Maintenance.MaintenanceTrackerService _maintenance;
    private readonly VehicleInfoService _vehicle;
    private readonly ProService _pro;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private readonly AppDatabase _database;
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
    public Color ResultBackground { get; private set; } = Color.FromArgb("#FFFFFF");

    // Result card
    public bool HasResult { get; private set; }
    public string ResultCaption { get; private set; } = string.Empty;
    public string ResultValue { get; private set; } = "—";
    public string VerdictText { get; private set; } = string.Empty;
    public bool HasVerdict => !string.IsNullOrWhiteSpace(VerdictText);
    public Color VerdictColor { get; private set; } = Grey;
    public Color VerdictBackground { get; private set; } = Color.FromArgb("#ECEEF0");
    public Color CardStroke { get; private set; } = Color.FromArgb("#DCDFE3");
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
    public string CheckButtonText => HasResult ? L("თავიდან შემოწმება", "Check again") : L("გარბენის შემოწმება", "Check mileage");
    public string LogToggleText => ShowLog ? L("ლოგის დამალვა", "Hide technical log") : L("ტექნიკური ლოგი ›", "Technical log ›");
    public string DisclaimerText => L(
        "ეს მინიშნებაა და არა საბოლოო მტკიცებულება: თუ გარბენი ყველა მოდულში შეცვალეს, აპი ამას ვერ დაინახავს. მოდულები, რომლებსაც მანქანა არ აძლევს, არ ჩანს.",
        "This is an indication, not final proof: if the distance was changed in every module, the app cannot see it. Modules the car does not expose are not shown.");

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
        _units.UnitSystemChanged += (_, __) => MainThread.BeginInvokeOnMainThread(Present);
        _localization.LanguageChanged += (_, __) => MainThread.BeginInvokeOnMainThread(Present);

        CheckCommand = new AsyncRelayCommand(CheckAsync);
        CopyLogCommand = new AsyncRelayCommand(CopyLogAsync);
        ToggleLogCommand = new RelayCommand(() => ShowLog = !ShowLog);
        Present();
    }

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

        if (!await _pro.EnsureProAsync())
        {
            await Shell.Current.DisplayAlertAsync(
                "PRO",
                L("გარბენის გადამოწმება PRO-შია.", "Mileage check is a PRO feature."),
                "OK");
            return;
        }

        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(IsIntro));
            StatusMessage = L("მანქანის ამოცნობა…", "Identifying the car…");
            var vehicle = await _vehicle.IdentifyConnectedVehicleAsync();
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
        }
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
            ResultBackground = Color.FromArgb("#FFFFFF");
            VerdictText = result is null ? string.Empty : ResultTitle;
            VerdictColor = Grey;
            VerdictBackground = Color.FromArgb("#ECEEF0");
            CardStroke = Color.FromArgb("#DCDFE3");
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
                ResultTitleColor = Color.FromArgb("#B3361F");
                ResultBackground = Color.FromArgb("#FCEDEA");
                ResultValue = $"{_units.FormatDistance(low)} / {_units.FormatDistance(high)}";
                VerdictText = ResultTitle;
                VerdictColor = Amber;
                VerdictBackground = Color.FromArgb("#FDF1D8");
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
                ResultTitle = L("✓ გარბენი ემთხვევა", "✓ The mileage matches");
                ResultTitleColor = Green;
                ResultBackground = Color.FromArgb("#E8F5EE");
                VerdictText = ResultTitle;
                VerdictColor = Green;
                VerdictBackground = Color.FromArgb("#E8F5EE");
                CardStroke = Color.FromArgb("#22C55E");
                VerdictNote = L(
                    $"{moduleCount} ბლოკი აჩვენებს ერთნაირს. გადახვევის ნიშანი არ ჩანს.",
                    $"{moduleCount} modules show the same distance. No sign of a rollback.");
            }
            else
            {
                ResultCaption = L("გარბენი", "Mileage");
                ResultTitle = L("გარბენი", "Mileage");
                ResultTitleColor = Grey;
                ResultBackground = Color.FromArgb("#FFFFFF");
                VerdictText = L("მხოლოდ 1 წყარო", "Only 1 source");
                VerdictColor = Grey;
                VerdictBackground = Color.FromArgb("#ECEEF0");
                CardStroke = Color.FromArgb("#DCDFE3");
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
