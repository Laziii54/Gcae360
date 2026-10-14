namespace CarScanner.Services.Coding;

public sealed record UdsDidSweepResult(List<CodingDidValue> Values, int Requests, string Stopped);

/// <summary>
/// Read-only sweep of the UDS ReadDataByIdentifier ($22) space of ONE module that already answered standard
/// identification, looking for identifiers beyond the fixed, documented list. Works for any brand/protocol
/// that answers $22 — unlike <see cref="GmlanSweep"/>, which is GM's own $1A service and only exists on GMLAN.
///
/// $22 identifiers are two bytes (65536 possible values), far too many to sweep exhaustively in one run — and
/// unlike GmlanSweep's $1A space, a module that cleanly refuses every unknown identifier with a normal NRC
/// (common, well-behaved UDS behavior) would never trigger a silence-based stop, since a refusal resets the
/// silence counter. So this sweep is bounded by wall-clock time as well, not just consecutive silence, to keep
/// Deep Scan's total duration predictable regardless of how a given module answers.
///
/// It sends nothing but "22 xx xx" reads; it stores what the module returns and never interprets it.
/// It stops early when the module says the service is not supported (NRC 11), when it stops answering, or when
/// the time budget runs out.
/// </summary>
public static class UdsDidSweep
{
    public const string StoppedNone = "";
    public const string StoppedServiceNotSupported = "service not supported (NRC 11)";
    public const string StoppedNoAnswers = "the module stopped answering";
    public const string StoppedByForeignReplies = "only another module answered";
    public const string StoppedTimeBudget = "reached the time budget";

    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromSeconds(90);

    /// <param name="read">Reads one $22 identifier of the module (the real call, or a fake in tests).</param>
    /// <param name="alreadyRead">Identifiers the normal backup already stored; not asked again.</param>
    /// <param name="maxDuration">Wall-clock budget for this one module; defaults to <see cref="DefaultMaxDuration"/>.</param>
    /// <param name="maxSilentInARow">How many requests in a row may get no usable answer before the sweep gives up.</param>
    /// <param name="clock">Test seam for the wall-clock budget; defaults to the real clock.</param>
    public static async Task<UdsDidSweepResult> RunAsync(
        Func<int, Task<CodingDidValue>> read,
        IReadOnlySet<int> alreadyRead,
        IProgress<double>? progress,
        CancellationToken token,
        TimeSpan? maxDuration = null,
        int maxSilentInARow = 24,
        Func<DateTimeOffset>? clock = null)
    {
        var now = clock ?? (() => DateTimeOffset.UtcNow);
        var deadline = now() + (maxDuration ?? DefaultMaxDuration);

        var values = new List<CodingDidValue>();
        var requests = 0;
        var silent = 0;
        var foreign = 0;
        var stopped = StoppedNone;

        for (var did = 0; did <= 0xFFFF; did++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(did / 65536d);
            if (alreadyRead.Contains(did))
                continue;

            if (now() >= deadline)
            {
                stopped = StoppedTimeBudget;
                break;
            }

            var value = await read(did).ConfigureAwait(false);
            requests++;

            if (value.IsRead)
            {
                values.Add(value);
                silent = 0;
                foreign = 0;
                continue;
            }

            if (value.Error == "NRC 11")
            {
                stopped = StoppedServiceNotSupported;
                break;
            }

            // A refusal of one identifier (31 = out of range, 22 = conditions …) is a normal answer, not silence.
            if (value.Error.StartsWith("NRC", StringComparison.Ordinal))
            {
                silent = 0;
                foreign = 0;
                continue;
            }

            if (value.Error.StartsWith("reply from another module", StringComparison.Ordinal))
                foreign++;
            silent++;
            if (silent >= maxSilentInARow)
            {
                stopped = foreign >= maxSilentInARow ? StoppedByForeignReplies : StoppedNoAnswers;
                break;
            }
        }

        progress?.Report(1);
        return new UdsDidSweepResult(values, requests, stopped);
    }
}
