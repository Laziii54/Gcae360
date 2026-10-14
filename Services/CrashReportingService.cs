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
    // Older versions kept a single pending report in this file.
    private const string PendingFileName = "pending-crash-report.json";
    private const string PendingFolderName = "pending-crash-reports";

    /// <summary>At most this many reports wait for upload; older ones are dropped.</summary>
    private const int MaxPending = 5;

    /// <summary>The server refuses reports older than 30 days (permanent 400).</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
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

    /// <summary>Failures already reported in this run (a loop that fails every second must not fill the queue).</summary>
    private readonly HashSet<string> _reportedThisRun = new(StringComparer.Ordinal);

    public void Capture(Exception exception, string source, bool fatal)
    {
        try
        {
            // User cancellation is not a failure.
            if (!fatal && CrashExceptionFlattener.IsOnlyCancellation(exception))
                return;

            // An AggregateException has no stack of its own: report what actually failed inside it.
            var wasAggregate = exception is AggregateException;
            var parts = CrashExceptionFlattener.Leaves(exception);
            if (parts.Count == 0)
                parts = new[] { exception };

            foreach (var part in parts.Take(CrashExceptionFlattener.MaxParts))
            {
                var type = Limit(CrashExceptionFlattener.TypeName(part), 200);
                var stack = CrashExceptionFlattener.SanitizeStack(part.StackTrace);
                var fingerprint = CrashExceptionFlattener.Fingerprint(type, stack);
                lock (_reportedThisRun)
                {
                    if (_reportedThisRun.Count > 200 || !_reportedThisRun.Add(fingerprint))
                        continue;
                }

                var report = new CrashEnvelope(
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow,
                    AppInfo.Current.VersionString ?? "unknown",
                    DeviceInfo.Current.Platform.ToString(),
                    DeviceInfo.Current.VersionString ?? "unknown",
                    type,
                    stack,
                    Limit(CrashExceptionFlattener.Category(source, fatal, wasAggregate), 80),
                    fatal);

                var json = JsonSerializer.Serialize(report);
                Directory.CreateDirectory(PendingFolder);
                File.WriteAllText(Path.Combine(PendingFolder, report.ReportId + ".json"), json);
            }

            TrimPending();
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
            if (!Uri.TryCreate(AIConfig.BackendBaseUrl, UriKind.Absolute, out var baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps)
                return;

            MigrateLegacyFile();
            if (!Directory.Exists(PendingFolder))
                return;
            TrimPending();

            foreach (var file in new DirectoryInfo(PendingFolder).GetFiles("*.json").OrderBy(f => f.LastWriteTimeUtc))
            {
                CrashEnvelope? report;
                try
                {
                    report = JsonSerializer.Deserialize<CrashEnvelope>(await File.ReadAllTextAsync(file.FullName, token));
                }
                catch (JsonException)
                {
                    report = null;
                }

                // Unreadable or too old for the server: it would be refused forever.
                if (report is null || report.OccurredAt < DateTimeOffset.UtcNow - MaxAge)
                {
                    TryDelete(file.FullName);
                    continue;
                }

                using var response = await _http.PostAsJsonAsync(
                    new Uri(baseUri, "/api/telemetry/crash"), report, token);

                // 400 = the server will never accept this report; do not retry it.
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    TryDelete(file.FullName);
                    continue;
                }

                // Rate limited or server down: keep the rest for the next launch.
                break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            // Keep the reports for the next launch.
        }
    }

    /// <summary>Moves the old single pending report into the folder.</summary>
    private static void MigrateLegacyFile()
    {
        try
        {
            if (!File.Exists(PendingPath))
                return;
            Directory.CreateDirectory(PendingFolder);
            File.Move(PendingPath, Path.Combine(PendingFolder, "legacy-" + Guid.NewGuid().ToString("N") + ".json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(PendingPath);
        }
    }

    /// <summary>Keeps only the newest <see cref="MaxPending"/> reports.</summary>
    private static void TrimPending()
    {
        try
        {
            if (!Directory.Exists(PendingFolder))
                return;
            foreach (var old in new DirectoryInfo(PendingFolder).GetFiles("*.json")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxPending))
                TryDelete(old.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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

    private static string PendingFolder =>
        Path.Combine(FileSystem.Current.AppDataDirectory, PendingFolderName);

    private static string Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= length ? value : value[..length];
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
