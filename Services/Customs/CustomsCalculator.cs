using System.Net.Http.Json;

namespace CarScanner.Services.Customs;

public enum CustomsFuel { Petrol, Hybrid, Electric }

/// <summary>Excise rules (from the server; built-in copy when offline).</summary>
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
    string? RatesDate)
{
    /// <summary>Tax Code of Georgia, amendment in force from 2 April 2026.</summary>
    public static CustomsRules BuiltIn { get; } = new(
        "2026-04-02",
        "საქართველოს საგადასახადო კოდექსი (ცვლილება, ძალაშია 2026-04-02)",
        "https://www.rs.ge/CarClearance",
        6, 1.50m, 4.50m, 0.60m, 3m, 0m, 3000m, null, null, null);
}

public sealed record CustomsInput(int ManufactureYear, int EngineCc, CustomsFuel Fuel, bool RightHandDrive, decimal PriceUsd, decimal ShippingUsd, decimal OtherUsd, int TaxYear);

public sealed record CustomsResult(int Age, bool Young, decimal RatePerCc, decimal ExciseGel, string FormulaKa, string FormulaEn, decimal? UsdRate, decimal? CarAndShippingGel, decimal? TotalGel, decimal? TotalUsd);

/// <summary>
/// Import cost of a passenger car in Georgia: excise by the Tax Code
/// (exact) plus the price and shipping the user enters. Customs service fees
/// and other charges are not included — the official rs.ge calculator is
/// linked for the final amount.
/// </summary>
public static class CustomsCalculator
{
    public static CustomsResult Calculate(CustomsRules rules, CustomsInput input, decimal? usdRate)
    {
        var age = Math.Max(0, input.TaxYear - input.ManufactureYear);
        var young = age <= rules.YoungMaxAge;
        decimal rate;
        decimal excise;
        string ka, en;

        if (input.Fuel == CustomsFuel.Electric)
        {
            rate = 0;
            excise = input.RightHandDrive ? rules.ElectricRightExcise : rules.ElectricLeftExcise;
            ka = input.RightHandDrive ? $"ელექტრო, მარჯვენასაჭიანი: {excise:N0} ₾" : "ელექტრო, მარცხენასაჭიანი: აქციზი არ არის";
            en = input.RightHandDrive ? $"Electric, right-hand drive: {excise:N0} ₾" : "Electric, left-hand drive: no excise";
        }
        else
        {
            rate = young ? rules.RatePerCcYoung : rules.RatePerCcOld;
            var notesKa = new List<string> { young ? $"≤{rules.YoungMaxAge} წ." : $">{rules.YoungMaxAge} წ." };
            var notesEn = new List<string> { young ? $"≤{rules.YoungMaxAge} yrs" : $">{rules.YoungMaxAge} yrs" };
            if (input.Fuel == CustomsFuel.Hybrid && young && !input.RightHandDrive)
            {
                rate *= 1 - rules.HybridYoungDiscount;
                notesKa.Add($"ჰიბრიდი −{rules.HybridYoungDiscount * 100:0}%");
                notesEn.Add($"hybrid −{rules.HybridYoungDiscount * 100:0}%");
            }
            if (input.RightHandDrive)
            {
                rate *= rules.RightHandMultiplier;
                notesKa.Add($"მარჯვენა საჭე ×{rules.RightHandMultiplier:0}");
                notesEn.Add($"right-hand drive ×{rules.RightHandMultiplier:0}");
            }

            excise = Math.Round(input.EngineCc * rate, 2);
            var litres = input.EngineCc / 1000m;
            ka = $"{litres:0.0#} ლ ({input.EngineCc:N0} სმ³) × {rate:0.##} ₾ ({string.Join(", ", notesKa)}) = {excise:N0} ₾";
            en = $"{litres:0.0#} L ({input.EngineCc:N0} cm³) × {rate:0.##} ₾ ({string.Join(", ", notesEn)}) = {excise:N0} ₾";
        }

        decimal? carGel = null, totalGel = null, totalUsd = null;
        if (usdRate is > 0 && (input.PriceUsd > 0 || input.ShippingUsd > 0 || input.OtherUsd > 0))
        {
            var usd = input.PriceUsd + input.ShippingUsd + input.OtherUsd;
            carGel = Math.Round(usd * usdRate.Value, 0);
            totalGel = carGel + excise;
            totalUsd = Math.Round(totalGel.Value / usdRate.Value, 0);
        }

        return new CustomsResult(age, young, rate, excise, ka, en, usdRate, carGel, totalGel, totalUsd);
    }
}

/// <summary>Loads the rules from the GCar360 server (cached); built-in rules when offline.</summary>
public sealed class CustomsRulesService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private CustomsRules? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<CustomsRules> GetAsync(CancellationToken token)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromHours(3))
            return _cached;

        try
        {
            var rules = await _http.GetFromJsonAsync<CustomsRules>(AIConfig.BackendBaseUrl.TrimEnd('/') + "/api/customs/rules", token);
            if (rules is { RatePerCcYoung: > 0, RatePerCcOld: > 0 })
            {
                _cached = rules;
                _cachedAt = DateTimeOffset.UtcNow;
                return rules;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Offline: built-in rules, exchange rate typed by the user.
        }

        return _cached ?? CustomsRules.BuiltIn;
    }
}
