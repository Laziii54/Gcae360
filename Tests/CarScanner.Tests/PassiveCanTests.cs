using CarScanner.Services.OBD;
using CarScanner.Services.Passive;

namespace CarScanner.Tests;

public sealed class DbcSignalDecoderTests
{
    private static DbcSignal Sig(int start, int length, bool bigEndian, bool signed = false, double factor = 1, double offset = 0,
        double? min = null, double? max = null) =>
        new("test.dbc", 0x100, "S", start, length, bigEndian, signed, factor, offset, min, max, "");

    [Fact]
    public void Intel_ByteAligned()
    {
        var data = new byte[] { 0x11, 0x34, 0x12, 0, 0, 0, 0, 0 };
        Assert.Equal(0x11, DbcSignalDecoder.ReadRaw(Sig(0, 8, false), data));
        Assert.Equal(0x1234, DbcSignalDecoder.ReadRaw(Sig(8, 16, false), data));
    }

    [Fact]
    public void Intel_Unaligned()
    {
        // bits 4..11 of 0xAB 0xCD (little endian): low nibble of byte1 (0xD) << 4 | high nibble of byte0 (0xA)
        var data = new byte[] { 0xAB, 0xCD, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(0xDA, DbcSignalDecoder.ReadRaw(Sig(4, 8, false), data));
    }

    [Fact]
    public void Motorola_ByteAligned_StartBit7IsMsbOfByte0()
    {
        var data = new byte[] { 0x12, 0x34, 0x56, 0, 0, 0, 0, 0 };
        Assert.Equal(0x12, DbcSignalDecoder.ReadRaw(Sig(7, 8, true), data));
        Assert.Equal(0x1234, DbcSignalDecoder.ReadRaw(Sig(7, 16, true), data));
        Assert.Equal(0x3456, DbcSignalDecoder.ReadRaw(Sig(15, 16, true), data));
    }

    [Fact]
    public void Motorola_SignalInsideOneByte_FromCatalogExample()
    {
        // opendbc ESR.dbc CAN_TX_TRACK_WIDTH: start bit 37, 4 bits, big endian, factor 0.5.
        var data = new byte[] { 0, 0, 0, 0, 0b0010_1100, 0, 0, 0 }; // bits 5..2 of byte 4 = 1011 = 11
        Assert.Equal(11, DbcSignalDecoder.ReadRaw(Sig(37, 4, true), data));
        Assert.Equal(5.5, DbcSignalDecoder.Decode(Sig(37, 4, true, factor: 0.5, min: 0, max: 7.5 + 10), data));
    }

    [Fact]
    public void SignedValues_AreSignExtended()
    {
        Assert.Equal(-1, DbcSignalDecoder.ReadRaw(Sig(0, 8, false, signed: true), new byte[] { 0xFF, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal(-2048, DbcSignalDecoder.ReadRaw(Sig(0, 12, false, signed: true), new byte[] { 0x00, 0x08, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal(-1, DbcSignalDecoder.ReadRaw(Sig(7, 16, true, signed: true), new byte[] { 0xFF, 0xFF, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void FactorAndOffset_AreApplied() =>
        Assert.Equal(90.0, DbcSignalDecoder.Decode(Sig(0, 8, false, factor: 1, offset: -40), new byte[] { 130, 0, 0, 0, 0, 0, 0, 0 }));

    [Fact]
    public void ValueOutsideTheDatabaseRange_IsNotShown() =>
        Assert.Null(DbcSignalDecoder.Decode(Sig(0, 8, false, min: 0, max: 100), new byte[] { 200, 0, 0, 0, 0, 0, 0, 0 }));

    [Fact]
    public void ShortFrame_OrBrokenDefinition_YieldsNull()
    {
        Assert.Null(DbcSignalDecoder.ReadRaw(Sig(56, 16, false), new byte[8]));
        Assert.Null(DbcSignalDecoder.ReadRaw(Sig(7, 16, true), new byte[1]));
        Assert.Null(DbcSignalDecoder.ReadRaw(Sig(0, 0, false), new byte[8]));
        Assert.Null(DbcSignalDecoder.ReadRaw(Sig(0, 65, false), new byte[8]));
        Assert.Null(DbcSignalDecoder.ReadRaw(Sig(-1, 8, false), new byte[8]));
        Assert.Null(DbcSignalDecoder.ReadRaw(Sig(0, 8, false), ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void ValueIsNeverNaNOrInfinity() =>
        Assert.Null(DbcSignalDecoder.Decode(Sig(0, 8, false, factor: double.PositiveInfinity), new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
}

public sealed class CanFrameParserTests
{
    [Fact]
    public void ParsesFramesAndSkipsEverythingElse()
    {
        var frames = CanFrameParser.ParseFrames("0A10102030405060708\rSEARCHING...\r0B2AABB\rBUFFER FULL\r0C312G4\r", false).ToArray();
        Assert.Equal(new[] { 0x0A1, 0x0B2 }, frames.Select(f => f.Id));
        Assert.Equal(new byte[] { 0xAA, 0xBB }, frames[1].Data);
    }

    [Fact]
    public void TruncatedFrame_IsDropped() =>
        Assert.Empty(CanFrameParser.ParseFrames("0A1010203040506070", false)); // 17 hex chars after the id → cut mid byte

    [Fact]
    public void Garbage_NeverThrows()
    {
        var text = new string(Enumerable.Range(0, 400).Select(i => (char)(i % 255)).ToArray());
        Assert.Empty(CanFrameParser.ParseFrames(text, false).ToArray());
        Assert.Empty(CanFrameParser.ParseFrames(null!, false).ToArray());
    }

    [Fact]
    public void Extended29Bit_UsesEightDigitIds()
    {
        var frame = Assert.Single(CanFrameParser.ParseFrames("18DAF1100341BE", true));
        Assert.Equal(0x18DAF110, frame.Id);
    }
}

public sealed class PassiveCanAnalyzerTests
{
    private sealed class FakeDbc(Func<IReadOnlyCollection<int>, IReadOnlyList<DbcMessageInfo>> lookup) : IDbcSource
    {
        public Task<IReadOnlyList<DbcMessageInfo>> FindMessagesAsync(IReadOnlyCollection<int> ids, CancellationToken token) =>
            Task.FromResult(lookup(ids));
    }

    private static DbcSignal Rpm(string file, int id) =>
        new(file, id, "RPM", 7, 16, true, false, 0.25, 0, 0, 9000, "rpm");

    private static DbcMessageInfo Msg(string file, int id, int dlc = 8) =>
        new(file, id, "MSG_" + id.ToString("X"), dlc, new[] { Rpm(file, id) });

    private static CanObservation Obs(int id, params byte[] data) => new(id, 10, data.Length == 8 ? data : data.Concat(new byte[8 - data.Length]).ToArray());

    [Fact]
    public void Aggregate_KeepsLastFramePerId_AndSurvivesCorruption()
    {
        var observations = PassiveCanAnalyzer.Aggregate(new[]
        {
            "1000102030405060708\r1000A0B0C0D0E0F1011\r\u0001@@\r2000000000000000000\rBUFFER FULL\r",
            "100AAAAAAAAAAAAAAAA\rZZZ\r"
        }, ext29: false, out var frames, out var overflow);

        Assert.True(overflow);
        Assert.Equal(4, frames);
        var first = observations.First(o => o.Id == 0x100);
        Assert.Equal(3, first.Count);
        Assert.Equal(Enumerable.Repeat((byte)0xAA, 8), first.LastData);
        Assert.Contains(observations, o => o.Id == 0x200);
    }

    [Fact]
    public async Task NoFrames_IsReportedAsNoTraffic()
    {
        var report = await PassiveCanAnalyzer.AnalyzeAsync(Array.Empty<CanObservation>(), 0, false, false, new FakeDbc(_ => throw new InvalidOperationException()), default);
        Assert.Equal(PassiveCanStatus.NoTraffic, report.Status);
    }

    [Fact]
    public async Task OnlyDiagnosticIds_AreNeverMatchedToADatabase()
    {
        var called = false;
        var report = await PassiveCanAnalyzer.AnalyzeAsync(
            new[] { Obs(0x7E8, 1), Obs(0x7DF, 2), Obs(0x7E0, 3) }, 5, false, false,
            new FakeDbc(_ => { called = true; return Array.Empty<DbcMessageInfo>(); }), default);

        Assert.Equal(PassiveCanStatus.OnlyDiagnosticTraffic, report.Status);
        Assert.False(called);
        Assert.Empty(report.Signals);
    }

    [Fact]
    public async Task TooFewMatches_ShowsNothing()
    {
        var observations = Enumerable.Range(0x100, 10).Select(i => Obs(i, 1, 2)).ToArray();
        var report = await PassiveCanAnalyzer.AnalyzeAsync(observations, 100, false, false,
            new FakeDbc(ids => ids.Take(3).Select(i => Msg("a.dbc", i)).ToArray()), default);

        Assert.Equal(PassiveCanStatus.NoDatabaseMatch, report.Status);
        Assert.Empty(report.Signals);
        Assert.Null(report.Best);
    }

    [Fact]
    public async Task ManyMatches_GivePossibleMatch_WithDecodedUnverifiedValues()
    {
        var observations = Enumerable.Range(0x100, 6).Select(i => Obs(i, 0x1A, 0xF8)).ToArray(); // 0x1AF8 * 0.25 = 1726
        var report = await PassiveCanAnalyzer.AnalyzeAsync(observations, 100, false, false,
            new FakeDbc(ids => ids.Select(i => Msg("honda.dbc", i)).Concat(new[] { Msg("other.dbc", 0x100) }).ToArray()), default);

        Assert.Equal(PassiveCanStatus.PossibleMatch, report.Status);
        Assert.Equal("honda.dbc", report.Best!.DbcFile);
        Assert.Equal(6, report.Best.MatchedIds);
        Assert.Equal(6, report.Signals.Count);
        Assert.All(report.Signals, s => Assert.Equal(1726.0, s.Value));
    }

    [Fact]
    public async Task DifferentFrameLength_IsNotAMatch()
    {
        var observations = Enumerable.Range(0x100, 6).Select(i => Obs(i, 1)).ToArray();
        var report = await PassiveCanAnalyzer.AnalyzeAsync(observations, 100, false, false,
            new FakeDbc(ids => ids.Select(i => Msg("x.dbc", i, dlc: 4)).ToArray()), default);

        Assert.Equal(PassiveCanStatus.NoDatabaseMatch, report.Status);
    }

    [Fact]
    public async Task ValueOutsideTheDatabaseRange_IsDropped_NotShown()
    {
        var observations = Enumerable.Range(0x100, 5).Select(i => Obs(i, 0xFF, 0xFF)).ToArray(); // 16383.75 rpm > max 9000
        var report = await PassiveCanAnalyzer.AnalyzeAsync(observations, 100, false, false,
            new FakeDbc(ids => ids.Select(i => Msg("x.dbc", i)).ToArray()), default);

        Assert.Equal(PassiveCanStatus.PossibleMatch, report.Status);
        Assert.Empty(report.Signals);
    }

    [Fact]
    public async Task BrokenDatabase_DoesNotCrashAnalysis()
    {
        var observations = Enumerable.Range(0x100, 6).Select(i => Obs(i, 1, 2)).ToArray();
        var report = await PassiveCanAnalyzer.AnalyzeAsync(observations, 100, false, false,
            new FakeDbc(_ => throw new InvalidOperationException("catalog missing")), default);

        Assert.Equal(PassiveCanStatus.NoDatabaseMatch, report.Status);
    }

    private sealed class MonitorOnlySession(Func<int, string> monitor) : Elm327Protocol.IDtcModuleSession
    {
        public int Calls;

        public Task<string> MonitorRawAsync(int? filter, int? mask, bool extended29Bit, int milliseconds, CancellationToken token) =>
            Task.FromResult(monitor(Calls++));

        // Anything that would transmit must never be reached by passive listening.
        public Task<string> SendAsync(int transmitCanId, int receiveCanId, bool extended29Bit, byte[] payload, bool longWait, CancellationToken token, byte? extendedAddress = null) =>
            throw new InvalidOperationException("passive listening tried to transmit");
        public Task<string> SendGmlanDtcRequestAsync(int transmitCanId, int uudtReceiveCanId, byte[] payload, CancellationToken token) =>
            throw new InvalidOperationException("passive listening tried to transmit");
        public Task<string> ListenBroadcastAsync(int canId, CancellationToken token) =>
            throw new InvalidOperationException("passive listening tried to transmit");
        public Task<string> MonitorBroadcastAsync(int canId, bool extended29Bit, int milliseconds, CancellationToken token) =>
            throw new InvalidOperationException("not used");
    }

    [Fact]
    public async Task Collect_UsesOnlyTheMonitorCall()
    {
        var session = new MonitorOnlySession(_ => "1000102030405060708\r2000000000000000000\r");
        var (observations, frames, _) = await PassiveCanAnalyzer.CollectAsync(session, false, TimeSpan.FromMilliseconds(700), default);

        Assert.True(session.Calls >= 1);
        Assert.Equal(2, observations.Count);
        Assert.Equal(session.Calls * 2, frames);
    }

    [Fact]
    public async Task Collect_KeepsWhatWasHeard_WhenALaterReadFails()
    {
        var session = new MonitorOnlySession(n => n == 0 ? "1000102030405060708\r" : throw new IOException("adapter hiccup"));
        var (observations, _, _) = await PassiveCanAnalyzer.CollectAsync(session, false, TimeSpan.FromMilliseconds(1200), default);

        Assert.Single(observations);
    }

    [Fact]
    public async Task Collect_StopsOnCancellation()
    {
        using var cts = new CancellationTokenSource(200);
        var session = new MonitorOnlySession(_ => "");
        var (observations, _, _) = await PassiveCanAnalyzer.CollectAsync(session, false, TimeSpan.FromMinutes(5), cts.Token);

        Assert.Empty(observations);
    }
}
