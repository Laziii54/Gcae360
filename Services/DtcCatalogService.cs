using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CarScanner.Models;
using SQLite;

namespace CarScanner.Services;

/// <summary>
/// Offline-first DTC catalog backed by the bundled SQLite master database.
///
/// Runtime lookup order:
/// 1) exact ECU-specific definition when a verified ECU context is supplied;
/// 2) manufacturer/OEM definition for the verified/selected brand;
/// 3) generic SAE/OBD-II definition.
///
/// A verified VIN mismatch always blocks the selected brand from being used for
/// manufacturer-specific lookup. Generic fallback remains available.
/// </summary>
public sealed class DtcCatalogService
{
    private const string DatabaseAssetName = "DtcCatalog.sqlite";

    // The catalog ships Brotli-compressed (~160 MB -> a small fraction) and is
    // unpacked once on first launch (or after an app update).
    private const string CompressedDatabaseAssetName = "DtcCatalog.sqlite.br";
    private const string DatabaseFileName = "DtcCatalog.sqlite";
    private const string DatabaseStampKey = "dtc_catalog_sqlite_asset_stamp";
    private const string ExpectedSchemaVersion = "5.0";

    // Changes whenever the bundled catalog content is updated, so a phone
    // with the same app version still unpacks the new file.
    private const string CatalogRevision = "obdb-2026-10-17";

    private readonly VehicleBrandProfileService _brands;
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _loadErrors = new();

    private readonly Dictionary<string, string> _manufacturerAliases =
        new(StringComparer.OrdinalIgnoreCase);

    private SQLiteAsyncConnection? _database;
    private bool _loaded;
    private int _count;
    private int _definitionCount;

    public DtcCatalogService(
        VehicleBrandProfileService brands,
        VehicleProfileResolverService vehicleProfiles)
    {
        _brands = brands;
        _vehicleProfiles = vehicleProfiles;
    }

    /// <summary>Unique DTC code count in the master database.</summary>
    public int Count => _count;

    /// <summary>Total source-definition rows in the master database.</summary>
    public int DefinitionCount => _definitionCount;

    public int LoadErrorCount => _loadErrors.Count;

    public IReadOnlyList<string> LoadErrors => _loadErrors.ToArray();

    public DateTimeOffset LastRefresh { get; private set; } =
        DateTimeOffset.MinValue;

    public string DatabasePath =>
        Path.Combine(
            FileSystem.AppDataDirectory,
            DatabaseFileName);

    // ============================================================
    // LOAD / DATABASE COPY
    // ============================================================

    public async Task EnsureLoadedAsync(
        CancellationToken token = default)
    {
        if (_loaded && _database is not null)
            return;

        await _gate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            if (_loaded && _database is not null)
                return;

            _loadErrors.Clear();

            Debug.WriteLine(
                "========== SQLITE DTC CATALOG LOAD START ==========");

            try
            {
                await EnsureDatabaseFileAsync(
                        forceCopy: false,
                        token)
                    .ConfigureAwait(false);

                await OpenAndValidateDatabaseAsync(token)
                    .ConfigureAwait(false);
            }
            catch (Exception firstError)
            {
                // If an old/corrupt copied database survived an update,
                // replace it once from the packaged MauiAsset and retry.
                _loadErrors.Add(
                    $"Initial SQLite open failed: {firstError.Message}");

                Debug.WriteLine(
                    $"DTC SQLITE RETRY: {firstError}");

                await CloseDatabaseAsync()
                    .ConfigureAwait(false);

                await EnsureDatabaseFileAsync(
                        forceCopy: true,
                        token)
                    .ConfigureAwait(false);

                await OpenAndValidateDatabaseAsync(token)
                    .ConfigureAwait(false);
            }

            var db = _database
                ?? throw new InvalidOperationException(
                    "DTC SQLite connection was not created.");

            _definitionCount =
                await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM dtc_definitions;")
                    .ConfigureAwait(false);

            _count =
                await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(DISTINCT code) FROM dtc_definitions;")
                    .ConfigureAwait(false);

            await LoadManufacturerAliasesAsync(db)
                .ConfigureAwait(false);

            _loaded = true;
            LastRefresh = DateTimeOffset.UtcNow;

            Preferences.Default.Set(
                DatabaseStampKey,
                BuildAssetStamp());

            Debug.WriteLine(
                $"DTC SQLITE READY: Codes={_count}, Definitions={_definitionCount}, Path={DatabasePath}");

            Debug.WriteLine(
                "========== SQLITE DTC CATALOG LOAD COMPLETE ==========");
        }
        catch (Exception ex)
        {
            _loaded = false;

            _loadErrors.Add(ex.ToString());

            Debug.WriteLine(
                $"DTC SQLITE LOAD FAILED: {ex}");

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ForceReloadAsync(
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            _loaded = false;
            _count = 0;
            _definitionCount = 0;
            _manufacturerAliases.Clear();
            _loadErrors.Clear();
            LastRefresh = DateTimeOffset.MinValue;

            await CloseDatabaseAsync()
                .ConfigureAwait(false);

            try
            {
                if (File.Exists(DatabasePath))
                    File.Delete(DatabasePath);
            }
            catch
            {
                // The following forced copy will surface a useful error
                // if the destination really cannot be replaced.
            }

            Preferences.Default.Remove(DatabaseStampKey);
        }
        finally
        {
            _gate.Release();
        }

        await EnsureLoadedAsync(token)
            .ConfigureAwait(false);
    }

    private async Task EnsureDatabaseFileAsync(
        bool forceCopy,
        CancellationToken token)
    {
        var targetPath = DatabasePath;

        var stampMatches =
            string.Equals(
                Preferences.Default.Get(
                    DatabaseStampKey,
                    string.Empty),
                BuildAssetStamp(),
                StringComparison.Ordinal);

        if (!forceCopy &&
            File.Exists(targetPath) &&
            stampMatches)
        {
            return;
        }

        var directory =
            Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath =
            targetPath + ".tmp";

        if (File.Exists(tempPath))
            File.Delete(tempPath);

        await using var source =
            await OpenPackagedDatabaseAsync()
                .ConfigureAwait(false);

        await using (var destination =
                     new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         1024 * 1024,
                         useAsync: true))
        {
            await source.CopyToAsync(
                    destination,
                    1024 * 1024,
                    token)
                .ConfigureAwait(false);

            await destination.FlushAsync(token)
                .ConfigureAwait(false);
        }

        File.Move(
            tempPath,
            targetPath,
            overwrite: true);
    }

    /// <summary>Compressed catalog when packaged, otherwise the plain file (older builds).</summary>
    private static async Task<Stream> OpenPackagedDatabaseAsync()
    {
        try
        {
            var packed = await FileSystem.OpenAppPackageFileAsync(
                    CompressedDatabaseAssetName)
                .ConfigureAwait(false);
            return new BrotliStream(packed, CompressionMode.Decompress, leaveOpen: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Compressed DTC catalog not found, using plain file: {ex.Message}");
            return await FileSystem.OpenAppPackageFileAsync(
                    DatabaseAssetName)
                .ConfigureAwait(false);
        }
    }

    private async Task OpenAndValidateDatabaseAsync(
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        await CloseDatabaseAsync()
            .ConfigureAwait(false);

        _database =
            new SQLiteAsyncConnection(
                DatabasePath,
                SQLiteOpenFlags.ReadOnly);

        var db = _database;

        var integrity =
            await db.ExecuteScalarAsync<string>(
                    "PRAGMA quick_check;")
                .ConfigureAwait(false);

        if (!string.Equals(
                integrity,
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"DtcCatalog.sqlite integrity check failed: {integrity}");
        }

        var schemaVersion =
            await db.ExecuteScalarAsync<string>(
                    "SELECT value FROM meta WHERE key = 'schema_version' LIMIT 1;")
                .ConfigureAwait(false);

        if (!string.Equals(
                schemaVersion,
                ExpectedSchemaVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported DTC database schema. Expected {ExpectedSchemaVersion}, found {schemaVersion ?? "NULL"}.");
        }
    }

    private async Task CloseDatabaseAsync()
    {
        if (_database is null)
            return;

        try
        {
            await _database.CloseAsync()
                .ConfigureAwait(false);
        }
        catch
        {
            // Closing is best-effort. A subsequent open/copy will expose
            // a real filesystem/database problem if one exists.
        }
        finally
        {
            _database = null;
        }
    }

    private static string BuildAssetStamp()
    {
        return string.Join(
            "|",
            AppInfo.Current.PackageName,
            AppInfo.Current.VersionString,
            AppInfo.Current.BuildString,
            ExpectedSchemaVersion,
            CatalogRevision);
    }

    private async Task LoadManufacturerAliasesAsync(
        SQLiteAsyncConnection db)
    {
        _manufacturerAliases.Clear();

        var rows =
            await db.QueryAsync<ManufacturerAliasRow>(
                    """
                    SELECT
                        alias AS Alias,
                        canonical AS Canonical
                    FROM manufacturer_aliases;
                    """)
                .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Alias) ||
                string.IsNullOrWhiteSpace(row.Canonical))
            {
                continue;
            }

            _manufacturerAliases[row.Alias.Trim()] =
                row.Canonical.Trim();
        }
    }

    // ============================================================
    // LOOKUP
    // ============================================================

    public async Task<DtcCatalogEntry?> GetAsync(
        string code,
        CancellationToken token = default)
    {
        return await GetAsync(
                code,
                ResolveRuntimeContext(),
                token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Explicit context overload used by a verified OEM ECU path.
    /// </summary>
    public async Task<DtcCatalogEntry?> GetAsync(
        string code,
        DtcLookupContext context,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var db =
            await GetDatabaseAsync(token)
                .ConfigureAwait(false);

        var normalizedCode =
            NormalizeCode(code);

        var rows =
            await db.QueryAsync<DtcRow>(
                    CandidateSql,
                    normalizedCode)
                .ConfigureAwait(false);

        if (rows.Count == 0)
            return null;

        var best =
            ChooseBestCandidate(
                rows,
                context);

        if (best is null)
            return null;

        var translation =
            await ResolveSafeGeorgianTranslationAsync(
                    db,
                    best)
                .ConfigureAwait(false);

        return BuildEntry(
            best,
            translation,
            context);
    }

    public async Task<bool> ContainsAsync(
        string code,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var db =
            await GetDatabaseAsync(token)
                .ConfigureAwait(false);

        var count =
            await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM dtc_contexts WHERE code = ?;",
                    NormalizeCode(code))
                .ConfigureAwait(false);

        return count > 0;
    }

    public async Task<IReadOnlyList<DtcCatalogEntry>> GetAllAsync(
        CancellationToken token = default)
    {
        var db =
            await GetDatabaseAsync(token)
                .ConfigureAwait(false);

        // This method is not used during a normal scan. It intentionally
        // returns one generic/preferred English record per code to avoid
        // expanding tens of thousands of OEM/ECU variants into the UI.
        var rows =
            await db.QueryAsync<DtcRow>(
                    AllCodesSql)
                .ConfigureAwait(false);

        var result =
            new List<DtcCatalogEntry>(
                rows.Count);

        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();

            var translation =
                await ResolveSafeGeorgianTranslationAsync(
                        db,
                        row)
                    .ConfigureAwait(false);

            result.Add(
                BuildEntry(
                    row,
                    translation,
                    new DtcLookupContext()));
        }

        return result;
    }

    public async Task<Dictionary<string, int>> GetFamilyCountsAsync(
        CancellationToken token = default)
    {
        var db =
            await GetDatabaseAsync(token)
                .ConfigureAwait(false);

        var rows =
            await db.QueryAsync<FamilyCountRow>(
                    """
                    SELECT
                        substr(code, 1, 1) AS Family,
                        COUNT(DISTINCT code) AS Count
                    FROM dtc_definitions
                    GROUP BY substr(code, 1, 1)
                    ORDER BY Family;
                    """)
                .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.Family,
            row => row.Count,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync(
        CancellationToken token)
    {
        await EnsureLoadedAsync(token)
            .ConfigureAwait(false);

        await DtcGeorgianTextRepair.EnsureLoadedAsync()
            .ConfigureAwait(false);

        return _database
            ?? throw new InvalidOperationException(
                "DTC SQLite database is not available.");
    }

    // ============================================================
    // RUNTIME VEHICLE CONTEXT
    // ============================================================

    private DtcLookupContext ResolveRuntimeContext()
    {
        var resolved =
            _vehicleProfiles.Current;

        if (resolved is not null)
        {
            // A real VIN says a different manufacturer than the selection:
            // never apply OEM text from the selected brand.
            if (resolved.VerificationState ==
                VehicleProfileVerificationState.SelectionMismatch)
            {
                return new DtcLookupContext(
                    Manufacturer: null,
                    Model: KnownOrNull(resolved.Model.Value),
                    Year: KnownOrNull(resolved.Year.Value),
                    EcuModel: null,
                    EcuType: null,
                    AllowManufacturerSpecific: false,
                    ManufacturerContextVerified: false);
            }

            if (resolved.CanUseManufacturerSpecificDefinitions)
            {
                return new DtcLookupContext(
                    Manufacturer:
                        resolved.DiagnosticVehicle.Manufacturer,
                    Model:
                        KnownOrNull(
                            resolved.DiagnosticVehicle.Model),
                    Year:
                        KnownOrNull(
                            resolved.DiagnosticVehicle.Year),
                    EcuModel: null,
                    EcuType: null,
                    AllowManufacturerSpecific: true,
                    ManufacturerContextVerified: true);
            }
        }

        // Before VIN confirmation the user's brand selection can be used as
        // a routing hint for text lookup. It is deliberately marked unverified.
        // If a later VIN conflicts, the branch above blocks this hint.
        var selected =
            _brands.Current;

        if (!selected.Id.Equals(
                "other",
                StringComparison.OrdinalIgnoreCase))
        {
            return new DtcLookupContext(
                Manufacturer:
                    selected.DisplayName,
                AllowManufacturerSpecific: true,
                ManufacturerContextVerified: false);
        }

        return new DtcLookupContext(
            AllowManufacturerSpecific: false);
    }

    // ============================================================
    // CANDIDATE RANKING
    // ============================================================

    private DtcRow? ChooseBestCandidate(
        IReadOnlyList<DtcRow> rows,
        DtcLookupContext context)
    {
        var manufacturerCandidates =
            BuildManufacturerCandidates(
                context.Manufacturer);

        return rows
            .Select(
                row => new
                {
                    Row = row,
                    Score = ScoreCandidate(
                        row,
                        context,
                        manufacturerCandidates)
                })
            .Where(item => item.Score >= 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(
                item => item.Row.LookupPriority)
            .ThenByDescending(
                item => item.Row.EvidenceQuality)
            .ThenBy(item => item.Row.Id)
            .Select(item => item.Row)
            .FirstOrDefault();
    }

    private int ScoreCandidate(
        DtcRow row,
        DtcLookupContext context,
        IReadOnlyList<ManufacturerCandidate> manufacturers)
    {
        var scope =
            row.Scope?.Trim().ToLowerInvariant()
            ?? string.Empty;

        if (scope == "generic")
        {
            return 1000 +
                   row.LookupPriority +
                   row.EvidenceQuality;
        }

        if (!context.AllowManufacturerSpecific ||
            manufacturers.Count == 0)
        {
            return -1;
        }

        var manufacturerScore =
            GetManufacturerMatchScore(
                row,
                manufacturers);

        if (manufacturerScore < 0)
            return -1;

        if (scope == "oem")
        {
            return 3000 +
                   manufacturerScore +
                   row.LookupPriority +
                   row.EvidenceQuality;
        }

        if (scope == "ecu")
        {
            var ecuScore =
                GetEcuMatchScore(
                    row,
                    context);

            if (ecuScore < 0)
                return -1;

            return 5000 +
                   manufacturerScore +
                   ecuScore +
                   row.LookupPriority +
                   row.EvidenceQuality;
        }

        // "unscoped" / OTHER definitions are intentionally not promoted
        // to a trusted result. If there is no verified context, return null
        // rather than inventing applicability.
        return -1;
    }

    private int GetManufacturerMatchScore(
        DtcRow row,
        IReadOnlyList<ManufacturerCandidate> candidates)
    {
        var rowNames =
            new[]
            {
                row.Manufacturer,
                row.SourceManufacturer,
                row.EcuManufacturer
            }
            .Where(
                value =>
                    !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        var best = -1;

        foreach (var rowName in rowNames)
        {
            var canonicalRow =
                CanonicalizeManufacturer(
                    rowName);

            foreach (var candidate in candidates)
            {
                if (string.Equals(
                        canonicalRow,
                        candidate.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    best =
                        Math.Max(
                            best,
                            candidate.Weight);
                }
            }
        }

        return best;
    }

    private static int GetEcuMatchScore(
        DtcRow row,
        DtcLookupContext context)
    {
        var rowModel =
            NormalizeComparable(
                row.EcuModel);

        var contextModel =
            NormalizeComparable(
                context.EcuModel);

        if (!string.IsNullOrWhiteSpace(rowModel) &&
            !string.IsNullOrWhiteSpace(contextModel))
        {
            if (rowModel.Equals(
                    contextModel,
                    StringComparison.Ordinal))
            {
                return 500;
            }

            if (rowModel.Contains(
                    contextModel,
                    StringComparison.Ordinal) ||
                contextModel.Contains(
                    rowModel,
                    StringComparison.Ordinal))
            {
                return 420;
            }
        }

        var rowType =
            NormalizeEcuType(
                row.EcuType);

        var contextType =
            NormalizeEcuType(
                context.EcuType);

        if (!string.IsNullOrWhiteSpace(rowType) &&
            !string.IsNullOrWhiteSpace(contextType) &&
            rowType.Equals(
                contextType,
                StringComparison.Ordinal))
        {
            // If the source has a concrete ECU model but we only know a broad
            // type, do not claim that model-specific definition applies.
            return string.IsNullOrWhiteSpace(rowModel)
                ? 280
                : -1;
        }

        // An ECU-specific source must not win without ECU identity evidence.
        return -1;
    }

    private IReadOnlyList<ManufacturerCandidate>
        BuildManufacturerCandidates(
            string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
            return Array.Empty<ManufacturerCandidate>();

        var result =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        void Add(
            string? value,
            int weight)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var canonical =
                CanonicalizeManufacturer(value);

            if (string.IsNullOrWhiteSpace(canonical) ||
                canonical.Equals(
                    "GENERIC",
                    StringComparison.OrdinalIgnoreCase) ||
                canonical.Equals(
                    "OTHER",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.TryGetValue(
                    canonical,
                    out var current) ||
                weight > current)
            {
                result[canonical] =
                    weight;
            }
        }

        Add(
            manufacturer,
            500);

        var primary =
            CanonicalizeManufacturer(
                manufacturer);

        // Parent-group fallbacks are lower priority than an exact brand.
        // These groups are explicitly represented in the master data.
        if (primary is
            "CHEVROLET" or
            "GMC" or
            "BUICK" or
            "CADILLAC" or
            "PONTIAC" or
            "SATURN" or
            "OLDSMOBILE" or
            "GEO")
        {
            Add(
                "GM",
                300);
        }

        if (primary is
            "PSA/STELLANTIS" or
            "PEUGEOT" or
            "CITROEN" or
            "CITROËN" or
            "DS" or
            "OPEL" or
            "VAUXHALL")
        {
            Add(
                "PSA/STELLANTIS",
                300);
        }

        return result
            .Select(
                pair =>
                    new ManufacturerCandidate(
                        pair.Key,
                        pair.Value))
            .OrderByDescending(
                item => item.Weight)
            .ToArray();
    }

    private string CanonicalizeManufacturer(
        string value)
    {
        var trimmed =
            value.Trim();

        if (_manufacturerAliases.TryGetValue(
                trimmed,
                out var direct))
        {
            return direct.Trim().ToUpperInvariant();
        }

        var upper =
            trimmed.ToUpperInvariant();

        if (_manufacturerAliases.TryGetValue(
                upper,
                out var normalized))
        {
            return normalized.Trim().ToUpperInvariant();
        }

        // Small UI/display aliases that are not necessarily present
        // in every database build.
        return upper switch
        {
            "MB" => "MERCEDES-BENZ",
            "MERCEDES" => "MERCEDES-BENZ",
            "CHEVY" => "CHEVROLET",
            "VW" => "VOLKSWAGEN",
            "VAG" => "VOLKSWAGEN",
            _ => upper
        };
    }

    private static string NormalizeEcuType(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized =
            NormalizeComparable(value);

        if (ContainsAny(
                normalized,
                "ABS",
                "ESP",
                "ESC"))
        {
            return "ABSESC";
        }

        if (ContainsAny(
                normalized,
                "AIRBAG",
                "SRS"))
        {
            return "SRSAIRBAG";
        }

        if (ContainsAny(
                normalized,
                "ECM",
                "PCM",
                "ENGINE"))
        {
            return "ECMENGINE";
        }

        if (ContainsAny(
                normalized,
                "TCM",
                "TRANSMISSION",
                "GEARBOX"))
        {
            return "TCMTRANSMISSION";
        }

        if (ContainsAny(
                normalized,
                "BCM",
                "BODY"))
        {
            return "BCMBODY";
        }

        if (ContainsAny(
                normalized,
                "EPS",
                "STEERING"))
        {
            return "EPSSTEERING";
        }

        if (ContainsAny(
                normalized,
                "BMS",
                "BATTERY"))
        {
            return "BMSBATTERY";
        }

        return normalized;
    }

    private static bool ContainsAny(
        string source,
        params string[] values)
    {
        return values.Any(
            value =>
                source.Contains(
                    value,
                    StringComparison.Ordinal));
    }

    // ============================================================
    // SAFE LOCALIZATION
    // ============================================================

    private async Task<TranslationRow?>
        ResolveSafeGeorgianTranslationAsync(
            SQLiteAsyncConnection db,
            DtcRow row)
    {
        // Best case: translation belongs to the exact selected definition.
        var direct =
            await db.QueryAsync<TranslationRow>(
                    """
                    SELECT
                        title AS Title,
                        description AS Description,
                        source_description_en AS SourceDescriptionEn,
                        translation_status AS TranslationStatus
                    FROM dtc_translations
                    WHERE dtc_definition_id = ?
                      AND language = 'ka'
                    ORDER BY id
                    LIMIT 1;
                    """,
                    row.Id)
                .ConfigureAwait(false);

        if (direct.Count > 0)
            return direct[0];

        // A translation from another source is accepted only when its stored
        // English source text exactly matches the selected English definition.
        // This prevents a conflicting old translation from being attached to
        // a newer/corrected English meaning for the same DTC context.
        var exact =
            await db.QueryAsync<TranslationRow>(
                    """
                    SELECT
                        t.title AS Title,
                        t.description AS Description,
                        t.source_description_en AS SourceDescriptionEn,
                        t.translation_status AS TranslationStatus
                    FROM dtc_translations t
                    JOIN dtc_definitions td
                      ON td.id = t.dtc_definition_id
                    WHERE td.context_key = ?
                      AND t.language = 'ka'
                      AND (
                            lower(trim(COALESCE(t.source_description_en, ''))) =
                            lower(trim(COALESCE(?, '')))
                         OR lower(trim(COALESCE(t.source_description_en, ''))) =
                            lower(trim(COALESCE(?, '')))
                      )
                    ORDER BY
                        td.lookup_priority DESC,
                        td.evidence_quality DESC,
                        td.id ASC
                    LIMIT 1;
                    """,
                    row.ContextKey,
                    row.DefinitionEn,
                    row.DescriptionEn)
                .ConfigureAwait(false);

        if (exact.Count > 0)
            return exact[0];

        // Same code and scope, but the Georgian text is attached to a sibling
        // definition whose English wording differs slightly (e.g. "Bank 1
        // Sensor 3" vs "(Bank 1 Sensor 3)"). Prefer the same context, then
        // the closest English text, then the highest-priority definition.
        var sibling =
            await db.QueryAsync<TranslationRow>(
                    """
                    SELECT
                        t.title AS Title,
                        t.description AS Description,
                        t.source_description_en AS SourceDescriptionEn,
                        t.translation_status AS TranslationStatus
                    FROM dtc_translations t
                    JOIN dtc_definitions td
                      ON td.id = t.dtc_definition_id
                    WHERE upper(td.code) = upper(?)
                      AND td.scope = ?
                      AND t.language = 'ka'
                      AND trim(COALESCE(t.title, '')) <> ''
                    ORDER BY
                        (td.context_key = ?) DESC,
                        (lower(trim(COALESCE(td.definition_en, ''))) = lower(trim(COALESCE(?, '')))) DESC,
                        td.lookup_priority DESC,
                        td.evidence_quality DESC,
                        td.id ASC
                    LIMIT 1;
                    """,
                    row.Code,
                    row.Scope,
                    row.ContextKey,
                    row.DefinitionEn)
                .ConfigureAwait(false);

        return sibling.FirstOrDefault();
    }

    // ============================================================
    // BUILD ENTRY
    // ============================================================

    private DtcCatalogEntry BuildEntry(
        DtcRow row,
        TranslationRow? translation,
        DtcLookupContext context) =>
        DtcGeorgianTextRepair.Apply(
            BuildRawEntry(row, translation, context));

    private DtcCatalogEntry BuildRawEntry(
        DtcRow row,
        TranslationRow? translation,
        DtcLookupContext context)
    {
        var flags =
            ParseJsonObject(
                row.FlagsJson);

        var repairs =
            ParseJsonObject(
                row.RepairsJson);

        var causesEn =
            ExtractEnglishCauses(
                row.CausesJson);

        var symptomsEn =
            ExtractEnglishSymptoms(
                row.SymptomsJson);

        var causesKa =
            ExtractLocalizedListFromRawJson(
                row.RawJson,
                "common_causes",
                "ka");

        var symptomsKa =
            ExtractLocalizedListFromRawJson(
                row.RawJson,
                "symptoms",
                "ka");

        return new DtcCatalogEntry
        {
            Code =
                row.Code ?? string.Empty,

            Category =
                row.Category ?? string.Empty,

            TitleEn =
                FirstNonEmpty(
                    row.DefinitionEn,
                    row.DescriptionEn,
                    row.Code),

            DescriptionEn =
                FirstNonEmpty(
                    row.DescriptionEn,
                    row.DefinitionEn,
                    row.Code),

            CausesEn =
                causesEn,

            SymptomsEn =
                symptomsEn,

            TitleKa =
                translation?.Title
                ?? string.Empty,

            DescriptionKa =
                translation?.Description
                ?? string.Empty,

            CausesKa =
                causesKa,

            SymptomsKa =
                symptomsKa,

            Mil =
                ParseBool(
                    row.MilRaw) ||
                JsonBool(
                    flags,
                    "mil"),

            EmissionsRelevant =
                JsonBool(
                    flags,
                    "emissions_relevant"),

            LimpMode =
                JsonBool(
                    flags,
                    "limp_mode_possible"),

            Difficulty =
                JsonString(
                    repairs,
                    "difficulty"),

            Source =
                BuildSourceText(row),

            Scope =
                row.Scope ?? string.Empty,

            Manufacturer =
                FirstNonEmpty(
                    row.Manufacturer,
                    row.SourceManufacturer,
                    row.EcuManufacturer),

            EcuModel =
                row.EcuModel ?? string.Empty,

            EcuType =
                row.EcuType ?? string.Empty,

            ResolutionStatus =
                row.ResolutionStatus ?? string.Empty,

            ManufacturerContextVerified =
                context.ManufacturerContextVerified
        };
    }

    private static string BuildSourceText(
        DtcRow row)
    {
        var source =
            FirstNonEmpty(
                row.SourceName,
                "DtcCatalog.sqlite");

        if (!string.IsNullOrWhiteSpace(
                row.SourcePath))
        {
            return $"{source} · {row.SourcePath}";
        }

        return source;
    }

    // ============================================================
    // JSON HELPERS
    // ============================================================

    private static IReadOnlyList<string>
        ExtractEnglishCauses(
            string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var document =
                JsonDocument.Parse(json);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var result =
                new List<string>();

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var value =
                    ExtractLocalizedJsonText(
                        item,
                        "en");

                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);

                if (result.Count >= 8)
                    break;
            }

            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string>
        ExtractEnglishSymptoms(
            string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var document =
                JsonDocument.Parse(json);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var result =
                new List<string>();

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var value =
                    ExtractLocalizedJsonText(
                        item,
                        "en");

                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);

                if (result.Count >= 8)
                    break;
            }

            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? ExtractLocalizedJsonText(
        JsonElement element,
        string language)
    {
        if (element.ValueKind ==
            JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in new[]
                 {
                     language,
                     $"label_{language}",
                     "label_en",
                     "label",
                     "description",
                     "text",
                     "name"
                 })
        {
            if (!element.TryGetProperty(
                    key,
                    out var property))
            {
                continue;
            }

            if (property.ValueKind ==
                JsonValueKind.String)
            {
                return property.GetString();
            }

            if (property.ValueKind ==
                    JsonValueKind.Object &&
                property.TryGetProperty(
                    language,
                    out var localized) &&
                localized.ValueKind ==
                    JsonValueKind.String)
            {
                return localized.GetString();
            }
        }

        return null;
    }

    private static IReadOnlyList<string>
        ExtractLocalizedListFromRawJson(
            string? rawJson,
            string propertyName,
            string language)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return Array.Empty<string>();

        try
        {
            using var document =
                JsonDocument.Parse(rawJson);

            if (document.RootElement.ValueKind !=
                    JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(
                    propertyName,
                    out var array) ||
                array.ValueKind !=
                    JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var result =
                new List<string>();

            foreach (var item in array.EnumerateArray())
            {
                var value =
                    ExtractLocalizedJsonText(
                        item,
                        language);

                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);

                if (result.Count >= 8)
                    break;
            }

            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static JsonElement? ParseJsonObject(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document =
                JsonDocument.Parse(json);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Object)
            {
                return null;
            }

            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static bool JsonBool(
        JsonElement? element,
        string key)
    {
        if (element is null ||
            !element.Value.TryGetProperty(
                key,
                out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number =>
                value.TryGetInt32(out var number) &&
                number != 0,
            JsonValueKind.String =>
                ParseBool(value.GetString()),
            _ => false
        };
    }

    private static string JsonString(
        JsonElement? element,
        string key)
    {
        if (element is null ||
            !element.Value.TryGetProperty(
                key,
                out var value))
        {
            return string.Empty;
        }

        return value.ValueKind ==
               JsonValueKind.String
            ? value.GetString()
              ?? string.Empty
            : value.ToString();
    }

    private static bool ParseBool(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return bool.TryParse(
                   value,
                   out var parsed)
               ? parsed
               : value.Trim() is "1" or "yes" or "YES";
    }

    // ============================================================
    // TEXT HELPERS
    // ============================================================

    private static string NormalizeCode(
        string code)
    {
        return code
            .Trim()
            .ToUpperInvariant();
    }

    private static string? KnownOrNull(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals(
                "Unknown",
                StringComparison.OrdinalIgnoreCase) ||
            value.Equals(
                "უცნობია",
                StringComparison.OrdinalIgnoreCase) ||
            value == "—" ||
            value.Equals(
                "მიუწვდომელია",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    private static string NormalizeComparable(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed =
            value.Normalize(
                NormalizationForm.FormD);

        var builder =
            new StringBuilder(
                decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(
                    character) ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
                builder.Append(
                    char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private static string FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(
                   value =>
                       !string.IsNullOrWhiteSpace(value))
               ?? string.Empty;
    }

    // ============================================================
    // SQL
    // ============================================================

    private const string CandidateSql =
        """
        SELECT
            d.id AS Id,
            d.context_key AS ContextKey,
            d.code AS Code,
            d.category AS Category,
            d.scope AS Scope,
            d.manufacturer AS Manufacturer,
            d.source_manufacturer AS SourceManufacturer,
            d.ecu_manufacturer AS EcuManufacturer,
            d.ecu_model AS EcuModel,
            d.ecu_type AS EcuType,
            d.definition_en AS DefinitionEn,
            COALESCE(d.description_en, '') AS DescriptionEn,
            d.system AS System,
            d.subsystem AS Subsystem,
            d.severity AS Severity,
            d.mil AS MilRaw,
            d.is_generic AS IsGeneric,
            d.causes_json AS CausesJson,
            d.repairs_json AS RepairsJson,
            d.evidence_json AS EvidenceJson,
            d.raw_json AS RawJson,
            d.source_path AS SourcePath,
            d.lookup_priority AS LookupPriority,
            d.evidence_quality AS EvidenceQuality,
            d.affected_components_json AS AffectedComponentsJson,
            d.symptoms_json AS SymptomsJson,
            d.flags_json AS FlagsJson,
            c.resolution_status AS ResolutionStatus,
            sc.name AS SourceName
        FROM dtc_contexts c
        JOIN dtc_definitions d
          ON d.id = c.preferred_record_id
        LEFT JOIN source_catalogs sc
          ON sc.id = d.source_catalog_id
        WHERE c.code = ?;
        """;

    private const string AllCodesSql =
        """
        WITH ranked AS
        (
            SELECT
                d.id AS Id,
                d.context_key AS ContextKey,
                d.code AS Code,
                d.category AS Category,
                d.scope AS Scope,
                d.manufacturer AS Manufacturer,
                d.source_manufacturer AS SourceManufacturer,
                d.ecu_manufacturer AS EcuManufacturer,
                d.ecu_model AS EcuModel,
                d.ecu_type AS EcuType,
                d.definition_en AS DefinitionEn,
                COALESCE(d.description_en, '') AS DescriptionEn,
                d.system AS System,
                d.subsystem AS Subsystem,
                d.severity AS Severity,
                d.mil AS MilRaw,
                d.is_generic AS IsGeneric,
                d.causes_json AS CausesJson,
                d.repairs_json AS RepairsJson,
                d.evidence_json AS EvidenceJson,
                d.raw_json AS RawJson,
                d.source_path AS SourcePath,
                d.lookup_priority AS LookupPriority,
                d.evidence_quality AS EvidenceQuality,
                d.affected_components_json AS AffectedComponentsJson,
                d.symptoms_json AS SymptomsJson,
                d.flags_json AS FlagsJson,
                c.resolution_status AS ResolutionStatus,
                sc.name AS SourceName,
                ROW_NUMBER() OVER
                (
                    PARTITION BY d.code
                    ORDER BY
                        CASE d.scope
                            WHEN 'generic' THEN 0
                            WHEN 'oem' THEN 1
                            ELSE 2
                        END,
                        d.lookup_priority DESC,
                        d.evidence_quality DESC,
                        d.id ASC
                ) AS RowNumber
            FROM dtc_contexts c
            JOIN dtc_definitions d
              ON d.id = c.preferred_record_id
            LEFT JOIN source_catalogs sc
              ON sc.id = d.source_catalog_id
            WHERE d.scope IN ('generic', 'oem')
        )
        SELECT
            Id,
            ContextKey,
            Code,
            Category,
            Scope,
            Manufacturer,
            SourceManufacturer,
            EcuManufacturer,
            EcuModel,
            EcuType,
            DefinitionEn,
            DescriptionEn,
            System,
            Subsystem,
            Severity,
            MilRaw,
            IsGeneric,
            CausesJson,
            RepairsJson,
            EvidenceJson,
            RawJson,
            SourcePath,
            LookupPriority,
            EvidenceQuality,
            AffectedComponentsJson,
            SymptomsJson,
            FlagsJson,
            ResolutionStatus,
            SourceName
        FROM ranked
        WHERE RowNumber = 1
        ORDER BY Code;
        """;

    // ============================================================
    // SQLITE DTOs
    // ============================================================

    private sealed class DtcRow
    {
        public long Id { get; set; }
        public string ContextKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string? Manufacturer { get; set; }
        public string? SourceManufacturer { get; set; }
        public string? EcuManufacturer { get; set; }
        public string? EcuModel { get; set; }
        public string? EcuType { get; set; }
        public string DefinitionEn { get; set; } = string.Empty;
        public string DescriptionEn { get; set; } = string.Empty;
        public string? System { get; set; }
        public string? Subsystem { get; set; }
        public string? Severity { get; set; }
        public string? MilRaw { get; set; }
        public int IsGeneric { get; set; }
        public string CausesJson { get; set; } = "[]";
        public string RepairsJson { get; set; } = "[]";
        public string EvidenceJson { get; set; } = "[]";
        public string? RawJson { get; set; }
        public string? SourcePath { get; set; }
        public int LookupPriority { get; set; }
        public int EvidenceQuality { get; set; }
        public string? AffectedComponentsJson { get; set; }
        public string? SymptomsJson { get; set; }
        public string? FlagsJson { get; set; }
        public string? ResolutionStatus { get; set; }
        public string? SourceName { get; set; }
    }

    private sealed class TranslationRow
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? SourceDescriptionEn { get; set; }
        public string? TranslationStatus { get; set; }
    }

    private sealed class ManufacturerAliasRow
    {
        public string Alias { get; set; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
    }

    private sealed class FamilyCountRow
    {
        public string Family { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed record ManufacturerCandidate(
        string Name,
        int Weight);
}

/// <summary>
/// Optional explicit lookup context. The normal GetAsync(code) call builds this
/// automatically from VehicleProfileResolverService + the user's selected brand.
/// </summary>
public sealed record DtcLookupContext(
    string? Manufacturer = null,
    string? Model = null,
    string? Year = null,
    string? EcuModel = null,
    string? EcuType = null,
    bool AllowManufacturerSpecific = true,
    bool ManufacturerContextVerified = false);
