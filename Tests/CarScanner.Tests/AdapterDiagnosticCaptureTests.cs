using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

[CollectionDefinition("DiagnosticCapture")]
public sealed class DiagnosticCaptureCollection;

/// <summary>
/// Stage 0: the raw capture is controlled — off unless switched on, switches itself off, bounded, in memory, sanitized —
/// and it observes only: switching it on never changes what the protocol layer sends or returns. The identifiers used as
/// markers are made up for these tests.
/// </summary>
[Collection("DiagnosticCapture")]
public sealed class AdapterDiagnosticCaptureTests : IDisposable
{
    private DateTime _now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    public AdapterDiagnosticCaptureTests()
    {
        AdapterDiagnosticCapture.Disable();
        AdapterDiagnosticCapture.Clear();
        AdapterDiagnosticCapture.UtcNow = () => _now;
    }

    public void Dispose()
    {
        AdapterDiagnosticCapture.Disable();
        AdapterDiagnosticCapture.Clear();
        AdapterDiagnosticCapture.UtcNow = static () => DateTime.UtcNow;
    }

    private static void Record(string command, string? reply, int attempt = 1, int attempts = 1, double budgetSeconds = 2.5, int milliseconds = 12) =>
        AdapterDiagnosticCapture.RecordReply(command, attempt, attempts, TimeSpan.FromSeconds(budgetSeconds), TimeSpan.FromMilliseconds(milliseconds), reply);

    // ------------------------------------------------------------------ control

    [Fact]
    public void ItIsOffByDefault_AndRecordsNothing()
    {
        Assert.False(AdapterDiagnosticCapture.IsEnabled);

        Record("TESTMARK-A", "7E8 04 62 11 9F 80");
        AdapterDiagnosticCapture.RecordFailure("TESTMARK-A", 1, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), new TimeoutException());
        AdapterDiagnosticCapture.RecordTimeout("TESTMARK-A", 1, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Assert.Equal(0, AdapterDiagnosticCapture.Count);
        Assert.Equal(string.Empty, AdapterDiagnosticCapture.Export());
    }

    [Fact]
    public void SwitchedOn_ItRecordsTheRawReply_WithAttemptBudgetTimeAndKind()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(30));

        Record("TESTMARK-B", "7E8 06 62 DD 01 03 B5 8F\r\r>", attempt: 2, attempts: 3, budgetSeconds: 2.5, milliseconds: 412);

        var export = AdapterDiagnosticCapture.Export();
        Assert.Contains("raw capture (sanitized", export);
        Assert.Contains("TESTMARK-B try 2/3", export);
        Assert.Contains("wait 2.5s", export);
        Assert.Contains("took 412 ms", export);
        Assert.Contains("-> Data", export);
        Assert.Contains("7E8 06 62 DD 01 03 B5 8F\\r\\r>", export);     // the framing is visible: carriage returns and the prompt
    }

    [Fact]
    public void ItSwitchesItselfOff()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(10));
        Assert.True(AdapterDiagnosticCapture.IsEnabled);

        _now += TimeSpan.FromMinutes(9);
        Assert.True(AdapterDiagnosticCapture.IsEnabled);

        _now += TimeSpan.FromMinutes(2);
        Assert.False(AdapterDiagnosticCapture.IsEnabled);

        var before = AdapterDiagnosticCapture.Count;   // unrelated tests may have written while it was on; nothing can once it is off
        Record("TESTMARK-C", "OK");
        Assert.Equal(before, AdapterDiagnosticCapture.Count);
        Assert.DoesNotContain("TESTMARK-C", AdapterDiagnosticCapture.Export());
    }

    [Fact]
    public void ItCanBeSwitchedOnForAtMostTwoHours()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromDays(3));

        _now += TimeSpan.FromMinutes(119);
        Assert.True(AdapterDiagnosticCapture.IsEnabled);

        _now += TimeSpan.FromMinutes(2);
        Assert.False(AdapterDiagnosticCapture.IsEnabled);
    }

    [Fact]
    public void TheTimeLeft_CountsDown_AndIsZeroWhenItIsOffOrHasSwitchedItselfOff()
    {
        Assert.Equal(TimeSpan.Zero, AdapterDiagnosticCapture.Remaining);

        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(30), AdapterDiagnosticCapture.Remaining);

        _now += TimeSpan.FromMinutes(12);
        Assert.Equal(TimeSpan.FromMinutes(18), AdapterDiagnosticCapture.Remaining);

        _now += TimeSpan.FromMinutes(19);
        Assert.Equal(TimeSpan.Zero, AdapterDiagnosticCapture.Remaining);     // expired, never negative
        Assert.False(AdapterDiagnosticCapture.IsEnabled);

        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(5));
        AdapterDiagnosticCapture.Disable();
        Assert.Equal(TimeSpan.Zero, AdapterDiagnosticCapture.Remaining);
    }

    [Fact]
    public void TheTimeLeft_NeverExceedsTheTwoHourLimit()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromDays(3));

        Assert.Equal(TimeSpan.FromHours(2), AdapterDiagnosticCapture.Remaining);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ItRefusesANonPositiveDuration(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void SwitchingOff_KeepsWhatWasCaptured_UntilItIsCleared()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(5));
        Record("TESTMARK-D", "OK");

        AdapterDiagnosticCapture.Disable();
        Assert.False(AdapterDiagnosticCapture.IsEnabled);
        Assert.Contains("TESTMARK-D", AdapterDiagnosticCapture.Export());

        AdapterDiagnosticCapture.Clear();
        Assert.Equal(string.Empty, AdapterDiagnosticCapture.Export());
    }

    [Fact]
    public void ItKeepsAtMostCapacityEntries_NewestLast()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(5));

        for (var i = 0; i < AdapterDiagnosticCapture.Capacity + 50; i++)
            Record($"TESTMARK-E{i}", "OK");

        Assert.Equal(AdapterDiagnosticCapture.Capacity, AdapterDiagnosticCapture.Count);
        var export = AdapterDiagnosticCapture.Export();
        Assert.DoesNotContain("TESTMARK-E0 try", export);
        Assert.Contains($"TESTMARK-E{AdapterDiagnosticCapture.Capacity + 49} try", export);
    }

    [Fact]
    public void TheSwitchOn_AndOff_AreWrittenToTheConnectionLog()
    {
        var seen = new List<string>();
        LinkEventLog.Listener.Value = seen.Add;
        try
        {
            AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(30));
            AdapterDiagnosticCapture.Disable();
        }
        finally
        {
            LinkEventLog.Listener.Value = null;
        }

        Assert.Contains(seen, line => line.Contains("Raw capture ON for 30 min"));
        Assert.Contains(seen, line => line.Contains("Raw capture OFF"));
    }

    // ------------------------------------------------------------------ what is never stored

    [Theory]
    [InlineData("0902")]
    [InlineData("09 02")]
    [InlineData("09E7")]
    [InlineData("22F190")]
    [InlineData("22 F1 90")]
    [InlineData("22F1A7")]
    [InlineData("1A90")]
    [InlineData("1AE7")]
    public void TheReplyToAnIdentificationRequest_IsNeverStored(string command)
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(5));
        // A VIN-shaped answer, as hex bytes of its ASCII text (made-up VIN).
        const string vinReply = "7E8 10 14 49 02 01 31 48 47 43 4D 38 32 36 33 33 41 30 30 34 33 35 32";

        Record(command, vinReply);

        var export = AdapterDiagnosticCapture.Export();
        Assert.Contains("<not stored: identification request>", export);
        Assert.DoesNotContain("49 02 01", export);
        Assert.DoesNotContain("31 48 47", export);
        Assert.DoesNotContain("1HGCM", export);
    }

    [Theory]
    [InlineData("0902", true)]
    [InlineData("09", true)]
    [InlineData("22F190", true)]
    [InlineData("22F1A7", true)]
    [InlineData("1A90", true)]
    [InlineData("0100", false)]
    [InlineData("010C", false)]
    [InlineData("22DD01", false)]
    [InlineData("22119F", false)]
    [InlineData("22F0A0", false)]
    [InlineData("ATZ", false)]
    [InlineData("ATSH7E1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyIdentificationRequests_AreRecognised(string? command, bool expected) =>
        Assert.Equal(expected, AdapterDiagnosticCapture.IsIdentificationRequest(command));

    [Theory]
    [InlineData("VIN 1HGCM82633A004352 end", "<VIN-like text removed>", "1HGCM82633A004352")]
    [InlineData("WVWZZZ1KZAW123456", "<VIN-like text removed>", "WVWZZZ1KZAW123456")]
    [InlineData("x 1g1ra6e49eu123456 y", "<VIN-like text removed>", "1g1ra6e49eu123456")]
    [InlineData("31 48 47 43 4D 38 32 36 33 33 41 30 30 34 33 35 32", "<VIN-like bytes removed>", "31 48 47")]
    [InlineData("314847434D3832363333413030343335320D", "<VIN-like bytes removed>", "314847")]
    [InlineData("paired with AA:BB:CC:DD:EE:FF today", "<mac>", "AA:BB:CC")]
    [InlineData("paired with aa-bb-cc-dd-ee-ff today", "<mac>", "aa-bb-cc")]
    public void VinLikeText_VinBytes_AndMacAddresses_AreReplaced(string text, string replacement, string secret)
    {
        var clean = AdapterDiagnosticCapture.Sanitize(text, 200);

        Assert.Contains(replacement, clean);
        Assert.DoesNotContain(secret, clean);
    }

    [Theory]
    [InlineData("7E8 06 62 DD 01 03 B5 8F")]
    [InlineData("7E80762DD0103B58F")]                                  // seventeen hex characters, but a frame, not a VIN
    [InlineData("7E8 10 08 62 11 9F 80 7E8 21 00 00 00 00 00 00 00")]
    [InlineData("41 00 BE 3F A8 13")]
    [InlineData("ELM327 v1.5")]
    [InlineData("NO DATA")]
    public void OrdinaryFramesAndReplies_AreLeftAlone(string text) =>
        Assert.Equal(text, AdapterDiagnosticCapture.Sanitize(text, 200));

    [Fact]
    public void LongReplies_AreTruncated()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(5));

        Record("TESTMARK-F", new string('A', 1000));

        var line = AdapterDiagnosticCapture.Export().Split('\n').Single(item => item.Contains("TESTMARK-F"));
        Assert.True(line.Length < 450, $"{line.Length} characters");
        Assert.Contains("…(+", line);
    }

    [Fact]
    public void Failures_AreRecordedWithTheLayerTheyPointAt()
    {
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(5));

        AdapterDiagnosticCapture.RecordFailure("TESTMARK-G1", 1, 3, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(2003), new TimeoutException());
        AdapterDiagnosticCapture.RecordFailure("TESTMARK-G2", 1, 1, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(15), new ObdConnectionLostException("gone"));
        AdapterDiagnosticCapture.RecordFailure("TESTMARK-G3", 1, 1, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(70), new OperationCanceledException());
        AdapterDiagnosticCapture.RecordTimeout("TESTMARK-G4", 3, 3, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(2001));

        var lines = AdapterDiagnosticCapture.Export().Split('\n');
        Assert.Contains(lines, line => line.Contains("TESTMARK-G1") && line.Contains("TimedOut [transport]") && line.Contains("took 2003 ms"));
        Assert.Contains(lines, line => line.Contains("TESTMARK-G2") && line.Contains("LinkLost [transport]") && line.Contains("ObdConnectionLostException"));
        Assert.Contains(lines, line => line.Contains("TESTMARK-G3") && line.Contains("-> Cancelled"));
        Assert.Contains(lines, line => line.Contains("TESTMARK-G4 try 3/3") && line.Contains("TimedOut [transport]"));
    }

    [Fact]
    public void ABriefPieceOfAdapterText_IsOneShortSanitizedLine()
    {
        Assert.Equal("ELM327 v1.5", AdapterDiagnosticCapture.Brief("\r\rELM327 v1.5\r\r>"));
        Assert.Equal("no reply", AdapterDiagnosticCapture.Brief(null));
        Assert.Equal("no reply", AdapterDiagnosticCapture.Brief(">"));
        Assert.Equal(new string('x', 40) + "…(+60)", AdapterDiagnosticCapture.Brief(new string('x', 100)));
        Assert.DoesNotContain("1HGCM82633A004352", AdapterDiagnosticCapture.Brief("1HGCM82633A004352"));
    }

    [Theory]
    [InlineData(0.1, "0.1")]
    [InlineData(0.3, "0.3")]
    [InlineData(2.5, "2.5")]
    [InlineData(5.0, "5")]
    public void DurationsInTheLogs_AlwaysUseADecimalPoint_WhateverTheLanguageOfThePhone(double seconds, string expected)
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");   // decimal comma
        try
        {
            Assert.Equal(expected, AdapterDiagnosticCapture.Seconds(TimeSpan.FromSeconds(seconds)));

            AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(1));
            Record("TESTMARK-H", "OK", budgetSeconds: seconds);
            Assert.Contains($"TESTMARK-H try 1/1, wait {expected}s", AdapterDiagnosticCapture.Export());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("  ", "unknown")]
    [InlineData(" Classic SPP ", "Classic SPP")]
    [InlineData("BLE", "BLE")]
    public void TheTransportLabel_FallsBackToUnknown_AndIsTrimmed(string? label, string expected) =>
        Assert.Equal(expected, AdapterDiagnosticCapture.NormalizeLabel(label));

    // ------------------------------------------------------------------ it observes, it does not act

    [Fact]
    public async Task SwitchingTheCaptureOn_ChangesNeitherWhatIsSentNorWhatIsReturned()
    {
        // The same read on two identical simulated adapters, one with the capture on: same commands in the same order, same answer.
        var request = new byte[] { 0x22, 0xAB, 0x12 };
        var address = new ServiceDiagnosticAddress(0x7E0, 0x7E8, false, null, "11-bit CAN", "test module");

        static SimulatedElm327 Adapter()
        {
            var sim = new SimulatedElm327();
            sim.ModuleAnswers["7E0|22AB12"] = "7E8 04 62 AB 12 80";
            return sim;
        }

        var quietSim = Adapter();
        var quiet = await AdapterRigs.CreateAsync(quietSim);
        quietSim.Sent.Clear();
        var quietAnswer = await quiet.Obd.ReadPhysicalCanReadOnlyAsync(address, request, TimeSpan.FromSeconds(2));

        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(5));
        var watchedSim = Adapter();
        var watched = await AdapterRigs.CreateAsync(watchedSim);
        watchedSim.Sent.Clear();
        var watchedAnswer = await watched.Obd.ReadPhysicalCanReadOnlyAsync(address, request, TimeSpan.FromSeconds(2));

        Assert.Equal(quietAnswer, watchedAnswer);
        Assert.Equal(quietSim.Sent, watchedSim.Sent);
        Assert.Equal(quiet.Elm.LastPhysicalReadSetupNote, watched.Elm.LastPhysicalReadSetupNote);
        Assert.Contains("62 AB 12 80", watchedAnswer);

        // ...and the capture saw the read request and its raw answer.
        var line = AdapterDiagnosticCapture.Export().Split('\n').FirstOrDefault(item => item.Contains(" 22AB12 try 1/1"));
        Assert.NotNull(line);
        Assert.Contains("-> Data", line);
        Assert.Contains("62 AB 12 80", line);
    }
}
