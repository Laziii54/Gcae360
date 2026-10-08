using CarScanner.Services.Localization;

namespace CarScanner.Services;

/// <summary>
/// Launcher icon that follows the app theme: dark theme → dark icon,
/// light theme → light icon ("System" follows the phone's theme).
/// Android: two activity-aliases (.LauncherLight / .LauncherDark) in
/// AndroidManifest.xml; exactly one of them is enabled.
/// </summary>
public static class AppIconSwitcher
{
    // Switching the launcher activity-alias removes pinned home-screen
    // shortcuts on many launchers, so it happens only after the user changed
    // the theme in Settings — never for OS theme changes or on every stop.
    private const string PendingKey = "app_icon_switch_pending";

    /// <summary>Called by Settings when the user explicitly picks a theme.</summary>
    public static void RequestSwitch()
    {
        try { Preferences.Default.Set(PendingKey, true); }
        catch { }
    }

    /// <summary>Applies the icon only if Settings requested it (call while in the background).</summary>
    public static void ApplyIfRequested()
    {
        try
        {
            if (!Preferences.Default.Get(PendingKey, false))
                return;
            Preferences.Default.Remove(PendingKey);
        }
        catch
        {
            return;
        }

        Apply();
    }

    public static void Apply()
    {
#if ANDROID
        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app is null)
                return;

            var pref = Preferences.Default.Get("theme", "Light");
            var dark = pref switch
            {
                "Light" => false,
                "System" => app.PlatformAppTheme == AppTheme.Dark,
                _ => true
            };

            var context = Android.App.Application.Context;
            var packageManager = context.PackageManager;
            var packageName = context.PackageName;
            if (packageManager is null || string.IsNullOrEmpty(packageName))
                return;

            var light = new Android.Content.ComponentName(packageName, packageName + ".LauncherLight");
            var darkAlias = new Android.Content.ComponentName(packageName, packageName + ".LauncherDark");

            var wanted = dark ? darkAlias : light;
            var other = dark ? light : darkAlias;

            if (IsEnabled(packageManager, wanted, defaultEnabled: !dark) &&
                !IsEnabled(packageManager, other, defaultEnabled: dark))
                return;

            // Turn the new one on first, so there is always a launcher entry.
            packageManager.SetComponentEnabledSetting(
                wanted,
                Android.Content.PM.ComponentEnabledState.Enabled,
                Android.Content.PM.ComponentEnableOption.DontKillApp);
            packageManager.SetComponentEnabledSetting(
                other,
                Android.Content.PM.ComponentEnabledState.Disabled,
                Android.Content.PM.ComponentEnableOption.DontKillApp);
        }
        catch
        {
            // The icon is cosmetic; never let it break the app.
        }
#endif
    }

#if ANDROID
    private static bool IsEnabled(
        Android.Content.PM.PackageManager packageManager,
        Android.Content.ComponentName component,
        bool defaultEnabled)
    {
        var state = packageManager.GetComponentEnabledSetting(component);
        return state switch
        {
            Android.Content.PM.ComponentEnabledState.Enabled => true,
            Android.Content.PM.ComponentEnabledState.Disabled => false,
            _ => defaultEnabled
        };
    }
#endif
}
