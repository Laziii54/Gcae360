using CarScanner.Services.OBD;

namespace CarScanner.Services.Passive;

public sealed record CanObservation(int Id, int Count, byte[] LastData);

public sealed record DbcMessageInfo(string DbcFile, int MessageId, string Name, int Dlc, IReadOnlyList<DbcSignal> Signals);

public sealed record DbcMatch(string DbcFile, int MatchedIds, int ComparedIds, double Coverage);

public sealed record DecodedCanSignal(
    string DbcFile, int MessageId, string MessageName, string Signal, double Value, string Unit, int Samples);

public enum PassiveCanStatus
{
    NoTraffic,
    OnlyDiagnosticTraffic,
    NoDatabaseMatch,
    PossibleMatch
}

public sealed record PassiveCanReport(
    PassiveCanStatus Status,
    int FrameCount,
    int DistinctIds,
    bool BufferOverflow,
    DbcMatch? Best,
    IReadOnlyList<DbcMatch> Matches,
    IReadOnlyList<DecodedCanSignal> Signals);

/// <summary>Source of public CAN-database definitions (the catalog's opendbc tables).</summary>
public interface IDbcSource
{
    Task<IReadOnlyList<DbcMessageInfo>> FindMessagesAsync(IReadOnlyCollection<int> messageIds, CancellationToken token);
}

/// <summary>
/// Reads what the car broadcasts by itself (nothing is ever sent) and compares the message IDs with the
/// public opendbc databases. The result is a "possible match": the database is never assumed to belong to the
/// car because of its name, only because many observed IDs and frame lengths agree, and decoded values are
/// always presented as unverified.
/// </summary>
public static class PassiveCanAnalyzer
{
    public const int MinimumMatchedIds = 4;
    public const double MinimumCoverage = 0.4;

    public static bool IsDiagnosticId(int id, bool ext29) =>
        ext29 ? (id & 0x1FFF0000) == 0x18DA0000 || (id & 0x1FFF0000) == 0x18DB0000
              : id is >= 0x700 and <= 0x7FF;

    /// <summary>Folds raw monitor text into one observation per message ID.</summary>
    public static IReadOnlyList<CanObservation> Aggregate(IEnumerable<string> monitorTexts, bool ext29, out int frameCount, out bool overflow)
    {
        var map = new Dictionary<int, (int Count, byte[] Last)>();
        frameCount = 0;
        overflow = false;
        foreach (var text in monitorTexts)
        {
            if (text.Contains("BUFFER FULL", StringComparison.OrdinalIgnoreCase))
                overflow = true;

            foreach (var (id, data) in CanFrameParser.ParseFrames(text, ext29))
            {
                frameCount++;
                map.TryGetValue(id, out var entry);
                map[id] = (entry.Count + 1, data);
            }
        }

        return map.Select(p => new CanObservation(p.Key, p.Value.Count, p.Value.Last)).OrderBy(o => o.Id).ToArray();
    }

    public static async Task<PassiveCanReport> AnalyzeAsync(
        IReadOnlyList<CanObservation> observations,
        int frameCount,
        bool overflow,
        bool ext29,
        IDbcSource source,
        CancellationToken token)
    {
        if (observations.Count == 0)
            return new PassiveCanReport(PassiveCanStatus.NoTraffic, frameCount, 0, overflow, null, Array.Empty<DbcMatch>(), Array.Empty<DecodedCanSignal>());

        var candidates = observations.Where(o => !IsDiagnosticId(o.Id, ext29)).ToArray();
        if (candidates.Length == 0)
            return new PassiveCanReport(PassiveCanStatus.OnlyDiagnosticTraffic, frameCount, observations.Count, overflow, null, Array.Empty<DbcMatch>(), Array.Empty<DecodedCanSignal>());

        IReadOnlyList<DbcMessageInfo> known;
        try
        {
            known = await source.FindMessagesAsync(candidates.Select(c => c.Id).ToArray(), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            known = Array.Empty<DbcMessageInfo>();
        }

        var byId = candidates.ToDictionary(c => c.Id);
        var matches = known
            .Where(m => byId.TryGetValue(m.MessageId, out var o) && o.LastData.Length == m.Dlc)
            .GroupBy(m => m.DbcFile)
            .Select(g => new DbcMatch(g.Key, g.Select(m => m.MessageId).Distinct().Count(), candidates.Length,
                (double)g.Select(m => m.MessageId).Distinct().Count() / candidates.Length))
            .OrderByDescending(m => m.MatchedIds)
            .ThenBy(m => m.DbcFile, StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        var best = matches.FirstOrDefault();
        if (best is null || best.MatchedIds < MinimumMatchedIds || best.Coverage < MinimumCoverage)
            return new PassiveCanReport(PassiveCanStatus.NoDatabaseMatch, frameCount, observations.Count, overflow, null, matches, Array.Empty<DecodedCanSignal>());

        var signals = new List<DecodedCanSignal>();
        foreach (var message in known.Where(m => m.DbcFile == best.DbcFile && byId.ContainsKey(m.MessageId) && byId[m.MessageId].LastData.Length == m.Dlc))
        {
            var observed = byId[message.MessageId];
            foreach (var signal in message.Signals)
            {
                var value = DbcSignalDecoder.Decode(signal, observed.LastData);
                if (value is { } v)
                    signals.Add(new DecodedCanSignal(best.DbcFile, message.MessageId, message.Name, signal.Name, v, signal.Unit, observed.Count));
            }
        }

        return new PassiveCanReport(PassiveCanStatus.PossibleMatch, frameCount, observations.Count, overflow, best, matches, signals);
    }

    /// <summary>Listens in short windows; the session exposes no way to transmit through this path, only to monitor.</summary>
    public static async Task<(IReadOnlyList<CanObservation> Observations, int FrameCount, bool Overflow)> CollectAsync(
        Elm327Protocol.IDtcModuleSession session,
        bool ext29,
        TimeSpan duration,
        CancellationToken token)
    {
        var texts = new List<string>();
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until && !token.IsCancellationRequested)
        {
            var window = (int)Math.Clamp((until - DateTime.UtcNow).TotalMilliseconds, 300, 2000);
            string text;
            try
            {
                text = await session.MonitorRawAsync(null, null, ext29, window, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (texts.Count > 0)
            {
                break; // keep what was heard; a hiccup must not discard the recording
            }

            texts.Add(text ?? string.Empty);
        }

        var observations = Aggregate(texts, ext29, out var frames, out var overflow);
        return (observations, frames, overflow);
    }
}
