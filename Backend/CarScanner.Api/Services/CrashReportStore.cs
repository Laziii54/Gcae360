using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

public sealed class CrashReportStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CrashReportStore(IHostEnvironment env, IConfiguration configuration)
    {
        var path = BackendDataPath.ResolveDatabasePath(env, configuration);
        _connectionString = BackendDataPath.CreateConnectionString(path);
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS CrashReports (
    ReportId TEXT PRIMARY KEY,
    OccurredAt TEXT NOT NULL,
    ReceivedAt TEXT NOT NULL,
    AppVersion TEXT NOT NULL,
    Platform TEXT NOT NULL,
    OsVersion TEXT NOT NULL,
    ExceptionType TEXT NOT NULL,
    StackTrace TEXT NOT NULL,
    Source TEXT NOT NULL,
    Fatal INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_CrashReports_ReceivedAt ON CrashReports(ReceivedAt);
""";
        command.ExecuteNonQuery();
    }

    public async Task SaveAsync(CrashReportRequest report, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using (var cleanup = connection.CreateCommand())
            {
                cleanup.CommandText = """
DELETE FROM CrashReports WHERE ReceivedAt < $cutoff;
DELETE FROM CrashReports WHERE ReportId IN (
    SELECT ReportId FROM CrashReports ORDER BY ReceivedAt DESC LIMIT -1 OFFSET 5000
);
""";
                cleanup.Parameters.AddWithValue(
                    "$cutoff",
                    DateTimeOffset.UtcNow.AddDays(-90).ToString("O"));
                await cleanup.ExecuteNonQueryAsync(token);
            }
            using var command = connection.CreateCommand();
            command.CommandText = """
INSERT OR IGNORE INTO CrashReports
(ReportId,OccurredAt,ReceivedAt,AppVersion,Platform,OsVersion,ExceptionType,StackTrace,Source,Fatal)
VALUES($id,$occurred,$received,$app,$platform,$os,$type,$stack,$source,$fatal);
""";
            command.Parameters.AddWithValue("$id", report.ReportId);
            command.Parameters.AddWithValue("$occurred", report.OccurredAt.ToString("O"));
            command.Parameters.AddWithValue("$received", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$app", report.AppVersion);
            command.Parameters.AddWithValue("$platform", report.Platform);
            command.Parameters.AddWithValue("$os", report.OsVersion);
            command.Parameters.AddWithValue("$type", report.ExceptionType);
            command.Parameters.AddWithValue("$stack", report.StackTrace);
            command.Parameters.AddWithValue("$source", report.Source);
            command.Parameters.AddWithValue("$fatal", report.Fatal ? 1 : 0);
            await command.ExecuteNonQueryAsync(token);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record CrashReportRequest(
    string ReportId,
    DateTimeOffset OccurredAt,
    string AppVersion,
    string Platform,
    string OsVersion,
    string ExceptionType,
    string StackTrace,
    string Source,
    bool Fatal);
