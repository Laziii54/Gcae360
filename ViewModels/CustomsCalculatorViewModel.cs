using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Services.Customs;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One tappable option (fuel, steering, currency, engine size).</summary>
public sealed partial class CustomsChip : ObservableObject
{
    public CustomsChip(string key, string text, string subText, Action<CustomsChip> onTap)
    {
        Key = key;
        Text = text;
        SubText = subText;
        TapCommand = new RelayCommand(() => onTap(this));
    }

    public string Key { get; }
    public string Text { get; }
    public string SubText { get; }
    public bool HasSubText => !string.IsNullOrWhiteSpace(SubText);
    public IRelayCommand TapCommand { get; }

    [ObservableProperty] private bool isSelected;
}

/// <summary>One line of the cost breakdown with its share of the total.</summary>
public sealed record CustomsCostRow(string Title, string Detail, string Amount, double Share, Color BarColor, bool HasDetail);

/// <summary>A hint under the result.</summary>
public sealed record CustomsHint(string Text, bool IsWarning)
{
    public string Icon => IsWarning ? "!" : "i";
}

/// <summary>
/// Customs calculator: the full cost of bringing a car to Georgia
/// (price + auction + shipping + excise + clearance). Results update live.
/// Can be opened from a VIN report with year / engine / fuel filled in.
/// </summary>
public partial class CustomsCalculatorViewModel : BaseViewModel, IQueryAttributable
{
    private static readonly Color CarColor = Color.FromArgb("#64748B");
    private static readonly Color ExciseColor = Color.FromArgb("#1D5BD6");
    private static readonly Color FeeColor = Color.FromArgb("#0EA5A4");
    private static readonly Color VatColor = Color.FromArgb("#9333EA");
    private static readonly Color LocalColor = Color.FromArgb("#D97706");

    private readonly CustomsRulesService _rulesService;
    private readonly LocalizationService _localization;
    private CustomsRules _rules = CustomsRules.BuiltIn;
    private bool _loading;
    private bool _rateEditedByUser;

    public CustomsCalculatorViewModel(CustomsRulesService rulesService, LocalizationService localization)
    {
        _rulesService = rulesService;
        _localization = localization;

        FuelChips = new ObservableCollection<CustomsChip>
        {
            new("petrol", L("ბენზინი", "Petrol"), "", SelectFuel),
            new("diesel", L("დიზელი", "Diesel"), "", SelectFuel),
            new("hybrid", L("ჰიბრიდი", "Hybrid"), "HEV", SelectFuel),
            new("phev", L("Plug-in ჰიბრიდი", "Plug-in hybrid"), "PHEV", SelectFuel),
            new("electric", L("ელექტრო", "Electric"), "EV", SelectFuel)
        };
        SteeringChips = new ObservableCollection<CustomsChip>
        {
            new("left", L("მარცხენა საჭე", "Left-hand drive"), "", SelectSteering),
            new("right", L("მარჯვენა საჭე", "Right-hand drive"), "", SelectSteering)
        };
        CurrencyChips = new ObservableCollection<CustomsChip>
        {
            new("usd", "USD $", "", SelectCurrency),
            new("eur", "EUR €", "", SelectCurrency)
        };
        EngineChips = new ObservableCollection<CustomsChip>(
            new[] { "1.0", "1.4", "1.6", "1.8", "2.0", "2.5", "3.0", "3.5" }
                .Select(size => new CustomsChip(size, size, "", SelectEngine)));

        FuelChips[0].IsSelected = true;
        SteeringChips[0].IsSelected = true;
        CurrencyChips[0].IsSelected = true;
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    // ------------------------------------------------------------------
    // Inputs
    // ------------------------------------------------------------------

    public ObservableCollection<CustomsChip> FuelChips { get; }
    public ObservableCollection<CustomsChip> SteeringChips { get; }
    public ObservableCollection<CustomsChip> CurrencyChips { get; }
    public ObservableCollection<CustomsChip> EngineChips { get; }

    [ObservableProperty] private string yearText = "";
    [ObservableProperty] private string engineText = "";
    [ObservableProperty] private string priceText = "";
    [ObservableProperty] private string auctionText = "";
    [ObservableProperty] private string shippingText = "";
    [ObservableProperty] private string rateText = "";
    [ObservableProperty] private string localCostsText = "";

    [ObservableProperty] private CustomsFuel fuel = CustomsFuel.Petrol;
    [ObservableProperty] private bool rightHandDrive;
    [ObservableProperty] private CustomsCurrency currency = CustomsCurrency.Usd;

    partial void OnYearTextChanged(string value) => Recalculate();
    partial void OnEngineTextChanged(string value)
    {
        foreach (var chip in EngineChips)
            chip.IsSelected = chip.Key == (value ?? "").Trim().Replace(',', '.');
        Recalculate();
    }
    partial void OnPriceTextChanged(string value) => Recalculate();
    partial void OnAuctionTextChanged(string value) => Recalculate();
    partial void OnShippingTextChanged(string value) => Recalculate();
    partial void OnLocalCostsTextChanged(string value) => Recalculate();
    partial void OnRateTextChanged(string value)
    {
        if (!_loading)
            _rateEditedByUser = true;
        Recalculate();
    }

    partial void OnFuelChanged(CustomsFuel value)
    {
        foreach (var chip in FuelChips)
            chip.IsSelected = chip.Key == FuelKey(value);
        OnPropertyChanged(nameof(IsEngineVisible));
        Recalculate();
    }

    partial void OnRightHandDriveChanged(bool value)
    {
        foreach (var chip in SteeringChips)
            chip.IsSelected = chip.Key == (value ? "right" : "left");
        Recalculate();
    }

    partial void OnCurrencyChanged(CustomsCurrency value)
    {
        foreach (var chip in CurrencyChips)
            chip.IsSelected = chip.Key == (value == CustomsCurrency.Eur ? "eur" : "usd");
        _rateEditedByUser = false;
        ApplyOfficialRate();
        OnPropertyChanged(nameof(CurrencySymbol));
        OnPropertyChanged(nameof(PriceLabel));
        OnPropertyChanged(nameof(AuctionLabel));
        OnPropertyChanged(nameof(ShippingLabel));
        OnPropertyChanged(nameof(RateLabel));
        Recalculate();
    }

    private void SelectFuel(CustomsChip chip) => Fuel = chip.Key switch
    {
        "diesel" => CustomsFuel.Diesel,
        "hybrid" => CustomsFuel.Hybrid,
        "phev" => CustomsFuel.PlugInHybrid,
        "electric" => CustomsFuel.Electric,
        _ => CustomsFuel.Petrol
    };

    private void SelectSteering(CustomsChip chip) => RightHandDrive = chip.Key == "right";

    private void SelectCurrency(CustomsChip chip) =>
        Currency = chip.Key == "eur" ? CustomsCurrency.Eur : CustomsCurrency.Usd;

    private void SelectEngine(CustomsChip chip) => EngineText = chip.Key;

    private static string FuelKey(CustomsFuel fuel) => fuel switch
    {
        CustomsFuel.Diesel => "diesel",
        CustomsFuel.Hybrid => "hybrid",
        CustomsFuel.PlugInHybrid => "phev",
        CustomsFuel.Electric => "electric",
        _ => "petrol"
    };

    [RelayCommand]
    private void YearMinus() => StepYear(-1);

    [RelayCommand]
    private void YearPlus() => StepYear(1);

    private void StepYear(int delta)
    {
        var year = (int)Number(YearText);
        if (year < 1950)
            year = DateTime.Now.Year - 5;
        else
            year += delta;
        YearText = Math.Clamp(year, 1950, DateTime.Now.Year + 1).ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------
    // Texts
    // ------------------------------------------------------------------

    public string PageTitle => L("განბაჟების კალკულატორი", "Customs calculator");
    public string PageSubtitle => L("მანქანის სრული ღირებულება საქართველოში", "The full cost of a car in Georgia");
    public string CarSection => L("მანქანა", "Car");
    public string CostsSection => L("ყიდვა და ტრანსპორტირება", "Purchase and shipping");
    public string LocalSection => L("საქართველოში", "In Georgia");
    public string YearLabel => L("გამოშვების წელი", "Year of manufacture");
    public string EngineLabel => L("ძრავის მოცულობა", "Engine size");
    public string EngineHint => L("ლიტრი (2.0) ან სმ³ ტექპასპორტიდან (1998)", "Litres (2.0) or cm³ from the papers (1998)");
    public string FuelLabel => L("ძრავის ტიპი", "Powertrain");
    public string SteeringLabel => L("საჭე", "Steering");
    public string CurrencyLabel => L("ვალუტა", "Currency");
    public string CurrencySymbol => Currency == CustomsCurrency.Eur ? "€" : "$";
    public string PriceLabel => L($"მანქანის ფასი ({CurrencySymbol})", $"Car price ({CurrencySymbol})");
    public string AuctionLabel => L($"აუქციონის მოსაკრებელი ({CurrencySymbol})", $"Auction fees ({CurrencySymbol})");
    public string ShippingLabel => L($"ტრანსპორტირება ფოთამდე ({CurrencySymbol})", $"Shipping to Poti ({CurrencySymbol})");
    public string RateLabel => L($"კურსი: 1 {CurrencySymbol} = ₾", $"Rate: 1 {CurrencySymbol} = ₾");
    public string LocalCostsLabel => L("სხვა ხარჯი (₾)", "Other costs (₾)");
    public string LocalCostsHint => L("რეგისტრაცია, ნომრები, ტერმინალი, შიდა ტრანსპორტი", "Registration, plates, terminal, local transport");
    public string TotalCaption => L("სულ საქართველოში", "Total in Georgia");
    public string BreakdownCaption => L("რისგან შედგება", "Breakdown");
    public string TaxesCaption => L("გადასახადები", "Taxes and fees");
    public string ExciseCaption => L("აქციზი", "Excise");
    public string OfficialText => L("ოფიციალური კალკულატორი — rs.ge", "Official calculator — rs.ge");
    public string NoteText => L(
        "აქციზი ითვლება საგადასახადო კოდექსით, ზუსტად. ასაკი = განბაჟების წელი − გამოშვების წელი. საბაჟო გაფორმების საფასური მიახლოებითია; საბოლოო თანხა გადაამოწმე rs.ge-ზე.",
        "Excise follows the Tax Code exactly. Age = clearance year − year of manufacture. The customs service fee is approximate; check the final amount on rs.ge.");
    public bool IsEngineVisible => Fuel != CustomsFuel.Electric;

    // ------------------------------------------------------------------
    // Result
    // ------------------------------------------------------------------

    public ObservableCollection<CustomsCostRow> Rows { get; } = new();
    public ObservableCollection<CustomsHint> Hints { get; } = new();

    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private string placeholderText = "";
    [ObservableProperty] private string totalText = "—";
    [ObservableProperty] private string totalForeignText = "";
    [ObservableProperty] private string taxesText = "";
    [ObservableProperty] private string exciseText = "";
    [ObservableProperty] private string formulaText = "";
    [ObservableProperty] private string ageBadgeText = "";
    [ObservableProperty] private bool ageIsYoung = true;
    [ObservableProperty] private string carSummaryText = "";
    [ObservableProperty] private string rulesText = "";
    [ObservableProperty] private bool hasPriceHint;
    [ObservableProperty] private string priceHintText = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        string? Get(string key) => query.TryGetValue(key, out var v) ? Uri.UnescapeDataString(v?.ToString() ?? "") : null;
        if (Get("year") is { Length: 4 } year) YearText = year;
        if (Get("cc") is { Length: > 0 } cc)
            EngineText = decimal.TryParse(cc, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 20
                ? (v / 1000m).ToString("0.0#", CultureInfo.InvariantCulture)
                : cc;
        if (Get("litres") is { Length: > 0 } litres) EngineText = litres;
        Fuel = Get("fuel") switch
        {
            "hybrid" => CustomsFuel.Hybrid,
            "phev" => CustomsFuel.PlugInHybrid,
            "electric" => CustomsFuel.Electric,
            "diesel" => CustomsFuel.Diesel,
            "petrol" => CustomsFuel.Petrol,
            _ => Fuel
        };
    }

    public async Task LoadAsync()
    {
        _rules = await _rulesService.GetAsync(CancellationToken.None);
        ApplyOfficialRate();

        var rateDate = _rules.RatesDate is null ? "" : L($" · ეროვნული ბანკის კურსი {_rules.RatesDate}", $" · National Bank rate {_rules.RatesDate}");
        RulesText = _rulesService.IsOffline
            ? L($"ოფლაინ რეჟიმი: წესები {_rules.EffectiveFrom}-დან, კურსი ხელით ჩაწერე.", $"Offline: rules from {_rules.EffectiveFrom}, enter the rate yourself.")
            : L($"წესები ძალაშია {_rules.EffectiveFrom}-დან{rateDate}", $"Rules in force from {_rules.EffectiveFrom}{rateDate}");
        Recalculate();
    }

    private void ApplyOfficialRate()
    {
        if (_rateEditedByUser)
            return;
        var official = Currency == CustomsCurrency.Eur ? _rules.EurRate : _rules.UsdRate;
        _loading = true;
        RateText = official is { } rate ? rate.ToString("0.####", CultureInfo.InvariantCulture) : "";
        _loading = false;
    }

    private static decimal Number(string? text) =>
        decimal.TryParse((text ?? "").Replace(',', '.').Replace(" ", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;

    /// <summary>"2.0" / "2" → 2000 cm³; "1998" stays 1998.</summary>
    private static int EngineCc(string? text)
    {
        var value = Number(text);
        return value is > 0 and < 20 ? (int)Math.Round(value * 1000) : (int)value;
    }

    private void Recalculate()
    {
        var year = (int)Number(YearText);
        var cc = EngineCc(EngineText);
        var taxYear = DateTime.Now.Year;

        var yearOk = year >= 1950 && year <= taxYear + 1;
        var engineOk = Fuel == CustomsFuel.Electric || cc is >= 50 and <= 10000;
        if (!yearOk || !engineOk)
        {
            HasResult = false;
            PlaceholderText = !yearOk && !engineOk
                ? L("ჩაწერე გამოშვების წელი და ძრავის მოცულობა — შედეგი მაშინვე გამოჩნდება.", "Enter the year and the engine size — the result appears instantly.")
                : !yearOk
                    ? L("ჩაწერე გამოშვების წელი (მაგ. 2020).", "Enter the year of manufacture (e.g. 2020).")
                    : L("ჩაწერე ძრავის მოცულობა (მაგ. 2.0 ან 1998).", "Enter the engine size (e.g. 2.0 or 1998).");
            Rows.Clear();
            Hints.Clear();
            return;
        }

        var rate = Number(RateText);
        var result = CustomsCalculator.Calculate(_rules, new CustomsInput(
            year, cc, Fuel, RightHandDrive,
            Number(PriceText), Number(AuctionText), Number(ShippingText), rate,
            Number(LocalCostsText), taxYear));

        Func<decimal, string> money = CustomsCalculator.Money;
        TotalText = $"{money(result.TotalGel)} ₾";
        TotalForeignText = result.TotalForeign is { } f && result.CarValueGel > 0 ? $"≈ {CurrencySymbol}{money(f)}" : "";
        TaxesText = L($"გადასახადები: {money(result.TaxesGel)} ₾", $"Taxes and fees: {money(result.TaxesGel)} ₾");
        ExciseText = $"{money(result.ExciseGel)} ₾";
        FormulaText = L(result.FormulaKa, result.FormulaEn);
        AgeIsYoung = result.Young;
        AgeBadgeText = result.Young
            ? L($"{result.Age} წ. · ≤{_rules.YoungMaxAge} წ. ტარიფი", $"{result.Age} yrs · ≤{_rules.YoungMaxAge} yr rate")
            : L($"{result.Age} წ. · >{_rules.YoungMaxAge} წ. ტარიფი", $"{result.Age} yrs · >{_rules.YoungMaxAge} yr rate");

        var fuelName = FuelChips.FirstOrDefault(c => c.Key == FuelKey(Fuel))?.Text ?? "";
        var litres = (cc / 1000m).ToString("0.0", CultureInfo.InvariantCulture);
        var engineName = Fuel == CustomsFuel.Electric ? "" : L($" · {litres} ლ", $" · {litres} L");
        CarSummaryText = $"{year} · {fuelName}{engineName} · {(RightHandDrive ? L("მარჯვენა საჭე", "RHD") : L("მარცხენა საჭე", "LHD"))}";

        HasPriceHint = result.CarValueGel <= 0;
        PriceHintText = rate <= 0
            ? L("კურსი ჩაწერე, რომ მანქანის ფასიც ჩაითვალოს.", "Enter the rate so the car price is included.")
            : L("ჩაწერე ფასი და ტრანსპორტირება — ჯამში ჩაითვლება.", "Enter the price and shipping to include them in the total.");

        BuildRows(result);

        Hints.Clear();
        foreach (var insight in result.Insights)
            Hints.Add(new CustomsHint(L(insight.Ka, insight.En), insight.IsWarning));

        HasResult = true;
    }

    private void BuildRows(CustomsResult result)
    {
        Rows.Clear();
        var total = Math.Max(1m, result.TotalGel);
        Func<decimal, string> money = CustomsCalculator.Money;

        void Add(string title, string detail, decimal amount, Color color)
        {
            Rows.Add(new CustomsCostRow(title, detail, $"{money(amount)} ₾", (double)(amount / total), color, !string.IsNullOrWhiteSpace(detail)));
        }

        if (result.CarValueGel > 0)
        {
            var foreign = Number(PriceText) + Number(AuctionText) + Number(ShippingText);
            Add(L("მანქანა + ტრანსპორტი", "Car + shipping"), $"{CurrencySymbol}{money(foreign)} × {Number(RateText):0.####}", result.CarValueGel, CarColor);
        }

        Add(ExciseCaption, L(result.FormulaKa, result.FormulaEn), result.ExciseGel, ExciseColor);
        Add(L("საბაჟო გაფორმება", "Customs clearance fee"), L("მიახლოებით", "approximate"), result.CustomsFeeGel, FeeColor);
        if (result.VatGel > 0)
            Add(L("დღგ", "VAT"), $"{_rules.VatRate * 100:0}%", result.VatGel, VatColor);
        if (result.LocalCostsGel > 0)
            Add(LocalCostsLabel.Replace(" (₾)", ""), "", result.LocalCostsGel, LocalColor);
    }

    [RelayCommand]
    private async Task OpenOfficial()
    {
        try { await Launcher.Default.OpenAsync(_rules.OfficialCalculatorUrl); }
        catch { }
    }
}
