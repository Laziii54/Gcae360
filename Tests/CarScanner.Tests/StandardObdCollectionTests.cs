using CarScanner.Api.Services;
using CarScanner.Models;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

internal static class StdEx
{
    public static ObdRawExchange Std(string request, string response, bool? isCan = false) =>
        new(0x7DF, 0, false, request, response, ObdExchangeOrigin.StandardObd, isCan);

    public static string Summary(string request, string response, bool? isCan = false) =>
        StandardObdClassifier.Summarize(Std(request, response, isCan))!.Response;
}

public sealed class StandardObdClassifierTests
{
    [Fact]
    public void SupportedPidBlock_ReportsTheBitmap() =>
        Assert.Equal("valid;bitmap=BE3FA813", StdEx.Summary("0100", "41 00 BE 3F A8 13"));

    [Fact]
    public void SeveralModulesAnswering_ReportEachDistinctBitmap() =>
        Assert.Equal("valid;bitmap=BE3FA813,98180001", StdEx.Summary("0100", "41 00 BE 3F A8 13 41 00 98 18 00 01 41 00 BE 3F A8 13"));

    [Fact]
    public void OrdinaryPid_ReportsOnlyThatItAnswered_NeverTheValue()
    {
        var summary = StdEx.Summary("010C", "41 0C 1A F8");
        Assert.Equal("valid", summary);
        Assert.Equal(summary, StdEx.Summary("010C", "41 0C 2B 00"));
    }

    [Fact]
    public void StoredCodes_AreNormalised()
    {
        Assert.Equal("valid;dtc=P0133", StdEx.Summary("03", "43 01 33 00 00 00 00", isCan: false));
        Assert.Equal("valid;dtc=P0133", StdEx.Summary("03", "43 01 01 33 00 00 00", isCan: true));
    }

    [Fact]
    public void PendingAndPermanentModes_AreSupported()
    {
        Assert.StartsWith("valid;dtc=", StdEx.Summary("07", "47 01 33 00 00 00 00", isCan: false));
        Assert.StartsWith("valid;dtc=", StdEx.Summary("0A", "4A 01 33 00 00 00 00", isCan: false));
    }

    [Fact]
    public void NoCodes_IsAValidAnswer_NotAnError() =>
        Assert.Equal("valid;dtc=none", StdEx.Summary("03", "43 00", isCan: null));

    [Theory]
    [InlineData("7F 01 12", "unsupported;nrc=12")]
    [InlineData("7F 01 11", "unsupported;nrc=11")]
    [InlineData("7F 01 78", "inconclusive;negative-78")]
    [InlineData("7F 01 31", "inconclusive;negative-31")]
    [InlineData("7F 03 12", "inconclusive;malformed")] // negative reply to a different service is not ours
    public void NegativeReplies_AreSeparatedByCode(string response, string expected) =>
        Assert.Equal(expected, StdEx.Summary("010C", response));

    [Theory]
    [InlineData("NO DATA", "inconclusive;no-data")]
    [InlineData("TIMEOUT", "inconclusive;timeout")]
    [InlineData("", "inconclusive;no-response")]
    [InlineData("UNABLE TO CONNECT", "inconclusive;adapter-error")]
    [InlineData("CAN ERROR", "inconclusive;adapter-error")]
    [InlineData("?", "inconclusive;adapter-error")]
    [InlineData("\u0001@@##\u0002", "inconclusive;malformed")]
    [InlineData("41 0C", "inconclusive;malformed")]
    public void NoAnswerOrDamagedAnswer_IsInconclusive_NeverUnsupported(string response, string expected)
    {
        Assert.Equal(expected, StdEx.Summary("010C", response));
        Assert.DoesNotContain("unsupported", StdEx.Summary("0100", response));
        Assert.DoesNotContain("unsupported", StdEx.Summary("03", response));
    }

    [Fact]
    public void TruncatedBitmap_IsMalformed() =>
        Assert.Equal("inconclusive;malformed", StdEx.Summary("0100", "41 00 BE 3F"));

    [Fact]
    public void ValueBytesThatLookLikeANegativeReply_DoNotFlipAValidAnswer() =>
        Assert.Equal("valid", StdEx.Summary("010D", "41 0D 7F 01 12"));

    [Theory]
    [InlineData("43")]
    [InlineData("43 ZZ")]
    [InlineData("43 01 33")]
    public void DamagedDtcAnswer_IsNeverReportedAsCleanOrUnsupported(string response)
    {
        var summary = StdEx.Summary("03", response, isCan: true);
        Assert.StartsWith("inconclusive;", summary);
    }

    [Theory]
    [InlineData("0902")] // VIN
    [InlineData("0900")]
    [InlineData("04")] // clear DTC
    [InlineData("0201")] // freeze frame
    [InlineData("22F190")]
    [InlineData("ATZ")]
    [InlineData("ATSH7E0")]
    [InlineData("2E1234AA")]
    [InlineData("2701")]
    [InlineData("3101FF00")]
    [InlineData("14FFFFFF")]
    [InlineData("0100FF")]
    [InlineData("")]
    public void ForbiddenRequests_NeverProduceARecord(string request)
    {
        Assert.False(ObdExchangeTap.IsObservableStandardRequest(request));
        Assert.Null(StandardObdClassifier.Summarize(StdEx.Std(request, "41 00 BE 3F A8 13")));
    }

    [Theory]
    [InlineData("0100")]
    [InlineData("010C")]
    [InlineData("01A0")]
    [InlineData("03")]
    [InlineData("07")]
    [InlineData("0A")]
    [InlineData("0a")]
    [InlineData("01 0C")]
    public void AllowedRequests_AreOnlyModeOnePidsAndTheReadOnlyDtcModes(string request) =>
        Assert.True(ObdExchangeTap.IsObservableStandardRequest(request));

    [Fact]
    public void ModuleOriginExchange_IsNotClassifiedAsStandard() =>
        Assert.Null(StandardObdClassifier.Summarize(new ObdRawExchange(0x7E0, 0x7E8, false, "010C", "7E80441 0C1AF8".Replace(" ", ""))));

    [Fact]
    public void ModuleSanitiser_RefusesStandardOriginExchanges() =>
        Assert.Null(CollectionSanitizer.Sanitize(StdEx.Std("010C", "41 0C 1A F8")));
}

public sealed class StandardSummaryServerValidationTests
{
    private static CollectedExchange? V(string request, string response, int tx = 0x7DF, int rx = 0, bool ext29 = false) =>
        CollectionSanitizer.ValidateStandardSummary(tx, rx, ext29, request, response);

    [Theory]
    [InlineData("0100", "valid;bitmap=BE3FA813")]
    [InlineData("0120", "valid;bitmap=BE3FA813,98180001")]
    [InlineData("010C", "valid")]
    [InlineData("010C", "unsupported;nrc=12")]
    [InlineData("03", "valid;dtc=P0133,P0300")]
    [InlineData("07", "valid;dtc=none")]
    [InlineData("0A", "inconclusive;no-data")]
    [InlineData("010C", "inconclusive;timeout")]
    [InlineData("010C", "inconclusive;negative-78")]
    public void WellFormedSummaries_AreAccepted(string request, string response) =>
        Assert.NotNull(V(request, response));

    [Theory]
    [InlineData("010C", "41 0C 1A F8")] // raw payload
    [InlineData("010C", "7DF0441 0C1AF8")]
    [InlineData("0100", "valid")] // block answer must carry the bitmap
    [InlineData("010C", "valid;bitmap=BE3FA813")] // bitmap for a normal PID
    [InlineData("010C", "valid;dtc=P0133")] // dtc for a PID
    [InlineData("03", "valid")] // DTC mode without a code list
    [InlineData("03", "valid;bitmap=BE3FA813")]
    [InlineData("03", "valid;dtc=ZZZZZ")]
    [InlineData("03", "valid;dtc=P0133;extra")]
    [InlineData("010C", "unsupported;nrc=33")]
    [InlineData("010C", "unsupported")]
    [InlineData("010C", "inconclusive;because I said so")]
    [InlineData("010C", "valid\nWVWZZZ1JZ3W386752")]
    [InlineData("0902", "valid")] // VIN request
    [InlineData("04", "valid")] // clear DTC
    [InlineData("ATZ", "valid")]
    [InlineData("0100", "")]
    public void AnythingElse_IsRejected(string request, string response) =>
        Assert.Null(V(request, response));

    [Fact]
    public void ThirtyThreeCodes_AreRejected()
    {
        var codes = string.Join(",", Enumerable.Range(0, 33).Select(i => $"P{i:X4}"));
        Assert.Null(V("03", "valid;dtc=" + codes));
    }

    [Fact]
    public void WrongAddressing_IsRejected()
    {
        Assert.Null(V("010C", "valid", tx: 0x7E0));
        Assert.Null(V("010C", "valid", rx: 0x7E8));
        Assert.Null(V("010C", "valid", ext29: true));
    }
}

public sealed class FakeUploader : ICollectionUploader
{
    public Func<IReadOnlyList<QueuedExchange>, bool> OnUpload { get; set; } = _ => true;
    public Func<bool> OnDelete { get; set; } = () => true;
    public bool Throw { get; set; }
    public List<int> BatchSizes { get; } = new();
    public List<QueuedExchange> Uploaded { get; } = new();
    public int Deletes { get; private set; }
    public int UploadCalls { get; private set; }

    public Task<bool> UploadAsync(IReadOnlyList<QueuedExchange> batch, CancellationToken token)
    {
        UploadCalls++;
        if (Throw)
            throw new HttpRequestException("backend unavailable");
        if (!OnUpload(batch))
            return Task.FromResult(false);
        BatchSizes.Add(batch.Count);
        Uploaded.AddRange(batch);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteMineAsync(CancellationToken token)
    {
        Deletes++;
        if (Throw)
            throw new HttpRequestException("backend unavailable");
        return Task.FromResult(OnDelete());
    }
}

public sealed class CollectionPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gcar-pipe-" + Guid.NewGuid().ToString("N"));
    private string QueuePath => Path.Combine(_dir, "q.json");
    private bool _enabled;
    private VehicleSnapshot? _vehicle = new("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4");
    private DateTimeOffset _now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (CollectionPipeline Pipe, CollectionQueue Queue, FakeUploader Up) Create(CollectionQueue? queue = null, FakeUploader? up = null)
    {
        queue ??= new CollectionQueue(QueuePath);
        up ??= new FakeUploader();
        var pipe = new CollectionPipeline(queue, () => _enabled, () => _vehicle, up, () => _now);
        _disposables.Add(pipe);
        return (pipe, queue, up);
    }

    private static ObdRawExchange Module(string request, string response) => new(0x7E0, 0x7E8, false, request, response);

    [Fact]
    public async Task OffByDefault_NothingIsQueued()
    {
        var (pipe, queue, _) = Create();
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        pipe.Observe(Module("22F40C", "7E80562F40C1AF8"));
        await pipe.DrainAsync();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task UnknownVehicle_StartUpProbe_CreatesNoRecord()
    {
        _enabled = true;
        _vehicle = null;
        var (pipe, queue, _) = Create();
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        pipe.Observe(Module("22F40C", "7E80562F40C1AF8"));
        _vehicle = new VehicleSnapshot("", "", "", "");
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task StandardObservations_BecomeSummariesForTheKnownVehicle()
    {
        _enabled = true;
        var (pipe, queue, _) = Create();
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        pipe.Observe(StdEx.Std("010C", "41 0C 1A F8"));
        pipe.Observe(StdEx.Std("03", "43 00", null));
        await pipe.DrainAsync();

        var items = queue.Peek(10);
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal("GOLF", i.Model));
        Assert.Contains(items, i => i.Exchange.Request == "0100" && i.Exchange.Response == "valid;bitmap=BE3FA813");
        Assert.Contains(items, i => i.Exchange.Request == "010C" && i.Exchange.Response == "valid");
        Assert.Contains(items, i => i.Exchange.Request == "03" && i.Exchange.Response == "valid;dtc=none");
        Assert.All(items, i => Assert.Equal("2026-10-10", i.ObservedOn));
    }

    [Fact]
    public async Task ForbiddenStandardRequests_AreNeverQueued()
    {
        _enabled = true;
        var (pipe, queue, _) = Create();
        foreach (var request in new[] { "0902", "04", "0201", "ATZ", "22F190", "2701", "2E1234" })
            pipe.Observe(StdEx.Std(request, "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ContinuousLiveDataStream_ProducesOneRecord()
    {
        _enabled = true;
        var (pipe, queue, _) = Create();
        for (var i = 0; i < 300; i++)
            pipe.Observe(StdEx.Std("010C", $"41 0C {i % 256:X2} F8"));
        await pipe.DrainAsync();

        var item = Assert.Single(queue.Peek(10));
        Assert.Equal("valid", item.Exchange.Response);
    }

    [Fact]
    public async Task OemModuleCollection_StillWorks_AndStaysRawAndSanitised()
    {
        _enabled = true;
        var (pipe, queue, _) = Create();
        pipe.Observe(Module("22F40C", "7E80562F40C1AF8"));
        pipe.Observe(Module("0902", "7E8101449020157564E")); // VIN: still dropped
        pipe.Observe(Module("22F190", "7E80562F190AABB")); // VIN DID: still dropped
        await pipe.DrainAsync();

        var item = Assert.Single(queue.Peek(10));
        Assert.Equal("22F40C", item.Exchange.Request);
        Assert.Equal("7E80562F40C1AF8", item.Exchange.Response); // raw, not a summary
        Assert.Equal(0x7E0, item.Exchange.TxCanId);
    }

    [Fact]
    public async Task Upload_SendsEverythingOnce_InBatches_AndNextSessionDoesNotResend()
    {
        _enabled = true;
        var (pipe, queue, up) = Create();
        for (var pid = 1; pid <= 120; pid++)
            if (pid % 0x20 != 0)
                pipe.Observe(StdEx.Std($"01{pid:X2}", $"41 {pid:X2} 01"));
        await pipe.DrainAsync();
        // Reaching the threshold already triggers a background upload, so wait until everything has gone out.
        for (var i = 0; i < 100 && queue.Count > 0; i++)
        {
            await pipe.FlushAsync(default, force: true);
            await Task.Delay(20);
        }

        Assert.Equal(0, queue.Count);
        Assert.Equal(117, up.Uploaded.Select(u => u.Key).Distinct().Count());
        Assert.True(up.BatchSizes.Max() <= CollectionPipeline.BatchSize);

        // New session (new objects, same files): the same facts are not queued or uploaded again.
        var (pipe2, queue2, up2) = Create(new CollectionQueue(QueuePath), new FakeUploader());
        for (var pid = 1; pid <= 120; pid++)
            if (pid % 0x20 != 0)
                pipe2.Observe(StdEx.Std($"01{pid:X2}", $"41 {pid:X2} 02"));
        await pipe2.DrainAsync();
        Assert.Equal(0, queue2.Count);
        Assert.Equal(0, await pipe2.FlushAsync(default, force: true));
        Assert.Equal(0, up2.UploadCalls);
    }

    [Fact]
    public async Task BackendOutage_KeepsTheQueue_BacksOff_AndRecovers()
    {
        _enabled = true;
        var up = new FakeUploader { Throw = true };
        var (pipe, queue, _) = Create(up: up);
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();

        Assert.Equal(0, await pipe.FlushAsync(default));
        Assert.Equal(1, queue.Count);
        Assert.Equal(1, pipe.ConsecutiveFailures);
        Assert.True(pipe.NextAttempt > _now);

        var calls = up.UploadCalls;
        Assert.Equal(0, await pipe.FlushAsync(default)); // still inside the back-off window
        Assert.Equal(calls, up.UploadCalls);

        _now = _now.AddMinutes(1);
        Assert.Equal(0, await pipe.FlushAsync(default)); // retried, fails again, waits longer
        Assert.Equal(2, pipe.ConsecutiveFailures);
        Assert.True(pipe.NextAttempt > _now.AddSeconds(45));

        up.Throw = false;
        _now = _now.AddHours(2);
        Assert.Equal(1, await pipe.FlushAsync(default));
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, pipe.ConsecutiveFailures);
    }

    [Fact]
    public async Task RejectedUpload_IsAFailureToo_NotLostData()
    {
        _enabled = true;
        var up = new FakeUploader { OnUpload = _ => false };
        var (pipe, queue, _) = Create(up: up);
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();

        Assert.Equal(0, await pipe.FlushAsync(default));
        Assert.Equal(1, queue.Count);
        Assert.Equal(1, pipe.ConsecutiveFailures);
    }

    [Fact]
    public async Task Observe_NeverThrows_EvenWhenTheEnvironmentFails()
    {
        var pipe = new CollectionPipeline(new CollectionQueue(QueuePath), () => throw new InvalidOperationException("prefs broken"),
            () => throw new InvalidOperationException("vehicle broken"), new FakeUploader());
        _disposables.Add(pipe);
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        pipe.Observe(null!);
        await pipe.DrainAsync();
    }

    [Fact]
    public async Task OptOut_ClearsQueueAndDedupMemory_AndAsksTheServerToDelete()
    {
        _enabled = true;
        var (pipe, queue, up) = Create();
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        pipe.Observe(Module("22F40C", "7E80562F40C1AF8"));
        await pipe.DrainAsync();
        await pipe.FlushAsync(default, force: true);
        pipe.Observe(StdEx.Std("010C", "41 0C 1A F8"));
        await pipe.DrainAsync();
        Assert.Equal(1, queue.Count);
        Assert.True(queue.SentCount > 0);

        _enabled = false;
        Assert.True(await pipe.DisableAsync(default));

        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.SentCount);
        Assert.False(queue.DeletePending);
        Assert.Equal(1, up.Deletes);
        Assert.False(File.Exists(QueuePath));

        // Nothing new is collected while off.
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, await pipe.FlushAsync(default, force: true));
    }

    [Fact]
    public async Task OptOut_WithServerDown_KeepsTheDeletionPending_AndRetriesLater()
    {
        _enabled = true;
        var up = new FakeUploader();
        var (pipe, queue, _) = Create(up: up);
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();

        _enabled = false;
        up.Throw = true;
        Assert.False(await pipe.DisableAsync(default));
        Assert.Equal(0, queue.Count);
        Assert.True(queue.DeletePending);
        Assert.True(new CollectionQueue(QueuePath).DeletePending); // survives an app restart

        up.Throw = false;
        _now = _now.AddHours(2);
        await pipe.FlushAsync(default); // still disabled: only the deletion is attempted
        Assert.False(queue.DeletePending);
        Assert.Equal(0, up.UploadCalls);
    }

    [Fact]
    public async Task ReEnabling_WhileDeletionIsPending_DeletesFirst_ThenUploads()
    {
        _enabled = true;
        var up = new FakeUploader();
        var (pipe, queue, _) = Create(up: up);
        _enabled = false;
        up.Throw = true;
        await pipe.DisableAsync(default);
        up.Throw = false;

        _enabled = true;
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();
        _now = _now.AddHours(2);
        Assert.Equal(1, await pipe.FlushAsync(default));
        Assert.Equal(2, up.Deletes);
        Assert.False(queue.DeletePending);
    }

    [Fact]
    public async Task ConsentWithdrawnDuringUpload_RemembersNothing()
    {
        _enabled = true;
        var up = new FakeUploader();
        up.OnUpload = _ => { _enabled = false; return true; };
        var (pipe, queue, _) = Create(up: up);
        pipe.Observe(StdEx.Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();
        await pipe.FlushAsync(default, force: true);
        Assert.Equal(0, queue.SentCount);
    }
}

[Collection("ObdExchangeTap")]
public sealed class StandardObdCaptureThroughProtocolTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gcar-proto-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private async Task<(SimulatedElm327 Sim, Elm327Protocol Elm, CollectionPipeline Pipe, CollectionQueue Queue, FakeUploader Up, Action<VehicleSnapshot?> SetVehicle)> SetupAsync(bool enabled = true)
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["03"] = "43 01 01 33 00 00 00";
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());

        VehicleSnapshot? vehicle = null; // the vehicle is only known after initialisation
        var queue = new CollectionQueue(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"));
        var up = new FakeUploader();
        var pipe = new CollectionPipeline(queue, () => enabled, () => vehicle, up);
        ObdExchangeTap.Observed += pipe.Observe;
        await elm.InitializeAsync();
        return (sim, elm, pipe, queue, up, v => vehicle = v);
    }

    private static void Detach(CollectionPipeline pipe)
    {
        ObdExchangeTap.Observed -= pipe.Observe;
        pipe.Dispose();
    }

    [Fact]
    public async Task InitialisationProbe_BeforeTheVehicleIsKnown_IsNotRecorded()
    {
        var (_, _, pipe, queue, _, _) = await SetupAsync();
        try
        {
            await pipe.DrainAsync();
            Assert.Equal(0, queue.Count);
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public async Task RealProtocolLayer_RecordsOnlyTheAllowedStandardRequests()
    {
        var (_, elm, pipe, queue, _, setVehicle) = await SetupAsync();
        try
        {
            setVehicle(new VehicleSnapshot("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4"));

            await elm.SendCommandAsync("0100");
            await elm.SendCommandAsync("010C");
            await elm.SendCommandAsync("010D");
            await elm.SendCommandAsync("03");
            await elm.SendCommandAsync("ATRV");        // adapter command
            await elm.SendCommandAsync("0902", TimeSpan.FromMilliseconds(300), 1, false); // VIN
            await elm.SendCommandAsync("04", TimeSpan.FromMilliseconds(300), 1, false);   // clear DTC
            await pipe.DrainAsync();

            var items = queue.Peek(20).Select(i => (i.Exchange.Request, i.Exchange.Response)).OrderBy(t => t.Request).ToArray();
            Assert.Equal(new[]
            {
                ("0100", "valid;bitmap=BE3FA813"),
                ("010C", "valid"),
                ("010D", "valid"),
                ("03", "valid;dtc=P0133")
            }, items);
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public async Task NoDataAndTimeout_AreRecordedAsInconclusive_NotUnsupported()
    {
        var (sim, elm, pipe, queue, _, setVehicle) = await SetupAsync();
        try
        {
            setVehicle(new VehicleSnapshot("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4"));

            sim.Fault = SimFault.NoData;
            sim.FaultAfter = 0;
            await Record.ExceptionAsync(() => elm.SendCommandAsync("0105", TimeSpan.FromMilliseconds(300), 1, true));

            sim.Fault = SimFault.Timeout;
            await Record.ExceptionAsync(() => elm.SendCommandAsync("010F", TimeSpan.FromMilliseconds(100), 1, false));
            await pipe.DrainAsync();

            var summaries = queue.Peek(10).ToDictionary(i => i.Exchange.Request, i => i.Exchange.Response);
            Assert.Equal("inconclusive;no-data", summaries["0105"]);
            Assert.Equal("inconclusive;timeout", summaries["010F"]);
            Assert.DoesNotContain(summaries.Values, v => v.StartsWith("unsupported"));
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public async Task ExplicitNotSupportedReply_IsRecordedAsUnsupported()
    {
        var (sim, elm, pipe, queue, _, setVehicle) = await SetupAsync();
        try
        {
            setVehicle(new VehicleSnapshot("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4"));
            sim.ObdAnswers["0133"] = "7F 01 12";
            await elm.SendCommandAsync("0133", TimeSpan.FromMilliseconds(300), 1, false);
            await pipe.DrainAsync();

            Assert.Equal("unsupported;nrc=12", Assert.Single(queue.Peek(5)).Exchange.Response);
        }
        finally { Detach(pipe); }
    }

    [Fact]
    public async Task DiagnosticsKeepWorking_WhenTheBackendIsDown_AndWhenCollectionIsDisabled()
    {
        foreach (var enabled in new[] { true, false })
        {
            var (sim, elm, pipe, queue, up, setVehicle) = await SetupAsync(enabled);
            try
            {
                setVehicle(new VehicleSnapshot("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4"));
                up.Throw = true;
                var decoder = new PIDDecoder();

                for (var i = 0; i < 40; i++)
                {
                    Assert.True(decoder.TryDecode(0x0C, await elm.SendCommandAsync("010C"), out var rpm));
                    Assert.Equal(1726, rpm);
                }

                // Observe() only queues the exchange for a background consumer. Without waiting for it, a busy machine
                // could count the queue before the consumer had stored anything (the cause of the rare failure).
                await pipe.DrainAsync();
                await pipe.FlushAsync(default, force: true); // fails quietly
                Assert.Equal(enabled ? 1 : 0, queue.Count);
                Assert.True(new PIDDecoder().TryDecode(0x0D, await elm.SendCommandAsync("010D"), out _));
            }
            finally { Detach(pipe); }
        }
    }

    [Fact]
    public async Task ThrowingObserver_DoesNotBreakStandardReads()
    {
        var (_, elm, pipe, _, _, _) = await SetupAsync();
        void Bad(ObdRawExchange e) => throw new InvalidOperationException("observer bug");
        ObdExchangeTap.Observed += Bad;
        try
        {
            Assert.True(new PIDDecoder().TryDecode(0x0C, await elm.SendCommandAsync("010C"), out _));
        }
        finally
        {
            ObdExchangeTap.Observed -= Bad;
            Detach(pipe);
        }
    }

    [Fact]
    public async Task OemModuleSessions_AreStillCollectedThroughTheSamePipeline()
    {
        var (sim, elm, pipe, queue, _, setVehicle) = await SetupAsync();
        try
        {
            setVehicle(new VehicleSnapshot("VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4"));
            sim.ModuleAnswers["7E0|22F40C"] = "7E80562F40C1AF8";
            await elm.RunDtcModuleSessionAsync(async s => await s.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x22, 0xF4, 0x0C }, false, default));
            await pipe.DrainAsync();

            var item = Assert.Single(queue.Peek(5));
            Assert.Equal("7E80562F40C1AF8", item.Exchange.Response);
        }
        finally { Detach(pipe); }
    }
}

public sealed class StandardObservationStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-std-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EcuObservationStore _store;
    private const string DeviceA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DeviceB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DeviceC = "cccccccccccccccccccccccccccccccc";

    public StandardObservationStoreTests() => _store = new EcuObservationStore($"Data Source={_file}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private static EcuObservationBatch Batch(params EcuObservationItem[] items) =>
        new("1.0", new List<EcuObservationGroup> { new("Volkswagen", "Golf", "2015", "CAN", items.ToList()) });

    private static EcuObservationItem Std(string request, string response, string? on = null) => new(0x7DF, 0, false, request, response, on);

    [Fact]
    public async Task SummaryRecords_AreStored_WithKindAndStatus()
    {
        var (accepted, rejected) = await _store.AddAsync(Batch(
            Std("0100", "valid;bitmap=BE3FA813"), Std("010C", "valid"), Std("03", "valid;dtc=P0133"),
            Std("010F", "inconclusive;timeout"), Std("0133", "unsupported;nrc=12")), DeviceA, default);
        Assert.Equal((5, 0), (accepted, rejected));

        var rows = (await _store.LatestAsync(10, default)).ToDictionary(r => r.Request);
        Assert.Equal(("obd-supported", "valid"), (rows["0100"].Kind, rows["0100"].Status));
        Assert.Equal(("obd-pid", "valid"), (rows["010C"].Kind, rows["010C"].Status));
        Assert.Equal(("obd-dtc", "valid"), (rows["03"].Kind, rows["03"].Status));
        Assert.Equal("inconclusive", rows["010F"].Status);
        Assert.Equal("unsupported", rows["0133"].Status);
    }

    [Fact]
    public async Task RawPayloadsAndForbiddenRequests_UnderTheStandardAddress_AreRejected()
    {
        var (accepted, rejected) = await _store.AddAsync(Batch(
            Std("010C", "41 0C 1A F8"),
            Std("0902", "valid"),
            Std("04", "valid"),
            Std("ATZ", "valid"),
            Std("010C", "valid;bitmap=BE3FA813"),
            Std("03", "valid;dtc=P0133\nWVWZZZ1JZ3W386752"),
            new EcuObservationItem(0x7DF, 0, false, "22F40C", "7E80562F40C1AF8")), DeviceA, default);

        Assert.Equal((0, 7), (accepted, rejected));
        Assert.Empty(await _store.LatestAsync(10, default));
    }

    [Fact]
    public async Task FutureOrInvalidDates_AreRejected_ValidOnesAreKept()
    {
        var future = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd");
        var (accepted, rejected) = await _store.AddAsync(Batch(
            Std("0100", "valid;bitmap=BE3FA813", future),
            Std("010C", "valid", "not-a-date"),
            Std("010D", "valid", "2019-01-01"),
            Std("010E", "valid", "2026-10-10T12:00:00Z"),
            Std("010F", "valid", DateTime.UtcNow.ToString("yyyy-MM-dd")),
            Std("0110", "valid", null)), DeviceA, default);

        Assert.Equal((2, 4), (accepted, rejected));
    }

    [Fact]
    public async Task StoredDates_HaveDayPrecisionOnly()
    {
        await _store.AddAsync(Batch(Std("010C", "valid")), DeviceA, default);
        var row = Assert.Single(await _store.LatestAsync(5, default));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", row.FirstSeen);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", row.LastSeen);
    }

    [Fact]
    public async Task IndependentDevices_CorroborateASummary_ButNothingBecomesVerified()
    {
        foreach (var device in new[] { DeviceA, DeviceB, DeviceC })
            await _store.AddAsync(Batch(Std("0100", "valid;bitmap=BE3FA813")), device, default);

        var row = Assert.Single(await _store.LatestAsync(5, default));
        Assert.Equal("corroborated", row.Reliability);
        Assert.Equal("unreviewed", row.Review);
    }

    [Fact]
    public async Task ServerSideDeletion_RemovesStandardSummariesToo()
    {
        await _store.AddAsync(Batch(Std("0100", "valid;bitmap=BE3FA813"), Std("010C", "valid")), DeviceA, default);
        await _store.AddAsync(Batch(Std("0100", "valid;bitmap=BE3FA813")), DeviceB, default);

        await _store.DeleteBySourceAsync(DeviceA, default);

        var row = Assert.Single(await _store.LatestAsync(5, default));
        Assert.Equal("0100", row.Request);
        Assert.Equal(1, row.SourceCount);
    }

    [Fact]
    public async Task StoredColumns_ContainNoVinOrIdentifiersBeyondTheHashedSource()
    {
        await _store.AddAsync(Batch(Std("0100", "valid;bitmap=BE3FA813")), DeviceA, default);

        await using var c = new SqliteConnection($"Data Source={_file}");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('EcuObservations') UNION ALL SELECT name FROM pragma_table_info('EcuObservationSources')";
        var columns = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                columns.Add(r.GetString(0));

        Assert.DoesNotContain(columns, n => n.Contains("Vin", StringComparison.OrdinalIgnoreCase) ||
                                            n.Contains("Ip", StringComparison.Ordinal) && n.Length == 2 ||
                                            n.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                                            n.Contains("Device", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("SourceHash", columns);
    }
}

public sealed class QueueDeduplicationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gcar-dedup-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static QueuedExchange Item(string response) =>
        new("VOLKSWAGEN", "GOLF", "2015", "CAN", new CollectedExchange("010C", response, 0x7DF, 0, false));

    [Fact]
    public void UploadedItems_AreRemembered_AcrossRestarts_UntilCleared()
    {
        var path = Path.Combine(_dir, "q.json");
        var queue = new CollectionQueue(path);
        Assert.True(queue.Add(Item("valid")));
        queue.Remove(queue.Peek(1));
        Assert.False(queue.Add(Item("valid")));

        var restarted = new CollectionQueue(path);
        Assert.False(restarted.Add(Item("valid")));
        Assert.True(restarted.Add(Item("inconclusive;timeout"))); // a different fact is new

        restarted.Clear();
        Assert.Equal(0, restarted.SentCount);
        Assert.True(new CollectionQueue(path).Add(Item("valid")));
    }

    [Fact]
    public void SentMemory_IsBounded()
    {
        var queue = new CollectionQueue(Path.Combine(_dir, "q.json"));
        for (var i = 0; i < CollectionQueue.SentCapacity + 50; i++)
        {
            queue.Add(Item($"valid;bitmap={i:X8}"));
            if (i % 100 == 99)
                queue.Remove(queue.Peek(200));
        }

        queue.Remove(queue.Peek(500));
        Assert.True(queue.SentCount <= CollectionQueue.SentCapacity);
    }

    [Fact]
    public void CorruptSentFile_IsIgnored()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "q.json");
        File.WriteAllText(path + ".sent", "}{ not json");
        var queue = new CollectionQueue(path);
        Assert.Equal(0, queue.SentCount);
        Assert.True(queue.Add(Item("valid")));
    }
}
