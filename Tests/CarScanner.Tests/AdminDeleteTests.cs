using CarScanner.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;

namespace CarScanner.Tests;

/// <summary>Admin-triggered deletes for the data that piles up over time: crash reports,
/// service-search reports and reset recordings (requested so the admin pages stay readable).</summary>
public sealed class AdminDeleteTests
{
    private static (IHostEnvironment Env, IConfiguration Config, string Dir) NewStorage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gcar360-admin-delete-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = directory })
            .Build();
        return (new TestEnvironment(directory), configuration, directory);
    }

    private static void Cleanup(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task CrashReportStore_DeleteGroup_RemovesOnlyMatchingRows()
    {
        var (env, config, dir) = NewStorage();
        try
        {
            var store = new CrashReportStore(env, config);
            await store.SaveAsync(Crash("NullReferenceException", "at CarScanner.A.B()"), default);
            await store.SaveAsync(Crash("NullReferenceException", "at CarScanner.A.B()"), default);
            await store.SaveAsync(Crash("TimeoutException", "at CarScanner.C.D()"), default);

            var groups = await store.GetGroupsAsync(30, default);
            Assert.Equal(2, groups.Count);

            var removed = await store.DeleteGroupAsync("NullReferenceException", "at CarScanner.A.B()", default);

            Assert.Equal(2, removed);
            var remaining = Assert.Single(await store.GetGroupsAsync(30, default));
            Assert.Equal("TimeoutException", remaining.ExceptionType);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task CrashReportStore_DeleteGroup_NoMatch_ReturnsZero()
    {
        var (env, config, dir) = NewStorage();
        try
        {
            var store = new CrashReportStore(env, config);
            await store.SaveAsync(Crash("TimeoutException", "at CarScanner.C.D()"), default);

            Assert.Equal(0, await store.DeleteGroupAsync("DoesNotExist", "nowhere", default));
            Assert.Single(await store.GetGroupsAsync(30, default));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task ServiceSearchStore_Delete_RemovesOnlyThatReport()
    {
        var (env, config, dir) = NewStorage();
        try
        {
            var store = new ServiceSearchStore(env, config);
            await store.AddAsync(SearchReport("Toyota"), "device-a", null, false, default);
            await store.AddAsync(SearchReport("Honda"), "device-a", null, false, default);

            var rows = await store.LatestAsync(10, default);
            Assert.Equal(2, rows.Count);
            var toDelete = rows.Single(r => r.Make == "TOYOTA");

            Assert.True(await store.DeleteAsync(toDelete.Id, default));

            var remaining = Assert.Single(await store.LatestAsync(10, default));
            Assert.Equal("HONDA", remaining.Make);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task ServiceSearchStore_Delete_UnknownId_ReturnsFalse()
    {
        var (env, config, dir) = NewStorage();
        try
        {
            var store = new ServiceSearchStore(env, config);
            Assert.False(await store.DeleteAsync(999_999, default));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task ResetRecordingStore_Delete_RemovesOnlyThatRecording()
    {
        var (env, config, dir) = NewStorage();
        try
        {
            var store = new ResetRecordingStore(env, config);
            var keepId = await store.AddAsync(ResetRecording("Toyota"), "device-a", null, default);
            var deleteId = await store.AddAsync(ResetRecording("Honda"), "device-a", null, default);

            Assert.True(await store.DeleteAsync(deleteId, default));

            var remaining = Assert.Single(await store.LatestAsync(10, default));
            Assert.Equal(keepId, remaining.Id);
            Assert.Equal("Toyota", remaining.Make);
        }
        finally { Cleanup(dir); }
    }

    private static CrashReportRequest Crash(string exceptionType, string frame) => new(
        Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "1.0", "Android", "14",
        exceptionType, $"System.Exception: boom\n{frame}\n   at Other.Frame()", "unhandled", Fatal: false);

    private static ServiceSearchReport SearchReport(string make) => new(
        make, "Model", "2020", "1HGCM82633A", 50.0, 3, 2, "1.0",
        new List<ServiceSearchReportHit> { new("source-1", "pid", 50.0, "%", "22F40C", "ECU", true) });

    private static ResetRecordingRequest ResetRecording(string make) => new(
        make, "Model", "2020", "1HGCM82633A", "MenuReset", "Oil",
        false, false, 100.0, 0.0, "1.0",
        new List<ResetRecordingCandidate> { new("write", "01", "AA", "note") },
        new List<string> { "frame-1" });

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CarScanner.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
