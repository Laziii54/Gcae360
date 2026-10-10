using System.Text.Json;
using CarScanner.Api.Services;
using CarScanner.Models;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Whole chain except the HTTP route itself: real protocol layer → tap → pipeline (consent, intake, queue) →
/// the exact JSON the app uploads → the server's batch model, validator and store → deletion.
/// The "network" here is an in-process call that serialises with the same defaults as <c>JsonContent.Create</c>
/// and the server's minimal-API binder (web defaults). It cannot show how the live Azure service behaves.
/// </summary>
public sealed class CollectionBackendContractTests : IDisposable
{
    private const string Device = "0123456789abcdef0123456789abcdef";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gcar-contract-" + Guid.NewGuid().ToString("N"));
    private readonly string _db = Path.Combine(Path.GetTempPath(), "gcar-contract-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EcuObservationStore _store;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // The exchange tap is static and other tests publish to it in parallel. This pipeline only listens to
    // exchanges produced by the test that set the flag (AsyncLocal flows with the test's own awaits).
    private static readonly AsyncLocal<bool> Mine = new();
    private readonly Dictionary<CollectionPipeline, Action<ObdRawExchange>> _filters = new();

    public CollectionBackendContractTests() => _store = new EcuObservationStore($"Data Source={_db}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Stands in for the HTTP hop: JSON out of the app, bound into the server model, handled by the store.</summary>
    private sealed class StoreBackedUploader(EcuObservationStore store) : ICollectionUploader
    {
        public int Accepted { get; private set; }
        public int Rejected { get; private set; }
        public string LastJson { get; private set; } = "";

        public async Task<bool> UploadAsync(IReadOnlyList<QueuedExchange> batch, CancellationToken token)
        {
            LastJson = JsonSerializer.Serialize(CollectionPayload.Build(batch, "1.0.0"), Web);
            var bound = JsonSerializer.Deserialize<EcuObservationBatch>(LastJson, Web);
            if (bound?.Groups is null)
                return false;

            var (accepted, rejected) = await store.AddAsync(bound, Device, token);
            Accepted += accepted;
            Rejected += rejected;
            return true;
        }

        public async Task<bool> DeleteMineAsync(CancellationToken token)
        {
            await store.DeleteBySourceAsync(Device, token);
            return true;
        }
    }

    private async Task<(SimulatedElm327 Sim, Elm327Protocol Elm, CollectionPipeline Pipe, StoreBackedUploader Up, bool[] Consent)> SetupAsync(bool consent = true)
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["03"] = "43 01 01 33 00 00 00";
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());

        var enabled = new[] { consent };
        var up = new StoreBackedUploader(_store);
        var pipe = new CollectionPipeline(
            new CollectionQueue(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json")),
            () => enabled[0],
            () => new VehicleSnapshot("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4"),
            up);
        Action<ObdRawExchange> filter = e => { if (Mine.Value) pipe.Observe(e); };
        _filters[pipe] = filter;
        ObdExchangeTap.Observed += filter;
        await elm.InitializeAsync();
        return (sim, elm, pipe, up, enabled);
    }

    private void Detach(CollectionPipeline pipe)
    {
        if (_filters.Remove(pipe, out var filter))
            ObdExchangeTap.Observed -= filter;
        pipe.Dispose();
    }

    [Fact]
    public async Task StandardObd2_FlowsFromTheProtocolLayer_ToTheServerStore_WhenConsentIsOn()
    {
        Mine.Value = true;
        var (_, elm, pipe, up, _) = await SetupAsync();
        try
        {
            await elm.SendCommandAsync("0100");
            await elm.SendCommandAsync("010C");
            await elm.SendCommandAsync("03");
            await pipe.DrainAsync();

            var sent = await pipe.FlushAsync(CancellationToken.None, force: true);

            Assert.True(sent >= 3);
            Assert.Equal(sent, up.Accepted);
            Assert.Equal(0, up.Rejected);

            var rows = await _store.LatestAsync(100, CancellationToken.None);
            var byRequest = rows.Where(r => r.Request is "0100" or "010C" or "03").ToDictionary(r => r.Request, r => r.Response);
            Assert.Equal("valid;bitmap=BE3FA813", byRequest["0100"]);
            Assert.Equal("valid", byRequest["010C"]);                 // that it answered, not the rpm
            Assert.Equal("valid;dtc=P0133", byRequest["03"]);
            Assert.All(rows, r => Assert.Equal("VOLKSWAGEN", r.Make));
            Assert.All(rows, r => Assert.Equal("single", r.Reliability));   // one device: never "verified"
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public async Task NothingReachesTheServer_WhenConsentIsOff()
    {
        Mine.Value = true;
        var (_, elm, pipe, up, _) = await SetupAsync(consent: false);
        try
        {
            await elm.SendCommandAsync("0100");
            await elm.SendCommandAsync("03");
            await pipe.DrainAsync();

            Assert.Equal(0, await pipe.FlushAsync(CancellationToken.None, force: true));
            Assert.Equal(0, up.Accepted);
            Assert.Empty(await _store.LatestAsync(100, CancellationToken.None));
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public async Task WithdrawingConsent_DeletesWhatTheServerStored()
    {
        Mine.Value = true;
        var (_, elm, pipe, up, consent) = await SetupAsync();
        try
        {
            await elm.SendCommandAsync("0100");
            await pipe.DrainAsync();
            await pipe.FlushAsync(CancellationToken.None, force: true);
            Assert.NotEmpty(await _store.LatestAsync(100, CancellationToken.None));

            consent[0] = false;
            Assert.True(await pipe.DisableAsync(CancellationToken.None));

            Assert.Empty(await _store.LatestAsync(100, CancellationToken.None));
            Assert.Equal(0, pipe.PendingCount);
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public async Task TheSamePhoneUploadingAgain_IsNotCountedAsASecondSource()
    {
        Mine.Value = true;
        var (_, elm, pipe, _, _) = await SetupAsync();
        try
        {
            await elm.SendCommandAsync("0100");
            await pipe.DrainAsync();
            await pipe.FlushAsync(CancellationToken.None, force: true);
            await elm.SendCommandAsync("0100");
            await pipe.DrainAsync();
            await pipe.FlushAsync(CancellationToken.None, force: true);

            var row = Assert.Single(await _store.LatestAsync(100, CancellationToken.None), r => r.Request == "0100");
            Assert.Equal(1, row.SourceCount);
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public void UploadJson_HasTheFieldNamesTheServerBindsTo_AndNoIdentifiers()
    {
        var items = new[]
        {
            new QueuedExchange("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4",
                new CollectedExchange("0100", "valid;bitmap=BE3FA813", 0x7DF, 0, false), "2026-10-10"),
            new QueuedExchange("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4",
                new CollectedExchange("22F40C", "7E80562F40C1AF8", 0x7E0, 0x7E8, false), "2026-10-10")
        };

        var json = JsonSerializer.Serialize(CollectionPayload.Build(items, "1.0.0"), Web);
        using var doc = JsonDocument.Parse(json);
        var group = doc.RootElement.GetProperty("groups")[0];
        Assert.Equal("VOLKSWAGEN", group.GetProperty("make").GetString());
        var first = group.GetProperty("items")[0];
        foreach (var name in new[] { "tx", "rx", "ext29", "request", "response", "observedOn" })
            Assert.True(first.TryGetProperty(name, out _), name);

        var allNames = doc.RootElement.EnumerateObject().Select(p => p.Name)
            .Concat(group.EnumerateObject().Select(p => p.Name))
            .Concat(first.EnumerateObject().Select(p => p.Name)).Select(n => n.ToLowerInvariant()).ToArray();
        Assert.DoesNotContain(allNames, n => n.Contains("vin") || n.Contains("account") || n.Contains("user") || n.Contains("token") || n.Contains("device"));

        var bound = JsonSerializer.Deserialize<EcuObservationBatch>(json, Web)!;
        Assert.Equal(2, bound.Groups!.Single().Items!.Count);
    }
}
