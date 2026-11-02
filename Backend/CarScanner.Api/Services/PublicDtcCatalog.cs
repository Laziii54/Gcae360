using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>The public-website answer for one DTC code: the same real catalog the app itself reads,
/// stripped to what an anonymous web visitor (no vehicle/ECU context) can honestly be told.</summary>
public sealed record PublicDtcResult(
    string Code,
    string Category,
    string TitleKa,
    string DescriptionKa,
    IReadOnlyList<string> CausesKa,
    IReadOnlyList<string> SymptomsKa);

/// <summary>
/// Read-only lookup against the same bundled DTC catalog the mobile app ships (see
/// Services/DtcCatalogService.cs there), for the OBD360.ge CarView web page. Deliberately NOT a
/// reuse of that service: this one has no vehicle/ECU context (an anonymous web visitor), so it
/// only ever asks for the generic SAE/OBD-II definition — none of that service's manufacturer- or
/// ECU-specific resolution applies here. The raw_json "common_causes"/"symptoms" extraction is
/// ported from it as-is, so a code shows the same Georgian text on the website as in the app.
/// </summary>
public sealed class PublicDtcCatalog
{
    private const string CompressedAssetFileName = "DtcCatalog.sqlite.br";
    private const string DatabaseFileName = "DtcCatalog.sqlite";
    private static readonly Regex CodeShape = new("^[PBCU][0-3][0-9A-F]{3}$", RegexOptions.Compiled);

    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _connectionString;

    public PublicDtcCatalog(IHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    /// <summary>Upper-cases and shape-checks a code; null when it is not a plausible SAE DTC
    /// (never passed to SQL either way, since a parameterized query is used regardless).</summary>
    public static string? NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim().ToUpperInvariant();
        return CodeShape.IsMatch(trimmed) ? trimmed : null;
    }

    public async Task<PublicDtcResult?> LookupAsync(string code, CancellationToken token)
    {
        var connectionString = await GetConnectionStringAsync(token).ConfigureAwait(false);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);

        // The generic (manufacturer-independent) definition first; "unscoped" codes have no
        // generic/OEM split at all in the source data and are tried next.
        foreach (var scope in new[] { "generic", "unscoped" })
        {
            var result = await QueryAsync(connection, code, scope, token).ConfigureAwait(false);
            if (result is not null)
                return result;
        }
        return null;
    }

    private static async Task<PublicDtcResult?> QueryAsync(SqliteConnection connection, string code, string scope, CancellationToken token)
    {
        const string sql = """
            SELECT v.category, v.title_ka, v.description_ka, d.raw_json
            FROM v_preferred_dtc_localized v
            JOIN dtc_definitions d ON d.id = v.record_id
            WHERE v.code = $code AND v.scope = $scope
            ORDER BY (v.title_ka IS NULL OR v.title_ka = '') ASC, v.record_id ASC
            LIMIT 1;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$scope", scope);

        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;

        var category = reader.IsDBNull(0) ? "" : reader.GetString(0);
        var titleKa = reader.IsDBNull(1) ? "" : reader.GetString(1);
        var descriptionKa = reader.IsDBNull(2) ? "" : reader.GetString(2);
        if (string.IsNullOrWhiteSpace(titleKa) && string.IsNullOrWhiteSpace(descriptionKa))
            return null; // no honest Georgian text for this entry — say so, don't show English instead

        var rawJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        return new PublicDtcResult(
            code,
            category,
            titleKa,
            descriptionKa,
            ExtractLocalizedListFromRawJson(rawJson, "common_causes"),
            ExtractLocalizedListFromRawJson(rawJson, "symptoms"));
    }

    private async Task<string> GetConnectionStringAsync(CancellationToken token)
    {
        if (_connectionString is not null)
            return _connectionString;

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_connectionString is not null)
                return _connectionString;

            var directory = BackendDataPath.ResolveDataDirectory(_environment, _configuration);
            var databasePath = Path.Combine(directory, DatabaseFileName);

            if (!File.Exists(databasePath))
                await UnpackAsync(databasePath, token).ConfigureAwait(false);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
            }.ToString();
            return _connectionString;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UnpackAsync(string databasePath, CancellationToken token)
    {
        // Not ContentRootPath: in `dotnet run` that is the project (source) directory, not the
        // build output this Content item is copied next to. AppContext.BaseDirectory is always
        // the running assembly's own directory — bin/ in dev, the publish directory in prod —
        // which is where this file actually lands either way.
        var compressedPath = Path.Combine(AppContext.BaseDirectory, CompressedAssetFileName);
        if (!File.Exists(compressedPath))
            throw new FileNotFoundException("DTC catalog asset is not deployed with this build.", compressedPath);

        // Decompress to a temp file next to the target and rename into place, so a request
        // arriving mid-unpack on a cold start never opens a half-written database.
        var tempPath = databasePath + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var source = File.OpenRead(compressedPath))
        await using (var brotli = new BrotliStream(source, CompressionMode.Decompress))
        await using (var destination = File.Create(tempPath))
        {
            await brotli.CopyToAsync(destination, token).ConfigureAwait(false);
        }
        File.Move(tempPath, databasePath, overwrite: true);
    }

    // ------------------------------------------------------------------
    // Georgian text extraction from raw_json — ported from DtcCatalogService.cs so a code reads
    // identically on the website and in the app; see that file for the English-language twin.
    // ------------------------------------------------------------------

    private static IReadOnlyList<string> ExtractLocalizedListFromRawJson(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out var array) ||
                array.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (var item in array.EnumerateArray())
            {
                var value = ExtractLocalizedJsonText(item, "ka");
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
                if (result.Count >= 8)
                    break;
            }
            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? ExtractLocalizedJsonText(JsonElement element, string language)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in new[] { language, $"label_{language}", "label_en", "label", "description", "text", "name" })
        {
            if (!element.TryGetProperty(key, out var property))
                continue;
            if (property.ValueKind == JsonValueKind.String)
                return property.GetString();
            if (property.ValueKind == JsonValueKind.Object &&
                property.TryGetProperty(language, out var localized) &&
                localized.ValueKind == JsonValueKind.String)
                return localized.GetString();
        }
        return null;
    }
}
