using System.Globalization;

namespace CarScanner.Services.Customs;

public enum CustomsFuel { Petrol, Diesel, Hybrid, PlugInHybrid, Electric }

public enum CustomsCurrency { Usd, Eur }

/// <summary>One step of the customs service fee: up to <see cref="UpToGel"/> of car value (null = above all).</summary>
public sealed record CustomsFeeTier(decimal? UpToGel, decimal FeeGel);

/// <summary>
/// Import rules (from the GCar360 server; built-in copy when offline).
/// Every field after <see cref="RatesDate"/> is optional so older servers
/// and older app versions keep working.
/// </summary>
public sealed record CustomsRules(
    string EffectiveFrom,
    string Source,
    string OfficialCalculatorUrl,
    int YoungMaxAge,
    decimal RatePerCcYoung,
    decimal RatePerCcOld,
    decimal HybridYoungDiscount,
    decimal RightHandMultiplier,
    decimal ElectricLeftExcise,
    decimal ElectricRightExcise,
    decimal? UsdRate,
    decimal? EurRate,
    string? RatesDate,
    bool PlugInCountsAsHybrid = true,
    IReadOnlyList<CustomsFeeTier>? CustomsFeeTiers = null,
    decimal VatRate = 0m)
{
    /// <summary>Tax Code of Georgia, amendment in force from 2 April 2026.</summary>
    public static CustomsRules BuiltIn { get; } = new(
        "2026-04-02",
        "საქართველოს საგადასახადო კოდექსი (ცვლილება, ძალაშია 2026-04-02)",
        "https://www.rs.ge/CarClearance",
        6, 1.50m, 4.50m, 0.60m, 3m, 0m, 3000m, null, null, null,
        true,
        DefaultFeeTiers,
        0m);

    public static IReadOnlyList<CustomsFeeTier> DefaultFeeTiers { get; } = new[]
    {
        new CustomsFeeTier(3000m, 150m),
        new CustomsFeeTier(15000m, 200m),
        new CustomsFeeTier(null, 300m)
    };

    public decimal CustomsFeeFor(decimal carValueGel)
    {
        var tiers = CustomsFeeTiers is { Count: > 0 } list ? list : DefaultFeeTiers;
        foreach (var tier in tiers)
        {
            if (tier.UpToGel is null || carValueGel <= tier.UpToGel.Value)
                return tier.FeeGel;
        }

        return tiers[^1].FeeGel;
    }
}

public sealed record CustomsInput(
    int ManufactureYear,
    int EngineCc,
    CustomsFuel Fuel,
    bool RightHandDrive,
    decimal PriceForeign,
    decimal AuctionFeesForeign,
    decimal ShippingForeign,
    decimal ForeignRate,
    decimal LocalCostsGel,
    int TaxYear);

/// <summary>A short hint shown under the result (e.g. "a 2020+ car would pay less").</summary>
public sealed record CustomsInsight(string Ka, string En, bool IsWarning);

public sealed record CustomsResult(
    int Age,
    bool Young,
    decimal RatePerCc,
    decimal ExciseGel,
    string FormulaKa,
    string FormulaEn,
    decimal CarValueGel,
    decimal CustomsFeeGel,
    decimal VatGel,
    decimal LocalCostsGel,
    decimal TaxesGel,
    decimal TotalGel,
    decimal? TotalForeign,
    IReadOnlyList<CustomsInsight> Insights);

/// <summary>
/// Import cost of a passenger car (HS 8703) in Georgia.
/// Excise follows the Tax Code exactly; the customs service fee and VAT come
/// from server rules so they can be corrected without an app update.
/// Age = tax year − year of manufacture (calendar years, as in the law).
/// </summary>
public static class CustomsCalculator
{
    public static bool GetsHybridRelief(CustomsRules rules, CustomsFuel fuel) =>
        fuel == CustomsFuel.Hybrid || (fuel == CustomsFuel.PlugInHybrid && rules.PlugInCountsAsHybrid);

    /// <summary>Excise in GEL and the per-cm³ rate that produced it.</summary>
    public static (decimal Excise, decimal Rate) Excise(CustomsRules rules, int engineCc, CustomsFuel fuel, bool rightHand, int age)
    {
        if (fuel == CustomsFuel.Electric)
            return (rightHand ? rules.ElectricRightExcise : rules.ElectricLeftExcise, 0m);

        var young = age <= rules.YoungMaxAge;
        var rate = young ? rules.RatePerCcYoung : rules.RatePerCcOld;
        if (GetsHybridRelief(rules, fuel) && young && !rightHand)
            rate *= 1 - rules.HybridYoungDiscount;
        if (rightHand)
            rate *= rules.RightHandMultiplier;

        return (Math.Round(Math.Max(0, engineCc) * rate, 0), rate);
    }

    public static CustomsResult Calculate(CustomsRules rules, CustomsInput input)
    {
        var age = Math.Max(0, input.TaxYear - input.ManufactureYear);
        var young = age <= rules.YoungMaxAge;
        var (excise, rate) = Excise(rules, input.EngineCc, input.Fuel, input.RightHandDrive, age);

        var foreign = Math.Max(0, input.PriceForeign) + Math.Max(0, input.AuctionFeesForeign) + Math.Max(0, input.ShippingForeign);
        var carValueGel = input.ForeignRate > 0 ? Math.Round(foreign * input.ForeignRate, 0) : 0m;
        var customsFee = rules.CustomsFeeFor(carValueGel);
        var vat = rules.VatRate > 0 ? Math.Round((carValueGel + excise) * rules.VatRate, 0) : 0m;
        var local = Math.Max(0, input.LocalCostsGel);
        var taxes = excise + customsFee + vat;
        var total = carValueGel + taxes + local;
        decimal? totalForeign = input.ForeignRate > 0 ? Math.Round(total / input.ForeignRate, 0) : null;

        var (formulaKa, formulaEn) = Formula(rules, input, age, young, rate, excise);

        return new CustomsResult(
            age, young, rate, excise, formulaKa, formulaEn,
            carValueGel, customsFee, vat, local, taxes, total, totalForeign,
            Insights(rules, input, age, young, excise));
    }

    private static (string Ka, string En) Formula(CustomsRules rules, CustomsInput input, int age, bool young, decimal rate, decimal excise)
    {
        if (input.Fuel == CustomsFuel.Electric)
        {
            return input.RightHandDrive
                ? ($"ელექტრო, მარჯვენა საჭე: ფიქსირებული {Money(excise)} ₾", $"Electric, right-hand drive: flat {Money(excise)} ₾")
                : ("ელექტრო, მარცხენა საჭე: აქციზი 0 ₾", "Electric, left-hand drive: excise 0 ₾");
        }

        var notesKa = new List<string> { young ? $"≤{rules.YoungMaxAge} წ." : $">{rules.YoungMaxAge} წ." };
        var notesEn = new List<string> { young ? $"≤{rules.YoungMaxAge} yrs" : $">{rules.YoungMaxAge} yrs" };
        if (GetsHybridRelief(rules, input.Fuel) && young && !input.RightHandDrive)
        {
            notesKa.Add($"ჰიბრიდი −{rules.HybridYoungDiscount * 100:0}%");
            notesEn.Add($"hybrid −{rules.HybridYoungDiscount * 100:0}%");
        }
        if (input.RightHandDrive)
        {
            notesKa.Add($"მარჯვენა საჭე ×{rules.RightHandMultiplier:0}");
            notesEn.Add($"right-hand ×{rules.RightHandMultiplier:0}");
        }

        var cc = Money(input.EngineCc);
        var r = rate.ToString("0.##", CultureInfo.InvariantCulture);
        return ($"{cc} სმ³ × {r} ₾ ({string.Join(", ", notesKa)}) = {Money(excise)} ₾",
                $"{cc} cm³ × {r} ₾ ({string.Join(", ", notesEn)}) = {Money(excise)} ₾");
    }

    private static IReadOnlyList<CustomsInsight> Insights(CustomsRules rules, CustomsInput input, int age, bool young, decimal excise)
    {
        var list = new List<CustomsInsight>();
        var hybridRelief = GetsHybridRelief(rules, input.Fuel);

        if (input.Fuel == CustomsFuel.Electric)
        {
            list.Add(input.RightHandDrive
                ? new($"მარჯვენასაჭიან ელექტრომობილზე აქციზი ფიქსირებულია ({Money(rules.ElectricRightExcise)} ₾). მარცხენა საჭით — 0 ₾.",
                      $"A right-hand-drive electric car pays a flat {Money(rules.ElectricRightExcise)} ₾. With left-hand drive — 0 ₾.", true)
                : new("ელექტრომობილზე აქციზი არ არის — იხდი მხოლოდ გაფორმებას.", "No excise on an electric car — only the clearance fee.", false));
            return list;
        }

        // Older than the young-car limit: show what a newer car would pay.
        if (!young)
        {
            var newestOldYear = input.TaxYear - rules.YoungMaxAge;
            var (youngExcise, _) = Excise(rules, input.EngineCc, input.Fuel, input.RightHandDrive, rules.YoungMaxAge);
            var saving = excise - youngExcise;
            if (saving > 0)
                list.Add(new(
                    $"{newestOldYear} წლის ან უფრო ახალი მანქანა იგივე ძრავით გადაიხდიდა {Money(youngExcise)} ₾-ს — {Money(saving)} ₾-ით ნაკლებს.",
                    $"A {newestOldYear} or newer car with the same engine would pay {Money(youngExcise)} ₾ — {Money(saving)} ₾ less.",
                    true));
            if (hybridRelief && !input.RightHandDrive)
                list.Add(new(
                    $"ჰიბრიდის 60%-იანი შეღავათი მხოლოდ {rules.YoungMaxAge} წლამდე მანქანებზე ვრცელდება.",
                    $"The 60% hybrid relief applies only to cars up to {rules.YoungMaxAge} years old.",
                    true));
        }
        else if (age == rules.YoungMaxAge)
        {
            // Age counts calendar years: on 1 January this car crosses the limit.
            var (nextYearExcise, _) = Excise(rules, input.EngineCc, input.Fuel, input.RightHandDrive, age + 1);
            var extra = nextYearExcise - excise;
            if (extra > 0)
                list.Add(new(
                    $"1 იანვრიდან ეს მანქანა {age + 1} წლის ჩაითვლება და აქციზი გახდება {Money(nextYearExcise)} ₾ (+{Money(extra)} ₾). განაბაჟე {input.TaxYear} წლის ბოლომდე.",
                    $"From 1 January this car counts as {age + 1} years old and excise becomes {Money(nextYearExcise)} ₾ (+{Money(extra)} ₾). Clear it before the end of {input.TaxYear}.",
                    true));
        }

        if (input.RightHandDrive)
        {
            var (leftExcise, _) = Excise(rules, input.EngineCc, input.Fuel, false, age);
            list.Add(new(
                $"მარჯვენა საჭეზე აქციზი {rules.RightHandMultiplier:0}-ჯერ მეტია. მარცხენასაჭიანი იგივე მანქანა: {Money(leftExcise)} ₾.",
                $"Right-hand drive pays {rules.RightHandMultiplier:0}× excise. The same car with left-hand drive: {Money(leftExcise)} ₾.",
                true));
            if (hybridRelief && young)
                list.Add(new(
                    "ჰიბრიდის შეღავათი მარჯვენასაჭიან მანქანაზე არ ვრცელდება.",
                    "The hybrid relief does not apply to right-hand-drive cars.",
                    true));
        }
        else if (young && !hybridRelief)
        {
            var (hybridExcise, _) = Excise(rules, input.EngineCc, CustomsFuel.Hybrid, false, age);
            if (hybridExcise < excise)
                list.Add(new(
                    $"იგივე ძრავით ჰიბრიდი გადაიხდიდა {Money(hybridExcise)} ₾-ს (−{rules.HybridYoungDiscount * 100:0}%).",
                    $"A hybrid with the same engine would pay {Money(hybridExcise)} ₾ (−{rules.HybridYoungDiscount * 100:0}%).",
                    false));
        }
        else if (young && hybridRelief)
        {
            list.Add(new(
                $"ჰიბრიდის შეღავათი გამოყენებულია: აქციზი {rules.HybridYoungDiscount * 100:0}%-ით ნაკლებია.",
                $"Hybrid relief applied: excise is {rules.HybridYoungDiscount * 100:0}% lower.",
                false));
        }

        return list;
    }

    /// <summary>12345 → "12 345".</summary>
    public static string Money(decimal value) =>
        Math.Round(value, 0).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

    public static string Money(int value) => Money((decimal)value);
}
