using System.Net.Http.Json;
using CarScanner.Database;
using CarScanner.Models;

namespace CarScanner.Services;

public sealed class AccelerationLeaderboardService
{
    private readonly AppDatabase _database;
    private readonly AccountService _account;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    // Video uploads can take a while on mobile data.
    private readonly HttpClient _upload = new() { Timeout = TimeSpan.FromMinutes(6) };

    /// <summary>Largest review video the server accepts.</summary>
    public const long MaxVideoBytes = 80L * 1024 * 1024;

    public AccelerationLeaderboardService(AppDatabase database, AccountService account)
    {
        _database = database;
        _account = account;
    }

    private static bool En => CarScanner.Services.Localization.LanguageCatalog.UsesEnglishBase;

    public async Task<IReadOnlyList<AccelerationLeaderboardEntry>> GetAsync(string testId = "0-100", CancellationToken token = default)
    {
        var real = await GetRealAsync(testId, token).ConfigureAwait(false);
#if DEBUG
        // Debug builds only: a full sample list to judge the page (see SampleLeaderboardEntries).
        return real.Concat(SampleLeaderboardEntries.For(testId)).ToList();
#else
        return real;
#endif
    }

    private async Task<IReadOnlyList<AccelerationLeaderboardEntry>> GetRealAsync(string testId, CancellationToken token)
    {
        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Get,
                "/api/acceleration/leaderboard?test=" + Uri.EscapeDataString(testId));
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<List<AccelerationLeaderboardEntry>>(cancellationToken: token)
                    .ConfigureAwait(false) ?? [];
        }
        catch { }

        // Offline fallback shows only this device's already validated runs.
        return (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .Where(x => x.LeaderboardEligible && x.TestId.Equals(testId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.DurationSeconds)
            .Take(100)
            .Select(x => new AccelerationLeaderboardEntry
            {
                SubmissionId = x.LeaderboardSubmissionId,
                UserName = x.UserName,
                VehicleModel = x.VehicleDisplayName,
                DurationSeconds = x.DurationSeconds,
                FinishedAt = x.FinishedAt,
                HasVerifiedVideo = x.HasVerifiedVideo,
                MaxSpeedKph = x.MaxSpeedKph,
                IsDemo = false
            })
            .ToArray();
    }

    /// <summary>This device's submitted results (to find "my place") and its best local run of one test.</summary>
    public async Task<(HashSet<string> SubmissionIds, AccelerationRun? Best)> GetMineAsync(string testId = "0-100")
    {
        var runs = (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .Where(x => x.TestId.Equals(testId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var ids = runs
            .Select(x => x.LeaderboardSubmissionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (ids, runs.OrderBy(x => x.DurationSeconds).FirstOrDefault());
    }

    public async Task<(bool Success, string Message)> PublishLatestAsync(string testId = "0-100", CancellationToken token = default)
    {
        var run = (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .FirstOrDefault(x => x.TestId.Equals(testId, StringComparison.OrdinalIgnoreCase));
        var testName = PerformanceTests.Standard.FirstOrDefault(t => t.Id == testId)?.DisplayName ?? testId;
        if (run is null)
            return (false, En ? $"No {testName} measurement yet." : $"ჯერ {testName} გაზომვა არ არის.");
        if (!run.LeaderboardEligible)
            return (false, string.IsNullOrWhiteSpace(run.LeaderboardRejectionReason)
                ? (En ? "The result does not meet the ranking rules." : "შედეგი რეიტინგის წესებს არ აკმაყოფილებს.")
                : run.LeaderboardRejectionReason);
        if (!_account.IsSignedIn)
            return (false, En ? "Sign in before adding a result to the ranking." : "რეიტინგში შედეგის დასამატებლად შედი ანგარიშზე.");

        run.LeaderboardSubmissionId = string.IsNullOrWhiteSpace(run.LeaderboardSubmissionId)
            ? Guid.NewGuid().ToString("N")
            : run.LeaderboardSubmissionId;

        var payload = new AccelerationLeaderboardSubmission
        {
            SubmissionId = run.LeaderboardSubmissionId,
            UserName = run.UserName,
            VehicleModel = run.VehicleDisplayName,
            Vin = run.VIN,
            VinVerified = run.VinVerified,
            SpeedSource = "OBD_PID_010D",
            TestId = run.TestId,
            StartSpeedKph = run.StartSpeedKph,
            TargetSpeedKph = run.TargetSpeedKph,
            DurationSeconds = run.DurationSeconds,
            SampleCount = run.SampleCount,
            MaxSpeedKph = run.MaxSpeedKph,
            DistanceMeters = run.DistanceMeters,
            PeakAcceleration = run.PeakAcceleration,
            FinishedAt = run.FinishedAt.ToUniversalTime(),
            HasVerifiedVideo = run.HasVerifiedVideo,
            SpeedTrace = ParseTrace(run.SpeedTraceJson),
            GpsTrace = ParseTrace(run.GpsTraceJson)
        };

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/acceleration/leaderboard");
            request.Content = JsonContent.Create(payload);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var (success, status, videoRequired, reason) = ReadAnswer(body);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, En ? "Your session has expired. Sign in again and retry." : "სესიას ვადა გაუვიდა. თავიდან შედი ანგარიშზე და სცადე.");
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return (false, En ? "Too many attempts. Try again in an hour." : "ძალიან ბევრი მცდელობაა. სცადე ერთ საათში.");
            if (!response.IsSuccessStatusCode)
                return (false, DescribeReason(reason));

            // A result the administrator already rejected stays rejected (HTTP 200, success:false).
            if (!success || string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                run.PublishedAt = DateTime.Now;
                run.LeaderboardStatus = "Rejected";
                await _database.SaveAccelerationRunAsync(run).ConfigureAwait(false);
                return (false, string.IsNullOrWhiteSpace(reason)
                    ? (En ? "This result was rejected by the review and is not shown in the ranking." : "ეს შედეგი შემოწმებისას უარყოფილია და რეიტინგში არ ჩანს.")
                    : DescribeReason(reason));
            }

            run.PublishedAt = DateTime.Now;
            run.LeaderboardStatus = string.IsNullOrWhiteSpace(status) ? "Approved" : status;
            await _database.SaveAccelerationRunAsync(run).ConfigureAwait(false);

            if (videoRequired || run.LeaderboardStatus == "Pending")
            {
                // Top-10 or unusually fast: the video recorded with the run is
                // sent for review straight away when there is one.
                if (HasLocalVideo(run))
                {
                    var upload = await UploadVideoAsync(run, token).ConfigureAwait(false);
                    return (true, upload.Success
                        ? (En ? "Result added. It is in the top places, so it is under review — your video was sent." : "შედეგი დაემატა. ტოპ ადგილზეა, ამიტომ მოწმდება — შენი ვიდეო გაიგზავნა.")
                        : upload.Message);
                }

                return (true, En
                    ? "Result added as “under review”. Top results need a video: measure again from the Video screen and the video is sent automatically."
                    : "შედეგი დაემატა „მოწმდება“ სტატუსით. ტოპ შედეგს ვიდეო სჭირდება: გაზომე ვიდეოს ეკრანიდან და ვიდეო ავტომატურად გაიგზავნება.");
            }

            if (run.LeaderboardStatus == "Verified")
                return (true, En ? "Result is in the ranking (verified)." : "შედეგი რეიტინგშია (დამოწმებული).");

            return (true, En ? "Result added to the ranking." : "შედეგი რეიტინგში დაემატა.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (false, En ? "Could not connect to the ranking server." : "რეიტინგის სერვერთან დაკავშირება ვერ მოხერხდა.");
        }
        catch (Exception ex)
        {
            return (false, UserError.Describe(ex));
        }
    }

    /// <summary>Reads { success, status, videoRequired, reason } from the server's answer.</summary>
    private static (bool Success, string? Status, bool VideoRequired, string? Reason) ReadAnswer(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (false, null, false, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return (false, null, false, null);

            string? Text(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
            bool Flag(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True;

            return (Flag("success"), Text("status"), Flag("videoRequired"), Text("reason") ?? Text("message") ?? Text("code"));
        }
        catch (System.Text.Json.JsonException)
        {
            return (false, null, false, null);
        }
    }

    /// <summary>The server's rejection reason in the user's language.</summary>
    private static string DescribeReason(string? reason)
    {
        var (ka, en) = (reason ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "vin verification required" => ("საჭიროა VIN-ის დადასტურება: წაიკითხე VIN მანქანიდან და გაზომე თავიდან.", "VIN verification is required: read the VIN from the car and measure again."),
            "ecu speed source required" => ("სიჩქარე მანქანის ECU-დან უნდა იყოს წაკითხული.", "The speed must be read from the car's ECU."),
            "standing start required" => ("გაზომვა ადგილიდან (0 km/h) უნდა დაიწყოს.", "The run must start from a standstill (0 km/h)."),
            "invalid target" or "invalid distance" => ("გაზომვის მიზანი ან მანძილი არასწორია.", "The run's target speed or distance is not valid."),
            "implausible time" => ("დრო არარეალურია — გაზომე თავიდან.", "The time is not plausible — measure again."),
            "invalid ecu samples" => ("ECU-დან საკმარისი სიჩქარის მონაცემი არ მოვიდა.", "Not enough speed readings came from the ECU."),
            "implausible telemetry" => ("გაზომვის მონაცემები ერთმანეთს არ ემთხვევა.", "The run's readings do not fit together."),
            "invalid timestamp" => ("გაზომვა 30 დღეზე ძველია ან თარიღი არასწორია.", "The run is older than 30 days or its date is wrong."),
            "this test is not ranked" => ("ეს ტესტი რეიტინგში არ მონაწილეობს.", "This test is not ranked."),
            "gps evidence required" or "gps missing" or "gps check failed" => ("GPS მონაცემი არ არის: ჩართე მდებარეობა და გაზომე ღია ცის ქვეშ.", "GPS data is missing: turn on location and measure under open sky."),
            "invalid evidence" => ("GPS/სიჩქარის ჩანაწერი დაზიანებულია — გაზომე თავიდან.", "The GPS/speed recording is damaged — measure again."),
            "gps speed does not match the ecu speed" => ("GPS-ის სიჩქარე მანქანის სიჩქარეს არ ემთხვევა.", "The GPS speed does not match the car's speed."),
            "invalid user name" => ("სახელი ცარიელია ან ძალიან გრძელია (მაქს. 40).", "The name is empty or too long (max 40)."),
            "invalid vehicle model" => ("მანქანის მოდელი ცარიელია ან ძალიან გრძელია.", "The vehicle model is empty or too long."),
            "invalid submission" => ("შედეგის ID არასწორია — გაზომე თავიდან.", "The result ID is not valid — measure again."),
            _ => ("სერვერმა შედეგი არ მიიღო" + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason), "The server did not accept the result" + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason))
        };
        return En ? en : ka;
    }

    private static List<double[]> ParseTrace(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<double[]>>(json) ?? new(); }
        catch (System.Text.Json.JsonException) { return new(); }
    }

    public static bool HasLocalVideo(AccelerationRun run) =>
        !string.IsNullOrWhiteSpace(run.VideoPath) && File.Exists(run.VideoPath);

    /// <summary>This device's published run that waits for a review video, if any.</summary>
    public async Task<AccelerationRun?> GetRunWaitingForVideoAsync(string testId)
    {
        return (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .Where(x => x.TestId.Equals(testId, StringComparison.OrdinalIgnoreCase) &&
                        x.LeaderboardStatus == "Pending" && x.VideoUploadedAt is null &&
                        !string.IsNullOrWhiteSpace(x.LeaderboardSubmissionId))
            .OrderByDescending(x => x.FinishedAt)
            .FirstOrDefault();
    }

    /// <summary>Sends the run's in-app video for review (top-10 / unusually fast results).</summary>
    public async Task<(bool Success, string Message)> UploadVideoAsync(AccelerationRun run, CancellationToken token = default)
    {
        if (!HasLocalVideo(run))
            return (false, En ? "There is no video recorded with this run on this phone." : "ამ გაზომვასთან ჩაწერილი ვიდეო ამ ტელეფონზე არ არის.");

        var info = new FileInfo(run.VideoPath);
        if (info.Length > MaxVideoBytes)
            return (false, En ? "The video is too large (max 80 MB). Record a shorter one." : "ვიდეო ძალიან დიდია (მაქს. 80 MB). ჩაწერე უფრო მოკლე.");

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post,
                $"/api/acceleration/leaderboard/{Uri.EscapeDataString(run.LeaderboardSubmissionId)}/video");
            await using var stream = File.OpenRead(run.VideoPath);
            using var content = new MultipartFormDataContent();
            var file = new StreamContent(stream);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            content.Add(file, "video", "run.mp4");
            request.Content = content;
            using var response = await _upload.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (false, En ? "The video could not be sent. Try again on Wi-Fi." : "ვიდეო ვერ გაიგზავნა. სცადე Wi-Fi-ით.");

            run.VideoUploadedAt = DateTime.Now;
            await _database.SaveAccelerationRunAsync(run).ConfigureAwait(false);
            return (true, En ? "Video sent. We will review it soon." : "ვიდეო გაიგზავნა. მალე გადავამოწმებთ.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return (false, En ? "The video could not be sent. Try again on Wi-Fi." : "ვიდეო ვერ გაიგზავნა. სცადე Wi-Fi-ით.");
        }
    }

    /// <summary>
    /// Links a video recorded and saved by the app to the matching recent run
    /// and keeps a private copy (for a review upload). Older copies are removed.
    /// </summary>
    public async Task AttachVideoToLatestEligibleRunAsync(DateTime recordingStartedUtc, string? videoFile = null)
    {
        var run = (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .Where(x => x.LeaderboardEligible && PerformanceTestDefinition.IsRanked(x.TestId))
            .OrderByDescending(x => x.FinishedAt)
            .FirstOrDefault();

        if (run is null || run.FinishedAt.ToUniversalTime() < recordingStartedUtc.AddMinutes(-1))
            return;

        run.HasVerifiedVideo = true;

        if (!string.IsNullOrWhiteSpace(videoFile) && File.Exists(videoFile))
        {
            try
            {
                var folder = Path.Combine(FileSystem.AppDataDirectory, "run-videos");
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, $"run_{run.Id}.mp4");
                File.Copy(videoFile, target, overwrite: true);
                run.VideoPath = target;

                // Keep only the three newest copies.
                foreach (var old in new DirectoryInfo(folder).GetFiles("run_*.mp4")
                             .OrderByDescending(f => f.LastWriteTimeUtc).Skip(3))
                {
                    try { old.Delete(); } catch (IOException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Run video copy failed: {ex.Message}");
            }
        }

        await _database.SaveAccelerationRunAsync(run).ConfigureAwait(false);

        // Already published as "under review": send the video now.
        if (run.LeaderboardStatus == "Pending" && run.VideoUploadedAt is null && HasLocalVideo(run))
            await UploadVideoAsync(run).ConfigureAwait(false);
    }
}
