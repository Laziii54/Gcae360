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

    private static bool En => Preferences.Default.Get("language", "English") == "English";

    public Task<IReadOnlyList<AccelerationLeaderboardEntry>> GetAsync(string testId = "0-100", CancellationToken token = default) =>
        GetRealAsync(testId, token);

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
            if (!response.IsSuccessStatusCode)
                return (false, En ? "The server did not accept the result. Check the run and try again." : "სერვერმა შედეგი არ მიიღო — გადაამოწმე გაზომვა და სცადე თავიდან.");

            var answer = await response.Content.ReadFromJsonAsync<AccelerationLeaderboardPublishResponse>(cancellationToken: token).ConfigureAwait(false);
            run.PublishedAt = DateTime.Now;
            run.LeaderboardStatus = answer?.Status ?? "Approved";
            await _database.SaveAccelerationRunAsync(run).ConfigureAwait(false);

            if (answer?.VideoRequired == true)
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

            return (true, En ? "Result added to the ranking." : "შედეგი რეიტინგში დაემატა.");
        }
        catch
        {
            return (false, En ? "Could not connect to the ranking server." : "რეიტინგის სერვერთან დაკავშირება ვერ მოხერხდა.");
        }
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
