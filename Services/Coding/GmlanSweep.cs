namespace CarScanner.Services.Coding;

public sealed record GmlanSweepResult(List<CodingDidValue> Values, int Requests, string Stopped);

/// <summary>
/// Read-only sweep of the GMLAN ReadDataByIdentifier ($1A) space of ONE module that already answered, to find the blocks
/// that hold its settings. A GM car lists no configuration identifiers in the catalog (Ford and VW do), so a normal
/// backup contains identification only and a before/after comparison would show nothing. The sweep sends nothing but
/// "1A xx" reads; it stores what the module returns and never interprets it.
/// It stops early when the module says the service is not supported (NRC 11), or when it stops answering.
/// </summary>
public static class GmlanSweep
{
    public const string StoppedNone = "";
    public const string StoppedServiceNotSupported = "service not supported (NRC 11)";
    public const string StoppedNoAnswers = "the module stopped answering";
    public const string StoppedByForeignReplies = "only another module answered";

    /// <param name="read">Reads one $1A identifier of the module (the real call, or a fake in tests).</param>
    /// <param name="alreadyRead">Identifiers the normal backup already stored; not asked again.</param>
    /// <param name="maxSilentInARow">How many requests in a row may get no usable answer before the sweep gives up.</param>
    public static async Task<GmlanSweepResult> RunAsync(
        Func<byte, Task<CodingDidValue>> read,
        IReadOnlySet<byte> alreadyRead,
        IProgress<double>? progress,
        CancellationToken token,
        int maxSilentInARow = 24)
    {
        var values = new List<CodingDidValue>();
        var requests = 0;
        var silent = 0;
        var foreign = 0;
        var stopped = StoppedNone;

        for (var did = 0; did <= 0xFF; did++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(did / 256d);
            if (alreadyRead.Contains((byte)did))
                continue;

            var value = await read((byte)did).ConfigureAwait(false);
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
        return new GmlanSweepResult(values, requests, stopped);
    }
}
