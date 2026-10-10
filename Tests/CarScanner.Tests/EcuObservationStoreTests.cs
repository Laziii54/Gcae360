using CarScanner.Api.Services;
using Microsoft.Data.Sqlite;

namespace CarScanner.Tests;

public sealed class EcuObservationStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-obs-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EcuObservationStore _store;

    public EcuObservationStoreTests()
    {
        _store = new EcuObservationStore($"Data Source={_file}");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private static EcuObservationBatch Batch(string request = "22F40C", string response = "7E80562F40C1AF8", string make = "Volkswagen") =>
        new("1.0", new List<EcuObservationGroup>
        {
            new(make, "Golf", "2015", "CAN", new List<EcuObservationItem> { new(0x7E0, 0x7E8, false, request, response) })
        });

    private const string DeviceA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DeviceB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DeviceC = "cccccccccccccccccccccccccccccccc";

    [Fact]
    public async Task NewAnswer_StartsAsSingle_AndIsNeverVerified()
    {
        var (accepted, rejected) = await _store.AddAsync(Batch(), DeviceA, default);
        Assert.Equal((1, 0), (accepted, rejected));

        var row = Assert.Single(await _store.LatestAsync(10, default));
        Assert.Equal("single", row.Reliability);
        Assert.Equal("unreviewed", row.Review);
        Assert.Equal(1, row.SourceCount);
    }

    [Fact]
    public async Task IdenticalAnswerFromIndependentDevices_RaisesReliability_ButNeverToVerified()
    {
        await _store.AddAsync(Batch(), DeviceA, default);
        Assert.Equal("single", Assert.Single(await _store.LatestAsync(10, default)).Reliability);

        await _store.AddAsync(Batch(), DeviceB, default);
        Assert.Equal("repeated", Assert.Single(await _store.LatestAsync(10, default)).Reliability);

        await _store.AddAsync(Batch(), DeviceC, default);
        var row = Assert.Single(await _store.LatestAsync(10, default));
        Assert.Equal("corroborated", row.Reliability);
        Assert.Equal(3, row.SourceCount);
        Assert.NotEqual("verified", row.Reliability);
        Assert.Equal("unreviewed", row.Review);
    }

    [Fact]
    public async Task SameDeviceRepeating_DoesNotCountAsCorroboration()
    {
        for (var i = 0; i < 5; i++)
            await _store.AddAsync(Batch(), DeviceA, default);

        var row = Assert.Single(await _store.LatestAsync(10, default));
        Assert.Equal(1, row.SourceCount);
        Assert.Equal("single", row.Reliability);
    }

    [Fact]
    public async Task DifferentAnswersForSameRequest_AreKeptSeparate_AndFlagged()
    {
        await _store.AddAsync(Batch(response: "7E80562F40C1AF8"), DeviceA, default);
        await _store.AddAsync(Batch(response: "7E80562F40C0000"), DeviceB, default);

        var rows = await _store.LatestAsync(10, default);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(2, r.DistinctAnswersForRequest));
        Assert.All(rows, r => Assert.Equal("single", r.Reliability));
    }

    [Fact]
    public async Task VinAndWriteRequests_AreRejectedByTheServerToo()
    {
        var batch = new EcuObservationBatch("1.0", new List<EcuObservationGroup>
        {
            new("VW", "Golf", "2015", "CAN", new List<EcuObservationItem>
            {
                new(0x7E0, 0x7E8, false, "0902", "7E8101449020157564E"),
                new(0x7E0, 0x7E8, false, "2E1234AA", "7E8036E1234"),
                new(0x7E0, 0x7E8, false, "22F190", "7E80562F190AABB"),
                new(0x7E0, 0x7E8, false, "22F40C", "garbage \u0001"),
                new(0x7FFFFFFF, 0x7E8, false, "22F40C", "7E80562F40C1AF8"),
                new(0x7E0, 0x7E8, false, "22F40C", "7E80562F40C1AF8")
            })
        });

        var (accepted, rejected) = await _store.AddAsync(batch, DeviceA, default);
        Assert.Equal(1, accepted);
        Assert.Equal(5, rejected);
        Assert.Single(await _store.LatestAsync(10, default));
    }

    [Fact]
    public async Task MissingVehicle_IsRejected()
    {
        var (accepted, rejected) = await _store.AddAsync(Batch(make: " "), DeviceA, default);
        Assert.Equal((0, 1), (accepted, rejected));
    }

    [Fact]
    public async Task DeleteMine_RemovesOwnData_AndKeepsOthers()
    {
        await _store.AddAsync(Batch(), DeviceA, default);
        await _store.AddAsync(Batch(), DeviceB, default);
        await _store.AddAsync(Batch(request: "22F40D", response: "7E80562F40D3C"), DeviceA, default);

        await _store.DeleteBySourceAsync(DeviceA, default);

        var row = Assert.Single(await _store.LatestAsync(10, default));
        Assert.Equal("22F40C", row.Request);
        Assert.Equal(1, row.SourceCount);
    }

    [Fact]
    public async Task NoSourceIsStoredInTheClear()
    {
        await _store.AddAsync(Batch(), DeviceA, default);

        await using var c = new SqliteConnection($"Data Source={_file}");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT group_concat(SourceHash) FROM EcuObservationSources";
        var hashes = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(hashes);
        Assert.DoesNotContain(DeviceA, hashes);
        Assert.Equal(EcuObservationStore.SourceHash(DeviceA), hashes);
        Assert.NotEqual(UsageStoreHash(DeviceA), hashes);
    }

    private static string UsageStoreHash(string deviceId) => UsageStore.HashDevice(deviceId);

    [Theory]
    [InlineData(1, "unreviewed", "single")]
    [InlineData(2, "unreviewed", "repeated")]
    [InlineData(3, "unreviewed", "corroborated")]
    [InlineData(40, "unreviewed", "corroborated")]
    [InlineData(1, "verified", "verified")]
    public void Reliability_OnlyManualReviewMeansVerified(int sources, string review, string expected) =>
        Assert.Equal(expected, EcuObservationStore.Reliability(sources, review));
}
