using System.Text;
using System.Text.RegularExpressions;

namespace CarScanner.Services.OBD;

/// <summary>
/// A controlled, opt-in record of the raw text the adapter returned, kept for support: it is OFF unless somebody switches it
/// on, switches itself off after a while, holds a bounded number of short entries in memory only, and is copied out by the
/// user (the same way as the connection log). Nothing is sent anywhere.
///
/// It records what already happened — it never sends, retries or changes a request. Before anything is stored:
/// • replies to identification requests (Mode 09, 22 F1xx, 1A xx) are not stored at all;
/// • VIN-shaped text, hex bytes that spell a VIN and Bluetooth MAC addresses are replaced;
/// • every entry is truncated.
/// </summary>
public static class AdapterDiagnosticCapture
{
    public const int Capacity = 600;
    private const int MaxReplyLength = 240;
    private static readonly TimeSpan LongestSwitchOn = TimeSpan.FromHours(2);

    private static readonly Regex MacAddress = new(@"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])", RegexOptions.Compiled);
    private static readonly Regex VinText = new(@"(?<![A-Za-z0-9])[A-HJ-NPR-Za-hj-npr-z0-9]{17}(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex HexBytes = new(@"(?:[0-9A-Fa-f]{2}[ \t]?){17,}", RegexOptions.Compiled);

    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new();
    private static long _enabledUntilTicks;
    private static long _sequence;
    private static volatile string _transportLabel = "unknown";

    /// <summary>The clock used for the switch-off time. Tests replace it; nothing else should.</summary>
    internal static Func<DateTime> UtcNow { get; set; } = static () => DateTime.UtcNow;

    /// <summary>How the adapter is reached ("BLE", "Classic SPP", "Wi-Fi TCP"). Set by the transport when it connects.</summary>
    public static string TransportLabel
    {
        get => _transportLabel;
        set => _transportLabel = NormalizeLabel(value);
    }

    internal static string NormalizeLabel(string? label) => string.IsNullOrWhiteSpace(label) ? "unknown" : label.Trim();

    /// <summary>A duration in seconds for the logs ("2.5"), always with a decimal point so a log reads the same on every phone.</summary>
    internal static string Seconds(TimeSpan span) => span.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsEnabled
    {
        get
        {
            var until = Interlocked.Read(ref _enabledUntilTicks);
            return until != 0 && UtcNow().Ticks < until;
        }
    }

    /// <summary>How long the capture will stay on; <see cref="TimeSpan.Zero"/> when it is off or has switched itself off.</summary>
    public static TimeSpan Remaining
    {
        get
        {
            var until = Interlocked.Read(ref _enabledUntilTicks);
            var left = until == 0 ? TimeSpan.Zero : new TimeSpan(until - UtcNow().Ticks);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public static int Count
    {
        get { lock (Gate) return Lines.Count; }
    }

    /// <summary>Switches the capture on for <paramref name="duration"/> (at most two hours); it switches itself off afterwards.</summary>
    public static void Enable(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (duration > LongestSwitchOn)
            duration = LongestSwitchOn;

        Interlocked.Exchange(ref _enabledUntilTicks, (UtcNow() + duration).Ticks);
        LinkEventLog.Add($"Raw capture ON for {(int)Math.Ceiling(duration.TotalMinutes)} min (sanitized, kept in memory only)");
    }

    public static void Disable()
    {
        var wasOn = Interlocked.Exchange(ref _enabledUntilTicks, 0) != 0;
        if (wasOn)
            LinkEventLog.Add($"Raw capture OFF ({Count} entries kept until cleared)");
    }

    public static void Clear()
    {
        lock (Gate)
            Lines.Clear();
    }

    /// <summary>The captured entries as text; empty when there are none.</summary>
    public static string Export()
    {
        lock (Gate)
        {
            if (Lines.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            builder.AppendLine($"OBD360 raw capture (sanitized, newest last) — {Lines.Count} entries, link: {TransportLabel}");
            builder.AppendLine("Replies to identification requests (Mode 09, 22 F1xx, 1A xx) are not stored; VIN-like text and MAC addresses are replaced.");
            foreach (var line in Lines)
                builder.AppendLine(line);
            return builder.ToString();
        }
    }

    /// <summary>One adapter reply (raw, as the transport returned it). A no-op unless the capture is on; never throws.</summary>
    public static void RecordReply(string command, int attempt, int attempts, TimeSpan budget, TimeSpan elapsed, string? rawReply)
    {
        if (!IsEnabled)
            return;

        try
        {
            var assessment = AdapterExchangeClassifier.Classify(command, rawReply);
            var shown = IsIdentificationRequest(command)
                ? "<not stored: identification request>"
                : Escape(Sanitize(rawReply, MaxReplyLength));
            Append(command, attempt, attempts, budget, elapsed, assessment, $" reply \"{shown}\"");
        }
        catch
        {
            // Observation must never affect a diagnostic request.
        }
    }

    /// <summary>One request that produced no reply (timeout, lost link, cancellation). A no-op unless the capture is on; never throws.</summary>
    public static void RecordFailure(string command, int attempt, int attempts, TimeSpan budget, TimeSpan elapsed, Exception exception)
    {
        if (!IsEnabled)
            return;

        try
        {
            Append(command, attempt, attempts, budget, elapsed, AdapterExchangeClassifier.Classify(exception), $" ({exception.GetType().Name})");
        }
        catch
        {
            // Observation must never affect a diagnostic request.
        }
    }

    /// <summary>A request that timed out where the transport reported it as a cancellation of its own timer.</summary>
    public static void RecordTimeout(string command, int attempt, int attempts, TimeSpan budget, TimeSpan elapsed)
    {
        if (!IsEnabled)
            return;

        try
        {
            Append(command, attempt, attempts, budget, elapsed, new ExchangeAssessment(ExchangeKind.TimedOut), string.Empty);
        }
        catch
        {
            // Observation must never affect a diagnostic request.
        }
    }

    /// <summary>
    /// One short, single-line, sanitized piece of adapter text for the connection log (a banner, the reply to a refused
    /// command). Never longer than <paramref name="maxLength"/>.
    /// </summary>
    public static string Brief(string? text, int maxLength = 40)
    {
        var oneLine = Regex.Replace(Redact(text ?? string.Empty), @"[\s\u0000-\u001F>]+", " ").Trim();
        return oneLine.Length == 0 ? "no reply" : Truncate(oneLine, maxLength);
    }

    /// <summary>VIN-shaped text, hex bytes that spell a VIN and MAC addresses removed, then truncated.</summary>
    public static string Sanitize(string? text, int maxLength)
    {
        return Truncate(Redact(text ?? string.Empty), maxLength);
    }

    /// <summary>True for the requests whose replies identify the vehicle or one of its modules: Mode 09, 22 F1xx, 1A xx.</summary>
    public static bool IsIdentificationRequest(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var compact = string.Concat(command.Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant();
        if (compact.Length < 2 || !compact.All(Uri.IsHexDigit))
            return false;

        return compact.StartsWith("09", StringComparison.Ordinal) ||
               compact.StartsWith("22F1", StringComparison.Ordinal) ||
               compact.StartsWith("1A", StringComparison.Ordinal);
    }

    private static string Redact(string text)
    {
        if (text.Length == 0)
            return text;

        var result = MacAddress.Replace(text, "<mac>");
        result = HexBytes.Replace(result, match => SpellsAVin(match.Value) ? "<VIN-like bytes removed>" : match.Value);
        // A VIN uses letters G–Z (never I, O, Q) somewhere; a 17-digit run of plain hex frame bytes does not, so frames stay readable.
        result = VinText.Replace(result, match => match.Value.Any(ch => char.ToUpperInvariant(ch) is >= 'G' and <= 'Z') ? "<VIN-like text removed>" : match.Value);
        return result;
    }

    private static bool SpellsAVin(string hexRun)
    {
        var compact = string.Concat(hexRun.Where(Uri.IsHexDigit));
        var bytes = new byte[compact.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(compact.Substring(i * 2, 2), 16);

        static bool InVinAlphabet(byte b) =>
            b is >= (byte)'0' and <= (byte)'9' or (>= (byte)'A' and <= (byte)'Z' and not (byte)'I' and not (byte)'O' and not (byte)'Q');

        for (var start = 0; start + 17 <= bytes.Length; start++)
        {
            var window = bytes.AsSpan(start, 17);
            var allVinCharacters = true;
            var hasLateLetter = false;
            foreach (var b in window)
            {
                if (!InVinAlphabet(b))
                {
                    allVinCharacters = false;
                    break;
                }

                hasLateLetter |= b is >= (byte)'G' and <= (byte)'Z';
            }

            if (allVinCharacters && hasLateLetter)
                return true;
        }

        return false;
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '"': builder.Append('\''); break;
                default:
                    if (char.IsControl(ch))
                        builder.Append("\\x").Append(((int)ch).ToString("X2"));
                    else
                        builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + $"…(+{text.Length - maxLength})";

    private static void Append(string command, int attempt, int attempts, TimeSpan budget, TimeSpan elapsed, ExchangeAssessment assessment, string detail)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var line = $"{DateTime.Now:HH:mm:ss.fff}  #{sequence} [{TransportLabel}] {Brief(command, 60)} try {attempt}/{attempts}, " +
                   $"wait {Seconds(budget)}s, took {(long)elapsed.TotalMilliseconds} ms -> {assessment}{detail}";
        lock (Gate)
        {
            Lines.Enqueue(line);
            while (Lines.Count > Capacity)
                Lines.Dequeue();
        }
    }
}
