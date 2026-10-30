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

    /// <summary>
    /// Crashes of the last <paramref name="days"/> days grouped by exception
    /// type + first app frame, newest group first (admin page).
    /// </summary>
    public async Task<IReadOnlyList<CrashGroup>> GetGroupsAsync(int days, CancellationToken token)
    {
        var rows = new List<(string Type, string Stack, string App, string Platform, string Os, string Source, bool Fatal, DateTimeOffset At)>();
        using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(token);
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT ExceptionType, StackTrace, AppVersion, Platform, OsVersion, Source, Fatal, ReceivedAt " +
                "FROM CrashReports WHERE ReceivedAt >= $from ORDER BY ReceivedAt DESC LIMIT 2000;";
            command.Parameters.AddWithValue("$from", DateTimeOffset.UtcNow.AddDays(-days).ToString("O"));
            using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt64(6) != 0,
                    DateTimeOffset.TryParse(reader.GetString(7), out var at) ? at : DateTimeOffset.MinValue));
            }
        }

        return rows
            .GroupBy(row => (row.Type, Frame: FirstAppFrame(row.Stack)))
            .Select(group =>
            {
                var latest = group.First();
                return new CrashGroup(
                    group.Key.Type,
                    group.Key.Frame,
                    group.Count(),
                    group.Count(row => row.Fatal),
                    latest.At,
                    group.Select(row => row.App).Distinct().OrderByDescending(v => v).Take(4).ToArray(),
                    group.Select(row => row.Platform).Distinct().ToArray(),
                    latest.Os,
                    latest.Source,
                    latest.Stack);
            })
            .OrderByDescending(group => group.LastAt)
            .ToArray();
    }

    /// <summary>Owner/admin delete: removes every stored crash whose type and first app-code frame match this group.</summary>
    public async Task<int> DeleteGroupAsync(string exceptionType, string frame, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);

            var ids = new List<string>();
            using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT ReportId, StackTrace FROM CrashReports WHERE ExceptionType=$type";
                select.Parameters.AddWithValue("$type", exceptionType);
                using var reader = await select.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    if (FirstAppFrame(reader.GetString(1)) == frame)
                        ids.Add(reader.GetString(0));
                }
            }
            if (ids.Count == 0)
                return 0;

            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM CrashReports WHERE ReportId IN (" +
                string.Join(",", ids.Select((_, i) => "$p" + i)) + ")";
            for (var i = 0; i < ids.Count; i++)
                delete.Parameters.AddWithValue("$p" + i, ids[i]);
            return await delete.ExecuteNonQueryAsync(token);
        }
        finally { _gate.Release(); }
    }

    /// <summary>First stack frame from the app's own code (CarScanner.*), else the first frame.</summary>
    public static string FirstAppFrame(string stack)
    {
        var lines = (stack ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("at ", StringComparison.Ordinal))
            .ToArray();
        var own = lines.FirstOrDefault(line => line.Contains("CarScanner.", StringComparison.Ordinal));
        var frame = own ?? lines.FirstOrDefault() ?? string.Empty;
        return frame.Length > 220 ? frame[..220] : frame;
    }
}

public sealed record CrashGroup(
    string ExceptionType,
    string Frame,
    int Count,
    int FatalCount,
    DateTimeOffset LastAt,
    IReadOnlyList<string> AppVersions,
    IReadOnlyList<string> Platforms,
    string OsVersion,
    string Source,
    string SampleStack);

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
