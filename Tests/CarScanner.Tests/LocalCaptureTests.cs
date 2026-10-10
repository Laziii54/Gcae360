using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// The local capture recording reuses the upload sanitiser and never talks to the car or the network.
/// Simulation only: these tests show the filtering and the isolation, not that any car answers a given request.
/// </summary>
public sealed class LocalCaptureSessionTests
{
    private static readonly CaptureVehicle Volt = new("Chevrolet", "Volt", "2014", "ISO 15765-4 CAN 11/500");
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static LocalCaptureSession Started(CaptureVehicle? vehicle = null, string? target = "chevrolet-volt")
    {
        var s = new LocalCaptureSession();
        s.Start(target, () => vehicle ?? Volt, () => Now);
        return s;
    }

    private static ObdRawExchange Module(string request, string response, int tx = 0x7E4, int rx = 0x7EC) =>
        new(tx, rx, false, request, response);

    [Fact]
    public void NothingIsRecorded_BeforeStart_AndAfterStop()
    {
        var s = new LocalCaptureSession();
        s.Observe(Module("22434F", "7EC0462434F2D"));
        Assert.Equal(0, s.ItemCount);

        s.Start(null, () => Volt);
        s.Observe(Module("22434F", "7EC0462434F2D"));
        s.Stop();
        s.Observe(Module("22434E", "7EC0462434E2D"));
        Assert.Equal(1, s.ItemCount);
        Assert.False(s.IsRecording);
    }

    [Fact]
    public void ReadOnlyModuleExchange_IsKept_WithVehicleButNoVin()
    {
        var s = Started();
        s.Observe(Module("22434F", "7EC0462434F2D"));

        var report = s.BuildReport("1.0.0");
        var item = Assert.Single(report.Items);
        Assert.Equal("Chevrolet", item.Make);
        Assert.Equal("Volt", item.Model);
        Assert.Equal("22434F", item.Request);
        Assert.Equal(0x7E4, item.Tx);
        Assert.Equal(0x7EC, item.Rx);
        Assert.Equal("2026-10-10", item.ObservedOn);         // a date, never a time
        Assert.Equal("chevrolet-volt", report.TargetId);
    }

    [Theory]
    [InlineData("0902")]     // VIN
    [InlineData("22F190")]   // VIN DID
    [InlineData("22F18C")]   // ECU serial number
    [InlineData("1003")]     // diagnostic session
    [InlineData("2701")]     // security access
    [InlineData("2E1234AA")] // write
    [InlineData("3101FF00")] // routine
    [InlineData("04")]       // clear DTC
    public void IdentifyingOrWritingRequests_AreNeverRecorded(string request)
    {
        var s = Started();
        s.Observe(Module(request, "7EC0462F1900102"));
        Assert.Equal(0, s.ItemCount);
        Assert.Equal(1, s.BuildReport("1").Counts.Rejected);
    }

    [Fact]
    public void AnAnswerThatContainsAVin_IsDropped()
    {
        var s = Started();
        var vinHex = Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes("1G1RA6E49EU123456"));
        // 62 F1 87 + the 17 VIN characters, as three ISO-TP frames
        s.Observe(Module("22F187", "7EC101462F187" + vinHex[..6] + " 7EC21" + vinHex[6..20] + " 7EC22" + vinHex[20..]));
        Assert.Equal(0, s.ItemCount);
    }

    [Fact]
    public void WithoutAKnownVehicle_NothingIsKept_AndItIsCounted()
    {
        var s = new LocalCaptureSession();
        s.Start(null, () => null);
        s.Observe(Module("22434F", "7EC0462434F2D"));
        Assert.Equal(0, s.ItemCount);
        Assert.Equal(1, s.BuildReport("1").Counts.NoVehicle);
    }

    [Fact]
    public void TheSameExchangeTwice_IsKeptOnce()
    {
        var s = Started();
        s.Observe(Module("22434F", "7EC0462434F2D"));
        s.Observe(Module("22434F", "7EC0462434F2D"));
        var counts = s.BuildReport("1").Counts;
        Assert.Equal(1, s.ItemCount);
        Assert.Equal(1, counts.Duplicates);
        Assert.Equal(2, counts.Observed);
    }

    [Fact]
    public void AFullRecording_DropsFurtherItems_WithoutThrowing()
    {
        var s = Started();
        for (var i = 0; i < LocalCaptureSession.MaxItems + 5; i++)
            s.Observe(Module("22" + (0x1000 + i).ToString("X4"), "7EC0462100001"));

        Assert.Equal(LocalCaptureSession.MaxItems, s.ItemCount);
        Assert.Equal(5, s.BuildReport("1").Counts.DroppedFull);
    }

    [Fact]
    public void StandardObd_IsRecordedAsCompatibilitySummary_NeverAsMeasuredValue()
    {
        var s = Started();
        s.Observe(new ObdRawExchange(0x7DF, 0, false, "010C", "41 0C 1A F8", ObdExchangeOrigin.StandardObd, false));

        var item = Assert.Single(s.BuildReport("1").Items);
        Assert.Equal("valid", item.Response);                 // that the PID answered, not the rpm
        Assert.DoesNotContain("1AF8", item.Response, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(s.BuildReport("1").Modules);             // functional requests are not a module
    }

    [Fact]
    public void ReportSeparatesModulesThatSentFramesFromThoseThatDidNot()
    {
        var s = Started();
        s.Observe(Module("22434F", "7EC0462434F2D"));
        s.Observe(Module("221234", "NO DATA", tx: 0x7E7, rx: 0x7EF));

        var modules = s.BuildReport("1").Modules;
        Assert.Equal(2, modules.Count);
        var answered = Assert.Single(modules, m => m.Tx == "7E4");
        Assert.Equal(1, answered.WithFrames);
        var silent = Assert.Single(modules, m => m.Tx == "7E7");
        Assert.Equal(0, silent.WithFrames);
        Assert.Equal(1, silent.StatusOnly);
    }

    [Fact]
    public void Report_IsLabelledAsUnreviewedRawMaterial_AndSerialises()
    {
        var s = Started();
        s.Observe(Module("22434F", "7EC0462434F2D"));
        var json = s.BuildJson("1.0.0");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("gcar360.capture/1", doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("UnreviewedObservation", doc.RootElement.GetProperty("evidenceLevel").GetString());
        Assert.Contains("NOT verification", doc.RootElement.GetProperty("notice").GetString());
        var itemFields = doc.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToArray();
        Assert.DoesNotContain("vin", itemFields);
    }

    [Fact]
    public void StartingAgain_DiscardsThePreviousRecording()
    {
        var s = Started();
        s.Observe(Module("22434F", "7EC0462434F2D"));
        s.Start("x", () => Volt, () => Now);
        Assert.Equal(0, s.ItemCount);
        Assert.Equal(0, s.BuildReport("1").Counts.Observed);
    }

    [Fact]
    public void AFailingVehicleLookup_NeverEscapes()
    {
        var s = new LocalCaptureSession();
        s.Start(null, () => throw new InvalidOperationException("boom"));
        s.Observe(Module("22434F", "7EC0462434F2D"));   // must not throw
        Assert.Equal(0, s.ItemCount);
    }

    // ---- isolation from ordinary diagnostics --------------------------------------------------

    private static async Task<(SimulatedElm327 Sim, OBDService Obd)> ConnectedAsync()
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["0100"] = "41 00 18 00 00 01";
        sim.ObdAnswers["010C"] = "41 0C 1F 40";
        sim.ObdAnswers["010D"] = "41 0D 3C";
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        return (sim, obd);
    }

    [Fact]
    public async Task Recording_DoesNotChangeWhatDiagnosticsReturn_EvenWithAFailingObserver()
    {
        var (_, baselineObd) = await ConnectedAsync();
        var baseline = (await baselineObd.ReadLiveValuesAsync(new[] { 0x0C, 0x0D })).Select(v => (v.Pid, v.Value)).ToArray();

        var session = Started();
        void Bad(ObdRawExchange _) => throw new InvalidOperationException("observer failure");
        ObdExchangeTap.Observed += Bad;
        ObdExchangeTap.Observed += session.Observe;
        try
        {
            var (_, obd) = await ConnectedAsync();
            var recorded = (await obd.ReadLiveValuesAsync(new[] { 0x0C, 0x0D })).Select(v => (v.Pid, v.Value)).ToArray();

            Assert.Equal(baseline, recorded);
            Assert.True(session.ItemCount > 0, "the standard exchanges should have been summarised");
        }
        finally
        {
            ObdExchangeTap.Observed -= session.Observe;
            ObdExchangeTap.Observed -= Bad;
        }
    }

    [Fact]
    public async Task Recording_SendsNothingToTheCar()
    {
        var (sim, obd) = await ConnectedAsync();
        var before = sim.Sent.Count;

        var session = Started();
        session.Observe(Module("22434F", "7EC0462434F2D"));
        session.BuildJson("1");
        await Task.Delay(20);

        Assert.Equal(before, sim.Sent.Count);
        Assert.True(obd.IsInitialized);
    }

    [Fact]
    public async Task ManyObservations_AreCheap()
    {
        var session = Started();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20000; i++)
            session.Observe(Module("22" + (0x1000 + i % 3000).ToString("X4"), "7EC0462100001"));
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 3000, $"20000 observations took {watch.ElapsedMilliseconds} ms");
    }
}
