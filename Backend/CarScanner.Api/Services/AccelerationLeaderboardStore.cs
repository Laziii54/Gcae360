using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

public sealed class AccelerationLeaderboardStore
{
    private static readonly Regex VinPattern = new("^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled);
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AccelerationLeaderboardStore(IHostEnvironment env, IConfiguration config)
    {
        _connectionString = BackendDataPath.CreateConnectionString(BackendDataPath.ResolveDatabasePath(env, config));
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"CREATE TABLE IF NOT EXISTS AccelerationLeaderboard (
SubmissionId TEXT NOT NULL, AccountId TEXT NOT NULL, UserName TEXT NOT NULL, VehicleModel TEXT NOT NULL,
VinHash TEXT NOT NULL, DurationSeconds REAL NOT NULL, FinishedAt TEXT NOT NULL, SampleCount INTEGER NOT NULL,
HasVerifiedVideo INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL, Status TEXT NOT NULL DEFAULT 'Pending',
ReviewedAt TEXT NULL, ReviewedBy TEXT NULL, IsDemo INTEGER NOT NULL DEFAULT 0,
PRIMARY KEY(AccountId, SubmissionId));
CREATE INDEX IF NOT EXISTS IX_AccelerationLeaderboard_Time ON AccelerationLeaderboard(DurationSeconds);
CREATE INDEX IF NOT EXISTS IX_AccelerationLeaderboard_Finished ON AccelerationLeaderboard(FinishedAt);";
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Status", "TEXT NOT NULL DEFAULT 'Pending'");
        EnsureColumn(connection, "ReviewedAt", "TEXT NULL");
        EnsureColumn(connection, "ReviewedBy", "TEXT NULL");
        EnsureColumn(connection, "IsDemo", "INTEGER NOT NULL DEFAULT 0");
        SeedDemo(connection);
    }

    public static string? Validate(AccelerationLeaderboardRequest request)
    {
        if (!Guid.TryParseExact(request.SubmissionId, "N", out _)) return "invalid submission";
        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Trim().Length > 40) return "invalid user name";
        if (string.IsNullOrWhiteSpace(request.VehicleModel) || request.VehicleModel.Trim().Length > 100) return "invalid vehicle model";
        if (!request.VinVerified || !VinPattern.IsMatch((request.Vin ?? string.Empty).Trim().ToUpperInvariant())) return "VIN verification required";
        if (!request.SpeedSource.Equals("OBD_PID_010D", StringComparison.Ordinal)) return "ECU speed source required";
        if (!request.TestId.Equals("0-100", StringComparison.OrdinalIgnoreCase) || request.StartSpeedKph is < 0 or > 0.5 || request.TargetSpeedKph is < 99 or > 101) return "only 0-100 is accepted";
        if (request.DurationSeconds is < 1.2 or > 60) return "implausible time";
        if (request.SampleCount is < 5 or > 1000 || request.MaxSpeedKph is < 100 or > 160) return "invalid ECU samples";
        if (request.DistanceMeters is < 5 or > 1500 || request.PeakAcceleration is < 0 or > 30) return "implausible telemetry";
        var finished = request.FinishedAt.ToUniversalTime();
        if (finished < DateTime.UtcNow.AddDays(-30) || finished > DateTime.UtcNow.AddMinutes(10)) return "invalid timestamp";
        return null;
    }

    public async Task SaveAsync(string accountId, AccelerationLeaderboardRequest request, CancellationToken token)
    {
        var vinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("gcar360-ranking:" + request.Vin.Trim().ToUpperInvariant())));
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO AccelerationLeaderboard
(SubmissionId,AccountId,UserName,VehicleModel,VinHash,DurationSeconds,FinishedAt,SampleCount,HasVerifiedVideo,CreatedAt,Status,IsDemo)
VALUES($s,$a,$u,$m,$v,$d,$f,$n,$video,$now,'Pending',0)
ON CONFLICT(AccountId,SubmissionId) DO UPDATE SET UserName=$u,VehicleModel=$m,DurationSeconds=$d,FinishedAt=$f,
SampleCount=$n,HasVerifiedVideo=MAX(HasVerifiedVideo,$video),Status='Pending',ReviewedAt=NULL,ReviewedBy=NULL;";
            command.Parameters.AddWithValue("$s", request.SubmissionId);
            command.Parameters.AddWithValue("$a", accountId);
            command.Parameters.AddWithValue("$u", request.UserName.Trim());
            command.Parameters.AddWithValue("$m", request.VehicleModel.Trim());
            command.Parameters.AddWithValue("$v", vinHash);
            command.Parameters.AddWithValue("$d", Math.Round(request.DurationSeconds, 2));
            command.Parameters.AddWithValue("$f", request.FinishedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$n", request.SampleCount);
            command.Parameters.AddWithValue("$video", request.HasVerifiedVideo ? 1 : 0);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(token);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<AccelerationLeaderboardItem>> GetAsync(CancellationToken token)
    {
        var result = new List<AccelerationLeaderboardItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT SubmissionId,UserName,VehicleModel,DurationSeconds,FinishedAt,HasVerifiedVideo,IsDemo
FROM AccelerationLeaderboard WHERE Status='Approved' ORDER BY DurationSeconds ASC,FinishedAt ASC LIMIT 100";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3),
                DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt32(5) != 0, reader.GetInt32(6) != 0));
        return result;
    }

    public async Task<IReadOnlyList<AccelerationLeaderboardAdminItem>> GetAdminAsync(CancellationToken token)
    {
        var result = new List<AccelerationLeaderboardAdminItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT SubmissionId,AccountId,UserName,VehicleModel,DurationSeconds,FinishedAt,SampleCount,HasVerifiedVideo,Status,IsDemo,CreatedAt
FROM AccelerationLeaderboard ORDER BY CASE Status WHEN 'Pending' THEN 0 WHEN 'Approved' THEN 1 ELSE 2 END,CreatedAt DESC LIMIT 500";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4),
                DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind), reader.GetInt32(6),
                reader.GetInt32(7) != 0, reader.GetString(8), reader.GetInt32(9) != 0,
                DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        return result;
    }

    public async Task<bool> ReviewAsync(string submissionId, string action, string reviewer, CancellationToken token)
    {
        if (submissionId.Length > 64 || action is not ("approve" or "reject" or "delete")) return false;
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = action == "delete"
                ? "DELETE FROM AccelerationLeaderboard WHERE SubmissionId=$id"
                : "UPDATE AccelerationLeaderboard SET Status=$status,ReviewedAt=$at,ReviewedBy=$by WHERE SubmissionId=$id";
            command.Parameters.AddWithValue("$id", submissionId);
            if (action != "delete")
            {
                command.Parameters.AddWithValue("$status", action == "approve" ? "Approved" : "Rejected");
                command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$by", reviewer);
            }
            return await command.ExecuteNonQueryAsync(token) > 0;
        }
        finally { _gate.Release(); }
    }

    private static void EnsureColumn(SqliteConnection connection, string name, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('AccelerationLeaderboard') WHERE name=$name";
        check.Parameters.AddWithValue("$name", name);
        if (Convert.ToInt32(check.ExecuteScalar()) != 0) return;
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE AccelerationLeaderboard ADD COLUMN {name} {definition}";
        alter.ExecuteNonQuery();
    }

    private static void SeedDemo(SqliteConnection connection)
    {
        using (var metadata = connection.CreateCommand())
        {
            metadata.CommandText = @"CREATE TABLE IF NOT EXISTS FeatureSeed (
SeedKey TEXT PRIMARY KEY, SeededAt TEXT NOT NULL);
SELECT COUNT(*) FROM FeatureSeed WHERE SeedKey='acceleration-demo-v1';";
            if (Convert.ToInt32(metadata.ExecuteScalar()) > 0)
                return;
        }

        var demos = new (string Name, string Car, double Time, bool Video)[]
        {
            ("ნიკა", "Chevrolet Corvette C8 2021", 3.12, true), ("გიორგი", "Subaru WRX STI 2019", 4.82, true),
            ("ლუკა", "Toyota GR Supra 2020", 4.35, false), ("დათო", "Chevrolet Camaro SS 2018", 4.41, true),
            ("საბა", "Subaru BRZ 2022", 6.18, false), ("ანა", "Toyota Camry 3.5 2021", 6.24, true),
            ("ირაკლი", "Chevrolet Volt 2017", 7.46, false), ("მარი", "Toyota RAV4 Hybrid 2020", 7.81, true),
            ("ლაშა", "Subaru Forester XT 2017", 7.12, false), ("თემო", "Chevrolet Malibu 2.0T 2019", 6.58, true),
            ("ბექა", "Toyota Prius 2014", 10.21, false), ("სანდრო", "Subaru Outback 2020", 8.74, true),
            ("ნინი", "Toyota Corolla Hybrid 2022", 9.44, false), ("ზურა", "Chevrolet Cruze 1.4T 2018", 8.91, false),
            ("ვატო", "Subaru Impreza 2018", 8.36, true), ("თაკო", "Toyota C-HR Hybrid 2021", 10.74, false),
            ("ალექსი", "Chevrolet Equinox 2.0T 2019", 7.05, true), ("კოტე", "Toyota Highlander Hybrid 2021", 8.18, false),
            ("ელენე", "Subaru Crosstrek 2021", 9.63, true), ("ანდრო", "Toyota Prius Prime 2020", 10.02, false)
        };
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < demos.Length; i++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT OR IGNORE INTO AccelerationLeaderboard
(SubmissionId,AccountId,UserName,VehicleModel,VinHash,DurationSeconds,FinishedAt,SampleCount,HasVerifiedVideo,CreatedAt,Status,ReviewedAt,ReviewedBy,IsDemo)
VALUES($id,'demo',$name,$car,$vin,$time,$finished,30,$video,$created,'Approved',$created,'DEMO SEED',1)";
            command.Parameters.AddWithValue("$id", $"demo-{i + 1:00}");
            command.Parameters.AddWithValue("$name", demos[i].Name);
            command.Parameters.AddWithValue("$car", demos[i].Car);
            command.Parameters.AddWithValue("$vin", $"DEMO{i + 1:00}");
            command.Parameters.AddWithValue("$time", demos[i].Time);
            command.Parameters.AddWithValue("$video", demos[i].Video ? 1 : 0);
            command.Parameters.AddWithValue("$finished", DateTime.UtcNow.AddDays(-(i % 24)).ToString("O"));
            command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = "INSERT INTO FeatureSeed(SeedKey,SeededAt) VALUES('acceleration-demo-v1',$at)";
            mark.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            mark.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}

public sealed record AccelerationLeaderboardRequest(
    string SubmissionId, string UserName, string VehicleModel, string Vin, bool VinVerified,
    string SpeedSource, string TestId, double StartSpeedKph, double TargetSpeedKph,
    double DurationSeconds, int SampleCount, int MaxSpeedKph, double DistanceMeters,
    double PeakAcceleration, DateTime FinishedAt, bool HasVerifiedVideo);

public sealed record AccelerationLeaderboardItem(
    string SubmissionId, string UserName, string VehicleModel, double DurationSeconds,
    DateTime FinishedAt, bool HasVerifiedVideo, bool IsDemo);

public sealed record AccelerationLeaderboardAdminItem(
    string SubmissionId, string AccountId, string UserName, string VehicleModel, double DurationSeconds,
    DateTime FinishedAt, int SampleCount, bool HasVerifiedVideo, string Status, bool IsDemo, DateTime CreatedAt);
