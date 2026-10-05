using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Energy;
using CarScanner.Database;
using CarScanner.Services.Localization;
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
    private double? _latestFuelRateLitersPerHour;
    private double? _latestMafGramsPerSecond;
    private int? _latestFuelTypeCode;
    private double? _latestFuelLevelPercent;
    private DateTime? _last;
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
    public ObservableCollection<RefuelRecord> RefuelHistory { get; } = new();
    public ObservableCollection<string> TripHistory { get; } = new();

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
        _units = units;
        _database = database;
        _vehicle = vehicle;
        _localization = localization;
        _evReader = evReader;
        _units.UnitSystemChanged += (_, __) => MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshPresentation();
            RebuildTripHistoryPresentation();
        });
        FuelPriceText = Preferences.Default.Get("fuel_price_per_liter", string.Empty);
        RefuelPriceText = FuelPriceText;
        Title = "Fuel Consumption";
        Status = _localization.IsEnglish ? "Stopped" : "გაჩერებულია";
        InitEnergy();
    }

    [RelayCommand]
    private async Task Start()
    {
        if (!await _pro.EnsureProAsync())
        {
            await Shell.Current.DisplayAlertAsync(CarScanner.Services.Localization.LocalizationService.T("PRO ფუნქცია"), CarScanner.Services.Localization.LocalizationService.T("Fuel consumption statistics ხელმისაწვდომია PRO-ში."), CarScanner.Services.Localization.LocalizationService.T("კარგი"));
            return;
        }

        Subscribe();
        _engine.SetFuelAnalyticsEnabled(true);
        if (_tripStartedAt is null)
        {
            ResetCurrentTrip();
            _tripStartedAt = DateTime.Now;
        }
        await _engine.StartAsync();
        StartEnergyLoop();
        Status = RunningStatus();
    }

    [RelayCommand]
    private async Task Pause()
    {
        StopEnergyLoop();
        await _engine.StopAsync();
        Unsubscribe();
        _engine.SetFuelAnalyticsEnabled(false);
        _last = null;
        Status = _localization.IsEnglish ? "Paused" : "დაპაუზებულია";
    }

    [RelayCommand]
    private async Task Stop()
    {
        StopEnergyLoop();
        await _engine.StopAsync();
        Unsubscribe();
        _engine.SetFuelAnalyticsEnabled(false);
        var saveResult = await SaveCurrentTripIfUsableAsync();
        Status = _localization.IsEnglish
            ? "Stopped"
            : saveResult ?? "გაჩერებულია";
    }

    [RelayCommand]
    private async Task Reset()
    {
        if (!await _pro.EnsureProAsync())
        {
            await Shell.Current.DisplayAlertAsync(CarScanner.Services.Localization.LocalizationService.T("PRO ფუნქცია"), CarScanner.Services.Localization.LocalizationService.T("Fuel consumption statistics ხელმისაწვდომია PRO-ში."), CarScanner.Services.Localization.LocalizationService.T("კარგი"));
            return;
        }

        ResetCurrentTrip();
        RefreshPresentation();
    }

    private void ResetCurrentTrip()
    {
        _distanceMeters = 0;
        _fuelLiters = 0;
        _instantLitersPer100Km = null;
        _latestFuelRateLitersPerHour = null;
        _latestMafGramsPerSecond = null;
        _latestFuelTypeCode = null;
        _latestFuelLevelPercent = null;
        _last = null;
        _movingSeconds = _idleSeconds = 0;
        _maxSpeedKph = 0;
        _rpmSeconds = _rpmWeightedSum = _loadSeconds = _loadWeightedSum = 0;
        _cityMeters = _cityFuelLiters = _highwayMeters = _highwayFuelLiters = 0;
        _tripStartedAt = null;
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

    private void OnValues(object? sender, IReadOnlyList<SensorValue> values) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var value in values)
            {
                if (!double.TryParse(value.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
                    continue;
                switch (value.Pid)
                {
                    case 0x5E: _latestFuelRateLitersPerHour = number; break;
                    case 0x10: _latestMafGramsPerSecond = number; break;
                    case 0x51:
                        _latestFuelTypeCode = (int)Math.Round(number);
                        OnFuelTypeCodeReceived(_latestFuelTypeCode.Value);
                        break;
                    case 0x2F: _latestFuelLevelPercent = number; break;
                }
            }
        });

    private void ApplyData(LiveDataModel data)
    {
        if (data.HasMaf)
            _latestMafGramsPerSecond = data.MafGs;
        if (data.HasFuelType)
        {
            _latestFuelTypeCode = data.FuelTypeCode;
            OnFuelTypeCodeReceived(data.FuelTypeCode);
        }
        if (data.HasFuelLevel)
            _latestFuelLevelPercent = data.FuelLevel;

        var now = data.Timestamp;
        var seconds = _last is null ? 0 : Math.Clamp((now - _last.Value).TotalSeconds, 0, 5);
        _last = now;
        var measurement = FuelConsumptionService.ResolveFuelRate(
            _latestFuelRateLitersPerHour,
            _latestMafGramsPerSecond,
            _latestFuelTypeCode ?? (data.HasFuelType ? data.FuelTypeCode : null));
        _instantLitersPer100Km = FuelConsumptionService.FuelEconomyLPer100Km(
            measurement?.LitersPerHour, data.Speed);
        if (measurement is { } fuelMeasurement && seconds > 0)
            _fuelLiters += fuelMeasurement.LitersPerHour / 3600d * seconds;

        if (data.HasSpeed)
        {
            _latestSpeedKph = data.Speed;
            if (data.Speed > 1)
            {
                var travelledMeters = data.Speed / 3.6d * seconds;
                _distanceMeters += travelledMeters;
                _movingSeconds += seconds;
                if (data.HasRpm && data.Rpm < 100)
                    _electricMovingSeconds += seconds;
                _maxSpeedKph = Math.Max(_maxSpeedKph, data.Speed);
                if (measurement is { } drivingMeasurement)
                {
                    if (data.Speed <= 60)
                    {
                        _cityMeters += travelledMeters;
                        _cityFuelLiters += drivingMeasurement.LitersPerHour / 3600d * seconds;
                    }
                    else
                    {
                        _highwayMeters += travelledMeters;
                        _highwayFuelLiters += drivingMeasurement.LitersPerHour / 3600d * seconds;
                    }
                }
            }
            else
            {
                _idleSeconds += seconds;
            }
        }

        if (data.HasRpm && seconds > 0)
        {
            _rpmSeconds += seconds;
            _rpmWeightedSum += data.Rpm * seconds;
        }
        if (data.HasEngineLoad && seconds > 0)
        {
            _loadSeconds += seconds;
            _loadWeightedSum += data.EngineLoad * seconds;
        }

        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        var measurement = FuelConsumptionService.ResolveFuelRate(
            _latestFuelRateLitersPerHour,
            _latestMafGramsPerSecond,
            _latestFuelTypeCode);
        Instant = _instantLitersPer100Km.HasValue ? _units.FormatFuelEconomy(_instantLitersPer100Km.Value) : "—";
        Distance = _units.FormatDistance(_distanceMeters / 1000d);
        FuelUsed = _units.Format(_fuelLiters, "L", "0.00");
        FuelRate = measurement is { } currentRate ? _units.Format(currentRate.LitersPerHour, "L/h", "0.00") : "—";
        FuelLevel = _latestFuelLevelPercent.HasValue
            ? $"{_latestFuelLevelPercent.Value:0}%"
            : _localization.IsEnglish ? "Unavailable from ECU" : "ECU-დან მიუწვდომელია";
        Driving = measurement is { IsDirect: true }
            ? "Direct ECU value — Fuel Rate PID 01 5E"
            : measurement is not null
                ? "Calculated value — MAF + fuel type"
                : "Fuel Rate ან MAF ჯერ არ მოსულა ECU-დან.";

        if (_distanceMeters > 100d)
        {
            var average = _fuelLiters / (_distanceMeters / 1000d) * 100d;
            Average = _units.FormatFuelEconomy(average);
            Trip = Average;
        }
        else
        {
            Average = Trip = "—";
        }

        TripTime = $"Trip: {(_movingSeconds + _idleSeconds) / 60d:0} min";
        MovingTime = $"{_movingSeconds / 60d:0} min";
        IdleTime = $"{_idleSeconds / 60d:0} min";
        MaximumSpeed = _maxSpeedKph > 0 ? _units.FormatSpeed(_maxSpeedKph) : "—";
        AverageSpeed = _movingSeconds > 0 ? _units.FormatSpeed((_distanceMeters / 1000d) / (_movingSeconds / 3600d)) : "—";
        AverageRpm = _rpmSeconds > 0 ? $"{_rpmWeightedSum / _rpmSeconds:0} rpm" : "—";
        EngineLoad = _loadSeconds > 0 ? $"{_loadWeightedSum / _loadSeconds:0}%" : "—";
        CityConsumption = FormatConsumption(_cityFuelLiters, _cityMeters);
        HighwayConsumption = FormatConsumption(_highwayFuelLiters, _highwayMeters);
        TripCost = TryGetFuelPrice(out var price)
            ? $"₾{_fuelLiters * price:0.00}"
            : "ფასის შეყვანის შემდეგ გამოჩნდება";
        RefreshEnergyPresentation();
    }

    partial void OnFuelPriceTextChanged(string value)
    {
        Preferences.Default.Set("fuel_price_per_liter", value ?? string.Empty);
        RefreshPresentation();
    }

    [RelayCommand]
    private async Task AddRefuel()
    {
        if (!await _pro.EnsureProAsync())
        {
            await Shell.Current.DisplayAlertAsync(CarScanner.Services.Localization.LocalizationService.T("PRO ფუნქცია"), CarScanner.Services.Localization.LocalizationService.T("Refuel Log ხელმისაწვდომია PRO-ში."), CarScanner.Services.Localization.LocalizationService.T("კარგი"));
            return;
        }

        if (!TryParsePositive(RefuelLitersText, out var liters) ||
            !TryParseNonNegative(RefuelOdometerText, out var odometer) ||
            !TryParsePositive(RefuelPriceText, out var price))
        {
            RefuelStatus = "შეიყვანე სწორი ოდომეტრი (km), ლიტრები და ფასი 1 ლიტრზე.";
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
            OdometerKm = odometer,
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
            RefuelHistory.Add(record);
        TankToTankSummary = BuildTankToTankSummary(records);
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

    private string FormatConsumption(double fuelLiters, double distanceMeters) => distanceMeters > 100d
        ? _units.FormatFuelEconomy(fuelLiters / (distanceMeters / 1000d) * 100d)
        : "—";

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
        // Battery energy (electric / hybrid): measured kWh, or the
        // state-of-charge estimate when the current could not be read.
        double? energyKWh = _mode == EnergyMode.Fuel ? null : _ev.HasSample ? _ev.NetKWh : ApproxKWh();
        var hasEnergy = energyKWh is > 0;
        var hasFuel = _fuelLiters > 0 && _mode != EnergyMode.Electric;

        if (_tripStartedAt is null || _distanceMeters <= 100d || (!hasFuel && !hasEnergy))
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

        var measurement = FuelConsumptionService.ResolveFuelRate(
            _latestFuelRateLitersPerHour,
            _latestMafGramsPerSecond,
            _latestFuelTypeCode);
        var distanceKm = _distanceMeters / 1000d;
        var priceIsKnown = TryGetFuelPrice(out var price);
        var record = new FuelTripRecord
        {
            VIN = vin!,
            StartedAt = _tripStartedAt.Value,
            FinishedAt = DateTime.Now,
            DistanceKm = distanceKm,
            FuelLiters = _fuelLiters,
            MovingSeconds = _movingSeconds,
            IdleSeconds = _idleSeconds,
            AverageSpeedKph = _movingSeconds > 0 ? distanceKm / (_movingSeconds / 3600d) : 0,
            MaximumSpeedKph = _maxSpeedKph,
            AverageFuelLPer100Km = distanceKm > 0 ? _fuelLiters / distanceKm * 100d : null,
            FuelPricePerLiter = priceIsKnown && hasFuel ? price : null,
            FuelDataSource = _mode == EnergyMode.Electric
                ? (_evSource?.Label ?? "State of charge × battery capacity (estimate)")
                : measurement is { IsDirect: true } ? "Direct ECU Fuel Rate PID 01 5E" : "Calculated from MAF + fuel type",
            EnergyKWh = hasEnergy ? energyKWh : null,
            RegeneratedKWh = hasEnergy && _ev.HasSample ? _ev.RegeneratedKWh : null,
            EnergyPricePerKWh = hasEnergy && TryParsePositive(ElectricityPriceText, out var kwhPrice) ? kwhPrice : null
        };
        if (!hasFuel)
        {
            record.FuelLiters = 0;
            record.AverageFuelLPer100Km = null;
        }

        await _database.SaveFuelTripAsync(record);
        _tripStartedAt = null;
        await LoadTripHistoryAsync();
        return hasFuel
            ? $"მოგზაურობა შენახულია: {_units.FormatDistance(record.DistanceKm)} · {_units.FormatFuelEconomy(record.AverageFuelLPer100Km ?? 0)}."
            : $"მოგზაურობა შენახულია: {_units.FormatDistance(record.DistanceKm)} · {record.EnergyKWh / distanceKm * 100d:0.0} kWh/100km.";
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
            TripHistory.Add($"{record.FinishedAt:dd.MM HH:mm} · {_units.FormatDistance(record.DistanceKm)} · {fuel} · {record.MovingSeconds / 60d:0} min{cost}\n{record.FuelDataSource}");
        }

        HasTripHistory = TripHistory.Count > 0;
        TripHistorySummary = _tripHistoryRecords.Count == 0
            ? "დასრულებული მოგზაურობები ჯერ არ არის. Start → Stop შემდეგ ჩანაწერი ავტომატურად შეინახება, თუ ECU-დან მოვა რეალური speed და fuel data."
            : $"შენახულია {_tripHistoryRecords.Count} მოგზაურობა ამ VIN-ზე.";
    }

    public async Task OnPageDisappearingAsync()
    {
        StopEnergyLoop();
        if (_tripStartedAt is not null)
            await Stop();
        else
        {
            Unsubscribe();
            _engine.SetFuelAnalyticsEnabled(false);
        }
    }
}
