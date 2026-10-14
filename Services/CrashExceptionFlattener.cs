using System.Reflection;
using System.Text.RegularExpressions;

namespace CarScanner.Services;

/// <summary>
/// Turns an exception into the parts a crash report should describe, without adding any new data to the report.
/// Why this exists: every task whose exception nobody observed reached the unobserved-task handler wrapped in an
/// <see cref="AggregateException"/>. That wrapper is never thrown, so it has no stack trace, and all of those events were
/// filed under one useless group ("System.AggregateException", 24 times in the last 30 days). The inner exceptions carry
/// the real type and stack; those are what get reported now. Normal user cancellation is not a failure and is dropped.
/// </summary>
public static class CrashExceptionFlattener
{
    private static readonly Regex SourcePath = new(@"\s+in\s+[^\r\n]+:line\s+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Secret = new(@"(?i)(api[_-]?key|authorization|bearer|token)(\s*[:=]\s*)\S+", RegexOptions.Compiled);

    /// <summary>Most reports one event may produce (an aggregate of many failures is capped).</summary>
    public const int MaxParts = 3;

    /// <summary>
    /// The exceptions that actually failed: aggregates are expanded (nested ones too), reflection wrappers are unwrapped,
    /// cancellations are left out. Empty when nothing but cancellation happened.
    /// </summary>
    public static IReadOnlyList<Exception> Leaves(Exception exception)
    {
        var result = new List<Exception>();
        Collect(exception, result, 0);
        return result;
    }

    private static void Collect(Exception exception, List<Exception> result, int depth)
    {
        if (depth > 8 || result.Count >= MaxParts * 4)
            return;

        switch (exception)
        {
            case AggregateException aggregate:
                foreach (var inner in aggregate.InnerExceptions)
                    Collect(inner, result, depth + 1);
                break;
            case TargetInvocationException { InnerException: { } inner }:
                Collect(inner, result, depth + 1);
                break;
            case OperationCanceledException:
                break;
            default:
                result.Add(exception);
                break;
        }
    }

    /// <summary>True when the event is only cancellation (a user leaving a page, a token being cancelled).</summary>
    public static bool IsOnlyCancellation(Exception exception) => Leaves(exception).Count == 0;

    /// <summary>The description of one reported part: exception type name only, never the message.</summary>
    public static string TypeName(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;

    /// <summary>Stack text without source paths and without anything that looks like a credential, capped.</summary>
    public static string SanitizeStack(string? stack, int limit = 8_000)
    {
        if (string.IsNullOrWhiteSpace(stack))
            return string.Empty;

        var sanitized = SourcePath.Replace(stack, string.Empty);
        sanitized = Secret.Replace(sanitized, "$1[REDACTED]");
        return sanitized.Length <= limit ? sanitized : sanitized[..limit];
    }

    /// <summary>Same type and same top frames = the same problem: used to report a repeated failure once per run.</summary>
    public static string Fingerprint(string exceptionType, string sanitizedStack)
    {
        var frames = sanitizedStack
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Take(4);
        return exceptionType + "|" + string.Join("|", frames);
    }

    /// <summary>
    /// The category of an event, in words the administrator can group by (kept inside the existing <c>source</c> field,
    /// so the report format the server accepts does not change).
    /// </summary>
    public static string Category(string source, bool fatal, bool wasAggregate)
    {
        var prefix = fatal ? "fatal" : "recoverable";
        return $"{prefix}:{source}" + (wasAggregate ? "/aggregate" : string.Empty);
    }
}
