using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CarScanner.Services.Localization;

public static class ThemeService
{
    public static void Apply()
    {
        if (Application.Current is null)
            return;

        // A clean light interface is the first-run default. A saved user
        // preference is always respected after that.
        var pref = Preferences.Default.Get("theme", "Light");

        Application.Current.UserAppTheme = pref switch
        {
            "Light" => AppTheme.Light,
            "System" => AppTheme.Unspecified,
            _ => AppTheme.Dark
        };

        var theme = pref == "System"
            ? Application.Current.RequestedTheme
            : Application.Current.UserAppTheme;

        var isLight = theme == AppTheme.Light;
        var resources = Application.Current.Resources;
        // Calm, flat palette (same as the website): neutral greys, one blue
        // accent and a soft amber PRO badge. No gradients, no purple.
        resources["PageBg"] = Color.FromArgb(isLight ? "#F4F5F3" : "#121416");
        resources["CardBg"] = Color.FromArgb(isLight ? "#FFFFFF" : "#1B1E22");
        resources["TextColor"] = Color.FromArgb(isLight ? "#1B1D20" : "#ECEEF0");
        resources["SubTextColor"] = Color.FromArgb(isLight ? "#5D636B" : "#9AA1A9");
        resources["BorderColor"] = Color.FromArgb(isLight ? "#DCDFE3" : "#2C3036");
        resources["DividerColor"] = Color.FromArgb(isLight ? "#ECEEF0" : "#24282D");
        resources["ButtonBg"] = Color.FromArgb(isLight ? "#FFFFFF" : "#23272C");
        resources["ButtonTextColor"] = Color.FromArgb(isLight ? "#1B1D20" : "#ECEEF0");
        resources["Primary"] = Color.FromArgb(isLight ? "#1D5BD6" : "#4F82E8");
        resources["ProBadgeBg"] = Color.FromArgb(isLight ? "#FBEECF" : "#3A2E12");
        resources["ProBadgeText"] = Color.FromArgb(isLight ? "#8A5A00" : "#F0B840");
        // Featured actions deliberately use high-contrast colors in both themes.
        // They should remain immediately visible among the neutral service actions.
        resources["ProButtonBg"] = Color.FromArgb(isLight ? "#7C2D12" : "#78350F");
        resources["ProButtonText"] = Color.FromArgb("#FFFFFF");
        resources["ProButtonBorder"] = Color.FromArgb(isLight ? "#FBBF24" : "#FCD34D");
        resources["AiServiceBg"] = Color.FromArgb(isLight ? "#1D5BD6" : "#4F82E8");
        resources["AiServiceText"] = Color.FromArgb("#FFFFFF");
        resources["AiServiceBorder"] = Color.FromArgb(isLight ? "#1D5BD6" : "#4F82E8");
        resources["AccelerationBg"] = Color.FromArgb(isLight ? "#075985" : "#0C4A6E");
        resources["AccelerationText"] = Color.FromArgb("#FFFFFF");
        resources["AccelerationBorder"] = Color.FromArgb(isLight ? "#7DD3FC" : "#38BDF8");
    }
}
