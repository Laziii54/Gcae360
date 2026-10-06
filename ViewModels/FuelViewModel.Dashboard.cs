using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Services.Energy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One line of "what this car reports" on the energy page.</summary>
public sealed record EnergyCapabilityRow(string Mark, Color MarkColor, string Title, string Detail);

/// <summary>One label / value line of the trip summary.</summary>
public sealed record TripSummaryRow(string Title, string Value);

/// <summary>
/// Electric / plug-in dashboard of the Fuel page: battery ring, drive state
/// (electric or engine), power history, electric/fuel split, data checklist
/// and the summary shown after a trip. Works for every plug-in hybrid and EV:
/// exact values when the car reports battery current, "≈" estimates when it
/// only reports the state of charge.
/// </summary>
public partial class FuelViewModel
{
    private const int PowerHistoryLength = 150;
    private const double EngineOnRpm = 300;
    private const double TypicalKWhPer100Km = 17;
    private const double ReferenceLitersPer100Km = 7.5;

    private static readonly Color GreenColor = Color.FromArgb("#1E8A4C");
    private static readonly Color GreenBackground = Color.FromArgb("#E2F3E9");
    private static readonly Color OrangeColor = Color.FromArgb("#D0701F");
    private static readonly Color OrangeBackground = Color.FromArgb("#FCEBDC");
    private static readonly Color AmberColor = Color.FromArgb("#B7791F");
    private static readonly Color AmberBackground = Color.FromArgb("#FDF3DF");
    private static readonly Color GreyColor = Color.FromArgb("#A2A8AF");

    private readonly List<double> _powerHistory = new();
    private double _latestRpm;
    private bool _rpmSeen;
    private bool _speedSeen;
    private double _electricMeters;
    private double _engineMeters;

    public ObservableCollection<EnergyCapabilityRow> EnergyCapabilities { get; } = new();
    public ObservableCollection<TripSummaryRow> TripSummaryRows { get; } = new();

    // Hero card
    [ObservableProperty] private double socProgress;
    [ObservableProperty] private Color socRingColor = GreenColor;
    [ObservableProperty] private string socBigText = "—";
    [ObservableProperty] private string rangeText = "";
    [ObservableProperty] private bool showDriveBadge;
    [ObservableProperty] private string driveBadgeText = "";
    [ObservableProperty] private Color driveBadgeColor = GreenColor;
    [ObservableProperty] private Color driveBadgeBackground = GreenBackground;
    [ObservableProperty] private string heroValueText = "—";
    [ObservableProperty] private string heroUnitText = "";
    [ObservableProperty] private Color heroValueColor = GreyColor;
    [ObservableProperty] private string heroHintText = "";
    [ObservableProperty] private bool showHeroPowerBar;

    // Power history
    [ObservableProperty] private IReadOnlyList<double> powerHistory = Array.Empty<double>();
    [ObservableProperty] private string powerHistoryCaption = "";

    // Energy card
    [ObservableProperty] private GridLength consumedStar = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength regenStar = new(0, GridUnitType.Star);
    [ObservableProperty] private bool showEnergyBar;
    [ObservableProperty] private string netEnergyCaption = "წმინდა ხარჯი";
    [ObservableProperty] private string netEnergyText = "—";
    [ObservableProperty] private string energyCardCaption = "ამ მოგზაურობის ენერგია";
    [ObservableProperty] private string energyNoteText = "";

    // Electric / fuel split (plug-in)
    [ObservableProperty] private bool showDriveSplit;
    [ObservableProperty] private GridLength electricShareStar = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength engineShareStar = new(0, GridUnitType.Star);
    [ObservableProperty] private string electricShareText = "";
    [ObservableProperty] private string engineShareText = "";
    [ObservableProperty] private string electricSplitText = "—";
    [ObservableProperty] private string engineSplitText = "—";
    [ObservableProperty] private string splitFuelAverageText = "—";
    [ObservableProperty] private string splitCaption = "რაზე იარე ამ მოგზაურობაში";

    // Battery card
    [ObservableProperty] private string socStartNowText = "—";
    [ObservableProperty] private string voltageCurrentText = "—";
    [ObservableProperty] private bool canFlipSign;
    [ObservableProperty] private string flipSignText = "ხარჯი და დამუხტვა არეულია? შეცვლა";

    // Trip summary
    [ObservableProperty] private bool isTripSummaryVisible;
    [ObservableProperty] private string tripSummaryTitle = "";
    [ObservableProperty] private string tripSummaryCostText = "—";
    [ObservableProperty] private string tripSummaryCostDetail = "";
    [ObservableProperty] private bool showTripSummaryComparison;
    [ObservableProperty] private GridLength summaryYouStar = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength summaryYouRest = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength summaryPetrolStar = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength summaryPetrolRest = new(0, GridUnitType.Star);
    [ObservableProperty] private string summaryYouText = "";
    [ObservableProperty] private string summaryPetrolText = "";
    [ObservableProperty] private string summarySavingText = "";

    private bool EngineRunning => _rpmSeen && _latestRpm >= EngineOnRpm;

    /// <summary>Called for every OBD sample of the trip (speed / rpm).</summary>
    private void TrackDrive(bool hasRpm, double rpm, bool hasSpeed, double travelledMeters)
    {
        if (hasRpm)
        {
            _rpmSeen = true;
            _latestRpm = rpm;
        }

        if (hasSpeed)
            _speedSeen = true;

        if (travelledMeters <= 0)
            return;

        // An EV has no engine rpm at all: every metre is electric.
        if (EngineRunning)
            _engineMeters += travelledMeters;
        else
            _electricMeters += travelledMeters;
    }

    /// <summary>Called with every battery reading (kW, + = consumption).</summary>
    private void PushPower(double kw)
    {
        _powerHistory.Add(kw);
        if (_powerHistory.Count > PowerHistoryLength)
            _powerHistory.RemoveAt(0);
        PowerHistory = _powerHistory.ToArray();
    }

    private void ResetDashboard()
    {
        _powerHistory.Clear();
        PowerHistory = Array.Empty<double>();
        _electricMeters = _engineMeters = 0;
        _rpmSeen = _speedSeen = false;
        _latestRpm = 0;
    }

    private string? _signVin;

    /// <summary>Before battery reads start: load this car's current direction once.</summary>
    private void PrepareSignForCurrentCar()
    {
        var vin = CurrentVin();
        if (_signVin == vin && _signVin is not null)
            return;

        _signVin = vin;
        ApplySavedSign(vin);
    }

    private string SignKey(string vin) => $"ev_current_sign_{vin}";

    /// <summary>Another car (or the first read): use its saved current direction.</summary>
    private void ApplySavedSign(string? vin)
    {
        _ev.ForgetSign();
        if (vin is null)
            return;

        var saved = Preferences.Default.Get(SignKey(vin), 0);
        if (saved != 0)
            _ev.LockSign(saved);
    }

    private void OnSignLearned(int sign)
    {
        if (CurrentVin() is { } vin)
            Preferences.Default.Set(SignKey(vin), sign);
    }

    [RelayCommand]
    private void FlipEnergySign()
    {
        var sign = _ev.FlipSign();
        if (CurrentVin() is { } vin)
            Preferences.Default.Set(SignKey(vin), sign);
        RefreshPresentation();
    }

    [RelayCommand]
    private void CloseTripSummary() => IsTripSummaryVisible = false;

    [RelayCommand]
    private async Task StartNewTrip()
    {
        try
        {
        IsTripSummaryVisible = false;
        ResetCurrentTrip();
        RefreshPresentation();
        await Start();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FuelViewModel.StartNewTrip");
        }
    }

    /// <summary>Usable battery capacity (kWh) entered for this car, if any.</summary>
    private double? CapacityKWh() =>
        TryParsePositive(BatteryCapacityText, out var capacity) ? capacity : null;

    private void RefreshDashboard()
    {
        var invariant = CultureInfo.InvariantCulture;
        var km = _distanceMeters / 1000d;
        var exact = _ev.HasSample;
        var approxKWh = ApproxKWh();

        // ---- Ring: state of charge + electric range
        SocProgress = _socNow is { } soc ? Math.Clamp(soc / 100d, 0, 1) : 0;
        SocBigText = _socNow is { } socValue ? $"{socValue.ToString("0", invariant)}%" : "—";
        SocRingColor = _socNow is < 20 ? OrangeColor : GreenColor;
        var perKm = (exact ? EvEnergyAccumulator.KWhPer100Km(_ev.NetKWh, km) : approxKWh is { } a ? EvEnergyAccumulator.KWhPer100Km(a, km) : null);
        var usePer100 = perKm is > 5 and < 60 && km >= 2 ? perKm.Value : TypicalKWhPer100Km;
        RangeText = _socNow is { } left && CapacityKWh() is { } capacity
            ? $"≈ {(left / 100d * capacity / usePer100 * 100d).ToString("0", invariant)} {L("კმ", "km")}"
            : _socNow is null ? L("% არ მოდის", "no %") : L("ტევადობა ქვემოთ", "set capacity");

        // ---- Drive state badge
        ShowDriveBadge = ShowEnergy;
        if (!exact && _mode == EnergyMode.Electric)
        {
            DriveBadgeText = L("≈ მიახლოებით", "≈ estimated");
            DriveBadgeColor = AmberColor;
            DriveBadgeBackground = AmberBackground;
        }
        else if (_mode == EnergyMode.Hybrid && EngineRunning)
        {
            DriveBadgeText = L("⛽ ძრავი მუშაობს", "⛽ engine running");
            DriveBadgeColor = OrangeColor;
            DriveBadgeBackground = OrangeBackground;
        }
        else
        {
            DriveBadgeText = L("⚡ ელექტროზე", "⚡ on electric");
            DriveBadgeColor = GreenColor;
            DriveBadgeBackground = GreenBackground;
        }

        // ---- Hero value: live power, or fuel use while the engine runs, or the estimate
        var power = _ev.PowerKw;
        if (exact)
        {
            HeroValueText = $"{(power < -0.05 ? "−" : "")}{Math.Abs(power).ToString("0.0", invariant)}";
            HeroUnitText = power > 0.05
                ? L("kW · იხარჯება", "kW · discharging")
                : power < -0.05 ? L("kW · იმუხტება", "kW · charging") : L("kW · უმოქმედო", "kW · idle");
            HeroValueColor = power < -0.05 ? GreenColor : power > 0.05 ? OrangeColor : GreyColor;
            HeroHintText = _ev.IsSignConfirmed ? string.Empty : L("დენის მიმართულება დგინდება — ერთხელ აჩქარდი.", "Learning current direction — accelerate once.");
            ShowHeroPowerBar = true;
        }
        else if (_mode == EnergyMode.Hybrid && EngineRunning && _instantLitersPer100Km is { } litres)
        {
            HeroValueText = litres.ToString("0.0", invariant);
            HeroUnitText = L("ლ/100კმ ახლა", "L/100km now");
            HeroValueColor = OrangeColor;
            HeroHintText = L("ბატარეის დენი ამ მანქანაზე არ იკითხება.", "This car does not report battery current.");
            ShowHeroPowerBar = false;
        }
        else
        {
            var estimate = approxKWh is { } e ? EvEnergyAccumulator.KWhPer100Km(e, km) : null;
            HeroValueText = estimate is { } est ? $"≈ {est.ToString("0", invariant)}" : "—";
            HeroUnitText = "kWh/100km";
            HeroValueColor = AmberColor;
            HeroHintText = _socStart is { } s0 && _socNow is { } s1
                ? $"{s0.ToString("0", invariant)}% → {s1.ToString("0", invariant)}%" + (CapacityKWh() is { } c ? $" · {L("ტევადობა", "capacity")} {c.ToString("0.#", invariant)} kWh" : string.Empty)
                : L("ბატარეის მონაცემს ველოდები…", "Waiting for battery data…");
            ShowHeroPowerBar = false;
        }

        PowerHistoryCaption = L("სიმძლავრე · ბოლო წუთები", "Power · last minutes");

        // ---- Energy card: used / recovered / net (used and recovered only grow)
        EnergyCardCaption = L("ამ მოგზაურობის ენერგია", "Energy this trip");
        NetEnergyCaption = L("წმინდა ხარჯი", "Net use");
        if (exact)
        {
            var used = _ev.ConsumedKWh;
            var back = _ev.RegeneratedKWh;
            ShowEnergyBar = used + back > 0.001;
            ConsumedStar = new GridLength(Math.Max(used, 0.0001), GridUnitType.Star);
            RegenStar = new GridLength(Math.Max(back, 0), GridUnitType.Star);
            NetEnergyText = $"{_ev.NetKWh.ToString("0.00", invariant)} kWh";
            EnergyRegenText = $"{back.ToString("0.00", invariant)} kWh";
            EnergyNoteText = L("„დახარჯული“ და „დაბრუნებული“ მხოლოდ იზრდება. დაღმართზე იზრდება „დაბრუნებული“ და მცირდება „წმინდა ხარჯი“.",
                "\"Used\" and \"recovered\" only grow. Downhill, \"recovered\" grows and \"net use\" drops.");
        }
        else
        {
            ShowEnergyBar = false;
            NetEnergyText = approxKWh is { } est ? $"≈ {est.ToString("0.0", invariant)} kWh" : "—";
            EnergyRegenText = L("ამ მანქანაზე არ ჩანს", "not reported by this car");
            EnergyNoteText = L("ზუსტი kWh-ისთვის საჭიროა ბატარეის დენი. ახლა ხარჯი ბატარეის %-დან და ტევადობიდან ითვლება.",
                "Exact kWh needs the battery current. Use is estimated from the battery % and capacity.");
        }

        // ---- Electric / fuel split (plug-in and hybrid)
        ShowDriveSplit = _mode == EnergyMode.Hybrid;
        var totalMeters = _electricMeters + _engineMeters;
        var electricShare = totalMeters > 50 ? _electricMeters / totalMeters : 0;
        ElectricShareStar = new GridLength(Math.Max(electricShare, 0.0001), GridUnitType.Star);
        EngineShareStar = new GridLength(totalMeters > 50 ? 1 - electricShare : 0.0001, GridUnitType.Star);
        ElectricShareText = totalMeters > 50 ? $"⚡ {(electricShare * 100).ToString("0", invariant)}%" : "⚡";
        EngineShareText = totalMeters > 50 && electricShare < 0.95 ? $"⛽ {((1 - electricShare) * 100).ToString("0", invariant)}%" : string.Empty;
        var electricKWh = exact ? _ev.NetKWh : approxKWh;
        ElectricSplitText = $"{(electricKWh is { } ek ? ek.ToString("0.00", invariant) + " kWh · " : string.Empty)}{_units.FormatDistance(_electricMeters / 1000d)}";
        EngineSplitText = $"{_fuelLiters.ToString("0.0", invariant)} {L("ლ", "L")} · {_units.FormatDistance(_engineMeters / 1000d)}";
        SplitFuelAverageText = km >= 0.1 && _fuelLiters > 0 ? _units.FormatFuelEconomy(_fuelLiters / km * 100d) : "—";
        SplitCaption = L("რაზე იარე ამ მოგზაურობაში", "What you drove on this trip");

        // ---- Battery card
        SocStartNowText = _socStart is { } first && _socNow is { } now
            ? $"{first.ToString("0", invariant)}% → {now.ToString("0", invariant)}%"
            : _socNow is { } only ? $"{only.ToString("0", invariant)}%" : "—";
        VoltageCurrentText = exact
            ? $"{_ev.LastVolts.ToString("0", invariant)} V · {(_ev.LastAmps * _ev.Sign).ToString("0.0", invariant)} A"
            : "—";
        CanFlipSign = exact;
        FlipSignText = L("ხარჯი და დამუხტვა არეულია? შეცვლა", "Used and charged swapped? Flip");

        // ---- What this car reports
        BuildCapabilities(exact);
    }

    private void BuildCapabilities(bool exact)
    {
        var ok = ("✓", GreenColor);
        var no = ("✕", GreyColor);
        var approx = ("≈", AmberColor);
        var rows = new List<EnergyCapabilityRow>
        {
            Row(_socNow is not null ? ok : no, L("ბატარეის %", "Battery %"),
                _socNow is not null ? SourceShort() : L("არ აბრუნებს", "not reported")),
            Row(_speedSeen ? ok : no, L("სიჩქარე და მანძილი", "Speed and distance"),
                _speedSeen ? "OBD" : L("ჯერ არ მოსულა", "not yet")),
            Row(exact ? ok : no, L("ძაბვა და დენი", "Voltage and current"),
                exact ? SourceShort() : _evResolving ? L("ვეძებ…", "searching…") : L("არ აბრუნებს", "not reported")),
            Row(exact ? ok : ApproxKWh() is not null ? approx : no, L("დახარჯული kWh", "kWh used"),
                exact ? L("ზუსტი", "exact") : ApproxKWh() is not null ? L("%-დან", "from %") : L("ტევადობა და % სჭირდება", "needs capacity and %")),
            Row(exact ? ok : no, L("დამუხრუჭებით დაბრუნებული", "Recovered by braking"),
                exact ? L("ზუსტი", "exact") : L("დენი სჭირდება", "needs current")),
        };

        if (_mode == EnergyMode.Hybrid)
            rows.Add(Row(_rpmSeen ? ok : no, L("ძრავი ჩართულია თუ არა", "Engine on / off"),
                _rpmSeen ? L("ბრუნებიდან", "from rpm") : L("ბრუნები არ მოდის", "no rpm")));

        var changed = rows.Count != EnergyCapabilities.Count ||
                      rows.Where((row, index) => row != EnergyCapabilities[index]).Any();
        if (!changed)
            return;

        EnergyCapabilities.Clear();
        foreach (var row in rows)
            EnergyCapabilities.Add(row);

        static EnergyCapabilityRow Row((string Mark, Color Color) state, string title, string detail) =>
            new(state.Mark, state.Color, title, detail);
    }

    private string SourceShort() => _evSource is null
        ? L("მარკის კოდი", "brand identifier")
        : _evSource.IsStandard ? "OBD 01 9A" : L("მარკის კოდი", "brand identifier");

    /// <summary>Summary card shown after Stop (before the trip is saved and reset).</summary>
    private void BuildTripSummary()
    {
        var invariant = CultureInfo.InvariantCulture;
        var km = _distanceMeters / 1000d;
        if (km < 0.1)
        {
            IsTripSummaryVisible = false;
            return;
        }

        var exact = _ev.HasSample;
        double? energy = _mode == EnergyMode.Fuel ? null : exact ? _ev.NetKWh : ApproxKWh();
        var fuelPriceKnown = TryGetFuelPrice(out var litrePrice) && litrePrice > 0;
        var kwhPriceKnown = TryParsePositive(ElectricityPriceText, out var kwhPrice);
        double? fuelCost = _mode != EnergyMode.Electric && fuelPriceKnown ? _fuelLiters * litrePrice : null;
        double? energyCost = energy is { } kwh && kwhPriceKnown ? Math.Max(0, kwh) * kwhPrice : null;
        var total = (fuelCost ?? 0) + (energyCost ?? 0);

        TripSummaryTitle = $"{(_tripStartedAt ?? DateTime.Now):HH:mm} – {DateTime.Now:HH:mm} · {_vehicle.CurrentVehicle?.Manufacturer} {_vehicle.CurrentVehicle?.Model}".Trim();
        TripSummaryCostText = fuelCost is null && energyCost is null ? "—" : $"₾{total.ToString("0.00", invariant)}";
        TripSummaryCostDetail = string.Join(" · ", new[]
        {
            energyCost is { } ec ? $"{L("ელექტრო", "electric")} ₾{ec.ToString("0.00", invariant)}" : null,
            fuelCost is { } fc && _mode != EnergyMode.Electric ? $"{L("ბენზინი", "fuel")} ₾{fc.ToString("0.00", invariant)}" : null,
            fuelCost is null && energyCost is null ? L("ფასი შეიყვანე, რომ ღირებულება დაითვალოს", "Enter prices to see the cost") : null
        }.Where(part => part is not null));

        // Same distance in an ordinary petrol car (7.5 L/100 km).
        var petrolCost = fuelPriceKnown ? km * ReferenceLitersPer100Km / 100d * litrePrice : (double?)null;
        ShowTripSummaryComparison = _mode != EnergyMode.Fuel && petrolCost is > 0 && (fuelCost is not null || energyCost is not null);
        if (ShowTripSummaryComparison && petrolCost is { } petrol)
        {
            var maximum = Math.Max(petrol, total);
            SummaryYouStar = new GridLength(Math.Max(total / maximum, 0.02), GridUnitType.Star);
            SummaryYouRest = new GridLength(Math.Max(1 - total / maximum, 0.0001), GridUnitType.Star);
            SummaryPetrolStar = new GridLength(Math.Max(petrol / maximum, 0.02), GridUnitType.Star);
            SummaryPetrolRest = new GridLength(Math.Max(1 - petrol / maximum, 0.0001), GridUnitType.Star);
            SummaryYouText = $"₾{total.ToString("0.00", invariant)}";
            SummaryPetrolText = $"₾{petrol.ToString("0.00", invariant)}";
            SummarySavingText = petrol > total
                ? L($"დაზოგე ≈ ₾{(petrol - total).ToString("0.00", invariant)} (ჩვეულებრივ მანქანასთან, 7.5 ლ/100კმ)",
                    $"You saved ≈ ₾{(petrol - total).ToString("0.00", invariant)} (vs. a 7.5 L/100km car)")
                : L("ამ მოგზაურობაზე დანაზოგი არ გამოვიდა.", "No saving on this trip.");
        }

        TripSummaryRows.Clear();
        TripSummaryRows.Add(new(L("მანძილი", "Distance"), _units.FormatDistance(km)));
        TripSummaryRows.Add(new(L("დრო", "Time"), $"{(_movingSeconds + _idleSeconds) / 60d:0} {L("წთ", "min")}"));
        if (_mode != EnergyMode.Fuel)
        {
            if (exact)
            {
                TripSummaryRows.Add(new(L("⚡ დახარჯული / დაბრუნებული", "⚡ Used / recovered"),
                    $"{_ev.ConsumedKWh.ToString("0.00", invariant)} / {_ev.RegeneratedKWh.ToString("0.00", invariant)} kWh"));
            }

            if (energy is { } e)
            {
                var per100 = EvEnergyAccumulator.KWhPer100Km(e, km);
                TripSummaryRows.Add(new(L("⚡ წმინდა ხარჯი", "⚡ Net use"),
                    $"{(exact ? string.Empty : "≈ ")}{e.ToString("0.00", invariant)} kWh" + (per100 is { } p ? $" · {p.ToString("0.0", invariant)} /100{L("კმ", "km")}" : string.Empty)));
            }

            if (_socStart is { } s0 && _socNow is { } s1)
                TripSummaryRows.Add(new(L("ბატარეა", "Battery"), $"{s0.ToString("0", invariant)}% → {s1.ToString("0", invariant)}%"));
        }

        if (_mode == EnergyMode.Hybrid && _electricMeters + _engineMeters > 50)
        {
            var share = _electricMeters / (_electricMeters + _engineMeters) * 100d;
            TripSummaryRows.Add(new(L("ელექტროზე / ძრავზე", "Electric / engine"), $"{share.ToString("0", invariant)}% / {(100 - share).ToString("0", invariant)}%"));
        }

        if (_mode != EnergyMode.Electric && _fuelLiters > 0)
            TripSummaryRows.Add(new(L("⛽ ბენზინი", "⛽ Fuel"), $"{_fuelLiters.ToString("0.00", invariant)} {L("ლ", "L")} · {_units.FormatFuelEconomy(_fuelLiters / km * 100d)}"));

        IsTripSummaryVisible = true;

        // The trip becomes the "trip" part of the combined report.
        var section = new CarScanner.Services.Reports.ReportSectionItem
        {
            Kind = "trip",
            Icon = "⛽",
            Title = L("მოგზაურობა და ხარჯი", "Trip and consumption"),
            Status = CarScanner.Services.Reports.ReportStatus.Info,
            Summary = string.Join(" · ", new[] { _units.FormatDistance(km), TripSummaryCostText }.Where(part => part != "—"))
        };
        foreach (var row in TripSummaryRows)
            section.Lines.Add(new() { Label = row.Title, Value = row.Value });
        if (ShowTripSummaryComparison)
            section.Lines.Add(new() { Label = string.Empty, Value = SummarySavingText });
        CarScanner.Services.Reports.VehicleReportService.Instance?.AddOrReplace(section);
    }
}
