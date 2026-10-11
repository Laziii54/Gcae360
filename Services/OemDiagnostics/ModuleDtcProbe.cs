namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// The decision tree that turns the replies of ONE module address into a result: UDS 19 02 first, KWP2000 18 02
/// when the module replied but refused, GMLAN $A9 for GM. It sends nothing itself (the caller supplies the send
/// delegates) and reads nothing from hardware, so the tree is tested with replies copied from real captures.
///
/// Reading rules (these were the defects found in the captures):
/// • only frames from the module's own reply id count as its answer; frames from another id (the engine ECU's
///   0x7E8 arrived for 20+ other addresses) are reported as foreign and never turned into a module;
/// • a multi-frame answer that stopped after its first frame is "answered, incomplete", not silence;
/// • "NO DATA" is "no reply", never "module absent".
/// </summary>
public static class ModuleDtcProbe
{
    public sealed record Result(
        ModuleProbeState State,
        // "UDS", "KWP", "GMLAN", "—" (answered, no code list) or empty (no answer).
        string Protocol,
        // Positive reply (complete, or the received prefix when <see cref="Incomplete"/>).
        byte[]? Message,
        IReadOnlyList<byte[]> GmlanFrames,
        bool Incomplete,
        byte? Nrc,
        string? ForeignSender,
        string Detail,
        string Raw)
    {
        public bool Responded => State is ModuleProbeState.Answered or ModuleProbeState.AnsweredNegativeOnly or ModuleProbeState.AnsweredIncomplete;
    }

    /// <param name="sendIso">Sends an ISO-TP request (the payload without PCI) and returns the adapter text. Argument: (payload, longWait).</param>
    /// <param name="sendGmlan">Sends a raw GMLAN $A9 frame; null when the vehicle is not GM or the adapter cannot do it.</param>
    public static async Task<Result> ProbeAsync(
        Func<byte[], bool, Task<string>> sendIso,
        Func<byte[], Task<string>>? sendGmlan,
        int receiveId,
        bool ext29,
        byte? extendedAddress)
    {
        Task<ModuleReplyAnalysis.ModuleExchange> Ask(byte[] payload) =>
            ModuleReplyAnalysis.ExchangeAsync(longWait => sendIso(payload, longWait), receiveId, ext29, extendedAddress, payload[0]);

        var uds = await Ask(new byte[] { 0x19, 0x02, 0xFF }).ConfigureAwait(false);
        var foreign = new Dictionary<string, int>(StringComparer.Ordinal);
        Merge(foreign, uds);
        var raw = uds.Raw;

        if (uds.Positive is { Length: >= 3 } udsMessage && udsMessage[0] == 0x59 && udsMessage[1] == 0x02)
            return Answered("UDS", udsMessage, uds, foreign);

        if (uds.Incomplete && uds.FragmentIsPositiveFor(0x19) && uds.Fragment!.Data.Length >= 2 && uds.Fragment.Data[1] == 0x02)
            return IncompleteResult("UDS", uds, foreign);

        // GMLAN modules (GM / Chevrolet Volt …) often ignore UDS $19 completely: still try $A9.
        var tryGmlan = sendGmlan is not null && !ext29;
        if (!uds.AnyReply && !tryGmlan)
            return Silent(uds, foreign, raw);

        if (uds.AnyReply)
        {
            // KWP2000 ReadDTCByStatus (older Hyundai/Kia, Toyota …).
            var kwp = await Ask(new byte[] { 0x18, 0x02, 0xFF, 0x00 }).ConfigureAwait(false);
            Merge(foreign, kwp);
            if (kwp.Positive is { Length: >= 2 } kwpMessage && kwpMessage[0] == 0x58)
                return Answered("KWP", kwpMessage, kwp, foreign);
            if (kwp.Incomplete && kwp.FragmentIsPositiveFor(0x18))
                return IncompleteResult("KWP", kwp, foreign);
        }

        if (tryGmlan)
        {
            // GM (GMLAN) $A9: the reply comes as unsegmented frames on 0x5xx.
            var uudt = 0x500 | (receiveId & 0xFF);
            try
            {
                var gmRaw = await sendGmlan!(new byte[] { 0xA9, 0x81, 0x12 }).ConfigureAwait(false);
                var gm = ModuleReplyAnalysis.Split(gmRaw, uudt, ext29: false);
                foreach (var sender in gm.Foreign)
                    foreign[sender.Header] = Math.Max(foreign.GetValueOrDefault(sender.Header), sender.Frames);
                if (gm.Own.Count > 0)
                {
                    return new Result(ModuleProbeState.Answered, "GMLAN", null, gm.Own, false, null, null,
                        "GMLAN $A9 answered on 0x" + uudt.ToString("X3"), Trim(gmRaw));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Some adapters cannot send raw (unformatted) CAN frames: not a statement about the module.
                System.Diagnostics.Debug.WriteLine($"GMLAN A9 {receiveId:X} failed: {ex.Message}");
            }

            // Neither $19 nor $A9 got any answer from this module.
            if (!uds.AnyReply)
                return Silent(uds, foreign, raw);
        }

        // The module answered, but only with a refusal: it is there, it does not report trouble codes this way.
        var nrc = uds.Nrc;
        return new Result(
            ModuleProbeState.AnsweredNegativeOnly, "—", null, Array.Empty<byte[]>(), false, nrc, null,
            nrc is { } code
                ? $"Refused the trouble-code request (0x19 → NRC 0x{code:X2} {NrcName(code)})."
                : "Answered, but not with a trouble-code list.",
            raw);
    }

    private static Result Answered(string protocol, byte[] message, ModuleReplyAnalysis.ModuleExchange exchange, Dictionary<string, int> foreign) =>
        new(ModuleProbeState.Answered, protocol, message, Array.Empty<byte[]>(), false, null, null,
            foreign.Count > 0 ? "Frames from another CAN id were also received and ignored: " + Senders(foreign) : string.Empty,
            exchange.Raw);

    private static Result IncompleteResult(string protocol, ModuleReplyAnalysis.ModuleExchange exchange, Dictionary<string, int> foreign)
    {
        var fragment = exchange.Fragment!;
        return new Result(ModuleProbeState.AnsweredIncomplete, protocol, fragment.Data, Array.Empty<byte[]>(), true, null, null,
            $"Reply incomplete: {fragment.Data.Length} of {fragment.ExpectedLength} bytes received; the code list may be longer.",
            exchange.Raw);
    }

    private static Result Silent(ModuleReplyAnalysis.ModuleExchange exchange, Dictionary<string, int> foreign, string raw)
    {
        if (foreign.Count > 0)
        {
            var sender = Senders(foreign);
            return new Result(ModuleProbeState.ForeignReplyOnly, string.Empty, null, Array.Empty<byte[]>(), false, null, sender,
                $"No reply from this module; frames from {sender} arrived instead and were not attributed to it.", raw);
        }

        return new Result(ModuleProbeState.NoReply, string.Empty, null, Array.Empty<byte[]>(), false, null, null,
            string.IsNullOrWhiteSpace(exchange.Status) ? "No reply." : exchange.Status, raw);
    }

    private static void Merge(Dictionary<string, int> target, ModuleReplyAnalysis.ModuleExchange exchange)
    {
        foreach (var sender in exchange.Foreign)
            target[sender.Header] = Math.Max(target.GetValueOrDefault(sender.Header), sender.Frames);
    }

    private static string Senders(Dictionary<string, int> foreign) =>
        string.Join(", ", foreign.Keys.OrderBy(key => key, StringComparer.Ordinal).Select(key => "0x" + key));

    private static string Trim(string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 240 ? text : text[..240] + "…";
    }

    /// <summary>ISO 14229 negative response codes that occur when a module refuses a read request.</summary>
    public static string NrcName(byte code) => code switch
    {
        0x10 => "generalReject",
        0x11 => "serviceNotSupported",
        0x12 => "subFunctionNotSupported",
        0x13 => "incorrectMessageLengthOrInvalidFormat",
        0x14 => "responseTooLong",
        0x21 => "busyRepeatRequest",
        0x22 => "conditionsNotCorrect",
        0x24 => "requestSequenceError",
        0x31 => "requestOutOfRange",
        0x33 => "securityAccessDenied",
        0x35 => "invalidKey",
        0x7E => "subFunctionNotSupportedInActiveSession",
        0x7F => "serviceNotSupportedInActiveSession",
        _ => "unknown"
    };
}
