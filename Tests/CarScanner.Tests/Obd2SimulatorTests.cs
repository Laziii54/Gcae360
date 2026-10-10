using CarScanner.Models;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;
using CarScanner.Services.Passive;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Drives the real <see cref="Elm327Protocol"/> (and the decoders on top of it) against a software ELM327/car.
/// What is verified here: reading and decoding work, bad data never crashes anything, a dead link surfaces as
/// the expected exceptions, and the app recovers when the adapter comes back.
/// </summary>
[Collection("ObdExchangeTap")]
public sealed class Obd2SimulatorTests
{
    private static async Task<(SimulatedElm327 Sim, Elm327Protocol Elm)> ConnectedAsync(Action<SimulatedElm327>? configure = null)
    {
        var sim = new SimulatedElm327();
        configure?.Invoke(sim);
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        return (sim, elm);
    }

    private static async Task<(SimulatedElm327 Sim, Elm327Protocol Elm)> InitializedAsync(Action<SimulatedElm327>? configure = null)
    {
        var (sim, elm) = await ConnectedAsync(configure);
        await elm.InitializeAsync();
        return (sim, elm);
    }

    private static bool IsExpectedFailure(Exception ex) =>
        ex is Elm327TimeoutException or Elm327CommandException or Elm327InitializationException or Elm327NotConnectedException
            or ObdAdapterNotRespondingException or ObdConnectionLostException or IOException or TimeoutException
            or OperationCanceledException or NotSupportedException or InvalidOperationException;

    // ------------------------------------------------------------------ reading and decoding

    [Fact]
    public async Task HealthyCar_Initialises_AsCan11Bit()
    {
        var (_, elm) = await InitializedAsync();

        Assert.True(elm.IsInitialized);
        Assert.Equal(ObdProtocol.ISO_15765_4_CAN_11BIT_500K, elm.CurrentProtocol);
    }

    [Fact]
    public async Task LiveData_IsReadAndDecoded()
    {
        var (_, elm) = await InitializedAsync();
        var decoder = new PIDDecoder();

        Assert.True(decoder.TryDecode(0x0C, await elm.SendCommandAsync("010C"), out var rpm));
        Assert.Equal(1726, rpm);
        Assert.True(decoder.TryDecode(0x05, await elm.SendCommandAsync("0105"), out var coolant));
        Assert.Equal(83, coolant);
        Assert.True(decoder.TryDecode(0x0D, await elm.SendCommandAsync("010D"), out var speed));
        Assert.Equal(60, speed);
    }

    [Fact]
    public async Task StoredTroubleCode_IsReadAndDecoded()
    {
        var (_, elm) = await InitializedAsync();
        var codes = new DTCDecoder().DecodeStored(await elm.SendCommandAsync("03"));

        Assert.Equal("P0133", Assert.Single(codes).Code);
    }

    [Fact]
    public async Task NoStoredCodes_GivesEmptyList_NotAnError()
    {
        var (_, elm) = await InitializedAsync(s => s.ObdAnswers["03"] = "43 00");
        Assert.Empty(new DTCDecoder().DecodeStored(await elm.SendCommandAsync("03")));
    }

    // ------------------------------------------------------------------ error handling at start-up

    [Theory]
    [InlineData(SimFault.NoData)]
    [InlineData(SimFault.UnableToConnect)]
    public async Task CarThatDoesNotAnswer_FailsInitialisationCleanly(SimFault fault)
    {
        var (_, elm) = await ConnectedAsync(s => s.Fault = fault);

        await Assert.ThrowsAsync<Elm327InitializationException>(() => elm.InitializeAsync());
        Assert.False(elm.IsInitialized);
    }

    [Fact]
    public async Task LinkDroppingDuringInitialisation_IsAnExpectedFailure()
    {
        var (_, elm) = await ConnectedAsync(s => { s.Fault = SimFault.Disconnect; s.FaultAfter = 0; });

        var ex = await Record.ExceptionAsync(() => elm.InitializeAsync());
        Assert.NotNull(ex);
        Assert.True(IsExpectedFailure(ex!), ex!.GetType().FullName);
        Assert.False(elm.IsInitialized);
    }

    // ------------------------------------------------------------------ unknown / corrupted data

    [Theory]
    [InlineData(SimFault.Garbage)]
    [InlineData(SimFault.Truncated)]
    [InlineData(SimFault.Noise)]
    [InlineData(SimFault.NoData)]
    public async Task CorruptedAnswers_NeverCrash_AndNeverProduceInventedValues(SimFault fault)
    {
        var (sim, elm) = await InitializedAsync();
        sim.Fault = fault;
        sim.FaultAfter = 0;
        var decoder = new PIDDecoder();
        var dtc = new DTCDecoder();

        for (var i = 0; i < 25; i++)
        {
            string raw;
            try { raw = await elm.SendCommandAsync("010C"); }
            catch (Exception ex) when (IsExpectedFailure(ex)) { continue; }

            // Decoding must be safe whatever came back…
            var ok = decoder.TryDecode(0x0C, raw, out var rpm);
            if (ok && fault is SimFault.Garbage or SimFault.NoData)
                Assert.Fail($"garbage/no-data was decoded as RPM {rpm}: '{raw}'");
            if (ok && fault == SimFault.Noise)
                Assert.Equal(1726, rpm); // junk lines around a valid answer must not change the value

            // The trouble-code decoder reports unusable text with FormatException (OBDService catches exactly that).
            Assert.Null(Record.Exception(() => { try { dtc.DecodeStored(raw); } catch (FormatException) { } }));
            Assert.Null(Record.Exception(() => { try { dtc.DecodePending(raw); } catch (FormatException) { } }));
            Assert.Null(Record.Exception(() => { try { dtc.DecodePermanent(raw); } catch (FormatException) { } }));
        }

        // …and the link must still be usable once the car answers properly again.
        sim.Fault = SimFault.None;
        Assert.True(decoder.TryDecode(0x0C, await elm.SendCommandAsync("010C"), out var recovered));
        Assert.Equal(1726, recovered);
    }

    [Fact]
    public async Task TruncatedAnswer_IsNotDecodedAsAValue()
    {
        var (sim, elm) = await InitializedAsync(s => s.ObdAnswers["010C"] = "41 0C 1A");
        Assert.False(new PIDDecoder().TryDecode(0x0C, await elm.SendCommandAsync("010C"), out _));
        sim.ObdAnswers["010C"] = "41 0C";
        Assert.False(new PIDDecoder().TryDecode(0x0C, await elm.SendCommandAsync("010C"), out _));
    }

    [Fact]
    public async Task AdapterTimeout_IsReportedAsATimeout_AndTheLinkRecovers()
    {
        var (sim, elm) = await InitializedAsync();
        sim.Fault = SimFault.Timeout;
        sim.FaultAfter = 0;

        var ex = await Record.ExceptionAsync(() => elm.SendCommandAsync("010C"));
        Assert.True(ex is Elm327TimeoutException, ex?.GetType().FullName);

        sim.Fault = SimFault.None;
        Assert.True(new PIDDecoder().TryDecode(0x0C, await elm.SendCommandAsync("010C"), out _));
    }

    // ------------------------------------------------------------------ connection loss and recovery

    [Fact]
    public async Task LinkDropMidSession_GivesAnExpectedFailure_ThenReconnectAndReinitialiseWork()
    {
        var (sim, elm) = await InitializedAsync();
        Assert.True(new PIDDecoder().TryDecode(0x0C, await elm.SendCommandAsync("010C"), out _));

        sim.Drop();
        var ex = await Record.ExceptionAsync(() => elm.SendCommandAsync("010C"));
        Assert.NotNull(ex);
        Assert.True(IsExpectedFailure(ex!), ex!.GetType().FullName);

        await sim.ConnectAsync(new BleDeviceItem());
        await elm.InitializeAsync();
        Assert.True(elm.IsInitialized);
        Assert.True(new PIDDecoder().TryDecode(0x0C, await elm.SendCommandAsync("010C"), out _));
    }

    [Fact]
    public async Task LinkDropWhileSending_IsAnExpectedFailure()
    {
        var (sim, elm) = await InitializedAsync();
        sim.Fault = SimFault.Disconnect;
        sim.FaultAfter = 0;

        var ex = await Record.ExceptionAsync(() => elm.SendCommandAsync("010C"));
        Assert.NotNull(ex);
        Assert.True(IsExpectedFailure(ex!), ex!.GetType().FullName);
        Assert.False(sim.IsConnected);
    }

    [Fact]
    public async Task CancelledRead_StopsWithoutBreakingTheLink()
    {
        var (_, elm) = await InitializedAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => elm.SendCommandAsync("010C", cts.Token));
        Assert.True(new PIDDecoder().TryDecode(0x0C, await elm.SendCommandAsync("010C"), out _));
    }

    // ------------------------------------------------------------------ module session + data-collection tap

    private static void AddModuleAnswers(SimulatedElm327 sim)
    {
        sim.ModuleAnswers["7E0|22F40C"] = "7E80562F40C1AF8";
        sim.ModuleAnswers["7E0|22F189"] = "7E80762F189313233";
        // A module that returns the VIN, and a write the sanitiser must never keep.
        sim.ModuleAnswers["7E0|22F190"] = "7E8101462F190574 6\r7E8215657 5A5A5A314A".Replace(" ", "");
    }

    [Fact]
    public async Task ModuleSession_Reads_AndPublishesOnlySanitisableExchanges()
    {
        var (sim, elm) = await InitializedAsync(AddModuleAnswers);
        var seen = new List<ObdRawExchange>();
        void Handler(ObdRawExchange e) { lock (seen) seen.Add(e); }
        ObdExchangeTap.Observed += Handler;
        var queue = new CollectionQueue(Path.Combine(Path.GetTempPath(), "gcar-sim-" + Guid.NewGuid().ToString("N") + ".json"));
        try
        {
            string? answer = null;
            await elm.RunDtcModuleSessionAsync(async session =>
            {
                answer = await session.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x22, 0xF4, 0x0C }, false, default);
                await session.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x22, 0xF1, 0x89 }, false, default);
                await session.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x22, 0xF1, 0x90 }, false, default);
            });

            Assert.Contains("62F40C1AF8", answer);
            Assert.Equal(3, seen.Count);

            foreach (var exchange in seen)
                CollectionIntake.TryAdd(queue, exchange, "VOLKSWAGEN", "GOLF", "2015", "ISO 15765-4");

            var kept = queue.Peek(10);
            Assert.Equal(new[] { "22F189", "22F40C" }, kept.Select(k => k.Exchange.Request).OrderBy(x => x));
            Assert.DoesNotContain(kept, k => k.Exchange.Request == "22F190");
        }
        finally
        {
            ObdExchangeTap.Observed -= Handler;
            queue.Clear();
        }
    }

    [Fact]
    public async Task FailingTapSubscriber_DoesNotBreakTheDiagnosticRead()
    {
        var (_, elm) = await InitializedAsync(AddModuleAnswers);
        void Throwing(ObdRawExchange e) => throw new InvalidOperationException("observer bug");
        ObdExchangeTap.Observed += Throwing;
        try
        {
            string? answer = null;
            await elm.RunDtcModuleSessionAsync(async session =>
                answer = await session.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x22, 0xF4, 0x0C }, false, default));

            Assert.Contains("62F40C1AF8", answer);
        }
        finally
        {
            ObdExchangeTap.Observed -= Throwing;
        }
    }

    [Fact]
    public async Task ModuleSession_RefusesToSendWriteServices()
    {
        var (sim, elm) = await InitializedAsync(AddModuleAnswers);
        var sentBefore = sim.Sent.Count;

        var ex = await Record.ExceptionAsync(() => elm.RunDtcModuleSessionAsync(async session =>
            await session.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x2E, 0x12, 0x34, 0x01 }, false, default)));

        Assert.NotNull(ex);
        Assert.DoesNotContain(sim.Sent.Skip(sentBefore), c => c.Replace(" ", "").StartsWith("2E1234", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(SimFault.Garbage)]
    [InlineData(SimFault.Truncated)]
    [InlineData(SimFault.Noise)]
    [InlineData(SimFault.NoData)]
    public async Task ModuleSession_WithCorruptedAnswers_NeverKeepsJunkForCollection(SimFault fault)
    {
        var (sim, elm) = await InitializedAsync(AddModuleAnswers);
        sim.Fault = fault;
        sim.FaultAfter = 0;
        var queue = new CollectionQueue(Path.Combine(Path.GetTempPath(), "gcar-sim-" + Guid.NewGuid().ToString("N") + ".json"));
        void Handler(ObdRawExchange e) => CollectionIntake.TryAdd(queue, e, "VOLKSWAGEN", "GOLF", "2015", "CAN");
        ObdExchangeTap.Observed += Handler;
        try
        {
            var ex = await Record.ExceptionAsync(() => elm.RunDtcModuleSessionAsync(async session =>
                await session.SendAsync(0x7E0, 0x7E8, false, new byte[] { 0x22, 0xF4, 0x0C }, false, default)));
            if (ex is not null)
                Assert.True(IsExpectedFailure(ex), ex.GetType().FullName);

            foreach (var item in queue.Peek(10))
            {
                var lines = item.Exchange.Response.Split('\n');
                Assert.All(lines, l => Assert.True(l == "NO DATA" || (l.Length >= 5 && (l.Length - 3) % 2 == 0 && l.All(Uri.IsHexDigit)), $"junk line kept: '{l}'"));
            }
        }
        finally
        {
            ObdExchangeTap.Observed -= Handler;
            queue.Clear();
        }
    }

    // ------------------------------------------------------------------ passive CAN through the real session

    [Fact]
    public async Task PassiveListening_SendsNoDataCommands_OnlyMonitorAndSetup()
    {
        var (sim, elm) = await InitializedAsync(s => s.MonitorTraffic = "1000102030405060708\r2000000000000000000\r");
        var sentBefore = sim.Sent.Count;
        IReadOnlyList<CanObservation> observed = Array.Empty<CanObservation>();

        await elm.RunDtcModuleSessionAsync(async session =>
            (observed, _, _) = await PassiveCanAnalyzer.CollectAsync(session, false, TimeSpan.FromMilliseconds(700), default));

        Assert.Equal(2, observed.Count);
        foreach (var command in sim.Sent.Skip(sentBefore))
        {
            var isSetup = command.StartsWith("AT", StringComparison.OrdinalIgnoreCase);
            var isMonitor = ObdMonitorCommand.TryParse(command, out _, out var inner) && inner == "ATMA";
            Assert.True(isSetup || isMonitor, $"passive listening sent a data command: '{command}'");
        }
    }

    [Theory]
    [InlineData(SimFault.Garbage)]
    [InlineData(SimFault.Truncated)]
    [InlineData(SimFault.Noise)]
    [InlineData(SimFault.BufferFull)]
    [InlineData(SimFault.NoData)]
    public async Task PassiveListening_WithBadTraffic_ProducesAReportWithoutCrashing(SimFault fault)
    {
        var (sim, elm) = await InitializedAsync(s => s.MonitorTraffic = "1000102030405060708\r1010102030405060708\r");
        sim.Fault = fault;
        sim.FaultAfter = 0;
        IReadOnlyList<CanObservation> observed = Array.Empty<CanObservation>();
        var frames = 0;
        var overflow = false;

        await elm.RunDtcModuleSessionAsync(async session =>
            (observed, frames, overflow) = await PassiveCanAnalyzer.CollectAsync(session, false, TimeSpan.FromMilliseconds(700), default));

        var report = await PassiveCanAnalyzer.AnalyzeAsync(observed, frames, overflow, false, new EmptyDbc(), default);
        Assert.NotEqual(PassiveCanStatus.PossibleMatch, report.Status);
        Assert.Empty(report.Signals);
        if (fault == SimFault.BufferFull)
            Assert.True(report.BufferOverflow);
    }

    [Fact]
    public async Task PassiveListening_LinkDrop_SurfacesAsAnExpectedFailure()
    {
        var (sim, elm) = await InitializedAsync(s => s.MonitorTraffic = "1000102030405060708\r");
        sim.Fault = SimFault.Disconnect;
        sim.FaultAfter = 0;

        var ex = await Record.ExceptionAsync(() => elm.RunDtcModuleSessionAsync(async session =>
            await PassiveCanAnalyzer.CollectAsync(session, false, TimeSpan.FromMilliseconds(700), default)));

        if (ex is not null)
            Assert.True(IsExpectedFailure(ex), ex.GetType().FullName);
    }

    private sealed class EmptyDbc : IDbcSource
    {
        public Task<IReadOnlyList<DbcMessageInfo>> FindMessagesAsync(IReadOnlyCollection<int> ids, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<DbcMessageInfo>>(Array.Empty<DbcMessageInfo>());
    }
}

[CollectionDefinition("ObdExchangeTap", DisableParallelization = true)]
public sealed class ObdExchangeTapCollection;
