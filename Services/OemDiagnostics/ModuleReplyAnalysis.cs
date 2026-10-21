using System.Globalization;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Pure, adapter-independent helpers for the module scan: which frames of an adapter reply really came from the
/// module that was asked, how ISO-TP messages are assembled (and which ones stay incomplete), and how one
/// request/reply exchange is classified. Nothing here talks to hardware, so it is covered by tests with
/// frames copied from real captures.
///
/// Evidence rules:
/// • a frame counts for a module only when its CAN id is that module's reply id;
/// • frames from another id are kept as "foreign" (never discarded silently, never attributed to the module);
/// • "NO DATA" is silence on the asked reply id: it does not prove the module is absent.
/// </summary>
public static class ModuleReplyAnalysis
{
    /// <summary>One CAN id that answered although a different id was expected, and how many frames it sent.</summary>
    public sealed record ForeignSender(string Header, int Frames);

    /// <summary>Adapter output split by sender.</summary>
    public sealed class SplitReply
    {
        /// <summary>Data bytes (PCI included) of the frames sent by the expected reply id, in arrival order.</summary>
        public List<byte[]> Own { get; } = new();

        /// <summary>Frames from any other id (the adapter delivered them although they were not asked for).</summary>
        public List<ForeignSender> Foreign { get; } = new();

        /// <summary>Non-frame words from the adapter ("NO DATA", "CAN ERROR" …), joined by spaces.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Tokens that looked like frames but had an impossible length (cut or garbled output).</summary>
        public int Malformed { get; set; }
    }

    /// <summary>An ISO-TP message that started (first frame) but did not arrive completely.</summary>
    public sealed record IsoTpFragment(int ExpectedLength, byte[] Data);

    public sealed class IsoTpAssembly
    {
        public List<byte[]> Messages { get; } = new();
        public List<IsoTpFragment> Incomplete { get; } = new();

        /// <summary>Frames that could not be used (single frame with an impossible length, orphan consecutive frame …).</summary>
        public int Unusable { get; set; }
    }

    /// <summary>
    /// Splits adapter output into frames. Compact tokens ("7E8037F1911", ATS0) and spaced frames
    /// ("7E8 03 7F 19 11", ATS1, lines joined by spaces) are both understood.
    /// </summary>
    public static SplitReply Split(string? raw, int expectedReceiveId, bool ext29)
    {
        var result = new SplitReply();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var width = ext29 ? 8 : 3;
        var expected = expectedReceiveId.ToString(ext29 ? "X8" : "X3", CultureInfo.InvariantCulture);
        var foreign = new Dictionary<string, int>(StringComparer.Ordinal);
        var status = new List<string>();

        string? spacedHeader = null;
        var spacedData = new List<byte>();

        void Accept(string header, List<byte> data)
        {
            if (data.Count == 0)
            {
                result.Malformed++;
                return;
            }

            if (string.Equals(header, expected, StringComparison.Ordinal))
                result.Own.Add(data.ToArray());
            else
                foreign[header] = foreign.GetValueOrDefault(header) + 1;
        }

        void FlushSpaced()
        {
            if (spacedHeader is not null)
                Accept(spacedHeader, spacedData);
            spacedHeader = null;
            spacedData = new List<byte>();
        }

        foreach (var rawToken in raw.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim().ToUpperInvariant();
            var isHex = token.Length > 0 && token.All(Uri.IsHexDigit);

            if (isHex && token.Length == width)
            {
                // Spaced format: a bare CAN id, the data bytes follow as two-digit tokens.
                FlushSpaced();
                spacedHeader = token;
            }
            else if (isHex && token.Length == 2 && spacedHeader is not null)
            {
                spacedData.Add(byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            else if (isHex && token.Length > width && (token.Length - width) % 2 == 0)
            {
                FlushSpaced();
                var data = new List<byte>((token.Length - width) / 2);
                for (var i = width; i < token.Length; i += 2)
                    data.Add(byte.Parse(token.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                Accept(token[..width], data);
            }
            else
            {
                FlushSpaced();
                if (isHex)
                    result.Malformed++;
                else
                    status.Add(token);
            }
        }

        FlushSpaced();
        result.Status = string.Join(" ", status);
        foreach (var pair in foreign.OrderBy(item => item.Key, StringComparer.Ordinal))
            result.Foreign.Add(new ForeignSender(pair.Key, pair.Value));
        return result;
    }

    /// <summary>One CAN frame of adapter output, as it was written: its CAN id and its text (the id and the data tokens it was made of).</summary>
    public sealed record FrameText(string Header, string Text);

    /// <summary>
    /// Splits one run of adapter output into its frames, keeping each frame's text and its own CAN id. Use it where frames of
    /// several modules may sit in the same text and each frame must stay tied to the id that sent it.
    /// <para>
    /// FORMAT (ATH1, headers on): the ELM327 prints every CAN frame on a line of its own. With ATS0 (compact) a frame is ONE token:
    /// the id (3 hex digits, 11-bit; 8, 29-bit) followed by its data bytes, e.g. <c>7EF05624181CED8</c>. With ATS1 (spaced) the id is a
    /// token of its own followed by two-digit byte tokens, e.g. <c>7EF 05 62 41 81 CE D8</c>. The protocol layer
    /// (<c>Elm327Protocol.CleanResponse</c>) joins the lines with single spaces, so several frames can end up in one line:
    /// <c>7E8 05 62 41 81 AA BB 7EF05624181CED8</c>. A frame therefore starts at a token that is exactly an id wide (spaced), or at
    /// a token longer than an id whose remainder is whole bytes (compact); a two-digit token is data of the frame in progress. These
    /// shapes cannot be mistaken for each other for 11-bit ids (data tokens are 2 digits, ids 3). The rules are the same as in
    /// <see cref="Split"/>, which returns the bytes of the asked id's frames; this returns the texts and the ids of all frames.
    /// </para>
    /// Returns null when the text is not made of frames alone — a status word, an echoed command, a token of impossible length, or an
    /// id without data — so that the caller keeps handling such text as it always did. Nothing is repaired or invented.
    /// </summary>
    public static IReadOnlyList<FrameText>? SplitFrameTexts(string? raw, bool ext29)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var width = ext29 ? 8 : 3;
        var frames = new List<FrameText>();
        string? header = null;
        var parts = new List<string>();

        bool Flush()
        {
            if (header is null)
                return true;
            if (parts.Count < 2)
                return false;                                       // an id with no data
            frames.Add(new FrameText(header, string.Join(" ", parts)));
            header = null;
            parts.Clear();
            return true;
        }

        foreach (var rawToken in raw.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim().ToUpperInvariant();
            if (token.Length == 0 || !token.All(Uri.IsHexDigit))
                return null;

            if (token.Length == width)
            {
                if (!Flush())
                    return null;
                header = token;
                parts.Add(token);
            }
            else if (token.Length == 2 && header is not null)
            {
                parts.Add(token);
            }
            else if (token.Length > width && (token.Length - width) % 2 == 0)
            {
                if (!Flush())
                    return null;
                frames.Add(new FrameText(token[..width], token));
            }
            else
            {
                return null;
            }
        }

        return Flush() && frames.Count > 0 ? frames : null;
    }

    /// <summary>
    /// ISO-TP messages from frames: single frames, first + consecutive frames. A message that never completes is
    /// returned in <see cref="IsoTpAssembly.Incomplete"/> instead of being dropped.
    /// </summary>
    public static IsoTpAssembly Assemble(IReadOnlyList<byte[]> frames)
    {
        var result = new IsoTpAssembly();
        List<byte>? buffer = null;
        var expected = 0;
        var sequence = 0;

        void AbandonBuffer()
        {
            if (buffer is not null)
                result.Incomplete.Add(new IsoTpFragment(expected, buffer.ToArray()));
            buffer = null;
        }

        foreach (var frame in frames)
        {
            if (frame.Length == 0)
                continue;

            switch (frame[0] >> 4)
            {
                case 0:
                {
                    var length = frame[0] & 0x0F;
                    if (length > 0 && length <= frame.Length - 1)
                    {
                        AbandonBuffer();
                        result.Messages.Add(frame.Skip(1).Take(length).ToArray());
                    }
                    else
                    {
                        result.Unusable++;
                    }

                    break;
                }
                case 1:
                {
                    var length = frame.Length >= 2 ? ((frame[0] & 0x0F) << 8) | frame[1] : 0;
                    // A first frame announces more than a single frame can hold.
                    if (length < 8)
                    {
                        result.Unusable++;
                        break;
                    }

                    AbandonBuffer();
                    expected = length;
                    buffer = new List<byte>(Math.Min(length, 4095));
                    buffer.AddRange(frame.Skip(2));
                    sequence = 1;
                    break;
                }
                case 2:
                {
                    if (buffer is null)
                    {
                        result.Unusable++;
                        break;
                    }

                    var number = frame[0] & 0x0F;
                    if (number == ((sequence - 1) & 0x0F))
                        break; // the same consecutive frame delivered twice

                    if (number != (sequence & 0x0F))
                    {
                        AbandonBuffer(); // a frame was lost: what we have is a fragment, not a message
                        break;
                    }

                    buffer.AddRange(frame.Skip(1));
                    sequence++;
                    break;
                }
                default:
                    // Flow control (3) and anything else carries no payload for us.
                    break;
            }

            if (buffer is not null && buffer.Count >= expected)
            {
                result.Messages.Add(buffer.Take(expected).ToArray());
                buffer = null;
            }
        }

        AbandonBuffer();
        return result;
    }

    // ------------------------------------------------------------------
    // One request / reply exchange
    // ------------------------------------------------------------------

    /// <summary>Sends one ISO-TP request and returns the adapter output (frames as tokens).</summary>
    public delegate Task<string> ModuleSend(bool longWait);

    /// <summary>What one request got back from the module that was asked.</summary>
    public sealed record ModuleExchange(
        byte[]? Positive,
        byte? Nrc,
        bool AnyReply,
        IsoTpFragment? Fragment,
        IReadOnlyList<ForeignSender> Foreign,
        string Status,
        string Raw,
        int Attempts)
    {
        /// <summary>The reply started but did not arrive completely.</summary>
        public bool Incomplete => Positive is null && Nrc is null && Fragment is not null;

        /// <summary>The incomplete reply begins like a positive answer to <paramref name="service"/>.</summary>
        public bool FragmentIsPositiveFor(byte service) =>
            Fragment is { Data.Length: > 0 } fragment && fragment.Data[0] == (byte)(service + 0x40);
    }

    /// <summary>
    /// Runs one request. A "response pending" (7F xx 78) reply, or a multi-frame reply that stopped after its
    /// first frame, is asked once more with a longer answer window; nothing is repeated otherwise.
    /// </summary>
    public static async Task<ModuleExchange> ExchangeAsync(
        ModuleSend send,
        int expectedReceiveId,
        bool ext29,
        byte? extendedAddress,
        byte service)
    {
        var attempts = 1;
        var raw = await send(false).ConfigureAwait(false);
        var split = Split(raw, expectedReceiveId, ext29);
        var assembly = Assemble(StripExtended(split.Own, extendedAddress));
        var foreign = new Dictionary<string, int>(StringComparer.Ordinal);
        MergeForeign(foreign, split);

        var onlyPending = assembly.Messages.Count > 0 && assembly.Messages.All(message => IsPending(message, service));
        var stalledMultiFrame = assembly.Messages.Count == 0 && assembly.Incomplete.Count > 0;
        if (onlyPending || stalledMultiFrame)
        {
            attempts++;
            var retryRaw = await send(true).ConfigureAwait(false);
            var retry = Split(retryRaw, expectedReceiveId, ext29);
            MergeForeign(foreign, retry);
            var retryAssembly = Assemble(StripExtended(retry.Own, extendedAddress));

            // The longer window can only improve on what was received.
            if (retryAssembly.Messages.Count > 0 || retryAssembly.Incomplete.Count >= assembly.Incomplete.Count)
            {
                split = retry.Own.Count > 0 || split.Own.Count == 0 ? retry : split;
                assembly = retryAssembly;
                raw = retryRaw;
            }
        }

        var positive = assembly.Messages.LastOrDefault(message => message.Length > 0 && message[0] == (byte)(service + 0x40));
        var negative = assembly.Messages.LastOrDefault(message =>
            message.Length >= 3 && message[0] == 0x7F && message[1] == service && message[2] != 0x78);
        var fragment = assembly.Incomplete.OrderByDescending(item => item.Data.Length).FirstOrDefault();
        var any = assembly.Messages.Count > 0 || assembly.Incomplete.Count > 0;

        return new ModuleExchange(
            positive,
            positive is null ? negative?[2] : null,
            any,
            positive is null && negative is null ? fragment : null,
            foreign.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new ForeignSender(item.Key, item.Value)).ToList(),
            split.Status,
            Trim(raw),
            attempts);
    }

    public static bool IsPending(byte[] message, byte service) =>
        message.Length >= 3 && message[0] == 0x7F && message[1] == service && message[2] == 0x78;

    /// <summary>BMW-style extended addressing: replies start with the tester address (0xF1).</summary>
    public static List<byte[]> StripExtended(List<byte[]> frames, byte? extendedAddress)
    {
        if (!extendedAddress.HasValue)
            return frames;

        return frames
            .Select(frame => frame.Length > 1 && frame[0] == 0xF1 ? frame[1..] : frame)
            .ToList();
    }

    private static void MergeForeign(Dictionary<string, int> target, SplitReply reply)
    {
        foreach (var sender in reply.Foreign)
            target[sender.Header] = Math.Max(target.GetValueOrDefault(sender.Header), sender.Frames);
    }

    private static string Trim(string? raw)
    {
        var text = (raw ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 240 ? text : text[..240] + "…";
    }
}

/// <summary>How one probed module address ended. "No reply" is not "absent".</summary>
public enum ModuleProbeState
{
    /// <summary>Nothing came back on the module's reply id (NO DATA / timeout). Absent, asleep and unreachable look the same.</summary>
    NoReply,

    /// <summary>The module returned a usable answer.</summary>
    Answered,

    /// <summary>The module replied, but only with a refusal (negative response) to the trouble-code request.</summary>
    AnsweredNegativeOnly,

    /// <summary>The module started a multi-frame answer that did not arrive completely.</summary>
    AnsweredIncomplete,

    /// <summary>Only frames from a different CAN id came back; nothing from the asked module.</summary>
    ForeignReplyOnly,

    /// <summary>The request could not be sent or read (adapter or transport error).</summary>
    Error
}

/// <summary>Outcome of probing one module address, kept for the scan summary.</summary>
public sealed record ModuleProbe(
    string ModuleId,
    int Tx,
    int Rx,
    bool Ext29,
    byte? ExtendedAddress,
    ModuleProbeState State,
    string Detail,
    string? ForeignSender,
    string Raw)
{
    public string AddressText => Ext29 ? $"0x{Tx:X8}" : $"0x{Tx:X3}" + (ExtendedAddress is { } ext ? $"/{ext:X2}" : string.Empty);

    /// <summary>Probes whose silence says nothing certain about the module.</summary>
    public static ModuleProbeState ClassifySilent(ModuleReplyAnalysis.ModuleExchange exchange) =>
        exchange.Foreign.Count > 0 ? ModuleProbeState.ForeignReplyOnly : ModuleProbeState.NoReply;

    public static string Describe(ModuleProbeState state, string? foreignSender = null) => state switch
    {
        ModuleProbeState.Answered => "answered",
        ModuleProbeState.AnsweredNegativeOnly => "answered with a refusal only (no trouble-code list)",
        ModuleProbeState.AnsweredIncomplete => "answered, but the reply arrived incomplete",
        ModuleProbeState.ForeignReplyOnly => $"nothing from this module; only a reply from {foreignSender ?? "another id"}",
        ModuleProbeState.Error => "request failed",
        _ => "no reply (absent, asleep or not reachable with this request)"
    };
}
