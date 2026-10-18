using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.Energy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// Bridge between the Fuel page and <see cref="FuelTripEngine"/>. The engine owns every number (distance, fuel, averages, instant
/// consumption); the older presentation code reads them through the private fields that <see cref="SyncFromEngine"/> refreshes.
/// Also: unit and fuel-type choices, data-quality labels, "what this car reports", and recovery of an interrupted trip.
/// </summary>
public partial class FuelViewModel
{
    private const string CheckpointFile = "fuel_trip_checkpoint.json";
    private static readonly TimeSpan CheckpointEvery = TimeSpan.FromSeconds(20);

    private readonly FuelTripEngine _fuel = new();
    private FuelKind? _userFuelKind;
    private DateTime? _lastAverageAt;
    private Timer? _checkpointTimer;
    private FuelTripCheckpoint? _interruptedCheckpoint;
    private volatile bool _tripActive;

    public ObservableCollection<EnergyCapabilityRow> FuelSupportRows { get; } = new();

    public string[] FuelUnitOptions { get; } = Enum.GetValues<FuelEconomyUnit>().Select(FuelUnits.Label).ToArray();
    public string[] FuelKindOptions { get; } = BuildFuelKindOptions();

    [ObservableProperty] private string instantNote = "";
    [ObservableProperty] private string qualityText = "";
    [ObservableProperty] private Color qualityColor = GreyColor;
    [ObservableProperty] private string qualityDetail = "";
    [ObservableProperty] private bool hasQualityDetail;
    [ObservableProperty] private string tripQualityText = "";
    [ObservableProperty] private string hybridNote = "";
    [ObservableProperty] private bool showHybridNote;
    [ObservableProperty] private string supportCaption = "";
    [ObservableProperty] private string unitCaption = "";
    [ObservableProperty] private string fuelKindCaption = "";
    [ObservableProperty] private bool hasInterruptedTrip;
    [ObservableProperty] private string interruptedTripText = "";
    [ObservableProperty] private string engineOnAverageText = "—";
    [ObservableProperty] private bool showEngineOnAverage;

    public int SelectedFuelUnitIndex
    {
        get => (int)_units.FuelEconomyUnit;
        set
        {
            if (value < 0 || value >= FuelUnitOptions.Length || value == (int)_units.FuelEconomyUnit)
                return;
            _units.SetFuelEconomyUnit((FuelEconomyUnit)value);
        }
    }

    /// <summary>0 = detect automatically; otherwise the combustion fuel the user chose for this car.</summary>
    public int SelectedFuelKindIndex
    {
        get => _userFuelKind is { } kind ? Array.IndexOf(UserChoosableKinds, kind) + 1 : 0;
        set
        {
            var chosen = value <= 0 || value > UserChoosableKinds.Length ? (FuelKind?)null : UserChoosableKinds[value - 1];
            if (chosen == _userFuelKind)
                return;

            _userFuelKind = chosen;
            if (CurrentVin() is { } vin)
            {
                if (chosen is null)
                    Preferences.Default.Remove($"fuel_kind_{vin}");
                else
                    Preferences.Default.Set($"fuel_kind_{vin}", chosen.ToString());
            }

            UpdateFuelKind();
            OnPropertyChanged(nameof(SelectedFuelKindIndex));
            RefreshPresentation();
        }
    }

    private static readonly FuelKind[] UserChoosableKinds =
        { FuelKind.Gasoline, FuelKind.Diesel, FuelKind.Ethanol, FuelKind.Lpg };

    private static string[] BuildFuelKindOptions() =>
        new[] { "Auto" }.Concat(UserChoosableKinds.Select(kind => FuelKinds.Display(kind, true))).ToArray();

    private void InitFuelEngine()
    {
        _units.FuelEconomyUnitChanged += (_, __) => MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(SelectedFuelUnitIndex));
            RefreshPresentation();
            RebuildTripHistoryPresentation();
            RebuildRefuelSummaries();
        });
        LoadUserFuelKind();
        UpdateFuelKind();
        _ = LoadInterruptedTripAsync();
    }

    private void LoadUserFuelKind()
    {
        _userFuelKind = null;
        if (CurrentVin() is { } vin &&
            Enum.TryParse<FuelKind>(Preferences.Default.Get($"fuel_kind_{vin}", string.Empty), out var saved) &&
            UserChoosableKinds.Contains(saved))
            _userFuelKind = saved;
    }

    /// <summary>The combustion fuel used for air-flow estimates: the user's choice, else PID 51, else the vehicle text — never a guess.</summary>
    private void UpdateFuelKind()
    {
        var resolved = _mode == EnergyMode.Electric
            ? FuelKind.Electric
            : FuelKinds.Resolve(_userFuelKind, _latestFuelTypeCode, _vehicle.CurrentVehicle?.FuelType);
        _fuel.Kind = resolved;
        _engine.SetFuelAnalyticsLambda(resolved == FuelKind.Diesel);
        OnPropertyChanged(nameof(SelectedFuelKindIndex));
    }

    // ------------------------------------------------------------------
    // Engine bridge
    // ------------------------------------------------------------------

    /// <summary>Copies the engine's totals into the fields the page code reads.</summary>
    private void SyncFromEngine(DateTime now)
    {
        var trip = _fuel.Snapshot();
        _distanceMeters = trip.DistanceKm * 1000d;
        _fuelLiters = trip.FuelLiters;
        _movingSeconds = trip.MovingSeconds;
        _idleSeconds = trip.IdleSeconds;
        _maxSpeedKph = (int)Math.Round(trip.MaximumSpeedKph);
        _cityMeters = trip.CityKm * 1000d;
        _cityFuelLiters = trip.CityLiters;
        _highwayMeters = trip.HighwayKm * 1000d;
        _highwayFuelLiters = trip.HighwayLiters;
        _electricMeters = trip.EngineOffKm * 1000d;
        _engineMeters = trip.EngineOnKm * 1000d;
        _instantLitersPer100Km = _fuel.Instant.LitersPer100Km;
        _latestSpeedKph = _fuel.Signals.Get(FuelSignal.SpeedKph, now, FuelLimits.DriveSignalMaxAge);
        _speedSeen = _fuel.Signals.HasEverReceived(FuelSignal.SpeedKph);
        var rpm = _fuel.Signals.Get(FuelSignal.Rpm, now, FuelLimits.DriveSignalMaxAge);
        _rpmSeen = rpm is not null;
        _latestRpm = rpm ?? 0;
    }

    /// <summary>Time-weighted average rpm and engine load (only from readings that arrived in this tick).</summary>
    private void TrackAverages(LiveDataModel data, DateTime now)
    {
        var seconds = _lastAverageAt is { } last ? (now - last).TotalSeconds : 0;
        _lastAverageAt = now;
        if (seconds <= 0 || seconds > FuelLimits.MaximumGap.TotalSeconds)
            return;

        if (data.HasRpm)
        {
            _rpmSeconds += seconds;
            _rpmWeightedSum += data.Rpm * seconds;
        }

        if (data.HasEngineLoad)
        {
            _loadSeconds += seconds;
            _loadWeightedSum += data.EngineLoad * seconds;
        }
    }

    // ------------------------------------------------------------------
    // Presentation of the fuel side
    // ------------------------------------------------------------------

    private void RefreshFuelPresentation()
    {
        var trip = _fuel.Snapshot();
        var unit = _units.FuelEconomyUnit;
        var en = _localization.IsEnglish;
        var instant = FuelPresenter.Instant(_fuel.Instant, unit, en);

        Instant = instant.IsAvailable && instant.Value != "—" ? $"{instant.Value} {instant.Unit}" : "—";
        InstantNote = instant.Note;
        FuelUsed = FuelPresenter.FuelUsed(trip, unit);
        _fuelRateSensor.Value = _fuel.Instant.LitersPerHour;
        FuelRate = _fuel.Instant.LitersPerHour is { } flow ? _units.FormatFuelRate(flow, _fuel.Instant.IsEstimate) : "—";

        var quality = _fuel.Instant.Quality;
        QualityText = FuelPresenter.QualityLabel(quality, en);
        QualityColor = quality switch
        {
            FuelDataQuality.Direct => GreenColor,
            FuelDataQuality.Estimated => AmberColor,
            _ => GreyColor
        };
        QualityDetail = string.Join(" · ", new[] { _fuel.Instant.Source, _fuel.Instant.Assumption }.Where(part => !string.IsNullOrWhiteSpace(part)));
        HasQualityDetail = QualityDetail.Length > 0;
        Driving = quality == FuelDataQuality.None ? instant.Note : QualityText;

        Average = Trip = FuelPresenter.Average(trip, unit);
        TripQualityText = FuelPresenter.TripQualityLabel(trip.Quality, trip.FuelCoverage, en);

        // A plug-in / hybrid: two different statements about the same fuel, plus what they mean.
        ShowEngineOnAverage = _mode == EnergyMode.Hybrid && trip.EngineOnLPer100Km is not null;
        EngineOnAverageText = trip.EngineOnLPer100Km is { } onEngine ? FuelUnits.Format(onEngine, unit, trip.Quality != TripFuelQuality.Direct) : "—";
        HybridNote = BuildHybridNote(trip, en);
        ShowHybridNote = _mode == EnergyMode.Hybrid && HybridNote.Length > 0;
    }

    private string BuildHybridNote(FuelTripSnapshot trip, bool en)
    {
        if (_mode != EnergyMode.Hybrid)
            return string.Empty;

        var lines = new List<string>
        {
            en
                ? "Fuel average = fuel ÷ the whole trip distance, including kilometres driven on electricity. It is not the consumption of the engine alone."
                : "საწვავის საშუალო = საწვავი ÷ მთელი მანძილი, ელექტროზე ნავალი კილომეტრების ჩათვლით. ეს მარტო ძრავის ხარჯი არ არის."
        };

        if (trip.EngineOnLPer100Km is not null)
        {
            lines.Add(en
                ? "Engine-on figure = the same fuel ÷ only the distance driven with the engine running (from rpm)."
                : "„ძრავის ჩართვისას“ = იგივე საწვავი ÷ მხოლოდ იმ მანძილზე, როცა ძრავი მუშაობდა (ბრუნვებიდან).");
        }

        var electric = ElectricTripCalculator.Compute(_mode, trip, _ev.HasSample, _ev.ConsumedKWh, _ev.RegeneratedKWh, _socStart, _socNow, CapacityKWh());
        if (electric.ChargeBalanceNotable && electric.SocChange is { } change)
        {
            lines.Add(en
                ? $"The battery charge changed by {change:+0;-0}% during this trip, so fuel use alone is not the whole energy used (or saved)."
                : $"ამ მოგზაურობაში ბატარეის მუხტი {change:+0;-0}%-ით შეიცვალა, ამიტომ მხოლოდ საწვავი მთლიან ენერგიას არ ასახავს.");
        }

        lines.Add(en
            ? "A stopped engine does not mean no energy was used — the electric part is shown separately and is only as good as the battery data."
            : "გამორთული ძრავი არ ნიშნავს, რომ ენერგია არ დახარჯულა — ელექტრო ნაწილი ცალკეა და ბატარეის მონაცემის სიზუსტეზეა დამოკიდებული.");
        return string.Join("\n", lines);
    }

    /// <summary>"What this car reports" for the fuel side: which calculations are possible and on what basis.</summary>
    private void RebuildSupportRows()
    {
        var en = _localization.IsEnglish;
        var facts = new EnergyFacts(
            _mode,
            _fuel.Kind,
            AdvertisedPids(),
            _evSource is not null,
            _socNow is not null || _evSource is not null,
            CapacityKWh() is not null,
            AdvertisedPids()?.Contains(0x24) == true,
            _evAdapterLimited);

        SupportCaption = en ? "What this car reports" : "რას გადმოსცემს ეს მანქანა";
        UnitCaption = en ? "Consumption unit" : "ხარჯის ერთეული";
        FuelKindCaption = en ? "Fuel type (for the air-flow estimate)" : "საწვავის ტიპი (ჰაერის ხარჯით გამოთვლისთვის)";

        var rows = EnergyCompatibility.Evaluate(facts)
            .Where(capability => capability.Reason != EnergyReason.NotApplicableToThisPowertrain)
            .Select(capability => new EnergyCapabilityRow(
                FuelPresenter.Mark(capability.Support),
                capability.Support switch
                {
                    EnergySupport.DirectlySupported or EnergySupport.CalculatedFromAvailableData => GreenColor,
                    EnergySupport.Estimated or EnergySupport.PartiallySupported => AmberColor,
                    _ => GreyColor
                },
                FuelPresenter.MetricName(capability.Metric, en),
                string.Join(" · ", new[]
                {
                    FuelPresenter.SupportLabel(capability.Support, en),
                    FuelPresenter.ReasonText(capability.Reason, en)
                }.Where(part => part.Length > 0))))
            .ToList();

        var changed = rows.Count != FuelSupportRows.Count || rows.Where((row, index) => row != FuelSupportRows[index]).Any();
        if (!changed)
            return;

        FuelSupportRows.Clear();
        foreach (var row in rows)
            FuelSupportRows.Add(row);
    }

    private IReadOnlySet<int>? AdvertisedPids() => _engine.AdvertisedPids;

    // ------------------------------------------------------------------
    // Checkpoint: an interrupted trip can be recovered
    // ------------------------------------------------------------------

    private static string CheckpointPath => Path.Combine(FileSystem.AppDataDirectory, CheckpointFile);

    private void BeginTripTracking()
    {
        _tripActive = true;
        _checkpointTimer?.Dispose();
        _checkpointTimer = new Timer(_ => MainThread.BeginInvokeOnMainThread(OnCheckpointTimer), null, CheckpointEvery, CheckpointEvery);
    }

    private void OnCheckpointTimer()
    {
        WriteCheckpoint();

        // The notification otherwise keeps showing the last distance as if the trip were still being measured while
        // the adapter link is down or the car is asleep: say that no data is coming in.
        if (CarScanner.Services.Background.TripKeepAlive.IsActive && DateTime.UtcNow - _lastDataAtUtc > StaleDataAfter)
            CarScanner.Services.Background.TripKeepAlive.Update(
                CarScanner.Services.Localization.LocalizationService.T(L("OBD360 — ველოდები მანქანის მონაცემებს…", "OBD360 — waiting for data from the car…")));
    }

    private void EndTripTracking(bool deleteCheckpoint)
    {
        _tripActive = false;
        _checkpointTimer?.Dispose();
        _checkpointTimer = null;
        if (deleteCheckpoint)
            DeleteCheckpoint();
        else
            WriteCheckpoint();
    }

    private void WriteCheckpoint()
    {
        try
        {
            if (_fuel.StartedAt is null || _tripStartedAt is null)
                return;

            var energyIsEstimate = !_ev.HasSample;
            double? net = _mode == EnergyMode.Fuel ? null : _ev.HasSample ? _ev.NetKWh : ApproxKWh();
            double? regen = _mode != EnergyMode.Fuel && _ev.HasSample ? _ev.RegeneratedKWh : null;
            var checkpoint = _fuel.ExportCheckpoint(DateTime.UtcNow, _mode.ToString(), net, regen, energyIsEstimate, CurrentVin());

            // Write to a temp file and move it over the old one: a crash in the middle never leaves a half-written checkpoint.
            var temp = CheckpointPath + ".tmp";
            File.WriteAllText(temp, checkpoint.ToJson());
            File.Move(temp, CheckpointPath, overwrite: true);
        }
        catch
        {
            // Checkpoints are a safety net; they must never disturb the trip.
        }
    }

    private static void DeleteCheckpoint()
    {
        try
        {
            if (File.Exists(CheckpointPath))
                File.Delete(CheckpointPath);
            if (File.Exists(CheckpointPath + ".tmp"))
                File.Delete(CheckpointPath + ".tmp");
        }
        catch
        {
        }
    }

    private async Task LoadInterruptedTripAsync()
    {
        try
        {
            await Task.Yield();
            if (_tripActive || _tripStartedAt is not null || !File.Exists(CheckpointPath))
                return;

            var checkpoint = FuelTripCheckpoint.TryParse(await File.ReadAllTextAsync(CheckpointPath));
            if (checkpoint is null || !checkpoint.IsValid(DateTime.UtcNow) || checkpoint.DistanceKm < 0.1)
            {
                DeleteCheckpoint();   // damaged, too old, or nothing worth keeping
                return;
            }

            _interruptedCheckpoint = checkpoint;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                InterruptedTripText = L(
                    $"გაწყვეტილი მოგზაურობა: {_units.FormatDistance(checkpoint.DistanceKm)} · {checkpoint.StartedAt.ToLocalTime():dd.MM HH:mm}. აპი მის დასრულებამდე დაიხურა.",
                    $"Interrupted trip: {_units.FormatDistance(checkpoint.DistanceKm)} · {checkpoint.StartedAt.ToLocalTime():dd.MM HH:mm}. The app stopped before it was finished.");
                HasInterruptedTrip = true;
            });
        }
        catch
        {
            // a damaged file is simply ignored
        }
    }

    [RelayCommand]
    private void DiscardInterruptedTrip()
    {
        _interruptedCheckpoint = null;
        HasInterruptedTrip = false;
        DeleteCheckpoint();
    }

    [RelayCommand]
    private async Task SaveInterruptedTrip()
    {
        try
        {
            var checkpoint = _interruptedCheckpoint;
            if (checkpoint is null)
                return;

            var vin = checkpoint.Vin;
            if (IsUnknownVin(vin))
            {
                InterruptedTripText = L("მოგზაურობა ვერ შეინახა: მანქანის VIN უცნობია.", "The trip could not be saved: the vehicle's VIN is unknown.");
                return;
            }

            var engine = new FuelTripEngine();
            if (!engine.TryRestore(checkpoint, DateTime.UtcNow))
            {
                DiscardInterruptedTrip();
                return;
            }

            var trip = engine.Snapshot();
            var energyKWh = checkpoint.EnergyNetKWh;
            var record = BuildTripRecord(vin!, checkpoint.StartedAt.ToLocalTime(), checkpoint.SavedAt.ToLocalTime(), trip, engine.Kind,
                Enum.TryParse<EnergyMode>(checkpoint.Mode, out var mode) ? mode : EnergyMode.Fuel,
                energyKWh, checkpoint.EnergyRegeneratedKWh, checkpoint.EnergyIsEstimate, interrupted: true);

            await _database.SaveFuelTripAsync(record);   // a trip that was already saved is not stored twice
            DiscardInterruptedTrip();
            await LoadTripHistoryAsync();
        }
        catch (Exception ex)
        {
            CarScanner.Services.CommandErrors.Report(ex, "FuelViewModel.SaveInterruptedTrip");
        }
    }

    private FuelTripRecord BuildTripRecord(
        string vin, DateTime started, DateTime finished, FuelTripSnapshot trip, FuelKind kind, EnergyMode mode,
        double? energyKWh, double? regeneratedKWh, bool energyIsEstimate, bool interrupted)
    {
        var hasEnergy = mode != EnergyMode.Fuel && energyKWh is > 0;
        var hasFuel = mode != EnergyMode.Electric && trip.FuelLiters > 0;
        var priceKnown = TryGetFuelPrice(out var price) && price > 0;
        var energyQuality = !hasEnergy ? EnergyQuality.Unavailable : energyIsEstimate ? EnergyQuality.Estimated : EnergyQuality.Measured;
        var total = trip.FuelLiters;

        return new FuelTripRecord
        {
            VIN = vin,
            StartedAt = started,
            FinishedAt = finished,
            DistanceKm = trip.DistanceKm,
            FuelLiters = hasFuel ? trip.FuelLiters : 0,
            MovingSeconds = trip.MovingSeconds,
            IdleSeconds = trip.IdleSeconds,
            AverageSpeedKph = trip.AverageSpeedKph ?? 0,
            MaximumSpeedKph = trip.MaximumSpeedKph,
            AverageFuelLPer100Km = hasFuel ? trip.AverageLPer100Km : null,
            FuelPricePerLiter = priceKnown && hasFuel ? price : null,
            FuelDataSource = FuelPresenter.TripSource(hasFuel ? trip.Quality : TripFuelQuality.None, kind, energyQuality, interrupted),
            EnergyKWh = hasEnergy ? energyKWh : null,
            RegeneratedKWh = hasEnergy && !energyIsEstimate ? regeneratedKWh : null,
            EnergyPricePerKWh = hasEnergy && TryParsePositive(ElectricityPriceText, out var kwhPrice) ? kwhPrice : null,
            Powertrain = mode.ToString(),
            FuelKind = kind.ToString(),
            EstimatedFuelShare = total > 0 ? trip.EstimatedLiters / total : 0,
            FuelCoverage = trip.FuelCoverage,
            EnergyIsEstimate = hasEnergy && energyIsEstimate,
            EngineOffKm = mode == EnergyMode.Hybrid ? trip.EngineOffKm : null,
            GapSeconds = trip.GapSeconds,
            IsInterrupted = interrupted
        };
    }
}
