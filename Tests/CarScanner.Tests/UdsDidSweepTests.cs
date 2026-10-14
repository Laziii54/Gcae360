using CarScanner.Services.Coding;

namespace CarScanner.Tests;

/// <summary>
/// The $22 identifier sweep used by Deep Scan on every module, regardless of brand/protocol — unlike GmlanSweep's
/// $1A space (256 possible values, naturally bounded), $22 has 65536 possible values, and a module that cleanly
/// refuses every unknown identifier with an NRC (normal, well-behaved UDS behavior) would never trigger
/// GmlanSweep-style silence-based stopping, since a refusal resets the silence counter. These tests specifically
/// cover the wall-clock time budget this sweep adds on top of GmlanSweep's own stopping rules, using a fake clock
/// so the test itself never actually waits.
/// </summary>
public sealed class UdsDidSweepTests
{
    private static CodingDidValue Read(int did) => new() { Did = did.ToString("X4"), Hex = "0102" };
    private static CodingDidValue Err(int did, string error) => new() { Did = did.ToString("X4"), Error = error };
    private static readonly HashSet<int> None = new();

    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;
        public void Advance(TimeSpan by) => Now += by;
        public DateTimeOffset Read() => Now;
    }

    [Fact]
    public async Task OnlyReadValues_AreStored_AndTheKnownOnesAreNotAskedAgain()
    {
        var asked = new List<int>();
        var known = new HashSet<int> { 0x0090, 0x00B0 };

        var result = await UdsDidSweep.RunAsync(did =>
        {
            asked.Add(did);
            return Task.FromResult(did is 0x0010 or 0x00A0 ? Read(did) : Err(did, "NRC 31"));
        }, known, null, CancellationToken.None, maxDuration: TimeSpan.FromDays(1));

        Assert.DoesNotContain(0x0090, asked);
        Assert.DoesNotContain(0x00B0, asked);
        Assert.Equal(new[] { "0010", "00A0" }, result.Values.Select(value => value.Did));
        // A 65536-value sweep that always answers with a normal NRC would run forever without the time
        // budget below — here the budget is effectively unlimited, so it legitimately runs the full space.
        Assert.Equal(65536 - known.Count, result.Requests);
        Assert.Equal(UdsDidSweep.StoppedNone, result.Stopped);
    }

    [Fact]
    public async Task ServiceNotSupported_StopsAtOnce()
    {
        var calls = 0;

        var result = await UdsDidSweep.RunAsync(did => { calls++; return Task.FromResult(Err(did, "NRC 11")); }, None, null, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(UdsDidSweep.StoppedServiceNotSupported, result.Stopped);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task ASilentModule_IsAbandonedAfterAFewRequests_NotAfterTheFullSpace()
    {
        var calls = 0;

        var result = await UdsDidSweep.RunAsync(did => { calls++; return Task.FromResult(Err(did, "no answer")); }, None, null, CancellationToken.None);

        Assert.Equal(24, calls);
        Assert.Equal(UdsDidSweep.StoppedNoAnswers, result.Stopped);
    }

    [Fact]
    public async Task RepliesOfAnotherModule_AreReportedAsSuch()
    {
        var result = await UdsDidSweep.RunAsync(did => Task.FromResult(Err(did, "reply from another module (0x7E8)")), None, null, CancellationToken.None);

        Assert.Equal(UdsDidSweep.StoppedByForeignReplies, result.Stopped);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task AnAnswer_ResetsTheSilenceCounter()
    {
        // 20 silent, one answer, 20 silent, one answer … never 24 in a row.
        var result = await UdsDidSweep.RunAsync(
            did => Task.FromResult(did % 21 == 20 ? Read(did) : Err(did, "no answer")),
            None, null, CancellationToken.None, maxDuration: TimeSpan.FromDays(1));

        Assert.Equal(UdsDidSweep.StoppedNone, result.Stopped);
        Assert.NotEmpty(result.Values);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() => UdsDidSweep.RunAsync(did =>
        {
            if (++calls == 3) cts.Cancel();
            return Task.FromResult(Err(did, "NRC 31"));
        }, None, null, cts.Token));
    }

    [Fact]
    public async Task AModuleThatAlwaysRefusesCleanly_IsStoppedByTheTimeBudget_NotBySilence()
    {
        // The exact scenario GmlanSweep's logic cannot handle for a 65536-value space: every identifier gets a
        // normal "out of range" refusal, which resets the silence counter every time — so without a wall-clock
        // budget this would run all 65536 requests. The fake clock advances on every call, so the budget is hit
        // almost immediately without the test actually waiting.
        var clock = new FakeClock();
        var calls = 0;

        var result = await UdsDidSweep.RunAsync(
            did =>
            {
                calls++;
                clock.Advance(TimeSpan.FromSeconds(1));
                return Task.FromResult(Err(did, "NRC 31"));
            },
            None, null, CancellationToken.None,
            maxDuration: TimeSpan.FromSeconds(5),
            clock: clock.Read);

        Assert.Equal(UdsDidSweep.StoppedTimeBudget, result.Stopped);
        Assert.True(calls is >= 4 and < 65536, $"expected the budget to cut this off well short of the full space, got {calls} calls");
    }

    [Fact]
    public async Task WithNoExplicitBudget_TheDefaultIsStillBounded()
    {
        Assert.True(UdsDidSweep.DefaultMaxDuration < TimeSpan.FromMinutes(5),
            "the default budget must keep Deep Scan's total duration predictable even for a well-behaved, always-answering module");
    }
}
