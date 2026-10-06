using System.Net.Http.Json;

namespace CarScanner.Services.Customs;

/// <summary>Loads the rules and NBG rates from the GCar360 server (cached); built-in rules when offline.</summary>
public sealed class CustomsRulesService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private CustomsRules? _cached;
    private DateTimeOffset _cachedAt;

    public bool IsOffline { get; private set; }

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
                IsOffline = false;
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

        IsOffline = _cached is null;
        return _cached ?? CustomsRules.BuiltIn;
    }
}
