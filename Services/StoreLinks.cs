namespace CarScanner.Services;

public static class StoreLinks
{
    /// <summary>The public website (privacy policy, terms, support). The API the app calls is configured separately in AIConfig.</summary>
    public const string Website = "https://obd360.ge";

    public const string PrivacyPolicyUrl = Website + "/privacy-policy";
    public const string TermsOfUseUrl = Website + "/terms-of-use";
    public const string SupportUrl = Website + "/support";
    public const string ProUrl = Website + "/pro";

    /// <summary>
    /// /pro with the signed-in e-mail pre-filled, so only the code is left to
    /// request. <paramref name="fromAndroidApp"/> adds the "source=android"
    /// marker: the website then offers the monthly plans only, because it is
    /// the Android app's own checkout and the mobile flow never sells a year.
    /// Nothing secret is ever put in this URL.
    /// </summary>
    public static string ProUrlFor(string? email, bool fromAndroidApp = false)
    {
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(email))
            query.Add("email=" + Uri.EscapeDataString(email));

        if (fromAndroidApp)
            query.Add("source=android");

        return query.Count == 0
            ? ProUrl
            : ProUrl + "?" + string.Join("&", query);
    }

    public static string ManageSubscriptionUrl => DeviceInfo.Platform == DevicePlatform.iOS
        ? "https://apps.apple.com/account/subscriptions"
        : "https://play.google.com/store/account/subscriptions?package=com.obd360.app&sku=com.georgiancarscanner.pro.monthly";

    public static Task OpenAsync(string url) => Launcher.Default.OpenAsync(url);
}
