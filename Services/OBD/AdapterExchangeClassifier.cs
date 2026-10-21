using System.Globalization;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services.OBD;

/// <summary>
/// Which side of the link a finished (or failed) exchange points at. The point is to read a log and tell a dead link
/// from an adapter that refused something, from a car that stayed silent, from a reply the app could not match —
/// without guessing. Nothing here is remembered or used to decide anything: it only labels what was observed.
/// </summary>
public enum ExchangeLayer
{
    /// <summary>Nothing went wrong.</summary>
    None,

    /// <summary>No reply reached the app: timeout, dropped link, cancellation.</summary>
    Transport,

    /// <summary>The adapter itself refused, reported a status word, echoed the command or said nothing.</summary>
    Adapter,

    /// <summary>The adapter delivered what the bus did: silence, a refusal from an ECU, frames from another ECU.</summary>
    Ecu,

    /// <summary>A reply arrived but is not the answer to the request (unfinished, garbled, other identifier): a matching or parsing problem.</summary>
    Reply
}

/// <summary>What one exchange turned out to be.</summary>
public enum ExchangeKind
{
    /// <summary>"OK": an AT command was accepted.</summary>
    Accepted,

    /// <summary>Hex data came back. This says nothing about whether it is the right data.</summary>
    Data,

    /// <summary>The reply starts with the command that was sent: echo is on although it should be off.</summary>
    Echoed,

    /// <summary>"NO DATA": nothing answered on the asked address in time.</summary>
    NoData,

    /// <summary>An ECU refused the request (7F service code).</summary>
    NegativeResponse,

    /// <summary>The ECU asked for more time (7F service 78) and nothing final followed.</summary>
    ResponsePending,

    /// <summary>A multi-frame reply started and did not arrive completely.</summary>
    Incomplete,

    /// <summary>Frames arrived, but none from the asked module.</summary>
    ForeignReply,

    /// <summary>"?": the adapter did not understand the command.</summary>
    AdapterRejected,

    /// <summary>A bus or adapter status word (CAN ERROR, BUFFER FULL, UNABLE TO CONNECT …).</summary>
    AdapterStatus,

    /// <summary>SEARCHING… / BUS INIT…: the adapter is still busy.</summary>
    Searching,

    /// <summary>Nothing but the prompt came back.</summary>
    Empty,

    /// <summary>Readable text that is none of the above (banner, voltage, protocol description).</summary>
    Text,

    /// <summary>Nothing came back in time.</summary>
    TimedOut,

    /// <summary>The link to the adapter was lost.</summary>
    LinkLost,

    /// <summary>The caller cancelled the request.</summary>
    Cancelled,

    /// <summary>A reply arrived that matches nothing known (not an answer to this request).</summary>
    Unexpected,

    /// <summary>
    /// An unexpected error (not a timeout, not a recognised link loss) stopped the request before any reply was read — for
    /// example a transport that was already down. The exception type, kept in the log and the capture, says which.
    /// </summary>
    Fault
}

public readonly record struct ExchangeAssessment(ExchangeKind Kind)
{
    public ExchangeLayer Layer => AdapterExchangeClassifier.LayerOf(Kind);

    public override string ToString() => Layer == ExchangeLayer.None ? Kind.ToString() : $"{Kind} [{Layer.ToString().ToLowerInvariant()}]";
}

/// <summary>
/// Pure classification of what an ELM327 exchange returned. It never sends anything and never changes how a request is
/// executed; the log lines and the optional raw capture use it to say which layer a failure belongs to.
/// </summary>
public static class AdapterExchangeClassifier
{
    // The words an ELM327 prints for a bus or adapter problem (same set the protocol layer treats as negative status).
    private static readonly string[] BusStatusWords =
    {
        "UNABLE TO CONNECT", "BUS BUSY", "BUFFER FULL", "CAN ERROR", "FB ERROR", "DATA ERROR", "RX ERROR",
        "ACT ALERT", "LP ALERT", "LV RESET", "STOPPED", "ERROR"
    };

    public static ExchangeLayer LayerOf(ExchangeKind kind) => kind switch
    {
        ExchangeKind.Accepted or ExchangeKind.Data or ExchangeKind.Text => ExchangeLayer.None,
        ExchangeKind.TimedOut or ExchangeKind.LinkLost or ExchangeKind.Fault => ExchangeLayer.Transport,
        ExchangeKind.Cancelled => ExchangeLayer.None,
        ExchangeKind.AdapterRejected or ExchangeKind.AdapterStatus or ExchangeKind.Searching or ExchangeKind.Echoed or ExchangeKind.Empty
            => ExchangeLayer.Adapter,
        ExchangeKind.NoData or ExchangeKind.NegativeResponse or ExchangeKind.ResponsePending or ExchangeKind.ForeignReply
            => ExchangeLayer.Ecu,
        _ => ExchangeLayer.Reply
    };

    /// <summary>
    /// Classifies the text an adapter returned for <paramref name="command"/>. Judges the shape of the text only (it does not
    /// know which ECU was asked); <see cref="Classify(string?, string?, int?, bool)"/> adds the asked module.
    /// </summary>
    public static ExchangeAssessment Classify(string? command, string? reply)
    {
        var text = Normalize(reply);
        if (text.Length == 0)
            return new(ExchangeKind.Empty);

        var upper = text.ToUpperInvariant();
        if (upper == "?")
            return new(ExchangeKind.AdapterRejected);
        if (upper.Contains("NO DATA", StringComparison.Ordinal))
            return new(ExchangeKind.NoData);
        foreach (var word in BusStatusWords)
            if (upper.Contains(word, StringComparison.Ordinal))
                return new(ExchangeKind.AdapterStatus);
        if (upper.Contains("SEARCHING", StringComparison.Ordinal) || upper.Contains("BUS INIT", StringComparison.Ordinal))
            return new(ExchangeKind.Searching);
        if (IsEcho(command, upper))
            return new(ExchangeKind.Echoed);

        var compact = Compact(upper);
        if (compact == "OK" || IsAcceptedWithEcho(command, upper))
            return new(ExchangeKind.Accepted);
        if (compact.Length >= 2 && compact.All(Uri.IsHexDigit))
            return new(ExchangeKind.Data);
        return new(ExchangeKind.Text);
    }

    /// <summary>
    /// Classifies a reply that was read with headers on (ATH1) for a request addressed to a module whose reply id is
    /// <paramref name="expectedReceiveId"/> (null = not known). Uses the same evidence rules as the module scan, so a
    /// reply that carries the right service and identifier but came from another module is a <see cref="ExchangeKind.ForeignReply"/>.
    /// </summary>
    public static ExchangeAssessment Classify(string? requestHex, string? reply, int? expectedReceiveId, bool extended29Bit)
    {
        var request = ParseHex(requestHex);
        if (request.Length == 0)
            return Classify(requestHex, reply);

        int? did = request[0] == 0x22 && request.Length >= 3 ? (request[1] << 8) | request[2] : null;
        return FromFailure(AdapterReadFailure.ClassifyReply(reply, expectedReceiveId, did, request[0]));
    }

    public static ExchangeAssessment Classify(Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException:
                return new(ExchangeKind.Cancelled);

            case Elm327TimeoutException or TimeoutException:
                return new(ExchangeKind.TimedOut);

            case Elm327NotConnectedException or ObdConnectionLostException or AdapterNotConnectedException or IOException:
                return new(ExchangeKind.LinkLost);

            case Elm327CommandException command:
                // The adapter's own words say what happened ("?", "CAN ERROR" …); plain text or data is not a verdict.
                var shape = Classify(null, command.RawResponse);
                return shape.Kind is ExchangeKind.Text or ExchangeKind.Data ? new(ExchangeKind.Unexpected) : shape;

            default:
                // An exception is not a reply: it stopped the request before one was read.
                return new(ExchangeKind.Fault);
        }
    }

    /// <summary>Maps the existing failure vocabulary (module scan, service reads) onto the exchange kinds.</summary>
    public static ExchangeAssessment FromFailure(AdapterFailure failure) => new(failure.Kind switch
    {
        AdapterFailureKind.None => ExchangeKind.Data,
        AdapterFailureKind.AdapterCommandUnsupported => ExchangeKind.AdapterRejected,
        AdapterFailureKind.AdapterLinkLost => ExchangeKind.LinkLost,
        AdapterFailureKind.NoReplyInTime => ExchangeKind.Empty,
        AdapterFailureKind.EcuDidNotAnswer => ExchangeKind.NoData,
        AdapterFailureKind.EcuRefused => ExchangeKind.NegativeResponse,
        AdapterFailureKind.BusOrProtocolError => ExchangeKind.AdapterStatus,
        AdapterFailureKind.ReplyFromAnotherModule => ExchangeKind.ForeignReply,
        AdapterFailureKind.Undetermined => ExchangeKind.Fault,      // what AdapterReadFailure returns for an exception it does not know
        AdapterFailureKind.IncompleteReply => failure.Text.Contains("more time", StringComparison.Ordinal)
            ? ExchangeKind.ResponsePending
            : ExchangeKind.Incomplete,
        _ => ExchangeKind.Unexpected
    });

    /// <summary>
    /// The reply id ISO 15765-4 pairs with a request id (7E0…7E7 are answered on 7E8…7EF). Null for every other id: nothing
    /// about those is assumed.
    /// </summary>
    public static int? StandardReplyId(int transmitCanId, bool extended29Bit) =>
        !extended29Bit && transmitCanId is >= 0x7E0 and <= 0x7E7 ? transmitCanId + 8 : null;

    /// <summary>
    /// One log line for a finished physically addressed read: what was asked, who answered and what that adds up to. The
    /// reply's data bytes are never part of it.
    /// </summary>
    public static string DescribeRead(
        int transmitCanId,
        int? receiveCanId,
        bool extended29Bit,
        string requestHex,
        string? reply,
        TimeSpan elapsed)
    {
        var width = extended29Bit ? "X8" : "X3";
        var asked = receiveCanId is { } rx
            ? $"reply id {rx.ToString(width, CultureInfo.InvariantCulture)}"
            : StandardReplyId(transmitCanId, extended29Bit) is { } pair
                ? $"reply id not given, standard pair {pair.ToString(width, CultureInfo.InvariantCulture)}"
                : "reply id not given";

        var judged = receiveCanId ?? StandardReplyId(transmitCanId, extended29Bit);
        var assessment = Classify(requestHex, reply, judged, extended29Bit);

        // Every sender is listed, whether or not it is the asked module (an impossible id makes them all "foreign").
        var senders = ModuleReplyAnalysis.Split(reply, judged ?? -1, extended29Bit);
        var heard = new List<string>();
        if (judged is { } own && senders.Own.Count > 0)
            heard.Add($"{own.ToString(width, CultureInfo.InvariantCulture)}x{senders.Own.Count}");
        heard.AddRange(senders.Foreign.Select(item => $"{item.Header}x{item.Frames}"));

        return $"Read {requestHex}: tx {transmitCanId.ToString(width, CultureInfo.InvariantCulture)}, {asked}; " +
               $"frames from {(heard.Count == 0 ? "none" : string.Join(", ", heard))}; {assessment}; {(long)elapsed.TotalMilliseconds} ms";
    }

    private static bool IsEcho(string? command, string upperReply)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var sent = Compact(command.ToUpperInvariant());
        // The reply to ATZ and to the very first ATE0 legitimately contains the command (echo is still on at that point);
        // a command too short to tell from data (03, 0A) is not judged.
        if (sent.Length < 4 || sent is "ATE0" or "ATE1")
            return false;

        return Compact(upperReply).StartsWith(sent, StringComparison.Ordinal);
    }

    /// <summary>The reply to ATE0 / ATE1 is "OK", usually with the command in front of it (echo was still on when it arrived).</summary>
    private static bool IsAcceptedWithEcho(string? command, string upperReply)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var sent = Compact(command.ToUpperInvariant());
        return sent is "ATE0" or "ATE1" && Compact(upperReply) == sent + "OK";
    }

    private static string Normalize(string? reply)
    {
        if (string.IsNullOrEmpty(reply))
            return string.Empty;

        var chars = reply.Where(ch => ch != '>' && !char.IsControl(ch) || ch is '\r' or '\n').ToArray();
        var lines = new string(chars).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
        return string.Join(" ", lines);
    }

    private static string Compact(string text) => string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));

    private static byte[] ParseHex(string? hex)
    {
        var compact = hex is null ? string.Empty : Compact(hex);
        if (compact.Length == 0 || compact.Length % 2 != 0 || !compact.All(Uri.IsHexDigit))
            return Array.Empty<byte>();

        var bytes = new byte[compact.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(compact.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
}

/// <summary>
/// Counts what a run of requests turned out to be (one module session, for example) and who answered, so a log line can
/// say "96 requests: 92 data, 4 NO DATA; frames from 7EF x92, 7E8 x4" instead of 96 lines.
/// </summary>
public sealed class ExchangeTally
{
    private readonly object _gate = new();
    private readonly SortedDictionary<ExchangeKind, int> _kinds = new();
    private readonly SortedDictionary<string, int> _senders = new(StringComparer.Ordinal);
    private int _requests;
    private int _onlyOtherModules;

    public int Requests
    {
        get { lock (_gate) return _requests; }
    }

    public void Add(ExchangeAssessment assessment)
    {
        lock (_gate)
        {
            _requests++;
            _kinds[assessment.Kind] = _kinds.GetValueOrDefault(assessment.Kind) + 1;
        }
    }

    /// <summary>Records which CAN ids the frames of one reply came from, and whether none came from the asked module.</summary>
    public void NoteSenders(string? reply, int expectedReceiveId, bool extended29Bit)
    {
        var split = ModuleReplyAnalysis.Split(reply, expectedReceiveId, extended29Bit);
        var own = expectedReceiveId.ToString(extended29Bit ? "X8" : "X3", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            if (split.Own.Count > 0)
                _senders[own] = _senders.GetValueOrDefault(own) + split.Own.Count;
            foreach (var other in split.Foreign)
                _senders[other.Header] = _senders.GetValueOrDefault(other.Header) + other.Frames;
            if (split.Own.Count == 0 && split.Foreign.Count > 0)
                _onlyOtherModules++;
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            var kinds = _kinds.Count == 0 ? "nothing sent" : string.Join(", ", _kinds.Select(item => $"{item.Key} x{item.Value}"));
            var senders = _senders.Count == 0 ? "none" : string.Join(", ", _senders.Select(item => $"{item.Key} x{item.Value}"));
            var foreign = _onlyOtherModules == 0 ? string.Empty : $"; {_onlyOtherModules} repl{(_onlyOtherModules == 1 ? "y" : "ies")} came only from other modules";
            return $"{_requests} request(s): {kinds}; frames from {senders}{foreign}";
        }
    }
}
