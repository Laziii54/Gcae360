using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Which ways of talking to the adapter reach the opt-in collection, checked against the real protocol layer and
/// the software ELM327. Simulation only: it proves the app's own routing, not what any car answers.
/// </summary>
public sealed class AdapterPathCollectionTests : IDisposable
{
    private static readonly AsyncLocal<bool> Mine = new();     // the exchange tap is static; see CollectionBackendContractTests
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gcar-paths-" + Guid.NewGuid().ToString("N"));
    private readonly List<Action> _cleanup = new();

    public void Dispose()
    {
        foreach (var c in _cleanup) c();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed record Rig(SimulatedElm327 Sim, Elm327Protocol Elm, OBDService Obd, CollectionPipeline Pipe, CollectionQueue Queue, bool[] Consent);

    // Not async on purpose: the flag must be set in the test's own context, not inside an async method's copy of it.
    private Task<Rig> SetupAsync(bool consent = true)
    {
        Mine.Value = true;
        return SetupCoreAsync(consent);
    }

    private async Task<Rig> SetupCoreAsync(bool consent)
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());

        var enabled = new[] { consent };
        var queue = new CollectionQueue(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"));
        var pipe = new CollectionPipeline(queue, () => enabled[0], () => new VehicleSnapshot("CHEVROLET", "VOLT", "2014", "ISO 15765-4"), new FakeUploader());
        await obd.InitializeAsync();   // start-up probes are not part of what these tests look at

        Action<ObdRawExchange> filter = e => { if (Mine.Value) pipe.Observe(e); };
        ObdExchangeTap.Observed += filter;
        _cleanup.Add(() => { ObdExchangeTap.Observed -= filter; pipe.Dispose(); });
        return new Rig(sim, elm, obd, pipe, queue, enabled);
    }

    private static async Task<CollectedExchange[]> CollectedAsync(Rig rig)
    {
        await rig.Pipe.DrainAsync();
        return rig.Queue.Peek(100).Select(i => i.Exchange).ToArray();
    }

    private static readonly ServiceDiagnosticAddress Ecu = new(0x7E0, 0x7E8, false, null, "11-bit CAN", "test");

    // ---- paths that carry module reads ---------------------------------------------------------

    [Fact]
    public async Task IsoTpPhysicalRead_IsCollected_AsOneFramePerToken()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E0|22F40C"] = "7E8 05 62 F4 0C 1A F8";

        await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0xF4, 0x0C }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));

        var item = Assert.Single(await CollectedAsync(rig));
        Assert.Equal("22F40C", item.Request);
        Assert.Equal("7E80562F40C1AF8", item.Response);
        Assert.Equal(0x7E0, item.TxCanId);
        Assert.Equal(0x7E8, item.RxCanId);
    }

    [Fact]
    public async Task PhysicalReadOnlyRead_IsCollected_ThroughTheServiceReaderEntryPoint()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E0|22F40C"] = "7E8 05 62 F4 0C 1A F8";

        // OBDService.ReadPhysicalCanReadOnlyAsync is what the Service reader, EV/Fuel, Mileage and the Coding page's reads call.
        await rig.Obd.ReadPhysicalCanReadOnlyAsync(Ecu, new byte[] { 0x22, 0xF4, 0x0C }, TimeSpan.FromSeconds(1));

        var item = Assert.Single(await CollectedAsync(rig));
        Assert.Equal("22F40C", item.Request);
        Assert.Equal("7E80562F40C1AF8", item.Response);
    }

    [Fact]
    public async Task WithoutAReceiveId_TheReplyHeaderIsUsed()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E4|22434F"] = "7EC 05 62 43 4F 2D 00";

        await rig.Obd.ReadPhysicalCanReadOnlyAsync(new ServiceDiagnosticAddress(0x7E4, null, false, null, "11-bit CAN", "t"),
            new byte[] { 0x22, 0x43, 0x4F }, TimeSpan.FromSeconds(1));

        var item = Assert.Single(await CollectedAsync(rig));
        Assert.Equal(0x7EC, item.RxCanId);
    }

    [Fact]
    public async Task ReadServices19And21_AreCollectedToo()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E0|1902FF"] = "7E8 06 59 02 FF 01 33 00";
        rig.Sim.ModuleAnswers["7E3|2101"] = "7EB 04 61 01 55 66";

        await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x19, 0x02, 0xFF }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));
        await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x21, 0x01 }, 0x7E3, 0x7EB, false, TimeSpan.FromSeconds(1));

        var requests = (await CollectedAsync(rig)).Select(i => i.Request).OrderBy(r => r).ToArray();
        Assert.Equal(new[] { "1902FF", "2101" }, requests);
    }

    // ---- what must never be collected ------------------------------------------------------------

    [Theory]
    [InlineData("1003")]        // extended session (Coding, Service)
    [InlineData("1001")]        // default session
    [InlineData("3E00")]        // tester present
    [InlineData("2E060001")]    // write by identifier (coding / service reset)
    [InlineData("2701")]        // security access
    [InlineData("31010203")]    // routine
    public async Task SessionWriteSecurityAndRoutineRequests_AreNeverCollected(string hex)
    {
        var rig = await SetupAsync();
        var payload = Convert.FromHexString(hex);
        rig.Sim.ModuleAnswers["7E0|" + hex] = "7E8 03 7F " + hex[..2] + " 22";

        await rig.Elm.SendCanIsoTpPayloadAsync(payload, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));

        Assert.Empty(await CollectedAsync(rig));
    }

    [Fact]
    public async Task CodingRequestPath_WritesAndSessions_ReachNothing_ButDoNotBreak()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E0|1003"] = "7E8 02 50 03";
        rig.Sim.ModuleAnswers["7E0|2E060001"] = "7E8 03 6E 06 00";

        var session = await rig.Obd.SendCodingRequestAsync(Ecu, new byte[] { 0x10, 0x03 }, TimeSpan.FromSeconds(1));
        var write = await rig.Obd.SendCodingRequestAsync(Ecu, new byte[] { 0x2E, 0x06, 0x00, 0x01 }, TimeSpan.FromSeconds(1));

        Assert.Contains("50", session);                       // the coding path still returns the car's reply
        Assert.Contains("6E", write);
        Assert.Empty(await CollectedAsync(rig));
    }

    [Fact]
    public async Task AnAnswerCarryingAVin_IsDropped_OnTheNewPathsToo()
    {
        var rig = await SetupAsync();
        var vin = Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes("1G1RA6E49EU123456"));
        // 62 F1 87 followed by 17 VIN characters over three frames, spaced bytes as the adapter prints them
        string Spaced(string hex) => string.Join(" ", Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
        rig.Sim.ModuleAnswers["7E0|22F187"] = "7E8 10 14 62 F1 87 " + Spaced(vin[..6]) + " 7E8 21 " + Spaced(vin[6..20]) + " 7E8 22 " + Spaced(vin[20..]);

        await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0xF1, 0x87 }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));

        Assert.Empty(await CollectedAsync(rig));
    }

    [Theory]
    [InlineData("22F190")]   // VIN DID
    [InlineData("22F18C")]   // serial number
    public async Task IdentifyingDids_AreDropped_OnTheNewPathsToo(string hex)
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E0|" + hex] = "7E8 05 62 " + hex[2..4] + " " + hex[4..6] + " AA BB";

        await rig.Elm.SendCanIsoTpPayloadAsync(Convert.FromHexString(hex), 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));

        Assert.Empty(await CollectedAsync(rig));
    }

    [Fact]
    public async Task NothingIsCollected_WhenConsentIsOff()
    {
        var rig = await SetupAsync(consent: false);
        rig.Sim.ModuleAnswers["7E0|22F40C"] = "7E8 05 62 F4 0C 1A F8";

        await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0xF4, 0x0C }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));
        await rig.Obd.ReadPhysicalCanReadOnlyAsync(Ecu, new byte[] { 0x22, 0xF4, 0x0C }, TimeSpan.FromSeconds(1));

        Assert.Empty(await CollectedAsync(rig));
    }

    [Fact]
    public async Task NoAnswer_IsRecordedAsAStatus_NotAsData()
    {
        var rig = await SetupAsync();   // the simulator answers NO DATA for unknown requests

        await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0x12, 0x34 }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));

        var item = Assert.Single(await CollectedAsync(rig));
        Assert.Equal("NO DATA", item.Response);
    }

    [Fact]
    public async Task MultiLineFormattedReply_IsNotReported_AndNothingBreaks()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E0|22F40C"] = "7E8 00B\r0: 62 F4 0C 01 02 03\r1: 04 05 06";

        var raw = await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0xF4, 0x0C }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));

        Assert.Contains("62", raw);                           // the caller still gets the full reply
        Assert.Empty(await CollectedAsync(rig));
    }

    [Fact]
    public async Task ExtendedAddressReads_AreNotReported_BecauseTheyHaveNoCanIdPair()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["6F1|22F40C"] = "612 05 62 F4 0C 01 02";

        await rig.Obd.ReadPhysicalCanReadOnlyAsync(new ServiceDiagnosticAddress(0x6F1, 0x612, false, 0x12, "BMW", "t"),
            new byte[] { 0x22, 0xF4, 0x0C }, TimeSpan.FromSeconds(1));

        Assert.Empty(await CollectedAsync(rig));
    }

    [Fact]
    public async Task AFailingObserver_DoesNotChangeTheReplyTheCallerGets()
    {
        var rig = await SetupAsync();
        rig.Sim.ModuleAnswers["7E0|22F40C"] = "7E8 05 62 F4 0C 1A F8";
        var before = await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0xF4, 0x0C }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));

        void Bad(ObdRawExchange _) => throw new InvalidOperationException("observer failure");
        ObdExchangeTap.Observed += Bad;
        try
        {
            var after = await rig.Elm.SendCanIsoTpPayloadAsync(new byte[] { 0x22, 0xF4, 0x0C }, 0x7E0, 0x7E8, false, TimeSpan.FromSeconds(1));
            Assert.Equal(before, after);
        }
        finally { ObdExchangeTap.Observed -= Bad; }
    }

    // ---- frame rebuilding ------------------------------------------------------------------------

    [Fact]
    public void FramesForTap_RebuildsFramesFromCanIdTokens()
    {
        Assert.Equal("7E80562F40C1AF8", Elm327Protocol.FramesForTap("7E8 05 62 F4 0C 1A F8", false, out var id));
        Assert.Equal(0x7E8, id);
        Assert.Equal("7E8101462F18731 7E82147", Elm327Protocol.FramesForTap("7E8 10 14 62 F1 87 31 7E8 21 47", false, out _));
        Assert.Equal("18DAF11062F40C1A", Elm327Protocol.FramesForTap("18DAF110 62 F4 0C 1A", true, out var id29));
        Assert.Equal(0x18DAF110, id29);
    }

    [Fact]
    public void FramesForTap_KeepsStatusWords_AndRefusesWhatItCannotRebuild()
    {
        Assert.Equal("NO DATA", Elm327Protocol.FramesForTap("NO DATA", false, out var id));
        Assert.Null(id);
        Assert.Null(Elm327Protocol.FramesForTap("7E8 00B 0: 62 F4 0C", false, out _));
        Assert.Null(Elm327Protocol.FramesForTap("", false, out _));
        Assert.Null(Elm327Protocol.FramesForTap(null, false, out _));
    }

    // ---- no silent bypass ------------------------------------------------------------------------

    /// <summary>
    /// Every public way of sending data through the protocol layer must be accounted for. If this list changes, a
    /// new adapter path was added: decide whether it reports to the collection tap (read-only module/standard
    /// traffic) or why it does not, then update this list.
    /// </summary>
    [Fact]
    public void EveryPublicSendPath_IsAccountedFor()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Services/OBD/Elm327Protocol.cs"));

        var found = Regex.Matches(source, @"public (?:async )?Task<string> (\w+)\(")
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(n => n).ToArray();

        var accounted = new Dictionary<string, string>
        {
            ["SendCommandAsync"] = "standard: reported when the request is Mode 01/03/07/0A",
            ["SendAsync"] = "module session: reported",
            ["SendCanIsoTpPayloadAsync"] = "physical ISO-TP: reported for services 0x19/0x21/0x22 only",
            ["SendPhysicalCanReadOnlyPayloadAsync"] = "physical read-only: reported (not with extended addressing)",
            ["SendLegacyPhysicalPayloadAsync"] = "non-CAN physical request: no caller in the app, not reported",
            ["SendGmlanDtcRequestAsync"] = "GMLAN $A9 request: not an allowed read service for collection",
            ["MonitorBroadcastAsync"] = "passive listening: no request/answer pair",
            ["MonitorRawAsync"] = "passive listening: no request/answer pair",
            ["ListenBroadcastAsync"] = "passive listening: no request/answer pair",
            ["GetAdapterVersionAsync"] = "adapter command, not vehicle data",
            ["GetAdapterVoltageAsync"] = "adapter command, not vehicle data"
        };

        Assert.Equal(accounted.Keys.OrderBy(n => n).ToArray(), found);
    }

    // ---- a VIN must not travel in the vehicle description either ---------------------------------

    [Theory]
    [InlineData("1G1RA6E49EU123456", true)]
    [InlineData("WVWZZZ1KZAW123456", true)]
    [InlineData("Volt 1G1RA6E49EU123456", true)]
    [InlineData("Land Rover Range Rover Sport", false)]
    [InlineData("Mercedes-Benz", false)]
    [InlineData("ISO 15765-4 (CAN 11/500)", false)]
    [InlineData("2014", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeVin_RecognisesVinShapedText(string? text, bool expected) =>
        Assert.Equal(expected, CollectionSanitizer.LooksLikeVin(text));

    [Fact]
    public void AVinInTheVehicleDescription_DropsTheSampleOnThePhone()
    {
        var exchange = new ObdRawExchange(0x7E0, 0x7E8, false, "22F40C", "7E80562F40C1AF8");
        Assert.NotNull(CollectionIntake.TryCreate(exchange, "CHEVROLET", "VOLT", "2014", "CAN"));
        Assert.Null(CollectionIntake.TryCreate(exchange, "CHEVROLET", "1G1RA6E49EU123456", "2014", "CAN"));
        Assert.Null(CollectionIntake.TryCreate(exchange, "1G1RA6E49EU123456", "VOLT", "2014", "CAN"));
        Assert.Null(CollectionIntake.TryCreate(exchange, "CHEVROLET", "VOLT", "2014", "VIN 1G1RA6E49EU123456"));
    }

    [Fact]
    public async Task AVinInTheVehicleDescription_IsRejectedByTheServerToo()
    {
        var file = Path.Combine(Path.GetTempPath(), "gcar-vin-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new CarScanner.Api.Services.EcuObservationStore($"Data Source={file}");
            var item = new CarScanner.Api.Services.EcuObservationItem(0x7E0, 0x7E8, false, "22F40C", "7E80562F40C1AF8");
            var bad = new CarScanner.Api.Services.EcuObservationBatch("1", new List<CarScanner.Api.Services.EcuObservationGroup>
            {
                new("CHEVROLET", "1G1RA6E49EU123456", "2014", "CAN", new List<CarScanner.Api.Services.EcuObservationItem> { item })
            });
            var good = new CarScanner.Api.Services.EcuObservationBatch("1", new List<CarScanner.Api.Services.EcuObservationGroup>
            {
                new("CHEVROLET", "VOLT", "2014", "CAN", new List<CarScanner.Api.Services.EcuObservationItem> { item })
            });

            Assert.Equal((0, 1), await store.AddAsync(bad, "0123456789abcdef0123456789abcdef", CancellationToken.None));
            Assert.Empty(await store.LatestAsync(10, CancellationToken.None));
            Assert.Equal((1, 0), await store.AddAsync(good, "0123456789abcdef0123456789abcdef", CancellationToken.None));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(file); } catch { }
        }
    }
}
