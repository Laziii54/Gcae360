using CarScanner.Services.OBD;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Which side a failed read-only request failed on. The point is to stop everything being reported as "the ECU did not
/// answer" (or, worse, as "this adapter / car is not supported"): an adapter that refuses a setup command, a link that
/// dropped, a silent bus, an ECU that refuses the request and a reply the app could not match are different findings, and
/// only some of them say anything about the car.
/// A single failed request never proves an adapter or a car incompatible: nothing here is remembered, and
/// <see cref="Undetermined"/> is the honest answer when the evidence does not decide.
/// </summary>
public enum AdapterFailureKind
{
    /// <summary>A usable positive answer arrived; nothing failed.</summary>
    None,

    /// <summary>The adapter refused a command it needs to address the ECU ('?' or no acceptance). Another adapter may well do it.</summary>
    AdapterCommandUnsupported,

    /// <summary>The link to the adapter is down or dropped during the request.</summary>
    AdapterLinkLost,

    /// <summary>Nothing came back in time (slow or silent adapter / car).</summary>
    NoReplyInTime,

    /// <summary>"NO DATA": the adapter heard nothing from the asked address. Asleep, absent and unreachable look the same.</summary>
    EcuDidNotAnswer,

    /// <summary>The ECU answered with a negative response: it got the request and refuses it (or the identifier does not exist in it).</summary>
    EcuRefused,

    /// <summary>The adapter reported a bus or protocol problem (unable to connect, CAN error, buffer full …) or the protocol does not fit.</summary>
    BusOrProtocolError,

    /// <summary>Frames arrived, but only from other modules than the asked one.</summary>
    ReplyFromAnotherModule,

    /// <summary>A reply started (first frame, or "response pending") but did not arrive completely.</summary>
    IncompleteReply,

    /// <summary>Something arrived that is not the answer to this request (other service or identifier, garbled text).</summary>
    UnexpectedReply,

    /// <summary>The evidence does not decide; compatibility is simply not determined yet.</summary>
    Undetermined
}

public sealed record AdapterFailure(AdapterFailureKind Kind, string Text)
{
    /// <summary>Causes on the adapter's side: the request never reached the ECU properly, so another adapter may behave differently.</summary>
    public bool IsAdapterSide => Kind is AdapterFailureKind.AdapterCommandUnsupported or AdapterFailureKind.AdapterLinkLost;

    public override string ToString() => $"[{Text}]";
}

public static class AdapterReadFailure
{
    private static readonly string[] BusStatusWords =
    {
        "UNABLE TO CONNECT", "BUS INIT", "CAN ERROR", "BUS BUSY", "BUFFER FULL", "FB ERROR", "DATA ERROR",
        "RX ERROR", "ACT ALERT", "LV RESET", "STOPPED"
    };

    /// <summary>Classifies an exception thrown while reading through the adapter.</summary>
    public static AdapterFailure Classify(Exception exception)
    {
        switch (exception)
        {
            case Elm327CommandException { UnsupportedCommand: { Length: > 0 } command } refused:
                var shown = string.IsNullOrWhiteSpace(refused.RawResponse) ? "no usable answer" : refused.RawResponse.Trim();
                return new(AdapterFailureKind.AdapterCommandUnsupported,
                    $"adapter does not accept {command} (answered \"{shown}\") — a limit of this adapter's command set, not of the car");

            case Elm327TimeoutException or TimeoutException:
                return new(AdapterFailureKind.NoReplyInTime, "no reply in time");

            case Elm327NotConnectedException or ObdConnectionLostException or AdapterNotConnectedException or IOException:
                return new(AdapterFailureKind.AdapterLinkLost, "the link to the adapter was lost");

            case NotSupportedException:
                return new(AdapterFailureKind.BusOrProtocolError, "the active protocol does not fit this request");

            case Elm327CommandException other:
                return new(AdapterFailureKind.UnexpectedReply, $"adapter status: {Shorten(other.RawResponse)}");

            default:
                return new(AdapterFailureKind.Undetermined, exception.GetType().Name);
        }
    }

    /// <summary>
    /// Classifies the adapter's text for a request for <paramref name="did"/> with <paramref name="service"/> (0x22 =
    /// ReadDataByIdentifier). <see cref="AdapterFailureKind.None"/> means a positive answer for exactly that identifier.
    /// </summary>
    public static AdapterFailure ClassifyReply(string? raw, int? expectedReceiveId, int? did, byte service = 0x22)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new(AdapterFailureKind.NoReplyInTime, "no text came back");

        var upper = raw.ToUpperInvariant();
        if (upper.Trim() == "?")
            return new(AdapterFailureKind.AdapterCommandUnsupported, "the adapter did not understand the request (\"?\")");

        if (upper.Contains("NO DATA", StringComparison.Ordinal))
            return new(AdapterFailureKind.EcuDidNotAnswer, "NO DATA: nothing came back from the asked address (does not prove the ECU lacks the function)");

        var bus = BusStatusWords.FirstOrDefault(word => upper.Contains(word, StringComparison.Ordinal));
        if (bus is not null)
            return new(AdapterFailureKind.BusOrProtocolError, $"adapter reported \"{bus}\" (bus or protocol problem; check protocol and addressing)");

        var ext29 = expectedReceiveId is > 0x7FF;
        IReadOnlyList<byte[]> frames;
        if (expectedReceiveId is { } rx)
        {
            var split = ModuleReplyAnalysis.Split(raw, rx, ext29);
            if (split.Own.Count == 0 && split.Foreign.Count > 0)
            {
                var senders = string.Join(", ", split.Foreign.Select(item => item.Header));
                return new(AdapterFailureKind.ReplyFromAnotherModule, $"only frames from {senders} arrived, none from the asked module");
            }

            frames = split.Own;
        }
        else
        {
            frames = ObdResponseParser.ParseCanFrames(raw);
        }

        var assembly = ModuleReplyAnalysis.Assemble(frames);

        var positive = assembly.Messages.FirstOrDefault(message => message.Length > 0 && message[0] == (byte)(service + 0x40));
        if (positive is not null)
        {
            if (did is not { } wanted || service != 0x22 ||
                (positive.Length >= 3 && positive[1] == (byte)(wanted >> 8) && positive[2] == (byte)wanted))
                return new(AdapterFailureKind.None, "positive answer");

            return new(AdapterFailureKind.UnexpectedReply, "a positive answer arrived, but for a different identifier");
        }

        var negative = assembly.Messages.FirstOrDefault(message => message.Length >= 3 && message[0] == 0x7F && message[1] == service);
        if (negative is not null)
        {
            if (negative[2] == 0x78)
                return new(AdapterFailureKind.IncompleteReply, "the ECU asked for more time (response pending) and no final answer arrived");

            return new(AdapterFailureKind.EcuRefused,
                $"ECU refused: {UdsNegativeResponseDecoder.Describe(negative[2])} (NRC 0x{negative[2]:X2})");
        }

        if (assembly.Messages.Count == 0 && assembly.Incomplete.Count > 0)
            return new(AdapterFailureKind.IncompleteReply, "a multi-frame reply started but did not arrive completely");

        return new(AdapterFailureKind.UnexpectedReply, "something arrived that is not the answer to this request");
    }

    /// <summary>
    /// After a physically addressed request failed because the adapter refused one of its addressing/setup commands, the
    /// functional request (broadcast header, none of those commands) is still worth one try — it is a read-only 0x22 like
    /// the physical one. Timeouts, lost links and a protocol that does not fit are not retried that way: the functional
    /// request would only wait a second time, or fail the same way.
    /// </summary>
    public static bool ShouldTryFunctionalRequest(Exception exception) =>
        Classify(exception).Kind is AdapterFailureKind.AdapterCommandUnsupported;

    private static string Shorten(string text)
    {
        var oneLine = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 60 ? oneLine : oneLine[..60] + "…";
    }
}
