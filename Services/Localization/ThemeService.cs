using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CarScanner.Services.Localization;

public static class ThemeService
{
    // Last applied "preference|effective theme"; Apply() is called on every
    // navigation, so the ~40 resource writes are skipped when nothing changed.
    private static string? _lastAppliedKey;

    /// <summary>
    /// Applies the saved theme. Cheap when the theme has not changed since the
    /// last call; pass <paramref name="force"/> = true after the user changes
    /// the theme in Settings.
    /// </summary>
    public static void Apply(bool force = false)
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
        IsDark = !isLight;

        var key = pref + "|" + (isLight ? "L" : "D");
        if (!force && string.Equals(key, _lastAppliedKey, StringComparison.Ordinal))
            return;
        _lastAppliedKey = key;

        var resources = Application.Current.Resources;
        // Calm, flat palette (same as the website): neutral greys, one blue
        // accent and a soft amber PRO badge. No gradients, no purple.
        resources["PageBg"] = Color.FromArgb(isLight ? "#F4F5F3" : "#121416");
        resources["CardBg"] = Color.FromArgb(isLight ? "#FFFFFF" : "#1B1E22");
        resources["TextColor"] = Color.FromArgb(isLight ? "#1B1D20" : "#ECEEF0");
        resources["SubTextColor"] = Color.FromArgb(isLight ? "#5D636B" : "#9AA1A9");
        resources["BorderColor"] = Color.FromArgb(isLight ? "#DCDFE3" : "#2C3036");
        // A softer, less contrasty surface for grouped segmented controls (Graph Studio's view switcher).
        resources["SegTrackBg"] = Color.FromArgb(isLight ? "#EDEEEA" : "#1C1F23");
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
        resources["WarnBg"] = Color.FromArgb(isLight ? "#FFF6EC" : "#2E2412");
        resources["WarnStroke"] = Color.FromArgb(isLight ? "#F0C79A" : "#6B4E1F");
        resources["WarnTitle"] = Color.FromArgb(isLight ? "#9A4E0E" : "#F0B860");
        resources["WarnText"] = Color.FromArgb(isLight ? "#5A3A1A" : "#E6D3B8");
        resources["ErrBg"] = Color.FromArgb(isLight ? "#FDECEA" : "#3A1F1C");
        resources["ErrStroke"] = Color.FromArgb(isLight ? "#E9A99D" : "#7A3A30");
        resources["ErrText"] = Color.FromArgb(isLight ? "#7A2416" : "#F4B4A8");
        resources["InfoBg"] = Color.FromArgb(isLight ? "#EEF4FF" : "#17233A");
        resources["InfoStroke"] = Color.FromArgb(isLight ? "#C9DAFB" : "#2C4470");
        resources["OkText"] = Color.FromArgb(isLight ? "#1E8A4C" : "#4ADE80");
    }

    /// <summary>True when the dark palette is active (set by <see cref="Apply"/>).</summary>
    public static bool IsDark { get; private set; }

    // Light status tints/texts used from code → their dark-mode versions.
    private static readonly Dictionary<string, string> DarkTints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FFFFFF"] = "#1B1E22",
        ["#ECEEF0"] = "#24282D",
        ["#DCDFE3"] = "#2C3036",
        ["#1B1D20"] = "#ECEEF0",
        ["#FCEDEA"] = "#3A1F1C",
        ["#FDECEA"] = "#3A1F1C",
        ["#FDF1D8"] = "#33280F",
        ["#FDF4E3"] = "#33280F",
        ["#E8F5EE"] = "#16301F",
        ["#B3361F"] = "#F28B7D",
        ["#1F8A4C"] = "#4ADE80",
        ["#B06A00"] = "#F0B840",
        ["#5D636B"] = "#9AA1A9",
        // Service / fuel / report / subscription status tints.
        ["#E6F4EC"] = "#16301F",
        ["#E2F3E9"] = "#16301F",
        ["#FBF0D9"] = "#33280F",
        ["#FDF3DF"] = "#33280F",
        ["#FCEBDC"] = "#3A2614",
        ["#FBE5E1"] = "#3A1F1C",
        ["#FBE6E3"] = "#3A1F1C",
        ["#EEF0F2"] = "#24282D",
        ["#ECEFF3"] = "#24282D",
        ["#E4ECFB"] = "#17233A",
        ["#1E8A4C"] = "#4ADE80",
        ["#D0701F"] = "#F59E5B",
        ["#B7791F"] = "#F0B840",
        ["#C98A1A"] = "#F0B840",
        ["#C2372B"] = "#F28B7D",
        ["#C0392B"] = "#F28B7D",
        ["#8A9099"] = "#9AA1A9",
        ["#1D5BD6"] = "#4F82E8"
    };

    /// <summary>A colour from code that also reads well in dark mode.</summary>
    public static string SoftHex(string lightHex) =>
        IsDark && DarkTints.TryGetValue(lightHex, out var dark) ? dark : lightHex;

    /// <summary>Same as <see cref="SoftHex"/>, as a Color.</summary>
    public static Color Soft(string lightHex) => Color.FromArgb(SoftHex(lightHex));
}
