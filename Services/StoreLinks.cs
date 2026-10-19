namespace CarScanner.Services;

public static class StoreLinks
{
    /// <summary>The public website (privacy policy, terms, support). The API the app calls is configured separately in AIConfig.</summary>
    public const string Website = "https://obd360.ge";

    public const string PrivacyPolicyUrl = Website + "/privacy-policy";
    public const string TermsOfUseUrl = Website + "/terms-of-use";
    public const string SupportUrl = Website + "/support";
    public const string ProUrl = Website + "/pro";

    /// <summary>/pro with the signed-in e-mail pre-filled, so only the password is left to type.</summary>
    public static string ProUrlFor(string? email) =>
        string.IsNullOrWhiteSpace(email) ? ProUrl : ProUrl + "?email=" + Uri.EscapeDataString(email);

    public static string ManageSubscriptionUrl => DeviceInfo.Platform == DevicePlatform.iOS
        ? "https://apps.apple.com/account/subscriptions"
        : "https://play.google.com/store/account/subscriptions?package=com.obd360.app&sku=com.georgiancarscanner.pro.monthly";

    public static Task OpenAsync(string url) => Launcher.Default.OpenAsync(url);
}
