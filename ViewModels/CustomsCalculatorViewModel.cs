using System.Globalization;
using CarScanner.Services.Customs;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>
/// Customs calculator: how much a car costs in Georgia (price + shipping +
/// excise). Can be opened from a VIN report with year / engine / fuel filled in.
/// </summary>
public partial class CustomsCalculatorViewModel(CustomsRulesService rulesService, LocalizationService localization)
    : BaseViewModel, IQueryAttributable
{
    private CustomsRules _rules = CustomsRules.BuiltIn;

    [ObservableProperty] private string yearText = "";
    [ObservableProperty] private string engineText = "";
    [ObservableProperty] private int fuelIndex;
    [ObservableProperty] private bool rightHandDrive;
    [ObservableProperty] private string priceText = "";
    [ObservableProperty] private string shippingText = "";
    [ObservableProperty] private string otherText = "";
    [ObservableProperty] private string usdRateText = "";

    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private string exciseText = "";
    [ObservableProperty] private string formulaText = "";
    [ObservableProperty] private string totalText = "";
    [ObservableProperty] private string totalUsdText = "";
    [ObservableProperty] private string carText = "";
    [ObservableProperty] private string ageText = "";
    [ObservableProperty] private string rulesText = "";
    [ObservableProperty] private string errorText = "";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    private string L(string ka, string en) => localization.IsEnglish ? en : ka;

    public IReadOnlyList<string> FuelOptions => new[]
    {
        L("ბენზინი / დიზელი", "Petrol / diesel"),
        L("ჰიბრიდი", "Hybrid"),
        L("ელექტრო", "Electric")
    };

    public string PageTitle => L("განბაჟების კალკულატორი", "Customs calculator");
    public string YearLabel => L("გამოშვების წელი", "Year of manufacture");
    public string EngineLabel => L("ძრავი, ლიტრი (მაგ. 2.0)", "Engine, litres (e.g. 2.0)");
    public string FuelLabel => L("საწვავი", "Fuel");
    public string RightHandLabel => L("მარჯვენა საჭე", "Right-hand drive");
    public string PriceLabel => L("მანქანის ფასი ($)", "Car price ($)");
    public string ShippingLabel => L("ტრანსპორტირება ($)", "Shipping ($)");
    public string OtherLabel => L("აუქციონის მოსაკრებელი და სხვა ($)", "Auction fees and other ($)");
    public string RateLabel => L("დოლარის კურსი (₾)", "USD rate (₾)");
    public string CalculateText => L("გამოთვლა", "Calculate");
    public string OfficialText => L("ოფიციალური კალკულატორი (rs.ge)", "Official calculator (rs.ge)");
    public string ExciseCaption => L("აქციზი", "Excise");
    public string TotalCaption => L("სულ საქართველოში (მიახლოებით)", "Total in Georgia (approx.)");
    public string NoteText => L(
        "აქციზი ითვლება კანონის მიხედვით. 2.0 ლიტრი = 2000 სმ³; ზუსტ თანხას ტექპასპორტის სმ³ იძლევა (მაგ. 1998). საბაჟო მომსახურების საფასური, რეგისტრაცია და შიდა ტრანსპორტი ჯამში არ შედის — ზუსტი თანხა გადაამოწმე rs.ge-ზე.",
        "Excise follows the law. 2.0 litres = 2000 cm³; the exact amount uses the cm³ from the registration papers (e.g. 1998). Customs service fees, registration and local transport are not included — check the exact amount on rs.ge.");

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        string? Get(string key) => query.TryGetValue(key, out var v) ? Uri.UnescapeDataString(v?.ToString() ?? "") : null;
        if (Get("year") is { Length: 4 } year) YearText = year;
        if (Get("cc") is { Length: > 0 } cc)
            EngineText = decimal.TryParse(cc, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 20
                ? (v / 1000m).ToString("0.0#", CultureInfo.InvariantCulture)
                : cc;
        if (Get("litres") is { Length: > 0 } litres) EngineText = litres;
        FuelIndex = Get("fuel") switch { "hybrid" => 1, "electric" => 2, _ => FuelIndex };
    }

    public async Task LoadAsync()
    {
        _rules = await rulesService.GetAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(UsdRateText) && _rules.UsdRate is { } rate)
            UsdRateText = rate.ToString("0.####", CultureInfo.InvariantCulture);
        RulesText = L(
            $"წესები ძალაშია {_rules.EffectiveFrom}-დან{(_rules.RatesDate is null ? "" : $" · ეროვნული ბანკის კურსი {_rules.RatesDate}")}",
            $"Rules in force from {_rules.EffectiveFrom}{(_rules.RatesDate is null ? "" : $" · National Bank rate {_rules.RatesDate}")}");
        if (!string.IsNullOrWhiteSpace(YearText) && (!string.IsNullOrWhiteSpace(EngineText) || FuelIndex == 2))
            Calculate();
    }

    private static decimal Number(string? text) =>
        decimal.TryParse((text ?? "").Replace(',', '.').Replace(" ", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;

    [RelayCommand]
    private void Calculate()
    {
        ErrorText = "";
        var fuel = (CustomsFuel)Math.Clamp(FuelIndex, 0, 2);
        var year = (int)Number(YearText);
        // People know the engine as "2.0", "2" or "2.5" litres; 1998-style cm³ also works.
        var engine = Number(EngineText);
        var cc = engine is > 0 and < 20 ? (int)Math.Round(engine * 1000) : (int)engine;

        if (year < 1950 || year > DateTime.Now.Year + 1 || (fuel != CustomsFuel.Electric && cc < 50))
        {
            HasResult = false;
            ErrorText = L("ჩაწერე წელი და ძრავი (მაგ. 2018 და 2.5).", "Enter the year and the engine (e.g. 2018 and 2.5).");
            return;
        }

        var usdRate = Number(UsdRateText);
        var result = CustomsCalculator.Calculate(
            _rules,
            new CustomsInput(year, cc, fuel, RightHandDrive, Number(PriceText), Number(ShippingText), Number(OtherText), DateTime.Now.Year),
            usdRate > 0 ? usdRate : null);

        ExciseText = $"{result.ExciseGel:N0} ₾";
        FormulaText = L(result.FormulaKa, result.FormulaEn);
        AgeText = L($"ასაკი: {result.Age} წ.", $"Age: {result.Age} yrs");
        CarText = result.CarAndShippingGel is { } car
            ? L($"მანქანა + ტრანსპორტი: {car:N0} ₾", $"Car + shipping: {car:N0} ₾")
            : L("ფასი და კურსი ჩაწერე, რომ სრული ჯამი გამოჩნდეს.", "Enter the price and the rate to see the full total.");
        TotalText = result.TotalGel is { } total ? $"{total:N0} ₾" : "—";
        TotalUsdText = result.TotalUsd is { } usd ? $"≈ ${usd:N0}" : "";
        HasResult = true;
    }

    [RelayCommand]
    private async Task OpenOfficial()
    {
        try { await Launcher.Default.OpenAsync(_rules.OfficialCalculatorUrl); }
        catch { }
    }
}
