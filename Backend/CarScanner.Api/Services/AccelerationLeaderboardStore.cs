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

    /// <summary>
    /// Example (demo) results are shown only when explicitly enabled with
    /// GCS_ACCELERATION_DEMO=true (or Acceleration:ShowDemo). Production default: off.
    /// </summary>
    private readonly bool _showDemo;

    public AccelerationLeaderboardStore(IHostEnvironment env, IConfiguration config)
    {
        var databasePath = BackendDataPath.ResolveDatabasePath(env, config);
        _connectionString = BackendDataPath.CreateConnectionString(databasePath);
        VideoDirectory = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "acceleration-videos");
        Directory.CreateDirectory(VideoDirectory);
        var demoSetting = config["Acceleration:ShowDemo"] ?? Environment.GetEnvironmentVariable("GCS_ACCELERATION_DEMO");
        _showDemo = bool.TryParse(demoSetting, out var showDemo) && showDemo;
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
        // Several ranked tests: 0–100 km/h, 0–60 mph, ¼ mile.
        EnsureColumn(connection, "TestId", "TEXT NOT NULL DEFAULT '0-100'");
        EnsureColumn(connection, "MaxSpeedKph", "INTEGER NOT NULL DEFAULT 0");
        // Evidence: ECU and GPS speed traces, GPS difference, review video.
        EnsureColumn(connection, "SpeedTrace", "TEXT NULL");
        EnsureColumn(connection, "GpsTrace", "TEXT NULL");
        EnsureColumn(connection, "GpsDiffKph", "REAL NULL");
        EnsureColumn(connection, "VideoUploadedAt", "TEXT NULL");
        if (_showDemo)
            SeedDemo(connection);
    }

    public static string? Validate(AccelerationLeaderboardRequest request)
    {
        if (!Guid.TryParseExact(request.SubmissionId, "N", out _)) return "invalid submission";
        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Trim().Length > 40) return "invalid user name";
        if (string.IsNullOrWhiteSpace(request.VehicleModel) || request.VehicleModel.Trim().Length > 100) return "invalid vehicle model";
        if (!request.VinVerified || !VinPattern.IsMatch((request.Vin ?? string.Empty).Trim().ToUpperInvariant())) return "VIN verification required";
        if (!request.SpeedSource.Equals("OBD_PID_010D", StringComparison.Ordinal)) return "ECU speed source required";
        if (request.StartSpeedKph is < 0 or > 0.5) return "standing start required";
        switch (NormalizeTest(request.TestId))
        {
            case "0-100":
                if (request.TargetSpeedKph is < 99 or > 101) return "invalid target";
                if (request.DurationSeconds is < 1.2 or > 60) return "implausible time";
                if (request.SampleCount is < 5 or > 1000 || request.MaxSpeedKph is < 99 or > 160) return "invalid ECU samples";
                if (request.DistanceMeters is < 5 or > 1500) return "implausible telemetry";
                break;
            case "0-60mph":
                if (request.TargetSpeedKph is < 95.5 or > 97.5) return "invalid target";
                if (request.DurationSeconds is < 1.2 or > 60) return "implausible time";
                if (request.SampleCount is < 5 or > 1000 || request.MaxSpeedKph is < 95 or > 160) return "invalid ECU samples";
                if (request.DistanceMeters is < 5 or > 1500) return "implausible telemetry";
                break;
            case "quarter-mile":
                // ¼ mile = 402.3 m. Fastest street cars run ~8–9 s; trap speed must fit the time.
                if (request.DistanceMeters is < 400 or > 405) return "invalid distance";
                if (request.DurationSeconds is < 6 or > 40) return "implausible time";
                if (request.SampleCount is < 8 or > 1000 || request.MaxSpeedKph is < 60 or > 350) return "invalid ECU samples";
                // Average speed over the run can never exceed the trap speed.
                if (402.336 / request.DurationSeconds * 3.6 > request.MaxSpeedKph + 2) return "implausible telemetry";
                break;
            default:
                return "this test is not ranked";
        }
        if (request.PeakAcceleration is < 0 or > 20) return "implausible telemetry";
        var finished = request.FinishedAt.ToUniversalTime();
        if (finished < DateTime.UtcNow.AddDays(-30) || finished > DateTime.UtcNow.AddMinutes(10)) return "invalid timestamp";
        return null;
    }

    /// <summary>"0-100", "0-60mph" or "quarter-mile" (anything else is returned unchanged).</summary>
    public static string NormalizeTest(string? testId) => (testId ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "0-100" => "0-100",
        "0-60mph" => "0-60mph",
        "quarter-mile" => "quarter-mile",
        var other => other
    };

    /// <summary>Top places that always need a video and a review.</summary>
    public const int ReviewTopPlaces = 10;

    /// <summary>
    /// Until a test category has this many approved results, a GPS-confirmed
    /// run is approved without a video (otherwise the first users would all
    /// wait for a review). Extreme times are still reviewed.
    /// </summary>
    public const int ReviewAfterApproved = 10;

    /// <summary>Uploaded videos and GPS coordinates are deleted this long after the review.</summary>
    public static readonly TimeSpan EvidenceKeep = TimeSpan.FromDays(30);

    /// <summary>
    /// Server-side GPS check of the submitted traces. Null when the run is
    /// confirmed by GPS, else the reason.
    /// </summary>
    public static (string? Error, RunGpsCheckResult? Result) CheckGps(AccelerationLeaderboardRequest request)
    {
        if (request.SpeedTrace is not { Count: >= 5 and <= 1500 } speed ||
            request.GpsTrace is not { Count: <= 3000 } gps)
            return ("gps evidence required", null);
        if (speed.Any(p => p is not { Length: 2 }) || gps.Any(p => p is not { Length: >= 2 and <= 5 }))
            return ("invalid evidence", null);

        var result = RunGpsCheck.Check(
            speed.Select(p => (p[0], p[1])).ToArray(),
            gps.Select(p => (p[0], p[1])).ToArray());
        return result.Status switch
        {
            RunGpsCheck.Passed => (null, result),
            RunGpsCheck.Missing => ("gps missing", result),
            _ => ("gps speed does not match the ECU speed", result)
        };
    }

    /// <summary>Returned by <see cref="SaveAsync"/> when the SubmissionId belongs to another account (→ 409).</summary>
    public const string SubmissionIdTaken = "SUBMISSION_ID_TAKEN";

    /// <summary>
    /// Saves a checked result. Top-10 places and results that are much faster
    /// than the same model get "Pending" (shown as "under review") until the
    /// owner reviews the uploaded video. Returns the stored status.
    /// </summary>
    public async Task<string> SaveAsync(string accountId, AccelerationLeaderboardRequest request, RunGpsCheckResult gps, CancellationToken token)
    {
        var vinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("gcar360-ranking:" + request.Vin.Trim().ToUpperInvariant())));
        var test = NormalizeTest(request.TestId);
        var duration = Math.Round(request.DurationSeconds, 2);
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);

            // A SubmissionId used by ANOTHER account is refused (videos and
            // reviews are keyed by it; see SubmissionIdTaken).
            await using (var taken = connection.CreateCommand())
            {
                taken.CommandText = "SELECT COUNT(*) FROM AccelerationLeaderboard WHERE SubmissionId=$s AND AccountId<>$a";
                taken.Parameters.AddWithValue("$a", accountId);
                taken.Parameters.AddWithValue("$s", request.SubmissionId);
                if (Convert.ToInt32(await taken.ExecuteScalarAsync(token)) > 0)
                    return SubmissionIdTaken;
            }

            // Already reviewed results keep their status.
            await using (var existing = connection.CreateCommand())
            {
                existing.CommandText = "SELECT Status FROM AccelerationLeaderboard WHERE AccountId=$a AND SubmissionId=$s";
                existing.Parameters.AddWithValue("$a", accountId);
                existing.Parameters.AddWithValue("$s", request.SubmissionId);
                if (await existing.ExecuteScalarAsync(token) is string known && known is "Rejected" or "Verified")
                    return known;
            }

            int approvedInTest;
            await using (var approvedCount = connection.CreateCommand())
            {
                approvedCount.CommandText = @"SELECT COUNT(*) FROM AccelerationLeaderboard
WHERE TestId=$test AND IsDemo=0 AND Status IN ('Approved','Verified') AND NOT (AccountId=$a AND SubmissionId=$s)";
                approvedCount.Parameters.AddWithValue("$test", test);
                approvedCount.Parameters.AddWithValue("$a", accountId);
                approvedCount.Parameters.AddWithValue("$s", request.SubmissionId);
                approvedInTest = Convert.ToInt32(await approvedCount.ExecuteScalarAsync(token));
            }

            int faster;
            await using (var place = connection.CreateCommand())
            {
                place.CommandText = @"SELECT COUNT(*) FROM AccelerationLeaderboard
WHERE TestId=$test AND IsDemo=0 AND Status IN ('Approved','Pending','Verified') AND DurationSeconds<$d AND NOT (AccountId=$a AND SubmissionId=$s)";
                place.Parameters.AddWithValue("$test", test);
                place.Parameters.AddWithValue("$d", duration);
                place.Parameters.AddWithValue("$a", accountId);
                place.Parameters.AddWithValue("$s", request.SubmissionId);
                faster = Convert.ToInt32(await place.ExecuteScalarAsync(token));
            }

            var sameModel = new List<double>();
            await using (var model = connection.CreateCommand())
            {
                model.CommandText = @"SELECT DurationSeconds FROM AccelerationLeaderboard
WHERE TestId=$test AND IsDemo=0 AND Status IN ('Approved','Verified') AND VehicleModel=$m";
                model.Parameters.AddWithValue("$test", test);
                model.Parameters.AddWithValue("$m", request.VehicleModel.Trim());
                await using var reader = await model.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                    sameModel.Add(reader.GetDouble(0));
            }

            sameModel.Sort();
            var modelMedian = sameModel.Count >= 3 ? sameModel[sameModel.Count / 2] : (double?)null;
            var extreme = test switch
            {
                "0-100" => duration < 2.5,
                "0-60mph" => duration < 2.4,
                "quarter-mile" => duration < 9.0,
                _ => false
            };
            // The GPS cross-check already passed (CheckGps) before saving.
            var review = approvedInTest < ReviewAfterApproved
                ? extreme
                : faster < ReviewTopPlaces || extreme || (modelMedian is { } median && duration < median * 0.8);
            var status = review ? "Pending" : "Approved";

            // Coordinates are kept only for results that are reviewed.
            var gpsJson = System.Text.Json.JsonSerializer.Serialize(review
                ? (request.GpsTrace ?? Array.Empty<double[]>()).Select(p => p.Select(v => Math.Round(v, 6)).ToArray())
                : (request.GpsTrace ?? Array.Empty<double[]>()).Select(p => new[] { Math.Round(p[0], 2), Math.Round(p[1], 1) }));
            var speedJson = System.Text.Json.JsonSerializer.Serialize((request.SpeedTrace ?? Array.Empty<double[]>()).Select(p => new[] { Math.Round(p[0], 2), Math.Round(p[1], 1) }));

            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO AccelerationLeaderboard
(SubmissionId,AccountId,UserName,VehicleModel,VinHash,DurationSeconds,FinishedAt,SampleCount,HasVerifiedVideo,CreatedAt,Status,IsDemo,TestId,MaxSpeedKph,SpeedTrace,GpsTrace,GpsDiffKph)
VALUES($s,$a,$u,$m,$v,$d,$f,$n,0,$now,$status,0,$test,$max,$speed,$gps,$diff)
ON CONFLICT(AccountId,SubmissionId) DO UPDATE SET UserName=$u,VehicleModel=$m,DurationSeconds=$d,FinishedAt=$f,
SampleCount=$n,TestId=$test,MaxSpeedKph=$max,SpeedTrace=$speed,GpsTrace=$gps,GpsDiffKph=$diff,
Status=CASE WHEN AccelerationLeaderboard.Status='Pending' THEN 'Pending' ELSE $status END
WHERE AccelerationLeaderboard.Status NOT IN ('Rejected','Verified');";
            command.Parameters.AddWithValue("$s", request.SubmissionId);
            command.Parameters.AddWithValue("$a", accountId);
            command.Parameters.AddWithValue("$u", request.UserName.Trim());
            command.Parameters.AddWithValue("$m", request.VehicleModel.Trim());
            command.Parameters.AddWithValue("$v", vinHash);
            command.Parameters.AddWithValue("$d", duration);
            command.Parameters.AddWithValue("$f", request.FinishedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$n", request.SampleCount);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$test", test);
            command.Parameters.AddWithValue("$max", request.MaxSpeedKph);
            command.Parameters.AddWithValue("$speed", speedJson);
            command.Parameters.AddWithValue("$gps", gpsJson);
            command.Parameters.AddWithValue("$diff", gps.MedianDiffKph);
            await command.ExecuteNonQueryAsync(token);
            return status;
        }
        finally { _gate.Release(); }
    }

    // ============================================================
    // VIDEO EVIDENCE
    // ============================================================

    public string VideoDirectory { get; }

    /// <summary>Per-account limits for review videos.</summary>
    public const int MaxVideosPerAccountPerDay = 3;
    public const long MaxVideoBytesPerAccount = 300L * 1024 * 1024;

    /// <summary>Total review videos kept on disk (GCS_VIDEO_DISK_LIMIT_MB, default 5000).</summary>
    public static long VideoDiskLimitBytes =>
        (long.TryParse(Environment.GetEnvironmentVariable("GCS_VIDEO_DISK_LIMIT_MB"), out var mb) && mb > 0 ? mb : 5000) * 1024 * 1024;

    /// <summary>New uploads are refused when the data drive has less free space than this.</summary>
    public const long MinFreeDiskBytes = 2L * 1024 * 1024 * 1024;

    private static string AccountHash(string accountId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("gcar360-video:" + accountId)))[..16];

    /// <summary>Where a new upload is stored: {accountHash}_{submissionId}.mp4 (ids are unique only per account).</summary>
    public string VideoPath(string accountId, string submissionId) =>
        Path.Combine(VideoDirectory, AccountHash(accountId) + "_" + submissionId + ".mp4");

    /// <summary>Old file name (before account-scoped names); read and deleted, never written.</summary>
    private string LegacyVideoPath(string submissionId) => Path.Combine(VideoDirectory, submissionId + ".mp4");

    /// <summary>The existing video file of a result (new name first, then the old one), or null.</summary>
    public string? ExistingVideoPath(string accountId, string submissionId)
    {
        var current = VideoPath(accountId, submissionId);
        if (File.Exists(current))
            return current;
        var legacy = LegacyVideoPath(submissionId);
        return File.Exists(legacy) ? legacy : null;
    }

    private void DeleteVideoFiles(string accountId, string submissionId)
    {
        foreach (var path in new[] { VideoPath(accountId, submissionId), LegacyVideoPath(submissionId) })
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Null when a new upload of <paramref name="size"/> bytes is allowed,
    /// else the reason: per account 3 uploads / day and 300 MB kept; globally
    /// GCS_VIDEO_DISK_LIMIT_MB of videos and 2 GB free on the data drive.
    /// </summary>
    public async Task<string?> CheckVideoQuotaAsync(string accountId, string submissionId, long size, CancellationToken token)
    {
        var ownFiles = new List<string>();
        var uploadsToday = 0;
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT SubmissionId, VideoUploadedAt FROM AccelerationLeaderboard WHERE AccountId=$a AND VideoUploadedAt IS NOT NULL";
            command.Parameters.AddWithValue("$a", accountId);
            await using var reader = await command.ExecuteReaderAsync(token);
            var since = DateTime.UtcNow.AddDays(-1);
            while (await reader.ReadAsync(token))
            {
                var id = reader.GetString(0);
                // A re-upload for the same result replaces the file: not counted twice.
                if (id == submissionId)
                    continue;
                if (DateTime.TryParse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) &&
                    at.ToUniversalTime() > since)
                    uploadsToday++;
                if (ExistingVideoPath(accountId, id) is { } path)
                    ownFiles.Add(path);
            }
        }

        if (uploadsToday >= MaxVideosPerAccountPerDay)
            return "daily video limit reached";

        long ownBytes = 0, allBytes = 0;
        try
        {
            foreach (var path in ownFiles)
                ownBytes += new FileInfo(path).Length;
            foreach (var path in Directory.EnumerateFiles(VideoDirectory))
                allBytes += new FileInfo(path).Length;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (ownBytes + size > MaxVideoBytesPerAccount)
            return "video storage limit for this account reached";
        if (allBytes + size > VideoDiskLimitBytes)
            return "video storage is full";

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(VideoDirectory));
            if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace - size < MinFreeDiskBytes)
                return "server disk is almost full";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Free space unknown (e.g. a network share): the byte limits above still apply.
        }

        return null;
    }

    /// <summary>True when the result belongs to the account and waits for a video.</summary>
    public async Task<bool> CanUploadVideoAsync(string accountId, string submissionId, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AccelerationLeaderboard WHERE AccountId=$a AND SubmissionId=$s AND Status='Pending'";
        command.Parameters.AddWithValue("$a", accountId);
        command.Parameters.AddWithValue("$s", submissionId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0;
    }

    public async Task MarkVideoAsync(string accountId, string submissionId, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE AccelerationLeaderboard SET VideoUploadedAt=$at WHERE AccountId=$a AND SubmissionId=$s";
            command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$a", accountId);
            command.Parameters.AddWithValue("$s", submissionId);
            await command.ExecuteNonQueryAsync(token);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Deletes videos and GPS coordinates 30 days after the review.</summary>
    public async Task CleanupEvidenceAsync(CancellationToken token)
    {
        var cutoff = DateTime.UtcNow - EvidenceKeep;
        var expired = new List<(string AccountId, string SubmissionId)>();
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT AccountId, SubmissionId FROM AccelerationLeaderboard WHERE ReviewedAt IS NOT NULL AND ReviewedAt<$cut AND (VideoUploadedAt IS NOT NULL OR GpsTrace IS NOT NULL)";
                select.Parameters.AddWithValue("$cut", cutoff.ToString("O"));
                await using var reader = await select.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                    expired.Add((reader.GetString(0), reader.GetString(1)));
            }

            foreach (var (accountId, id) in expired)
            {
                DeleteVideoFiles(accountId, id);
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE AccelerationLeaderboard SET VideoUploadedAt=NULL, GpsTrace=NULL WHERE AccountId=$a AND SubmissionId=$s";
                update.Parameters.AddWithValue("$a", accountId);
                update.Parameters.AddWithValue("$s", id);
                await update.ExecuteNonQueryAsync(token);
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Account deletion: results waiting for a review are deleted; public
    /// results stay on the board anonymised ("Deleted", no VIN hash, no GPS
    /// coordinates). Review videos are deleted.
    /// </summary>
    public async Task ForgetAccountAsync(string accountId, CancellationToken token)
    {
        var ids = new List<string>();
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT SubmissionId FROM AccelerationLeaderboard WHERE AccountId=$a";
                select.Parameters.AddWithValue("$a", accountId);
                await using var reader = await select.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                    ids.Add(reader.GetString(0));
            }

            foreach (var id in ids)
                DeleteVideoFiles(accountId, id);

            await using var command = connection.CreateCommand();
            command.CommandText = @"DELETE FROM AccelerationLeaderboard WHERE AccountId=$a AND Status NOT IN ('Approved','Verified');
UPDATE AccelerationLeaderboard SET AccountId='deleted', UserName='Deleted', VinHash='', GpsTrace=NULL, VideoUploadedAt=NULL
WHERE AccountId=$a;";
            command.Parameters.AddWithValue("$a", accountId);
            await command.ExecuteNonQueryAsync(token);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<AccelerationLeaderboardItem>> GetAsync(CancellationToken token, string testId = "0-100")
    {
        var result = new List<AccelerationLeaderboardItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT SubmissionId,UserName,VehicleModel,DurationSeconds,FinishedAt,HasVerifiedVideo,IsDemo,MaxSpeedKph,Status
FROM AccelerationLeaderboard WHERE Status IN ('Approved','Verified') AND TestId=$test AND ($demo=1 OR IsDemo=0) ORDER BY DurationSeconds ASC,FinishedAt ASC LIMIT 100";
        command.Parameters.AddWithValue("$demo", _showDemo ? 1 : 0);
        command.Parameters.AddWithValue("$test", NormalizeTest(testId));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3),
                DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt32(5) != 0, reader.GetInt32(6) != 0, reader.GetInt32(7), reader.GetString(8)));
        return result;
    }

    public async Task<IReadOnlyList<AccelerationLeaderboardAdminItem>> GetAdminAsync(CancellationToken token)
    {
        var result = new List<AccelerationLeaderboardAdminItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT SubmissionId,AccountId,UserName,VehicleModel,DurationSeconds,FinishedAt,SampleCount,HasVerifiedVideo,Status,IsDemo,CreatedAt,TestId,MaxSpeedKph,
SpeedTrace,GpsTrace,GpsDiffKph,VideoUploadedAt
FROM AccelerationLeaderboard ORDER BY CASE Status WHEN 'Pending' THEN 0 WHEN 'Approved' THEN 1 ELSE 2 END,CreatedAt DESC LIMIT 500";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4),
                DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind), reader.GetInt32(6),
                reader.GetInt32(7) != 0, reader.GetString(8), reader.GetInt32(9) != 0,
                DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetString(11), reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetDouble(15),
                !reader.IsDBNull(16)));
        return result;
    }

    /// <summary>
    /// Compatibility overload (SubmissionId only): acts only when exactly one
    /// account has that SubmissionId, never on an ambiguous id.
    /// </summary>
    public async Task<bool> ReviewAsync(string submissionId, string action, string reviewer, CancellationToken token)
    {
        if (submissionId.Length > 64) return false;
        var owners = new List<string>();
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT AccountId FROM AccelerationLeaderboard WHERE SubmissionId=$id LIMIT 2";
            command.Parameters.AddWithValue("$id", submissionId);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                owners.Add(reader.GetString(0));
        }
        return owners.Count == 1 && await ReviewAsync(submissionId, owners[0], action, reviewer, token);
    }

    /// <summary>Admin review of ONE row: SubmissionId + AccountId (ids are unique only per account).</summary>
    public async Task<bool> ReviewAsync(string submissionId, string accountId, string action, string reviewer, CancellationToken token)
    {
        if (submissionId.Length > 64 || string.IsNullOrEmpty(accountId) || accountId.Length > 64 ||
            action is not ("approve" or "reject" or "delete")) return false;
        await _gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = action == "delete"
                ? "DELETE FROM AccelerationLeaderboard WHERE SubmissionId=$id AND AccountId=$a"
                // Approving a result that has a review video makes it "Verified".
                : @"UPDATE AccelerationLeaderboard SET
Status=CASE WHEN $status='Approved' AND VideoUploadedAt IS NOT NULL THEN 'Verified' ELSE $status END,
HasVerifiedVideo=CASE WHEN $status='Approved' AND VideoUploadedAt IS NOT NULL THEN 1 ELSE HasVerifiedVideo END,
ReviewedAt=$at,ReviewedBy=$by WHERE SubmissionId=$id AND AccountId=$a";
            command.Parameters.AddWithValue("$id", submissionId);
            command.Parameters.AddWithValue("$a", accountId);
            if (action != "delete")
            {
                command.Parameters.AddWithValue("$status", action == "approve" ? "Approved" : "Rejected");
                command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$by", reviewer);
            }
            var changed = await command.ExecuteNonQueryAsync(token) > 0;
            if (changed && action == "delete")
                DeleteVideoFiles(accountId, submissionId);
            return changed;
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
    double PeakAcceleration, DateTime FinishedAt, bool HasVerifiedVideo,
    IReadOnlyList<double[]>? SpeedTrace = null, IReadOnlyList<double[]>? GpsTrace = null);

public sealed record AccelerationLeaderboardItem(
    string SubmissionId, string UserName, string VehicleModel, double DurationSeconds,
    DateTime FinishedAt, bool HasVerifiedVideo, bool IsDemo, int MaxSpeedKph = 0, string Status = "Approved");

public sealed record AccelerationLeaderboardAdminItem(
    string SubmissionId, string AccountId, string UserName, string VehicleModel, double DurationSeconds,
    DateTime FinishedAt, int SampleCount, bool HasVerifiedVideo, string Status, bool IsDemo, DateTime CreatedAt,
    string TestId = "0-100", int MaxSpeedKph = 0,
    string? SpeedTrace = null, string? GpsTrace = null, double? GpsDiffKph = null, bool HasVideo = false);
