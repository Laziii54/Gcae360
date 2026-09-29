namespace CarScanner.Api.Services;

/// <summary>
/// Resolves one durable data location for all server-side SQLite stores.
/// Azure App Service deployments use the persistent HOME volume instead of
/// the deployable wwwroot directory. Other hosts can set GCS_DATA_DIRECTORY.
/// </summary>
public static class BackendDataPath
{
    private const string DatabaseFileName = "car-scanner-api.db";

    public static string ResolveDatabasePath(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        var configured =
            configuration["Storage:DataDirectory"] ??
            Environment.GetEnvironmentVariable(
                "GCS_DATA_DIRECTORY");

        string directory;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            directory = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(
                    environment.ContentRootPath,
                    configured);
        }
        else
        {
            var azureHome =
                Environment.GetEnvironmentVariable("HOME");
            var isAzureAppService =
                !string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(
                        "WEBSITE_SITE_NAME"));

            directory =
                isAzureAppService &&
                !string.IsNullOrWhiteSpace(azureHome)
                    ? Path.Combine(
                        azureHome,
                        "data",
                        "georgian-car-scanner")
                    : Path.Combine(
                        environment.ContentRootPath,
                        "data");
        }

        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        return Path.Combine(
            directory,
            DatabaseFileName);
    }

    public static string CreateConnectionString(string databasePath) =>
        new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared,
            DefaultTimeout = 15,
            Pooling = true
        }.ToString();
}
