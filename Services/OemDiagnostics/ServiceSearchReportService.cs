using System.Net.Http.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Shares Service "Search" results with the OBD360 server and asks which
/// sources other drivers already confirmed for the same make/model/year.
/// Sent: make, model, year, the first 11 VIN characters (no serial number),
/// the dashboard % and the catalog sources that answered. Never blocks the app.
/// </summary>
public sealed class ServiceSearchReportService(AccountService account)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task ReportAsync(
        VehicleInfo vehicle,
        ServiceSearchResult search,
        IReadOnlyList<ServiceSearchHit> hits,
        double? dashboardPercent)
    {
        if (!HasModel(vehicle))
            return;

        try
        {
            using var request = await account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/service-search/report");
            request.Content = JsonContent.Create(new
            {
                make = vehicle.Manufacturer,
                model = vehicle.Model,
                year = vehicle.Year,
                vinPrefix = vehicle.VIN is { Length: 17 } vin ? vin[..11] : null,
                dashboardPercent,
                tried = search.RequestsTried,
                answered = search.RequestsAnswered,
                appVersion = AppInfo.Current.VersionString,
                hits = hits.Take(60).Select(hit => new
                {
                    sourceKey = hit.SourceKey,
                    kind = hit.Kind.ToString(),
                    value = hit.Value,
                    unit = hit.Unit,
                    request = hit.Request,
                    module = hit.Module,
                    matched = hit.MatchesDashboard
                })
            });
            using var _ = await _http.SendAsync(request);
        }
        catch
        {
            // Sharing is optional; the result is already saved on the phone.
        }
    }

    /// <summary>Sources confirmed by other drivers for this make/model/year (empty when none or offline).</summary>
    public async Task<IReadOnlyList<string>> KnownSourcesAsync(VehicleInfo vehicle, CancellationToken token)
    {
        if (!HasModel(vehicle))
            return Array.Empty<string>();

        try
        {
            var path = "/api/service-search/known?make=" + Uri.EscapeDataString(vehicle.Manufacturer) +
                       "&model=" + Uri.EscapeDataString(vehicle.Model) +
                       "&year=" + Uri.EscapeDataString(vehicle.Year);
            using var request = await account.CreateAuthorizedRequestAsync(HttpMethod.Get, path);
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<string>();
            var body = await response.Content.ReadFromJsonAsync<KnownResponse>(cancellationToken: token);
            return body?.Sources ?? Array.Empty<string>();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool HasModel(VehicleInfo vehicle) =>
        Known(vehicle.Manufacturer) && Known(vehicle.Model) && Known(vehicle.Year);

    private static bool Known(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private sealed record KnownResponse(string[]? Sources);
}
