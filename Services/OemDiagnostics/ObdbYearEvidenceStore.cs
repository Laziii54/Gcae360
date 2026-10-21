namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Loads Resources/Raw/obdb_year_evidence.json once. If the file is missing or unreadable the evidence is empty and the pages
/// behave exactly as they did without it (the catalog's own "debug" flag, no year filtering).
/// </summary>
public static class ObdbYearEvidenceStore
{
    private const string AssetName = "obdb_year_evidence.json";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static ObdbYearEvidence? _loaded;

    public static async Task<ObdbYearEvidence> GetAsync(CancellationToken token = default)
    {
        if (_loaded is not null)
            return _loaded;

        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_loaded is not null)
                return _loaded;

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(AssetName).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                _loaded = ObdbYearEvidence.Parse(await reader.ReadToEndAsync(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _loaded = ObdbYearEvidence.Empty;
            }

            return _loaded;
        }
        finally
        {
            Gate.Release();
        }
    }
}
