using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CarScanner.Services;

/// <summary>
/// Persists an anonymous, sanitized crash envelope locally and sends it on the
/// next launch. Vehicle data, account data and exception messages are never
/// included in the envelope.
/// </summary>
public sealed partial class CrashReportingService
{
    private const string PendingFileName = "pending-crash-report.json";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private int _initialized;

    public void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser +=
            OnAndroidUnhandledException;
#endif
        _ = UploadPendingAsync();
    }

    public void Capture(Exception exception, string source, bool fatal)
    {
        try
        {
            var report = new CrashEnvelope(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                AppInfo.Current.VersionString ?? "unknown",
                DeviceInfo.Current.Platform.ToString(),
                DeviceInfo.Current.VersionString ?? "unknown",
                Limit(exception.GetType().FullName, 200),
                SanitizeStack(exception.StackTrace),
                Limit(source, 80),
                fatal);

            var json = JsonSerializer.Serialize(report);
            File.WriteAllText(PendingPath, json);
        }
        catch
        {
            // Crash handling must never cause a second crash.
        }
    }

    public async Task UploadPendingAsync(CancellationToken token = default)
    {
        try
        {
            if (!File.Exists(PendingPath) ||
                !Uri.TryCreate(AIConfig.BackendBaseUrl, UriKind.Absolute, out var baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps)
                return;

            var json = await File.ReadAllTextAsync(PendingPath, token);
            var report = JsonSerializer.Deserialize<CrashEnvelope>(json);
            if (report is null)
                return;

            using var response = await _http.PostAsJsonAsync(
                new Uri(baseUri, "/api/telemetry/crash"), report, token);
            if (response.IsSuccessStatusCode)
                File.Delete(PendingPath);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            // Keep the report for the next launch.
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            Capture(exception, "AppDomain", args.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Capture(args.Exception, "TaskScheduler", false);
        args.SetObserved();
    }

#if ANDROID
    private void OnAndroidUnhandledException(
        object? sender,
        Android.Runtime.RaiseThrowableEventArgs args) =>
        Capture(args.Exception, "AndroidRuntime", true);
#endif

    private static string PendingPath =>
        Path.Combine(FileSystem.Current.AppDataDirectory, PendingFileName);

    private static string SanitizeStack(string? stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
            return string.Empty;

        var sanitized = SourcePathRegex().Replace(stack, string.Empty);
        sanitized = SecretRegex().Replace(sanitized, "$1[REDACTED]");
        return Limit(sanitized, 8_000);
    }

    private static string Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= length ? value : value[..length];

    [GeneratedRegex(@"\s+in\s+[^\r\n]+:line\s+\d+", RegexOptions.IgnoreCase)]
    private static partial Regex SourcePathRegex();

    [GeneratedRegex(@"(?i)(api[_-]?key|authorization|bearer|token)(\s*[:=]\s*)\S+")]
    private static partial Regex SecretRegex();
}

public sealed record CrashEnvelope(
    string ReportId,
    DateTimeOffset OccurredAt,
    string AppVersion,
    string Platform,
    string OsVersion,
    string ExceptionType,
    string StackTrace,
    string Source,
    bool Fatal);
