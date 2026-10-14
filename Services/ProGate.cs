namespace CarScanner.Services;

/// <summary>
/// PRO pages open for everyone so the user can see what is inside. Only the
/// button that starts the work is PRO: it carries a "PRO" mark and, without
/// an active PRO, goes straight to the PRO (payment) page.
/// </summary>
public static class ProGate
{
    private static ProService? Pro => IPlatformApplication.Current?.Services.GetService<ProService>();

    /// <summary>True when the "PRO" mark should be shown on start buttons.</summary>
    public static bool IsLocked => Pro is not { HasObd: true };

    /// <summary>" · PRO" suffix for a start button's text while PRO is not active.</summary>
    public static string Suffix => IsLocked ? " · PRO" : string.Empty;

    /// <summary>
    /// Older call sites: same as <see cref="EnsureOrOpenAsync"/> — no message
    /// box any more, the PRO page opens directly.
    /// </summary>
    public static Task<bool> EnsureAsync(ProService pro, string featureKa, string featureEn) => EnsureOrOpenAsync(pro);

    /// <summary>Returns true when PRO is active; otherwise opens the PRO page and returns false.</summary>
    public static async Task<bool> EnsureOrOpenAsync(ProService? pro = null)
    {
        pro ??= Pro;
        var unlocked = false;
        try
        {
            unlocked = pro is not null && await pro.EnsureProAsync();
        }
        catch
        {
            // Treat a failed check as "not unlocked".
        }

        if (unlocked)
            return true;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.ProPage)));
        }
        catch (Exception exception)
        {
            CommandErrors.Report(exception, "ProGate.OpenProPage");
        }
        return false;
    }
}
