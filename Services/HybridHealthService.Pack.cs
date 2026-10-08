using System.Diagnostics;
using System.Text;
using CarScanner.Models;
using CarScanner.Services.OBD;

namespace CarScanner.Services;

/// <summary>
/// Direct HV battery reading shared by all makes: one averaged reading of the
/// pack, the load test, and the result for the page. Read-only.
/// </summary>
public sealed partial class HybridHealthService
{
    /// <summary>One averaged reading of the pack plus the extra values its ECU offers.</summary>
    public sealed record HvPackReading(
        HvPackKind Kind,
        string Source,
        double[] Blocks,
        double[] Temperatures,
        int[] Resistances,
        double? SocPercent,
        double? CurrentA,
        double? PackVoltage,
        double? SohPercent,
        int[]? BlockNumbers = null,
        double? CapacityAh = null,
        (double Max, double Min)? CellRange = null,
        // The load test can follow the highest / lowest cell from one request.
        bool RangeLoadTest = false,
        // Extra facts for the page ("battery age ≈ 6 years", "new: 94 Ah").
        string? Note = null);

    /// <summary>Reads one pack sample in an open session (set by the make-specific reader).</summary>
    private delegate Task<HvPackSample?> PackSampler(Elm327Protocol.IDtcModuleSession session, CancellationToken token);

    private PackSampler? _sampler;
    private HvPackKind? _lastPackKind;

    /// <summary>
    /// Reads the pack repeatedly for <paramref name="duration"/> (or until
    /// <paramref name="stop"/>) while the car is under load and keeps the
    /// largest spread and the block / cell that sags most. Run a check first.
    /// </summary>
    public async Task<HybridLoadOutcome> RunLoadTestAsync(
        TimeSpan duration,
        IProgress<HybridLoadProgress>? progress,
        CancellationToken stop,
        CancellationToken token)
    {
        if (_sampler is not { } sampler || _lastPackKind is not { } kind)
            throw new InvalidOperationException("ჯერ გაუშვი ჩვეულებრივი შემოწმება.");

        var tracker = new HvLoadTracker(HvPackProtocol.IsCellLevel(kind));
        var total = (int)Math.Round(duration.TotalSeconds);
        var clock = Stopwatch.StartNew();

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            while (clock.Elapsed < duration && !stop.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                var sample = await sampler(session, token).ConfigureAwait(false);
                if (sample is not null)
                    tracker.Add(sample);

                progress?.Report(new HybridLoadProgress(
                    Math.Min(total, (int)clock.Elapsed.TotalSeconds),
                    total,
                    tracker.Samples,
                    tracker.LastSpread,
                    tracker.MaxSpread,
                    tracker.LastCurrentA,
                    tracker.MaxDischargeA));
            }
        }, token).ConfigureAwait(false);

        var (block, deviation) = tracker.WeakestBlock();
        return new HybridLoadOutcome(tracker.Samples, tracker.MaxSpread, tracker.MaxDischargeA, tracker.WorstSample, block, deviation);
    }

    private static async Task<byte[]?> AskAsync(
        Elm327Protocol.IDtcModuleSession session,
        HvRequest request,
        StringBuilder? log,
        CancellationToken token)
    {
        string raw;
        try
        {
            raw = await session.SendAsync(request.Tx, request.Rx, request.Ext29, request.Payload, false, token, request.ExtendedAddress).ConfigureAwait(false);
            if (HvPackProtocol.IsResponsePending(raw, request))
                raw = await session.SendAsync(request.Tx, request.Rx, request.Ext29, request.Payload, true, token, request.ExtendedAddress).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.AppendLine($"{request.Label}: შეცდომა ({ex.GetType().Name})");
            return null;
        }

        var data = HvPackProtocol.ExtractData(raw, request);
        if (log is not null)
        {
            var compact = (raw ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            log.AppendLine($"{request.Label}: {(data is null ? "პასუხი არ არის" : $"{data.Length} ბაიტი")} · {Shorten(compact, 160)}");
        }
        return data;
    }

    /// <summary>Averages a few quick readings per block for a calm "at rest" picture.</summary>
    private static async Task<(double[] Blocks, List<HvPackSample> Samples)> AverageAsync(
        HvPackSample first,
        Func<Task<HvPackSample?>> next,
        int extra)
    {
        var samples = new List<HvPackSample> { first };
        for (var i = 0; i < extra; i++)
        {
            var sample = await next().ConfigureAwait(false);
            if (sample is not null && sample.Blocks.Length == first.Blocks.Length)
                samples.Add(sample);
        }

        var blocks = Enumerable.Range(0, first.Blocks.Length)
            .Select(index => Math.Round(samples.Average(sample => sample.Blocks[index]), 3))
            .ToArray();
        return (blocks, samples);
    }

    private static HybridHealthResult BuildPackResult(
        HybridProfileKind profile,
        string vehicleName,
        double? ecuLife,
        HvPackReading pack,
        string rawLog)
    {
        var cells = HvPackProtocol.IsCellLevel(pack.Kind);
        // Without single values (Leaf without 21 02) the controller's highest / lowest cell gives the spread.
        double? minimum = pack.Blocks.Length > 0 ? pack.Blocks.Min() : pack.CellRange?.Min;
        double? maximum = pack.Blocks.Length > 0 ? pack.Blocks.Max() : pack.CellRange?.Max;
        double? spread = minimum is { } low && maximum is { } high ? Math.Round(high - low, 3) : null;
        var (weakest, deviation) = HvPackAssessment.WeakestBlock(pack.Blocks, cells);

        int? score = spread is { } measured ? HvPackAssessment.Score(measured, cells, underLoad: false) : null;
        if (score is { } value)
        {
            if (pack.Temperatures.Length > 0 && pack.Temperatures.Max() > 45)
                value -= 10;
            value -= Math.Min(24, HvPackAssessment.HighResistanceBlocks(pack.Resistances).Length * 8);
            score = value;
        }
        // PID 5B (ecuLife) is not used for quality: on many cars it is the state of charge.
        if (pack.SohPercent is { } soh)
            score = (int)Math.Min(score ?? 100, Math.Round(soh));
        if (score is { } final)
            score = Math.Clamp(final, 0, 100);

        var unit = cells ? "უჯრედი" : "ბლოკი";
        var evidence = string.Join(" · ", new[]
        {
            pack.Blocks.Length > 0 ? $"{pack.Blocks.Length} {unit} ({pack.Source})" : pack.Source,
            pack.SohPercent.HasValue ? "SOH" : "",
            pack.CapacityAh.HasValue ? "ტევადობა" : "",
            pack.Temperatures.Length > 0 ? $"{pack.Temperatures.Length} ტემპერატურის მონაცემი" : "",
            pack.Resistances.Length > 0 ? "შიდა წინაღობა" : "",
            pack.SocPercent.HasValue ? "SOC" : "",
            pack.CurrentA.HasValue ? "დენი" : ""
        }.Where(text => text.Length > 0));

        var validation = pack.Kind switch
        {
            HvPackKind.ToyotaGen3NiMh when pack.Source.StartsWith("7E3", StringComparison.Ordinal) =>
                "ეს ბრძანება (7E3 2181) საზოგადოების მონაცემებითაა — რეალურ მანქანაზე ჯერ დასადასტურებელია.",
            _ => "ბაიტების განლაგება შემოწმებულია ნამდვილი მანქანების ჩანაწერებზე (OBDb)."
        };

        return new HybridHealthResult(
            profile == HybridProfileKind.Unsupported ? HybridProfileKind.GenericHybrid : profile,
            vehicleName,
            score,
            Rating(score),
            spread,
            minimum,
            maximum,
            pack.Temperatures.Length > 0 ? pack.Temperatures.Min() : null,
            pack.Temperatures.Length > 0 ? pack.Temperatures.Max() : null,
            pack.SohPercent,
            false,
            pack.Blocks.Length > 0 ? pack.Blocks.Length : (pack.CellRange is null ? 0 : 2),
            pack.Temperatures.Length,
            "რეალური მონაცემები მიღებულია HV ბატარეის ECU-დან.",
            validation,
            evidence,
            IsBlockVoltage: !cells,
            RawLog: rawLog,
            PackKind: pack.Kind,
            Blocks: pack.Blocks,
            Resistances: pack.Resistances,
            SocPercent: pack.SocPercent,
            CurrentA: pack.CurrentA,
            PackVoltage: pack.PackVoltage,
            WeakestBlock: weakest,
            WeakestDeviation: deviation,
            SohPercent: pack.SohPercent,
            BlockNumbers: pack.BlockNumbers,
            CapacityAh: pack.CapacityAh,
            LoadTestSupported: HvPackProtocol.SupportsLoadTest(pack.Kind) && (pack.Blocks.Length >= 10 || pack.RangeLoadTest),
            PackNote: pack.Note ?? string.Empty);
    }
}
