using System.Text.Json;
using CarScanner.Models;
using SQLite;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>One value that answered during the service search.</summary>
public sealed record ServiceSearchHit(
    string SourceKey,
    string CatalogMake,
    string Name,
    ServiceMetricKind Kind,
    double Value,
    string Unit,
    string Request,
    string Module,
    bool OwnBrand)
{
    /// <summary>Set after the search when the value equals what the dashboard shows.</summary>
    public bool MatchesDashboard { get; init; }
}

public sealed record ServiceSearchResult(
    IReadOnlyList<ServiceSearchHit> Hits,
    int RequestsTried,
    int RequestsAnswered);

/// <summary>
/// "Search" on the Service page: tries EVERY known service identifier of the
/// catalog (all makes, ~20 distinct requests) and does not stop at the first
/// answer. Read-only: only 0x22 / 0x21 read requests are sent.
///
/// Identifiers of other makes are sent only to diagnostic addresses
/// (11-bit 0x700–0x7FF, 29-bit 0x18DAxxF1), never to IDs that a car may use
/// for its normal messages. The user then compares the values with the
/// dashboard; the matching sources are remembered for this car and used by
/// the normal Service refresh from then on.
/// </summary>
public sealed partial class ServiceDataReaderService
{
    private const string PreferredSourcesKey = "svc_preferred_sources_v1";

    public async Task<ServiceSearchResult> SearchAllSourcesAsync(
        VehicleInfo vehicle,
        IProgress<string>? progress,
        CancellationToken token)
    {
        var resolved = _vehicleProfiles.Resolve(vehicle);
        await _databaseCatalog.EnsureLoadedAsync(token).ConfigureAwait(false);

        var ownMakes = new HashSet<string>(
            MakeAliases(resolved.Manufacturer.Value).Concat(BrandFamily(resolved.Manufacturer.Value)),
            StringComparer.OrdinalIgnoreCase);

        var rows = await QueryServiceRowsAsync(null, token).ConfigureAwait(false);
        var pairs = new List<(ServicePidRow Row, ServicePidCandidate Candidate, bool Own)>();
        foreach (var row in rows)
        {
            var candidate = TryBuildCandidate(row, out _);
            if (candidate is null || !IsSearchKind(candidate.Kind))
                continue;

            var own = ownMakes.Contains(row.Make ?? string.Empty);
            if (!own && !IsDiagnosticAddress(candidate.Address))
                continue;

            pairs.Add((row, candidate, own));
        }

        // Own brand first, then the rest; one request per identifier.
        var groups = pairs
            .GroupBy(item => $"{item.Candidate.ServiceId:X2}|{AddressKey(item.Candidate.Address, item.Candidate.DataIdentifier)}", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Any(item => item.Own))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hits = new List<ServiceSearchHit>();
        var answered = 0;
        for (var index = 0; index < groups.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var group = groups[index];
            var first = group.First().Candidate;
            progress?.Report($"{index + 1}/{groups.Length}");

            var request = first.ServiceId == 0x21
                ? new byte[] { 0x21, (byte)first.DataIdentifier }
                : new byte[] { 0x22, (byte)(first.DataIdentifier >> 8), (byte)(first.DataIdentifier & 0xFF) };

            string raw;
            try
            {
                raw = _readPhysicalOverride is not null
                    ? await _readPhysicalOverride(first.Address, request, TimeSpan.FromMilliseconds(1500), token).ConfigureAwait(false)
                    : await _obd.ReadPhysicalCanReadOnlyAsync(first.Address, request, TimeSpan.FromMilliseconds(1500), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue; // no answer = this identifier does not exist on this car
            }

            if (!TryExtractDidData(raw, first.DataIdentifier, first.Address.ReceiveCanId, out var data, first.ServiceId))
                continue;

            answered++;
            var requestText = string.Concat(request.Select(b => b.ToString("X2")));
            foreach (var (row, candidate, own) in group)
            {
                if (!TryDecodeValue(candidate, data, out var value) || !IsPlausible(candidate.Kind, candidate.Unit, value))
                    continue;

                hits.Add(new ServiceSearchHit(
                    SourceKey(row),
                    row.Make ?? string.Empty,
                    candidate.Name,
                    candidate.Kind,
                    value,
                    candidate.Unit,
                    requestText,
                    candidate.Address.Detail,
                    own));
            }
        }

        var distinct = hits
            .GroupBy(hit => $"{hit.Request}|{hit.Module}|{hit.Kind}|{hit.Value:0.###}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(hit => hit.OwnBrand).First())
            .OrderByDescending(hit => hit.OwnBrand)
            .ThenBy(hit => Priority(hit.Kind))
            .ToArray();

        return new ServiceSearchResult(distinct, groups.Length, answered);
    }

    /// <summary>Marks oil-life hits that equal the dashboard % (±2).</summary>
    public static IReadOnlyList<ServiceSearchHit> MatchDashboard(IReadOnlyList<ServiceSearchHit> hits, double? dashboardPercent) =>
        hits.Select(hit => hit with
        {
            MatchesDashboard = dashboardPercent is { } percent &&
                               hit.Kind == ServiceMetricKind.OilLifePercent &&
                               Math.Abs(hit.Value - percent) <= 2
        }).ToArray();

    /// <summary>Remembers the confirmed sources for this car (VIN, or make|model|year).</summary>
    public void SavePreferredSources(VehicleInfo vehicle, IEnumerable<string> sourceKeys)
    {
        var added = sourceKeys.Where(key => !string.IsNullOrWhiteSpace(key)).ToArray();
        if (added.Length == 0)
            return;

        try
        {
            var all = LoadAllPreferred();
            var vehicleKey = VehicleKey(vehicle);
            // New confirmations first; earlier ones are kept (oil + service due + inspection …).
            all[vehicleKey] = added
                .Concat(all.TryGetValue(vehicleKey, out var existing) ? existing : Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .Take(12)
                .ToArray();
            Preferences.Default.Set(PreferredSourcesKey, JsonSerializer.Serialize(all));
        }
        catch
        {
            // Preferences are a convenience; the search can be repeated.
        }
    }

    private static Dictionary<string, string[]> LoadAllPreferred()
    {
        try
        {
            var json = Preferences.Default.Get(PreferredSourcesKey, string.Empty);
            return string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                : JsonSerializer.Deserialize<Dictionary<string, string[]>>(json) is { } parsed
                    ? new Dictionary<string, string[]>(parsed, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Catalog candidates the user confirmed against the dashboard for this car.</summary>
    private async Task<IReadOnlyList<ServicePidCandidate>> LoadPreferredCandidatesAsync(VehicleInfo vehicle, CancellationToken token)
    {
        if (!LoadAllPreferred().TryGetValue(VehicleKey(vehicle), out var keys) || keys.Length == 0)
            return Array.Empty<ServicePidCandidate>();

        await _databaseCatalog.EnsureLoadedAsync(token).ConfigureAwait(false);
        var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
        var rows = await QueryServiceRowsAsync(null, token).ConfigureAwait(false);
        return rows
            .Where(row => wanted.Contains(SourceKey(row)))
            .Select(row => TryBuildCandidate(row, out _))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate! with { Debug = false, BrandFallback = false })
            .ToArray();
    }

    private async Task<List<ServicePidRow>> QueryServiceRowsAsync(string? make, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var connection = new SQLiteAsyncConnection(_databaseCatalog.DatabasePath, SQLiteOpenFlags.ReadOnly);
        try
        {
            return await connection.QueryAsync<ServicePidRow>(
                """
                SELECT
                    p.id AS Id, p.make AS Make, p.model AS Model, p.header AS Header,
                    p.command_json AS CommandJson, p.parameter_id AS ParameterId, p.name AS Name,
                    p.unit AS Unit, p.bit_length AS BitLength, p.bit_offset AS BitOffset, p.eax AS Eax,
                    p.format_json AS FormatJson, p.debug AS Debug, p.raw_json AS RawJson, s.name AS SourceCatalog
                FROM pid_definitions p
                INNER JOIN source_catalogs s ON s.id = p.source_catalog_id
                WHERE p.pid IN ('22', '21')
                  AND (? IS NULL OR lower(p.make) = lower(?))
                  AND (lower(p.name) LIKE '%oil life%' OR lower(p.name) LIKE '%oil degradation%'
                    OR lower(p.name) LIKE '%oil change%' OR lower(p.name) LIKE '%oil changed%'
                    OR lower(p.name) LIKE '%next service%' OR lower(p.name) LIKE '%service distance%'
                    OR lower(p.name) LIKE '%service days%' OR lower(p.name) LIKE '%until service%'
                    OR lower(p.name) LIKE '%inspection%' OR lower(p.name) LIKE '%maintenance%');
                """,
                make, make).ConfigureAwait(false);
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private static string SourceKey(ServicePidRow row) =>
        $"{row.Make}|{row.Header}|{row.CommandJson}|{row.ParameterId}";

    private static bool IsSearchKind(ServiceMetricKind kind) =>
        IsBrandWideKind(kind) ||
        kind is ServiceMetricKind.OilChangeInterval or ServiceMetricKind.MaintenanceCounter;

    /// <summary>Addresses reserved for diagnostics on practically every car.</summary>
    private static bool IsDiagnosticAddress(ServiceDiagnosticAddress address) =>
        address.ExtendedAddress is null &&
        (address.Extended29Bit
            ? (address.TransmitCanId & 0x1FFF00FF) == 0x18DA00F1 || address.TransmitCanId == 0x18DB33F1
            : address.TransmitCanId is >= 0x700 and <= 0x7FF);
}
