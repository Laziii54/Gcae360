namespace CarScanner.Services;

/// <summary>
/// Pull-to-refresh that starts a real refresh only when none is running. RefreshView sets its own flag to true before
/// the command runs, so the command must not look at that flag: it asks <paramref name="isBusy"/> about the page's own
/// work instead. A pull while a refresh is running is dropped (no second request to the car), and the completion
/// callback (a light haptic tap) runs only after a refresh that really ran and finished without an error.
/// </summary>
public static class PullRefreshRunner
{
    public static async Task<bool> RunAsync(Func<Task> refresh, Func<bool> isBusy, Action<bool> setRefreshing, Action? onCompleted = null)
    {
        if (isBusy())
        {
            setRefreshing(false);
            return false;
        }

        setRefreshing(true);
        try
        {
            await refresh().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            setRefreshing(false);
        }

        try { onCompleted?.Invoke(); }
        catch { /* a missing haptic engine must not turn a refresh into an error */ }
        return true;
    }
}
