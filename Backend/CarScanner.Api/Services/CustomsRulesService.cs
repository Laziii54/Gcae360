using System.Text.Json;

namespace CarScanner.Api.Services;

/// <summary>
/// Car import excise rules for the app's customs calculator, plus the
/// National Bank of Georgia USD / EUR rates (cached for 6 hours).
/// The rules live on the server so a change in the law needs only a server
/// update, not a new app version.
///
/// Source: Tax Code of Georgia, amendment in force from 2 April 2026
/// (matsne.gov.ge document 6826443): passenger car excise 1.50 GEL per cm³
/// up to 6 years old, 4.50 GEL per cm³ over 6 years; left-hand-drive hybrids
/// up to 6 years −60 %; right-hand drive ×3; right-hand-drive electric car
/// 3 000 GEL. Age = year of the taxable operation − year of manufacture.
/// </summary>
public sealed class CustomsRulesService(IHttpClientFactory clients, ILogger<CustomsRulesService> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (decimal? Usd, decimal? Eur, string? Date, DateTimeOffset At) _rates;
    private DateTimeOffset _lastFailure = DateTimeOffset.MinValue;

    public async Task<object> GetAsync(CancellationToken ct)
    {
        var (usd, eur, date) = await RatesAsync(ct);
        return new
        {
            effectiveFrom = "2026-04-02",
            source = "საქართველოს საგადასახადო კოდექსი (ცვლილება, ძალაშია 2026-04-02) · matsne.gov.ge/ka/document/view/6826443",
            officialCalculatorUrl = "https://www.rs.ge/CarClearance",
            youngMaxAge = 6,
            ratePerCcYoung = 1.50m,
            ratePerCcOld = 4.50m,
            hybridYoungDiscount = 0.60m,
            rightHandMultiplier = 3m,
            electricLeftExcise = 0m,
            electricRightExcise = 3000m,
            usdRate = usd,
            eurRate = eur,
            ratesDate = date,
            // Plug-in hybrids (HS 8703 60 / 70) are hybrids for the 60 % relief.
            plugInCountsAsHybrid = true,
            // Customs service fee by car value (GEL). Approximate — adjust here
            // when the Revenue Service changes it; the app needs no update.
            customsFeeTiers = new object[]
            {
                new { upToGel = (decimal?)3000m, feeGel = 150m },
                new { upToGel = (decimal?)15000m, feeGel = 200m },
                new { upToGel = (decimal?)null, feeGel = 300m }
            },
            // 0 = not added. Set e.g. 0.18 if VAT must be shown on import.
            vatRate = 0m
        };
    }

    private async Task<(decimal? Usd, decimal? Eur, string? Date)> RatesAsync(CancellationToken ct)
    {
        if (_rates.Usd is not null && DateTimeOffset.UtcNow - _rates.At < TimeSpan.FromHours(6))
            return (_rates.Usd, _rates.Eur, _rates.Date);

        await _gate.WaitAsync(ct);
        try
        {
            if ((_rates.Usd is not null && DateTimeOffset.UtcNow - _rates.At < TimeSpan.FromHours(6)) ||
                DateTimeOffset.UtcNow - _lastFailure < TimeSpan.FromMinutes(10))
                return (_rates.Usd, _rates.Eur, _rates.Date);

            var client = clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync("https://nbg.gov.ge/gw/api/ct/monetarypolicy/currencies/en/json/?currencies=USD,EUR", ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var day = doc.RootElement[0];
            decimal? usd = null, eur = null;
            foreach (var currency in day.GetProperty("currencies").EnumerateArray())
            {
                var code = currency.GetProperty("code").GetString();
                var quantity = currency.TryGetProperty("quantity", out var q) && q.TryGetDecimal(out var qq) && qq > 0 ? qq : 1m;
                var rate = currency.GetProperty("rate").GetDecimal() / quantity;
                if (code == "USD") usd = rate;
                if (code == "EUR") eur = rate;
            }

            var date = day.TryGetProperty("date", out var d) ? d.GetString() : null;
            if (usd is not null)
                _rates = (usd, eur, date?[..Math.Min(10, date.Length)], DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            _lastFailure = DateTimeOffset.UtcNow;
            log.LogWarning("NBG rates failed: {Message}", ex.Message);
        }
        finally
        {
            _gate.Release();
        }

        return (_rates.Usd, _rates.Eur, _rates.Date);
    }
}
