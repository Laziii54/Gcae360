using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Energy;
using CarScanner.Database;
using CarScanner.Services.Localization;
using CarScanner.Services.Background;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class FuelViewModel : BaseViewModel
{
    private readonly LiveDataEngine _engine;
    private readonly ProService _pro;
    private readonly UnitSystemService _units;
    private readonly AppDatabase _database;
    private readonly VehicleInfoService _vehicle;
    private readonly LocalizationService _localization;
    private readonly EvEnergyReader _evReader;
    private bool _subscribed;
    private double _distanceMeters;
    private double _fuelLiters;
    private double? _instantLitersPer100Km;
    private int? _latestFuelTypeCode;
    private double? _latestFuelLevelPercent;
    private double _movingSeconds;
    private double _idleSeconds;
    private int _maxSpeedKph;
    private double _rpmSeconds;
    private double _rpmWeightedSum;
    private double _loadSeconds;
    private double _loadWeightedSum;
    private double _cityMeters;
    private double _cityFuelLiters;
    private double _highwayMeters;
    private double _highwayFuelLiters;
    private DateTime? _tripStartedAt;
    private List<FuelTripRecord> _tripHistoryRecords = new();

    [ObservableProperty] private string instant = "—";
    [ObservableProperty] private string average = "—";
    [ObservableProperty] private string distance = "0.0 km";
    [ObservableProperty] private string status = "გაჩერებულია";
    [ObservableProperty] private string trip = "—";
    [ObservableProperty] private string fuelUsed = "—";
    [ObservableProperty] private string tripTime = "0 min";
    [ObservableProperty] private string driving = "Fuel Rate ან MAF ჯერ არ მოსულა ECU-დან.";
    [ObservableProperty] private string fuelRate = "—";
    [ObservableProperty] private string fuelLevel = "ECU-დან მიუწვდომელია";
    [ObservableProperty] private string movingTime = "0 min";
    [ObservableProperty] private string idleTime = "0 min";
    [ObservableProperty] private string averageSpeed = "—";
    [ObservableProperty] private string maximumSpeed = "—";
    [ObservableProperty] private string averageRpm = "—";
    [ObservableProperty] private string engineLoad = "—";
    [ObservableProperty] private string cityConsumption = "—";
    [ObservableProperty] private string highwayConsumption = "—";
    [ObservableProperty] private string tripCost = "ფასის შეყვანის შემდეგ გამოჩნდება";
    [ObservableProperty] private string fuelPriceText = "";
    [ObservableProperty] private string refuelOdometerText = "";
    [ObservableProperty] private string refuelLitersText = "";
    [ObservableProperty] private string refuelPriceText = "";
    [ObservableProperty] private string refuelFuelType = "";
    [ObservableProperty] private string refuelStation = "";
    [ObservableProperty] private string refuelNotes = "";
    [ObservableProperty] private bool refuelIsFullTank = true;
    [ObservableProperty] private string refuelStatus = "ჯერ არ არის დამატებული შევსების ჩანაწერი.";
    [ObservableProperty] private string tankToTankSummary = "Tank-to-Tank ხარჯისთვის საჭიროა ერთი VIN-ის ორი „სრული ავზი“ ჩანაწერი.";
    [ObservableProperty] private string tripHistorySummary = "დასრულებული მოგზაურობები ჯერ არ არის.";
    [ObservableProperty] private bool showTripHistory;
    public string TripHistoryToggleText => ShowTripHistory ? L("დამალვა", "Hide") : L("ისტორიის ნახვა ›", "Show history ›");
    /// <summary>The expanded list only renders once there is something to show it for.</summary>
    public bool ShowTripHistoryList => ShowTripHistory && HasTripHistory;
    public ObservableCollection<RefuelRecord> RefuelHistory { get; } = new();
    public ObservableCollection<string> TripHistory { get; } = new();

    /// <summary>"Start" — marked PRO until PRO is active (the page itself is open to everyone).</summary>
    public string StartButtonText => CarScanner.Services.Localization.LocalizationService.T("დაწყება") + ProGate.Suffix;

    public FuelViewModel(
        LiveDataEngine engine,
        ProService pro,
        UnitSystemService units,
        AppDatabase database,
        VehicleInfoService vehicle,
        LocalizationService localization,
        EvEnergyReader evReader)
    {
        _engine = engine;
        _pro = pro;
        _pro.ProChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(StartButtonText)));
        _units = units;
        _database = database;
        _vehicle = vehicle;
        _localization = localization;
        _evReader = evReader;
        _units.UnitSystemChanged += (_, __) => MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshPresentation();
            RebuildTripHistoryPresentation();
            RebuildRefuelSummaries();
        });
        InitFuelEngine();
        FuelPriceText = Preferences.Default.Get("fuel_price_per_liter", string.Empty);
        RefuelPriceText = FuelPriceText;
        Title = "Fuel Consumption";
        Status = _localization.IsEnglish ? "Stopped" : "გაჩერებულია";
        InitEnergy();
        UpdateFuelKind();
    }

    private bool _startInProgress;

    [RelayCommand]
    private async Task Start()
    {
        // A second tap while the first start is still initialising the adapter must not start a second one.
        if (_startInProgress)
            return;
        _startInProgress = true;
        var startedTripHere = false;
        try
        {
        if (!await ProGate.EnsureOrOpenAsync(_pro))
            return;

        IsTripSummaryVisible = false;

        // Polling comes first: nothing is subscribed, flagged or started until the adapter really answers, so a
        // missing connection leaves no half-started trip behind (the crash report: Start → StartAsync → InitializeAsync).
        var started = await _engine.TryStartAsync();
        if (started is LiveDataStartResult.AdapterNotConnected)
        {
            Status = L("ჯერ დააკავშირე OBD ადაპტერი, შემდეგ დაიწყე.", "Connect the OBD adapter first, then start.");
            return;
        }
        if (started is LiveDataStartResult.Cancelled)
            return;
        if (started is LiveDataStartResult.Failed)
        {
            Status = L("ადაპტერიდან კითხვა ვერ დაიწყო. სცადე თავიდან.", "Reading from the adapter could not start. Try again.");
            return;
        }

        try
        {
        Subscribe();
        _engine.SetFuelAnalyticsEnabled(true);
        if (_tripStartedAt is null)
        {
            ResetCurrentTrip();
            _tripStartedAt = DateTime.Now;
            startedTripHere = true;
        }
        UpdateFuelKind();
        _engine.KeepRunningForTrip = true;
        StartEnergyLoop();
        BeginTripTracking();
        HasInterruptedTrip = false;
        Status = RunningStatus();

        // Keep recording when the app goes to the background or the screen turns off.
        TripKeepAlive.Start(
            CarScanner.Services.Localization.LocalizationService.T(L("OBD360 — მოგზაურობა იწერება", "OBD360 — trip is recording")),
            CarScanner.Services.Localization.LocalizationService.T(L("შეეხე აპის გასახსნელად", "Tap to open the app")));
        }
        catch
        {
            // Undo what this start did, so the page is not left "recording" without a trip, then report it.
            StopEnergyLoop();
            _engine.KeepRunningForTrip = false;
            Unsubscribe();
            _engine.SetFuelAnalyticsEnabled(false);
            if (startedTripHere)
                _tripStartedAt = null;
            // Polling that THIS start began is stopped again; polling another page started is left alone.
            if (started is LiveDataStartResult.Started)
                await _engine.StopAsync(force: true);
            throw;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FuelViewModel.Start");
        }
        finally
        {
            _startInProgress = false;
        }
    }

    [RelayCommand]
    private async Task Pause()
    {
        try
        {
        StopEnergyLoop();
        _engine.KeepRunningForTrip = false;
        TripKeepAlive.Stop();
        await _engine.StopAsync(force: true);
        Unsubscribe();
        _engine.SetFuelAnalyticsEnabled(false);
        EndTripTracking(deleteCheckpoint: false);
        Status = _localization.IsEnglish ? "Paused" : "დაპაუზებულია";
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FuelViewModel.Pause");
        }
    }

    [RelayCommand]
    private async Task Stop()
    {
        try
        {
        StopEnergyLoop();
        _engine.KeepRunningForTrip = false;
        TripKeepAlive.Stop();
        await _engine.StopAsync(force: true);
        Unsubscribe();
        _engine.SetFuelAnalyticsEnabled(false);
        if (_tripStartedAt is not null)
            BuildTripSummary();
        var saveResult = await SaveCurrentTripIfUsableAsync();
        EndTripTracking(deleteCheckpoint: true);
        Status = _localization.IsEnglish
            ? "Stopped"
            : saveResult ?? "გაჩერებულია";
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FuelViewModel.Stop");
        }
    }

    [RelayCommand]
    private async Task Reset()
    {
        try
        {
        if (!await ProGate.EnsureOrOpenAsync(_pro))
            return;

        ResetCurrentTrip();
        RefreshPresentation();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FuelViewModel.Reset");
        }
    }

    private void ResetCurrentTrip()
    {
        _distanceMeters = 0;
        _fuelLiters = 0;
        _instantLitersPer100Km = null;
        _latestFuelTypeCode = null;
        _latestFuelLevelPercent = null;
        _movingSeconds = _idleSeconds = 0;
        _maxSpeedKph = 0;
        _rpmSeconds = _rpmWeightedSum = _loadSeconds = _loadWeightedSum = 0;
        _cityMeters = _cityFuelLiters = _highwayMeters = _highwayFuelLiters = 0;
        _tripStartedAt = null;
        _fuel.Reset();
        _lastAverageAt = null;
        EndTripTracking(deleteCheckpoint: true);
        ResetEnergy();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _engine.LiveDataUpdated += OnData;
        _engine.LiveValuesUpdated += OnValues;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _engine.LiveDataUpdated -= OnData;
        _engine.LiveValuesUpdated -= OnValues;
        _subscribed = false;
    }

    private void OnData(object? sender, LiveDataModel data) =>
        MainThread.BeginInvokeOnMainThread(() => ApplyData(data));

    private void OnValues(object? sender, IReadOnlyList<SensorValue> values)
    {
        // Stamp the readings with the moment they arrived, not with the moment the UI thread gets to them.
        var arrived = DateTime.UtcNow;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FuelSignalMapper.Apply(_fuel.Signals, values, arrived);
            foreach (var value in values)
            {
                if (!double.TryParse(value.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
                    continue;
                switch (value.Pid)
                {
                    case 0x51:
                        _latestFuelTypeCode = (int)Math.Round(number);
                        OnFuelTypeCodeReceived(_latestFuelTypeCode.Value);
                        UpdateFuelKind();
                        break;
                    case 0x2F: _latestFuelLevelPercent = number; break;
                }
            }
        });
    }

    private void ApplyData(LiveDataModel data)
    {
        // The engine decides what is fresh enough to combine; everything below only presents its results.
        var now = DateTime.UtcNow;
        FuelSignalMapper.Apply(_fuel.Signals, data, now);
        _fuel.Tick(now);
        SyncFromEngine(now);
        TrackAverages(data, now);
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        RefreshFuelPresentation();
        RebuildSupportRows();

        Distance = _units.FormatDistance(_distanceMeters / 1000d);
        _fuelLevelSensor.Value = _latestFuelLevelPercent;
        FuelLevel = _latestFuelLevelPercent.HasValue
            ? $"{_latestFuelLevelPercent.Value:0}%"
            : _localization.IsEnglish ? "Unavailable from ECU" : "ECU-დან მიუწვდომელია";

        TripTime = $"Trip: {(_movingSeconds + _idleSeconds) / 60d:0} min";
        MovingTime = $"{_movingSeconds / 60d:0} min";
        IdleTime = $"{_idleSeconds / 60d:0} min";
        MaximumSpeed = _maxSpeedKph > 0 ? _units.FormatSpeed(_maxSpeedKph) : "—";
        AverageSpeed = _fuel.Snapshot().AverageSpeedKph is { } averageSpeed ? _units.FormatSpeed(averageSpeed) : "—";
        AverageRpm = _rpmSeconds > 0 ? $"{_rpmWeightedSum / _rpmSeconds:0} rpm" : "—";
        EngineLoad = _loadSeconds > 0 ? $"{_loadWeightedSum / _loadSeconds:0}%" : "—";
        CityConsumption = FormatConsumption(_cityFuelLiters, _cityMeters);
        HighwayConsumption = FormatConsumption(_highwayFuelLiters, _highwayMeters);

        // The cost is only as good as the litres: an estimate is shown as one.
        var estimated = _fuel.Snapshot().Quality is TripFuelQuality.Estimated or TripFuelQuality.Mixed;
        TripCost = TryGetFuelPrice(out var price) && price > 0 && _fuelLiters > 0
            ? $"{(estimated ? "≈ " : string.Empty)}₾{_fuelLiters * price:0.00}"
            : "ფასის შეყვანის შემდეგ გამოჩნდება";
        RefreshEnergyPresentation();

        if (TripKeepAlive.IsActive)
            TripKeepAlive.Update(_mode == EnergyMode.Electric
                ? $"{Distance} · {TripDurationText}"
                : $"{Distance} · {CarScanner.Services.Localization.LocalizationService.T(L("საშუალო", "Average"))} {Average} · {FuelUsed}");
    }

    partial void OnFuelPriceTextChanged(string value)
    {
        Preferences.Default.Set("fuel_price_per_liter", value ?? string.Empty);
        RefreshPresentation();
    }

    partial void OnShowTripHistoryChanged(bool value)
    {
        OnPropertyChanged(nameof(TripHistoryToggleText));
        OnPropertyChanged(nameof(ShowTripHistoryList));
    }

    [RelayCommand]
    private void ToggleTripHistory() => ShowTripHistory = !ShowTripHistory;

    [RelayCommand]
    private async Task AddRefuel()
    {
        try
        {
        if (!await ProGate.EnsureOrOpenAsync(_pro))
            return;

        if (!TryParsePositive(RefuelLitersText, out var liters) ||
            !TryParseNonNegative(RefuelOdometerText, out var odometer) ||
            !TryParsePositive(RefuelPriceText, out var price))
        {
            RefuelStatus = $"შეიყვანე სწორი ოდომეტრი ({_units.DistanceUnit}), ლიტრები და ფასი 1 ლიტრზე.";
            return;
        }

        var vin = _vehicle.CurrentVehicle?.VIN;
        if (IsUnknownVin(vin))
        {
            RefuelStatus = "Refuel Log-ის შესანახად საჭიროა ამ მანქანის რეალური VIN. ერთხელ დააკავშირე OBD და წაიკითხე Vehicle Info.";
            return;
        }

        var record = new RefuelRecord
        {
            VIN = vin!,
            RefueledAt = DateTime.Now,
            // The field is typed in the selected unit (mi with Imperial); the
            // log is always stored in km and shown through _units.FormatDistance.
            OdometerKm = _units.DistanceToKilometers(odometer),
            Liters = liters,
            PricePerLiter = price,
            IsFullTank = RefuelIsFullTank,
            FuelType = string.IsNullOrWhiteSpace(RefuelFuelType) ? "არ არის მითითებული" : RefuelFuelType.Trim(),
            Station = string.IsNullOrWhiteSpace(RefuelStation) ? "არ არის მითითებული" : RefuelStation.Trim(),
            Notes = RefuelNotes?.Trim() ?? string.Empty
        };

        await _database.SaveRefuelAsync(record);
        RefuelStatus = $"შევსება შენახულია: {record.Liters:0.00} L · ₾{record.Liters * record.PricePerLiter:0.00}.";
        RefuelLitersText = string.Empty;
        RefuelOdometerText = string.Empty;
        RefuelNotes = string.Empty;
        await LoadRefuelHistoryAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FuelViewModel.AddRefuel");
        }
    }

    public async Task LoadRefuelHistoryAsync()
    {
        var vin = _vehicle.CurrentVehicle?.VIN;
        if (IsUnknownVin(vin))
        {
            RefuelHistory.Clear();
            TankToTankSummary = "Refuel ისტორიის სანახავად ამოიცანი მანქანა რეალური VIN-ით.";
            return;
        }

        var records = await _database.GetRefuelsAsync(vin);
        RefuelHistory.Clear();
        foreach (var record in records.Take(8))
        {
            record.FormattedSummary = FormatRefuelSummary(record);
            RefuelHistory.Add(record);
        }
        TankToTankSummary = BuildTankToTankSummary(records);
    }

    /// <summary>Refuel line with the odometer in the selected unit (km / mi).</summary>
    public string FormatRefuelSummary(RefuelRecord record) =>
        $"{record.RefueledAt:dd.MM.yyyy} · {_units.FormatDistance(record.OdometerKm, "0")} · {record.Liters:0.00} L · ₾{record.Liters * record.PricePerLiter:0.00}";

    private void RebuildRefuelSummaries()
    {
        for (var i = 0; i < RefuelHistory.Count; i++)
        {
            var record = RefuelHistory[i];
            record.FormattedSummary = FormatRefuelSummary(record);
            // Replace notifies the list so a bound row re-reads DisplaySummary.
            RefuelHistory[i] = record;
        }
    }

    public async Task LoadFuelHistoryAsync()
    {
        await LoadRefuelHistoryAsync();
        await LoadTripHistoryAsync();
    }

    private async Task LoadTripHistoryAsync()
    {
        var vin = _vehicle.CurrentVehicle?.VIN;
        if (IsUnknownVin(vin))
        {
            _tripHistoryRecords = new();
            TripHistory.Clear();
            TripHistorySummary = "Trip History-ის სანახავად ამოიცანი მანქანა რეალური VIN-ით.";
            return;
        }

        _tripHistoryRecords = await _database.GetFuelTripsAsync(vin);
        RebuildTripHistoryPresentation();
    }

    private string FormatConsumption(double fuelLiters, double distanceMeters)
    {
        if (distanceMeters <= 100d || fuelLiters <= 0)
            return "—";

        var estimated = _fuel.Snapshot().Quality is TripFuelQuality.Estimated or TripFuelQuality.Mixed;
        return _units.FormatFuelEconomy(fuelLiters / (distanceMeters / 1000d) * 100d, estimated);
    }

    private bool TryGetFuelPrice(out double price) => double.TryParse(FuelPriceText,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out price) ||
        double.TryParse(FuelPriceText, System.Globalization.NumberStyles.Float,
        new System.Globalization.CultureInfo("ka-GE"), out price);

    private static bool TryParsePositive(string? text, out double value) =>
        TryParseNonNegative(text, out value) && value > 0;

    private static bool TryParseNonNegative(string? text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, System.Globalization.NumberStyles.Float, new System.Globalization.CultureInfo("ka-GE"), out value);

    private static bool IsUnknownVin(string? vin) =>
        string.IsNullOrWhiteSpace(vin) || vin.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || vin.Length != 17;

    private string BuildTankToTankSummary(IReadOnlyCollection<RefuelRecord> records)
    {
        var fullTanks = records.Where(record => record.IsFullTank)
            .OrderBy(record => record.RefueledAt)
            .ToArray();
        if (fullTanks.Length < 2)
            return "Tank-to-Tank ხარჯისთვის საჭიროა ერთი VIN-ის ორი „სრული ავზი“ ჩანაწერი.";

        var previous = fullTanks[^2];
        var latest = fullTanks[^1];
        var distanceKm = latest.OdometerKm - previous.OdometerKm;
        if (distanceKm <= 0)
            return "Tank-to-Tank ვერ გამოითვალა: ბოლო სრული ავზის ოდომეტრი წინა ჩანაწერზე დიდი უნდა იყოს.";

        var economy = latest.Liters / distanceKm * 100d;
        var cost = latest.Liters * latest.PricePerLiter;
        return $"რეალური Tank-to-Tank: {_units.FormatFuelEconomy(economy)} · { _units.FormatDistance(distanceKm) } · {latest.Liters:0.00} L · ₾{cost:0.00}. გამოთვლაში გამოყენებულია ორი სრული ავზი.";
    }

    private async Task<string?> SaveCurrentTripIfUsableAsync()
    {
        var trip = _fuel.Snapshot();
        var electric = ElectricTripCalculator.Compute(_mode, trip, _ev.HasSample, _ev.ConsumedKWh, _ev.RegeneratedKWh, _socStart, _socNow, CapacityKWh());
        var hasEnergy = electric.Quality != EnergyQuality.Unavailable && electric.NetKWh is > 0;
        var hasFuel = trip.FuelLiters > 0 && _mode != EnergyMode.Electric;

        if (_tripStartedAt is null || trip.DistanceKm <= 0.1d || (!hasFuel && !hasEnergy))
        {
            _tripStartedAt = null;
            return _mode == EnergyMode.Electric
                ? "მოგზაურობა არ შენახულა: მანძილისა და ბატარეის მონაცემი საკმარისი არ იყო."
                : "მოგზაურობა არ შენახულა: რეალური მანძილისა და საწვავის მონაცემი საკმარისი არ იყო.";
        }

        var vin = _vehicle.CurrentVehicle?.VIN;
        if (IsUnknownVin(vin))
        {
            _tripStartedAt = null;
            return "მოგზაურობა არ შენახულა: რეალური VIN მიუწვდომელია.";
        }

        var record = BuildTripRecord(vin!, _tripStartedAt.Value, DateTime.Now, trip, _fuel.Kind, _mode,
            hasEnergy ? electric.NetKWh : null,
            electric.Quality == EnergyQuality.Measured ? electric.RegeneratedKWh : null,
            electric.Quality == EnergyQuality.Estimated, interrupted: false);

        await _database.SaveFuelTripAsync(record);
        _tripStartedAt = null;
        await LoadTripHistoryAsync();
        var estimateMark = record.EstimatedFuelShare > 0 ? "≈ " : string.Empty;
        return hasFuel
            ? $"მოგზაურობა შენახულია: {_units.FormatDistance(record.DistanceKm)} · {estimateMark}{_units.FormatFuelEconomy(record.AverageFuelLPer100Km ?? 0)}."
            : $"მოგზაურობა შენახულია: {_units.FormatDistance(record.DistanceKm)} · {(record.EnergyIsEstimate ? "≈ " : string.Empty)}{record.EnergyKWh / record.DistanceKm * 100d:0.0} kWh/100km.";
    }

    private void RebuildTripHistoryPresentation()
    {
        TripHistory.Clear();
        foreach (var record in _tripHistoryRecords.Take(8))
        {
            var fuel = record.AverageFuelLPer100Km.HasValue
                ? _units.FormatFuelEconomy(record.AverageFuelLPer100Km.Value)
                : record.EnergyKWh is { } energy && record.DistanceKm > 0
                    ? $"{energy / record.DistanceKm * 100d:0.0} kWh/100km"
                    : "—";
            if (record.AverageFuelLPer100Km.HasValue && record.EnergyKWh is { } hybridEnergy)
                fuel += $" + {hybridEnergy:0.00} kWh";
            var costValue = (record.FuelPricePerLiter is { } literPrice ? record.FuelLiters * literPrice : 0) +
                            (record.EnergyPricePerKWh is { } kwhPrice && record.EnergyKWh is { } kwh ? Math.Max(0, kwh) * kwhPrice : 0);
            var cost = record.FuelPricePerLiter.HasValue || record.EnergyPricePerKWh.HasValue
                ? $" · ₾{costValue:0.00}"
                : string.Empty;
            if (record.EstimatedFuelShare > 0 && record.AverageFuelLPer100Km.HasValue)
                fuel = "≈ " + fuel;
            else if (record.EnergyIsEstimate && record.EnergyKWh.HasValue && !record.AverageFuelLPer100Km.HasValue)
                fuel = "≈ " + fuel;
            var interrupted = record.IsInterrupted ? " · " + L("გაწყვეტილი", "interrupted") : string.Empty;
            TripHistory.Add($"{record.FinishedAt:dd.MM HH:mm} · {_units.FormatDistance(record.DistanceKm)} · {fuel} · {record.MovingSeconds / 60d:0} min{cost}{interrupted}\n{record.FuelDataSource}");
        }

        HasTripHistory = TripHistory.Count > 0;
        TripHistorySummary = _tripHistoryRecords.Count == 0
            ? "დასრულებული მოგზაურობები ჯერ არ არის. Start → Stop შემდეგ ჩანაწერი ავტომატურად შეინახება, თუ ECU-დან მოვა რეალური speed და fuel data."
            : $"შენახულია {_tripHistoryRecords.Count} მოგზაურობა ამ VIN-ზე.";
    }

    public Task OnPageDisappearingAsync()
    {
        // A started (or paused) trip keeps going when the user leaves the page
        // or the app; only Stop ends it. This view model is a singleton, so the
        // page shows the same trip again when it is reopened.
        if (_tripStartedAt is not null)
            return Task.CompletedTask;

        StopEnergyLoop();
        Unsubscribe();
        _engine.SetFuelAnalyticsEnabled(false);
        return Task.CompletedTask;
    }
}
