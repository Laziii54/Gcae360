using System.Globalization;
using CarScanner.Services.Energy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// Electric / hybrid part of the Fuel page: battery energy from
/// voltage × current (kWh, kWh/100km, regeneration).
/// </summary>
public partial class FuelViewModel
{
    private static readonly Color DischargeColor = Color.FromArgb("#2563EB");
    private static readonly Color RegenColor = Color.FromArgb("#1F8A4C");
    private static readonly Color WarnColor = Color.FromArgb("#B7791F");
    private static readonly Color NeutralColor = Color.FromArgb("#8A9099");

    private readonly EvEnergyAccumulator _ev = new();
    private EvBatterySource? _evSource;
    private string? _evSourceVin;
    private volatile bool _evResolving;
    private volatile bool _evResolveFailed;
    private CancellationTokenSource? _evCts;
    private double? _latestSpeedKph;
    private double _electricMovingSeconds;
    private double? _socStart;
    private double? _socNow;
    private bool _modeOverridden;
    private EnergyMode _mode = EnergyMode.Fuel;

    [ObservableProperty] private bool hasTripHistory;
    [ObservableProperty] private bool isFuelMode = true;
    [ObservableProperty] private bool isHybridMode;
    [ObservableProperty] private bool isElectricMode;
    [ObservableProperty] private bool showEnergy;
    [ObservableProperty] private bool showFuel = true;
    [ObservableProperty] private bool showPower;
    [ObservableProperty] private bool hasHeadUnits;
    [ObservableProperty] private string headLeftCaption = "მომენტალური";
    [ObservableProperty] private string headLeftValue = "—";
    [ObservableProperty] private string headLeftUnit = "";
    [ObservableProperty] private string headRightCaption = "საშუალო";
    [ObservableProperty] private string headRightValue = "—";
    [ObservableProperty] private string headRightUnit = "";
    [ObservableProperty] private double powerKw;
    [ObservableProperty] private string powerText = "—";
    [ObservableProperty] private Color powerColor = NeutralColor;
    [ObservableProperty] private string powerCaption = "";
    [ObservableProperty] private string energyUsedCaption = "დახარჯული ენერგია";
    [ObservableProperty] private string energyUsedText = "—";
    [ObservableProperty] private string energyRegenCaption = "რეკუპერაციით დაბრუნდა";
    [ObservableProperty] private string energyRegenText = "—";
    [ObservableProperty] private string batteryVoltageText = "—";
    [ObservableProperty] private string batteryCurrentText = "—";
    [ObservableProperty] private string socText = "—";
    [ObservableProperty] private string energyCostText = "—";
    [ObservableProperty] private string electricityPriceText = "";
    [ObservableProperty] private string energySourceTitle = "მონაცემის წყარო";
    [ObservableProperty] private string energySourceText = "";
    [ObservableProperty] private Color energySourceColor = NeutralColor;
    [ObservableProperty] private bool hasEnergyLog;
    [ObservableProperty] private bool showSocFallback;
    [ObservableProperty] private string batteryCapacityText = "";
    [ObservableProperty] private string socFallbackText = "—";
    [ObservableProperty] private string approxConsumptionText = "—";

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    private string? CurrentVin()
    {
        var vin = _vehicle.CurrentVehicle?.VIN;
        return IsUnknownVin(vin) ? null : vin;
    }

    private void InitEnergy()
    {
        ElectricityPriceText = Preferences.Default.Get("ev_price_per_kwh", string.Empty);
        BatteryCapacityText = Preferences.Default.Get("ev_capacity_kwh", string.Empty);
        DetectMode();
    }

    /// <summary>Called when the page appears (the connected car may have changed).</summary>
    public void OnPageAppearing()
    {
        if (_tripStartedAt is null)
            DetectMode();
        RefreshPresentation();
        _ = LoadFuelHistoryAsync();
    }

    // ------------------------------------------------------------------
    // Mode
    // ------------------------------------------------------------------

    private void DetectMode()
    {
        var vin = CurrentVin();
        var saved = vin is null ? string.Empty : Preferences.Default.Get($"energy_mode_{vin}", string.Empty);
        if (Enum.TryParse<EnergyMode>(saved, out var savedMode))
        {
            _modeOverridden = true;
            ApplyMode(savedMode);
            return;
        }

        _modeOverridden = false;
        var detected = EvSignalDecoder.ModeFromText(_vehicle.CurrentVehicle?.FuelType)
                       ?? EvSignalDecoder.ModeFromFuelTypeCode(_latestFuelTypeCode)
                       ?? EnergyMode.Fuel;
        ApplyMode(detected);
    }

    /// <summary>
    /// PID 01 51 arrived. Many hybrids report plain "gasoline" there, so it
    /// may only switch the page to Hybrid/Electric, never back to Fuel.
    /// </summary>
    private void OnFuelTypeCodeReceived(int code)
    {
        if (_modeOverridden || EvSignalDecoder.ModeFromText(_vehicle.CurrentVehicle?.FuelType) is not null)
            return;

        if (EvSignalDecoder.ModeFromFuelTypeCode(code) is { } mode && mode != EnergyMode.Fuel && mode != _mode)
        {
            ApplyMode(mode);
            if (_subscribed)
                StartEnergyLoop();
        }
    }

    [RelayCommand]
    private void SetMode(string? value)
    {
        if (!Enum.TryParse<EnergyMode>(value, true, out var mode))
            return;

        _modeOverridden = true;
        if (CurrentVin() is { } vin)
            Preferences.Default.Set($"energy_mode_{vin}", mode.ToString());

        if (mode == _mode)
            return;

        ApplyMode(mode);
        if (_subscribed)
        {
            if (mode == EnergyMode.Fuel)
                StopEnergyLoop();
            else
                StartEnergyLoop();
        }
    }

    private void ApplyMode(EnergyMode mode)
    {
        _mode = mode;
        IsFuelMode = mode == EnergyMode.Fuel;
        IsHybridMode = mode == EnergyMode.Hybrid;
        IsElectricMode = mode == EnergyMode.Electric;
        ShowEnergy = mode != EnergyMode.Fuel;
        ShowFuel = mode != EnergyMode.Electric;
        RefreshPresentation();
    }

    private string RunningStatus() => _mode switch
    {
        EnergyMode.Electric => L("მიმდინარეობს — ბატარეის მონაცემს ვკითხულობ…", "Running — reading battery data…"),
        EnergyMode.Hybrid => L("მიმდინარეობს — საწვავი და ბატარეა იზომება…", "Running — measuring fuel and battery…"),
        _ => L("მიმდინარეობს — ECU-ის Fuel Rate/MAF პასუხს ველოდები…", "Running — waiting for ECU Fuel Rate/MAF data…")
    };

    // ------------------------------------------------------------------
    // Battery polling
    // ------------------------------------------------------------------

    private void StartEnergyLoop()
    {
        StopEnergyLoop();
        if (_mode == EnergyMode.Fuel)
            return;

        var cts = new CancellationTokenSource();
        _evCts = cts;
        _ = RunEnergyLoopAsync(cts.Token);
    }

    private void StopEnergyLoop()
    {
        var cts = _evCts;
        _evCts = null;
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ResetEnergy()
    {
        _ev.Reset();
        _electricMovingSeconds = 0;
        _socStart = null;
        _socNow = null;
    }

    private async Task RunEnergyLoopAsync(CancellationToken token)
    {
        try
        {
            var vehicle = _vehicle.CurrentVehicle;
            var vin = CurrentVin();

            if (_evSource is null || _evSourceVin != vin)
            {
                _evSource = null;
                _evResolveFailed = false;
                _evResolving = true;
                MainThread.BeginInvokeOnMainThread(RefreshPresentation);

                var source = await _evReader.ResolveAsync(vehicle, token).ConfigureAwait(false);
                _evSource = source;
                _evSourceVin = vin;
                _evResolving = false;
                _evResolveFailed = source is null;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    HasEnergyLog = true;
                    RefreshPresentation();
                });
            }

            var active = _evSource;
            if (active is null)
            {
                await RunSocFallbackAsync(vehicle, token).ConfigureAwait(false);
                return;
            }

            DateTime? last = null;
            var tick = 0;
            while (!token.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                var reading = await _evReader.ReadAsync(active, tick++ % 10 == 0, token).ConfigureAwait(false);
                if (reading is { } value)
                {
                    var now = DateTime.UtcNow;
                    var seconds = last is null ? 0 : (now - last.Value).TotalSeconds;
                    last = now;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;
                        _ev.Add(value.Volts, value.Amps, seconds, _latestSpeedKph);
                        if (value.SocPercent is { } soc)
                        {
                            _socStart ??= soc;
                            _socNow = soc;
                        }

                        RefreshPresentation();
                    });
                }

                var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - started);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _evResolving = false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EnergySourceTitle = L("ბატარეის მონაცემი შეწყდა", "Battery data stopped");
                EnergySourceText = ex.Message;
                EnergySourceColor = WarnColor;
            });
        }
    }

    private async Task RunSocFallbackAsync(CarScanner.Models.VehicleInfo? vehicle, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var soc = await _evReader.ReadSocOnlyAsync(vehicle, token).ConfigureAwait(false);
            if (soc is { } value)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _socStart ??= value;
                    _socNow = value;
                    RefreshPresentation();
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task CopyEnergyLog()
    {
        var log = _evReader.Log;
        if (string.IsNullOrWhiteSpace(log))
            return;

        await Clipboard.Default.SetTextAsync(log);
        EnergySourceTitle = L("ლოგი დაკოპირდა", "Log copied");
    }

    partial void OnElectricityPriceTextChanged(string value)
    {
        Preferences.Default.Set("ev_price_per_kwh", value ?? string.Empty);
        RefreshPresentation();
    }

    partial void OnBatteryCapacityTextChanged(string value)
    {
        Preferences.Default.Set("ev_capacity_kwh", value ?? string.Empty);
        RefreshPresentation();
    }

    // ------------------------------------------------------------------
    // Presentation
    // ------------------------------------------------------------------

    /// <summary>kWh from the state-of-charge drop × battery capacity (fallback).</summary>
    private double? ApproxKWh()
    {
        if (_socStart is not { } start || _socNow is not { } now ||
            !TryParseNonNegative(BatteryCapacityText, out var capacity) || capacity <= 0)
            return null;

        return (start - now) / 100d * capacity;
    }

    private void RefreshEnergyPresentation()
    {
        var km = _distanceMeters / 1000d;
        var invariant = CultureInfo.InvariantCulture;

        // Headline
        switch (_mode)
        {
            case EnergyMode.Electric:
            {
                double? instant = _ev.HasSample
                    ? EvEnergyAccumulator.InstantKWhPer100Km(_ev.PowerKw, _latestSpeedKph ?? 0)
                    : null;
                var energy = _ev.HasSample ? _ev.NetKWh : ApproxKWh();
                double? average = energy is { } e ? EvEnergyAccumulator.KWhPer100Km(e, km) : null;
                HeadLeftCaption = L("მომენტალური", "Instant");
                HeadLeftValue = instant is { } i ? i.ToString("0.0", invariant) : "—";
                HeadLeftUnit = "kWh/100km";
                HeadRightCaption = L("საშუალო", "Average");
                HeadRightValue = average is { } a ? a.ToString("0.0", invariant) : "—";
                HeadRightUnit = "kWh/100km";
                HasHeadUnits = true;
                break;
            }
            case EnergyMode.Hybrid:
                HeadLeftCaption = L("საწვავი", "Fuel");
                HeadLeftValue = Average;
                HeadLeftUnit = "";
                HeadRightCaption = L("ელექტროთი ნავალი", "Driven on electric");
                HeadRightValue = _movingSeconds > 30
                    ? (_electricMovingSeconds / _movingSeconds * 100d).ToString("0", invariant)
                    : "—";
                HeadRightUnit = "%";
                HasHeadUnits = true;
                break;
            default:
                HeadLeftCaption = L("მომენტალური", "Instant");
                HeadLeftValue = Instant;
                HeadRightCaption = L("საშუალო", "Average");
                HeadRightValue = Average;
                HeadLeftUnit = HeadRightUnit = "";
                HasHeadUnits = false;
                break;
        }

        // Power bar
        ShowPower = ShowEnergy && _ev.HasSample;
        var power = _ev.PowerKw;
        PowerKw = power;
        PowerText = _ev.HasSample ? $"{(power > 0 ? "+" : power < 0 ? "−" : "")}{Math.Abs(power).ToString("0.0", invariant)} kW" : "—";
        PowerColor = power > 0.05 ? DischargeColor : power < -0.05 ? RegenColor : NeutralColor;
        PowerCaption = power > 0.05
            ? L("ბატარეა იხარჯება", "battery discharging")
            : power < -0.05
                ? L("ბატარეა იმუხტება", "battery charging")
                : L("ბატარეა უმოქმედოა", "battery idle");

        // Rows
        EnergyUsedCaption = _mode == EnergyMode.Hybrid
            ? L("ბატარეიდან აღებული", "Taken from battery")
            : L("დახარჯული ენერგია", "Energy used");
        EnergyRegenCaption = _mode == EnergyMode.Hybrid
            ? L("ბატარეაში დაბრუნებული", "Returned to battery")
            : L("რეკუპერაციით დაბრუნდა", "Recovered by regeneration");
        EnergyUsedText = _ev.HasSample
            ? $"{_ev.ConsumedKWh.ToString("0.00", invariant)} kWh"
            : ApproxKWh() is { } approxUsed ? $"≈ {approxUsed.ToString("0.0", invariant)} kWh" : "—";
        EnergyRegenText = _ev.HasSample ? $"{_ev.RegeneratedKWh.ToString("0.00", invariant)} kWh" : "—";
        BatteryVoltageText = _ev.HasSample ? $"{_ev.LastVolts.ToString("0", invariant)} V" : "—";
        BatteryCurrentText = _ev.HasSample ? $"{(_ev.LastAmps * _ev.Sign).ToString("0.0", invariant)} A" : "—";
        SocText = _socNow is { } socNow ? $"{socNow.ToString("0", invariant)}%" : "—";

        var energyForCost = _ev.HasSample ? _ev.NetKWh : ApproxKWh();
        EnergyCostText = TryParsePositive(ElectricityPriceText, out var price) && energyForCost is { } kwh
            ? $"₾{Math.Max(0, kwh * price).ToString("0.00", invariant)}"
            : L("ფასის შეყვანის შემდეგ", "after entering a price");

        // Source card
        if (_evResolving)
        {
            EnergySourceTitle = L("მონაცემის წყარო", "Data source");
            EnergySourceText = L("ბატარეის მონაცემს ვეძებ: ჯერ PID 01 9A, მერე მარკის კოდები…", "Looking for battery data: PID 01 9A first, then brand identifiers…");
            EnergySourceColor = NeutralColor;
        }
        else if (_evSource is { } source)
        {
            EnergySourceTitle = L("მონაცემის წყარო", "Data source");
            EnergySourceText = $"● {source.Label}\n" + (source.IsStandard
                ? L("სტანდარტული OBD · ძაბვა × დენი, წამში ერთხელ", "Standard OBD · voltage × current, once per second")
                : L("მარკის კოდი კატალოგიდან · ძაბვა × დენი, წამში ერთხელ", "Brand identifier from the catalog · voltage × current, once per second"))
                + (_ev.IsSignConfirmed
                    ? string.Empty
                    : "\n" + L("დენის მიმართულება ჯერ დგინდება: ერთხელ აჩქარდი.", "Current direction is still being learned: accelerate once."));
            EnergySourceColor = RegenColor;
        }
        else if (_evResolveFailed)
        {
            EnergySourceTitle = L("ბატარეის დენი ვერ წავიკითხეთ", "Battery current could not be read");
            EnergySourceText = L(
                "ამ მანქანამ არ უპასუხა არც სტანდარტულ PID 01 9A-ს და არც მარკის კოდებს." +
                (_mode == EnergyMode.Electric ? " ქვემოთ შეიყვანე ბატარეის ტევადობა, ხარჯი დამუხტვის %-ით მიახლოებით დაითვლება." : string.Empty),
                "This car did not answer the standard PID 01 9A or the brand identifiers." +
                (_mode == EnergyMode.Electric ? " Enter the battery capacity below to estimate use from the state of charge." : string.Empty));
            EnergySourceColor = WarnColor;
        }
        else
        {
            EnergySourceTitle = L("მონაცემის წყარო", "Data source");
            EnergySourceText = L("დააჭირე „დაწყებას“: აპი ბატარეის მონაცემს თავად მოძებნის.", "Press Start: the app finds the battery data by itself.");
            EnergySourceColor = NeutralColor;
        }

        // State-of-charge fallback
        ShowSocFallback = _mode == EnergyMode.Electric && _evResolveFailed;
        SocFallbackText = _socStart is { } first && _socNow is { } latest
            ? $"{first.ToString("0", invariant)}% → {latest.ToString("0", invariant)}%"
            : L("დამუხტვის % ჯერ არ მოსულა", "No state of charge yet");
        ApproxConsumptionText = ApproxKWh() is { } approx
            ? $"{approx.ToString("0.0", invariant)} kWh" +
              (EvEnergyAccumulator.KWhPer100Km(approx, km) is { } per100 ? $" · {per100.ToString("0.0", invariant)} kWh/100km" : string.Empty)
            : "—";
    }
}
