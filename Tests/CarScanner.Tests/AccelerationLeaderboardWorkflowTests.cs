using CarScanner.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;

namespace CarScanner.Tests;

public sealed class AccelerationLeaderboardWorkflowTests
{
    [Fact]
    public async Task DemoSeedAndOwnerApprovalWorkflow_WorkEndToEnd()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gcar360-ranking-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = directory })
                .Build();
            var environment = new TestEnvironment(directory);
            var store = new AccelerationLeaderboardStore(environment, configuration);

            var initial = await store.GetAsync(CancellationToken.None);
            Assert.Equal(20, initial.Count);
            Assert.All(initial, item => Assert.True(item.IsDemo));

            var request = ValidRequest();
            await store.SaveAsync("account-1", request, CancellationToken.None);
            Assert.Equal(20, (await store.GetAsync(CancellationToken.None)).Count);
            Assert.Contains(await store.GetAdminAsync(CancellationToken.None), item =>
                item.SubmissionId == request.SubmissionId && item.Status == "Pending");

            Assert.True(await store.ReviewAsync(request.SubmissionId, "approve", "owner@example.com", CancellationToken.None));
            Assert.Equal(21, (await store.GetAsync(CancellationToken.None)).Count);
            Assert.True(await store.ReviewAsync(request.SubmissionId, "delete", "owner@example.com", CancellationToken.None));
            Assert.Equal(20, (await store.GetAsync(CancellationToken.None)).Count);

            Assert.True(await store.ReviewAsync("demo-01", "delete", "owner@example.com", CancellationToken.None));
            _ = new AccelerationLeaderboardStore(environment, configuration);
            Assert.Equal(19, (await store.GetAsync(CancellationToken.None)).Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static AccelerationLeaderboardRequest ValidRequest() => new(
        Guid.NewGuid().ToString("N"), "Test Driver", "Toyota Prius 2014",
        "JTDBR32E720123456", true, "OBD_PID_010D", "0-100", 0, 100,
        9.84, 30, 101, 140, 4.2, DateTime.UtcNow, false);

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CarScanner.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
