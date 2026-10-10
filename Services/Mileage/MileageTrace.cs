namespace CarScanner.Services.Mileage;

/// <summary>What happened when a source was tried (or why it was not).</summary>
public enum MileageAttemptOutcome
{
    /// <summary>A value came back and was decoded.</summary>
    Read,

    /// <summary>Nothing was sent: the source does not apply to this car, or is unknown (see the compatibility fields).</summary>
    NotTried,

    /// <summary>The request was sent and nothing (or NO DATA) came back.</summary>
    ModuleNotResponding,

    /// <summary>The module answered but declined (negative reply) or has no such value.</summary>
    ReadingUnavailable,

    /// <summary>The module answered, but the reply was damaged, incomplete or implausible.</summary>
    InvalidReading
}

public sealed record MileageAttempt(
    string Source,
    string Method,
    MileageAttemptOutcome Outcome,
    string Detail,
    MileageSourceStatus? Compatibility = null,
    MileageSourceReason? Reason = null);

/// <summary>
/// Collects what the mileage check asked the car and what it answered, so a
/// failed check can be explained on the page and copied for support.
/// </summary>
public static class MileageTrace
{
    private static readonly object Gate = new();
    private static readonly List<string> Lines = new();
    private static readonly List<MileageAttempt> Attempts = new();

    public static void Reset()
    {
        lock (Gate)
        {
            Lines.Clear();
            Attempts.Clear();
        }
    }

    public static void Add(string line)
    {
        lock (Gate)
        {
            if (Lines.Count < 200)
                Lines.Add(line);
        }
    }

    public static void Attempt(MileageAttempt attempt)
    {
        lock (Gate)
        {
            if (Attempts.Count < 100)
                Attempts.Add(attempt);
        }
    }

    public static IReadOnlyList<MileageAttempt> AttemptsSnapshot()
    {
        lock (Gate)
            return Attempts.ToArray();
    }

    public static string Snapshot()
    {
        lock (Gate)
            return string.Join(Environment.NewLine, Lines);
    }
}
