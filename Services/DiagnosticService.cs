using CarScanner.Helpers;
using CarScanner.Models;
using CarScanner.Services.Localization;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services;

public sealed class DiagnosticService
{
    private readonly OBDService _obd;
    private readonly ILogger<DiagnosticService> _logger;
    private readonly DtcCatalogService _catalog;

    public DiagnosticService(
        OBDService obd,
        ILogger<DiagnosticService> logger,
        DtcCatalogService catalog)
    {
        _obd = obd;
        _logger = logger;
        _catalog = catalog;
    }

    // ============================================================
    // DTC SCAN
    // ============================================================

    public Task<List<ErrorCode>> ScanForErrorsAsync(
        CancellationToken token = default)
        => ScanForErrorsAsync(progress: null, token: token);

    public async Task<List<ErrorCode>> ScanForErrorsAsync(
        IProgress<DtcScanProgress>? progress,
        CancellationToken token = default)
    {
        // ------------------------------------------------------------
        // READ REAL DTCs FROM VEHICLE
        // ------------------------------------------------------------

        progress?.Report(new DtcScanProgress(40, "ECU-დან შენახული კოდების წაკითხვა…"));
        var stored =
            await _obd.ReadStoredDtcAsync(token);

        progress?.Report(new DtcScanProgress(50, "ECU-დან Pending კოდების წაკითხვა…"));
        var pending =
            await _obd.ReadPendingDtcAsync(token);

        progress?.Report(new DtcScanProgress(75, "ECU-დან Permanent კოდების წაკითხვა…"));
        var permanent =
            await _obd.ReadPermanentDtcAsync(token);

        progress?.Report(new DtcScanProgress(85, "DTC შედეგების დამუშავება…"));

        var all =
            stored
                .Concat(pending)
                .Concat(permanent)
                .ToList();

        // ------------------------------------------------------------
        // NORMALIZE + GROUP DUPLICATES
        // ------------------------------------------------------------

        var grouped =
            all
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Code))
                .GroupBy(
                    x => NormalizeCode(x.Code),
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        _logger.LogInformation(
            "DTC scan completed. Stored={Stored}, Pending={Pending}, Permanent={Permanent}, Unique={Unique}",
            stored.Count,
            pending.Count,
            permanent.Count,
            grouped.Count);

        var result =
            new List<ErrorCode>();

        if (grouped.Count == 0)
        {
            progress?.Report(new DtcScanProgress(100, "ECU კოდების წაკითხვა დასრულდა."));
            return result;
        }

        // Catalog is local enrichment, not part of the ECU scan. Loading it
        // only after an actual code is found keeps a clean vehicle scan fast.
        progress?.Report(new DtcScanProgress(90, "ნაპოვნი კოდების ახსნის მომზადება…"));
        try
        {
            // YAML parsing is CPU-heavy on the first scan. Keep it off the
            // Android UI thread so the system never shows an ANR dialog.
            await Task.Run(() => _catalog.EnsureLoadedAsync(token), token)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "DTC catalog loaded. Count={Count}",
                _catalog.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "DTC catalog could not be loaded. Continuing with OBD scan.");
        }

        // ------------------------------------------------------------
        // ENRICH EVERY DTC
        // ------------------------------------------------------------

        for (var groupIndex = 0; groupIndex < grouped.Count; groupIndex++)
        {
            token.ThrowIfCancellationRequested();

            var group = grouped[groupIndex];
            var first = group.First();

            var code =
                NormalizeCode(first.Code);

            // --------------------------------------------------------
            // Start from existing local DTC database.
            // --------------------------------------------------------

            var item =
                ErrorCodeDatabase.GetOrDefault(code);

            item.Code =
                code;

            // --------------------------------------------------------
            // Look up offline catalog.
            // --------------------------------------------------------

            DtcCatalogEntry? catalog = null;

            try
            {
                catalog =
                    await _catalog.GetAsync(
                        code,
                        token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "DTC catalog lookup failed for {Code}",
                    code);
            }

            if (catalog != null)
            {
                ApplyCatalogData(
                    item,
                    catalog,
                    first.Description);
            }
            else
            {
                ApplyUnknownDtcData(
                    item,
                    first.Description,
                    code);
            }

            // --------------------------------------------------------
            // STATUS
            // --------------------------------------------------------

            item.Status =
                BuildStatus(group);

            // Make sure the UI refreshes all language-dependent fields.
            item.OnChanged();

            result.Add(item);

            var percent = 90 + (int)Math.Round((groupIndex + 1) * 10d / grouped.Count);
            progress?.Report(new DtcScanProgress(percent, $"კოდის ახსნა: {code}"));
        }

        return result;
    }


    // ============================================================
    // APPLY CATALOG DATA
    // ============================================================

    private static void ApplyCatalogData(
        ErrorCode item,
        DtcCatalogEntry catalog,
        string? obdDescription)
    {
        // ------------------------------------------------------------
        // ENGLISH
        // ------------------------------------------------------------

        item.TitleEn =
            FirstNonEmpty(
                catalog.TitleEn,
                obdDescription,
                $"DTC {catalog.Code}");

        item.DescriptionEn =
            FirstNonEmpty(
                catalog.DescriptionEn,
                obdDescription,
                item.TitleEn);

        item.PossibleCausesEn =
            catalog.CausesEn.Count > 0
                ? string.Join(
                    "; ",
                    catalog.CausesEn)
                : "The cause must be confirmed with the appropriate diagnostic test.";

        item.SymptomsEn =
            catalog.SymptomsEn.Count > 0
                ? string.Join(
                    "; ",
                    catalog.SymptomsEn)
                : string.Empty;


        // ------------------------------------------------------------
        // GEORGIAN
        // ------------------------------------------------------------

        item.TitleKa =
            FirstNonEmpty(
                catalog.TitleKa,
                catalog.TitleEn,
                $"DTC {catalog.Code}");

        item.DescriptionKa =
            FirstNonEmpty(
                catalog.DescriptionKa,
                catalog.DescriptionEn,
                "ამ კოდის დამატებითი ინფორმაცია ოფლაინ კატალოგში ვერ მოიძებნა.");

        item.PossibleCausesKa =
            catalog.CausesKa.Count > 0
                ? string.Join(
                    "; ",
                    catalog.CausesKa)
                : catalog.CausesEn.Count > 0
                    ? string.Join(
                        "; ",
                        catalog.CausesEn)
                    : "მიზეზი უნდა დადასტურდეს შესაბამისი დიაგნოსტიკური ტესტით.";

        item.SymptomsKa =
            catalog.SymptomsKa.Count > 0
                ? string.Join(
                    "; ",
                    catalog.SymptomsKa)
                : string.Empty;


        // ------------------------------------------------------------
        // SEVERITY
        // ------------------------------------------------------------

        item.ChecksKa =
            catalog.ChecksKa.Count > 0
                ? string.Join(
                    "\n",
                    catalog.ChecksKa)
                : string.Empty;

        item.Severity =
            catalog.SeverityLevel > 0
                ? SeverityFromLevel(catalog.SeverityLevel)
                : DetermineSeverity(catalog);
    }

    private static string SeverityFromLevel(int level) =>
        level switch
        {
            >= 3 => "სერიოზული",
            2 => "საშუალო",
            _ => "დაბალი"
        };


    // ============================================================
    // UNKNOWN DTC
    // ============================================================

    private static void ApplyUnknownDtcData(
        ErrorCode item,
        string? obdDescription,
        string code)
    {
        item.TitleEn =
            FirstNonEmpty(
                item.TitleEn,
                obdDescription,
                $"DTC {code}");

        item.DescriptionEn =
            FirstNonEmpty(
                item.DescriptionEn,
                obdDescription,
                $"Diagnostic trouble code {code}.");

        item.PossibleCausesEn =
            FirstNonEmpty(
                item.PossibleCausesEn,
                "The cause must be confirmed with the appropriate diagnostic test.");

        item.SymptomsEn =
            FirstNonEmpty(
                item.SymptomsEn,
                string.Empty);


        // Hand-written Georgian text for generic codes missing from the catalog.
        if (DtcGeorgianTextRepair.IsGenericCode(code) &&
            DtcGeorgianTextRepair.TryGetCurated(code, out var curated))
        {
            item.TitleKa = curated.Title;
            item.DescriptionKa = curated.Description;
            item.PossibleCausesKa = string.Join("; ", curated.Causes);
            item.ChecksKa = string.Join("\n", curated.Checks);
            item.Severity = SeverityFromLevel(curated.Severity);
        }

        item.TitleKa =
            FirstNonEmpty(
                item.TitleKa,
                $"DTC {code}");

        item.DescriptionKa =
            FirstNonEmpty(
                item.DescriptionKa,
                "ამ კოდის დამატებითი ინფორმაცია ოფლაინ კატალოგში ვერ მოიძებნა.");

        item.PossibleCausesKa =
            FirstNonEmpty(
                item.PossibleCausesKa,
                "მიზეზი უნდა დადასტურდეს შესაბამისი დიაგნოსტიკური ტესტით.");

        item.SymptomsKa =
            FirstNonEmpty(
                item.SymptomsKa,
                string.Empty);

        item.Severity =
            FirstNonEmpty(
                item.Severity,
                "უცნობი");
    }


    // ============================================================
    // SEVERITY
    // ============================================================

    private static string DetermineSeverity(
        DtcCatalogEntry catalog)
    {
        if (catalog.LimpMode)
            return "სერიოზული";

        if (catalog.Mil)
            return "მაღალი";

        if (catalog.EmissionsRelevant)
            return "საშუალო";

        return "საშუალო";
    }


    // ============================================================
    // STATUS
    // ============================================================

    private static string BuildStatus(
        IEnumerable<DiagnosticCode> group)
    {
        var statuses =
            group
                .Select(x => x.Status)
                .Distinct()
                .ToList();

        var english =
            IsEnglish();

        return string.Join(
            ", ",
            statuses.Select(
                status =>
                    status switch
                    {
                        DiagnosticCodeStatus.Stored =>
                            english
                                ? "Stored"
                                : "შენახული",

                        DiagnosticCodeStatus.Pending =>
                            english
                                ? "Pending"
                                : "მოლოდინში",

                        DiagnosticCodeStatus.Permanent =>
                            english
                                ? "Permanent"
                                : "მუდმივი",

                        _ =>
                            english
                                ? "Unknown"
                                : "უცნობი"
                    }));
    }


    // ============================================================
    // FREEZE FRAME
    // ============================================================

    public async Task<Dictionary<string, string>>
        ReadCommonFreezeFrameAsync(
            CancellationToken token = default)
    {
        var result =
            new Dictionary<string, string>();

        foreach (var pid in new[]
                 {
                     0x04,
                     0x05,
                     0x0C,
                     0x0D,
                     0x11,
                     0x42
                 })
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var raw =
                    await _obd.ReadFreezeFrameRawAsync(
                        pid,
                        token);

                result[$"PID {pid:X2}"] =
                    raw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Freeze Frame PID {Pid:X2} unavailable",
                    pid);
            }
        }

        return result;
    }


    // ============================================================
    // CLEAR DTC
    // ============================================================

    public Task ClearErrorsAsync(
        CancellationToken token = default)
        => _obd.ClearDtcAsync(token);


    // ============================================================
    // HELPERS
    // ============================================================

    private static string NormalizeCode(
        string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToUpperInvariant();
    }


    private static string FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(
                   value =>
                       !string.IsNullOrWhiteSpace(value))
               ?? string.Empty;
    }


    private static bool IsEnglish()
    {
        var language =
            Preferences.Default.Get(
                LocalizationService.LanguageKey,
                "ქართული");

        return string.Equals(
            language,
            "English",
            StringComparison.OrdinalIgnoreCase);
    }
}
