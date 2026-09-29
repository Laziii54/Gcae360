using System.Net.Http.Json;
using CarScanner.Database;
using CarScanner.Models;

namespace CarScanner.Services;

public sealed class AccelerationLeaderboardService
{
    private readonly AppDatabase _database;
    private readonly AccountService _account;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public AccelerationLeaderboardService(AppDatabase database, AccountService account)
    {
        _database = database;
        _account = account;
    }

    public async Task<IReadOnlyList<AccelerationLeaderboardEntry>> GetAsync(CancellationToken token = default)
    {
        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Get, "/api/acceleration/leaderboard");
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<List<AccelerationLeaderboardEntry>>(cancellationToken: token)
                    .ConfigureAwait(false) ?? [];
        }
        catch { }

        // Offline fallback shows only this device's already validated runs.
        return (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .Where(x => x.LeaderboardEligible)
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
                IsDemo = false
            })
            .ToArray();
    }

    public async Task<(bool Success, string Message)> PublishLatestAsync(CancellationToken token = default)
    {
        var run = (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .FirstOrDefault(x => x.TestId.Equals("0-100", StringComparison.OrdinalIgnoreCase));
        if (run is null)
            return (false, "ჯერ 0–100 km/h გაზომვა არ არის.");
        if (!run.LeaderboardEligible)
            return (false, string.IsNullOrWhiteSpace(run.LeaderboardRejectionReason)
                ? "შედეგი რეიტინგის წესებს არ აკმაყოფილებს."
                : run.LeaderboardRejectionReason);
        if (!_account.IsSignedIn)
            return (false, "რეიტინგში შედეგის დასამატებლად შედი ანგარიშზე.");

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
            HasVerifiedVideo = run.HasVerifiedVideo
        };

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/acceleration/leaderboard");
            request.Content = JsonContent.Create(payload);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (false, "სერვერმა შედეგი არ მიიღო — გადაამოწმე გაზომვა და სცადე თავიდან.");

            run.PublishedAt = DateTime.Now;
            await _database.SaveAccelerationRunAsync(run).ConfigureAwait(false);
            return (true, "შედეგი გაიგზავნა და ადმინისტრატორის დამოწმებას ელოდება.");
        }
        catch
        {
            return (false, "რეიტინგის სერვერთან დაკავშირება ვერ მოხერხდა.");
        }
    }

    public async Task AttachVideoToLatestEligibleRunAsync(DateTime recordingStartedUtc)
    {
        var run = (await _database.GetAccelerationRunsAsync().ConfigureAwait(false))
            .Where(x => x.LeaderboardEligible && x.TestId.Equals("0-100", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.FinishedAt)
            .FirstOrDefault();

        if (run is null || run.FinishedAt.ToUniversalTime() < recordingStartedUtc.AddMinutes(-1))
            return;

        run.HasVerifiedVideo = true;
        await _database.SaveAccelerationRunAsync(run).ConfigureAwait(false);
    }
}
