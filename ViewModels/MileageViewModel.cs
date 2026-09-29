using CarScanner.Database;
using CarScanner.Services;
using CarScanner.Services.Mileage;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public class MileageViewModel : BaseViewModel
{
    private readonly ActualMileageService _mileage;
    private readonly VehicleInfoService _vehicle;
    private readonly ProService _pro;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private readonly AppDatabase _database;
    private double? _lastKnownMileageKm;
    private ActualMileageResult? _lastResult;
    private double? _lastManualMileageKm;

    public string Vin { get; set; } = "—";
    public string Result { get; set; } = "გარბენი ჯერ არ შემოწმებულა";
    public string Details { get; set; } = "";
    public string Status { get; set; } = "PRO ფუნქცია";
    public string StatusColor { get; set; } = "#94A3B8";
    public string SourceSummary { get; set; } = "წყაროები ჯერ არ შემოწმებულა";
    public string DashboardComparison { get; set; } = "Dashboard odometer სურვილისამებრ შეიყვანე ქვემოთ.";
    public string DashboardMileage { get; set; } = "";
    public string DashboardUnit => _units.DistanceUnit;

    public IAsyncRelayCommand CheckCommand { get; }

    public MileageViewModel(
        ActualMileageService mileage,
        VehicleInfoService vehicle,
        ProService pro,
        UnitSystemService units,
        LocalizationService localization,
        AppDatabase database)
    {
        _database = database;
        _mileage = mileage;
        _vehicle = vehicle;
        _pro = pro;
        _units = units;
        _localization = localization;
        _units.UnitSystemChanged += (_, __) => MainThread.BeginInvokeOnMainThread(RefreshUnitPresentation);
        _localization.LanguageChanged += (_, __) => MainThread.BeginInvokeOnMainThread(RefreshIdleText);

        Title = "Mileage Verification";

        CheckCommand = new AsyncRelayCommand(CheckAsync);
        RefreshIdleText();
    }

    public async Task LoadVehicleContextAsync()
    {
        var vehicle = _vehicle.CurrentVehicle;
        if (vehicle?.VIN is not { Length: 17 })
        {
            try { vehicle = await _vehicle.IdentifyConnectedVehicleAsync(); }
            catch { }
        }

        Vin = vehicle?.VIN is { Length: 17 } knownVin
            ? knownVin
            : _localization.IsEnglish ? "VIN unavailable" : "VIN მიუწვდომელია";
        OnPropertyChanged(nameof(Vin));
    }

    private void RefreshIdleText()
    {
        if (_lastResult is null)
        {
            Result = _localization.IsEnglish
                ? "Mileage has not been checked yet"
                : "გარბენი ჯერ არ შემოწმებულა";
            OnPropertyChanged(nameof(Result));
        }
    }

    private async Task CheckAsync()
    {
        if (IsBusy)
            return;

        if (!await _pro.EnsureProAsync())
        {
            await Shell.Current.DisplayAlertAsync(
                CarScanner.Services.Localization.LocalizationService.T("PRO ფუნქცია"),
                CarScanner.Services.Localization.LocalizationService.T("Mileage Verification ხელმისაწვდომია PRO-ში."),
                CarScanner.Services.Localization.LocalizationService.T("კარგი"));

            return;
        }

        try
        {
            IsBusy = true;

            StatusMessage = "მანქანის ინფორმაციის კითხვა...";

            var vehicle =
                await _vehicle.IdentifyConnectedVehicleAsync();

            Vin = vehicle.VIN;

            StatusMessage =
                "გარბენის ყველა ხელმისაწვდომი წყაროს შემოწმება...";

            // The last value this app ever read for this VIN (used only
            // when the car returns nothing this time).
            _lastKnownMileageKm = await LoadLastKnownMileageAsync(vehicle.VIN);

            var scan =
                await _mileage.ScanAsync(vehicle);

            _lastResult = scan;
            _lastManualMileageKm = TryGetDashboardMileageKm();
            RefreshUnitPresentation();

            StatusMessage =
                scan.HasMismatch
                    ? "გარბენის შეუსაბამობა აღმოჩნდა."
                    : "გარბენის შემოწმება დასრულდა.";

            OnPropertyChanged(nameof(Vin));
            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(DashboardMileage));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(SourceSummary));
            OnPropertyChanged(nameof(DashboardComparison));
        }
        catch (Exception ex)
        {
            Result = _lastKnownMileageKm.HasValue
                ? _units.FormatDistance(_lastKnownMileageKm.Value)
                : "—";
            Status = "შეცდომა";
            Details = ex.Message;
            StatusColor = "#EF4444";
            SourceSummary = "წყაროების შემოწმება შეცდომით შეწყდა.";
            DashboardComparison = "Dashboard მნიშვნელობა ვერ შეედარა.";
            StatusMessage = ex.Message;

            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(SourceSummary));
            OnPropertyChanged(nameof(DashboardComparison));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshUnitPresentation()
    {
        OnPropertyChanged(nameof(DashboardUnit));
        if (_lastResult is null)
            return;

        // Only a number is shown: the original mileage found in the car's
        // modules, else the odometer, else the last value read before.
        Result = _lastResult.FinalMileageKm.HasValue
            ? _units.FormatDistance(_lastResult.FinalMileageKm.Value)
            : _lastKnownMileageKm.HasValue
                ? _units.FormatDistance(_lastKnownMileageKm.Value)
                : "—";
        Status = _lastResult.Status;
        StatusColor = _lastResult.HasMismatch
            ? "#F59E0B"
            : _lastResult.SourceCount >= 2
                ? "#22C55E"
                : "#94A3B8";
        if (_lastResult.SourceCount == 0 &&
    _lastResult.FinalMileageKm.HasValue)
        {
            SourceSummary =
                "Standard OBD-II · მანქანის მიმდინარე odometer წაკითხულია.";
        }
        else
        {
            SourceSummary = _lastResult.SourceCount switch
            {
                0 =>
                    "გარბენის ხელმისაწვდომი წყაროებიდან მნიშვნელობა ვერ მოიძებნა.",

                1 =>
                    "წყაროები შემოწმდა: 1 · მნიშვნელობა წაკითხულია, მაგრამ დამოუკიდებელი გადამოწმება არ არსებობს.",

                _ =>
                    $"წყაროები შემოწმდა: {_lastResult.SourceCount} · " +
                    $"მაქსიმალური სხვაობა: " +
                    $"{_units.FormatDistance(_lastResult.MaximumDifferenceKm ?? 0)}"
            };
        }
        Details = _lastResult.Evidence.Count == 0
            ? _lastResult.Details
            : string.Join(Environment.NewLine, _lastResult.Evidence.Select(e => $"{e.Source} / {e.Ecu}: {_units.FormatDistance(e.ValueKm)}"));
        Details += $"\nსანდოობა: {_lastResult.Confidence:P0}";
        if (_lastResult.SourceCount == 1)
            Details += "\nერთი წყარო გარბენის ორიგინალობას ვერ ადასტურებს.";
        if (_lastResult.HasMismatch)
            Details += "\nECU წყაროებს შორის მნიშვნელოვანი სხვაობაა; ეს შეიძლება მოდულის შეცვლის ან reset-ის შედეგიც იყოს.";
        if (_lastManualMileageKm.HasValue)
        {
            if (_lastResult.FinalMileageKm.HasValue)
            {
                var difference = Math.Abs(_lastResult.FinalMileageKm.Value - _lastManualMileageKm.Value);
                var tolerance = Math.Max(5d, _lastResult.FinalMileageKm.Value * 0.001d);
                DashboardComparison = difference <= tolerance
                    ? $"Dashboard და წაკითხული ECU მნიშვნელობა ახლოსაა · სხვაობა: {_units.FormatDistance(difference)}"
                    : $"Dashboard და წაკითხული ECU მნიშვნელობა განსხვავდება · სხვაობა: {_units.FormatDistance(difference)}. ეს საბოლოო მტკიცებულება არ არის.";
            }
            else
            {
                DashboardComparison = $"Dashboard: {_units.FormatDistance(_lastManualMileageKm.Value)} · ავტომატური ECU წყარო ვერ მოიძებნა.";
            }
        }
        else
        {
            DashboardComparison = "თუ dashboard odometer იცი, ჩაწერე ქვემოთ — ხელით შეყვანილი მნიშვნელობა მკაფიოდ გამოიყოფა ECU მონაცემისგან.";
        }

        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(DashboardComparison));
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
                if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var km) &&
                    km is > 0 and < 2_000_000)
                {
                    return km;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private double? TryGetDashboardMileageKm()
    {
        var normalized = (DashboardMileage ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? _units.DistanceToKilometers(value)
            : null;
    }
}
