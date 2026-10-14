using CarScanner.Services.Localization;

namespace CarScanner.Services;

/// <summary>
/// Last line of defence for button commands and page events. An exception in an
/// async command or "async void" handler would otherwise close the whole app;
/// here it is logged for the crash report page and shown as a short message.
/// </summary>
public static class CommandErrors
{
    private static DateTime _lastAlert = DateTime.MinValue;

    public static void Report(Exception exception, string source)
    {
        if (exception is OperationCanceledException)
            return;

        System.Diagnostics.Debug.WriteLine($"{source}: {exception}");
        try
        {
            var reporter = IPlatformApplication.Current?.Services.GetService<CrashReportingService>();
            if (reporter is not null)
            {
                reporter.Capture(exception, source, fatal: false);
                _ = reporter.UploadPendingAsync();
            }
        }
        catch
        {
            // Reporting must never throw.
        }

        // One message at a time, even if several handlers fail together.
        if (DateTime.UtcNow - _lastAlert < TimeSpan.FromSeconds(3))
            return;
        _lastAlert = DateTime.UtcNow;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                Page? page = Shell.Current;
                page ??= Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null)
                    return;

                // Never show raw exception text (framework / socket / SQL
                // messages); UserError turns it into a short sentence and keeps
                // only messages the app itself wrote for the user.
                var detail = UserError.Describe(exception);
                var generic = UserError.Describe(null);
                await page.DisplayAlertLocalizedAsync(
                    LocalizationService.T("რაღაც ვერ შესრულდა"),
                    LocalizationService.T("მოქმედება ვერ დასრულდა. შეამოწმე კავშირი და სცადე თავიდან.") +
                    (string.IsNullOrWhiteSpace(detail) || string.Equals(detail, generic, StringComparison.Ordinal)
                        ? string.Empty
                        : "\n\n" + detail),
                    "OK");
            }
            catch
            {
                // The page may be closing; nothing else to do.
            }
        });
    }
}
