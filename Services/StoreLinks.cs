namespace CarScanner.Services;

public static class StoreLinks
{
    private const string Backend =
        "https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net";

    public const string PrivacyPolicyUrl = Backend + "/privacy-policy";
    public const string TermsOfUseUrl = Backend + "/terms-of-use";
    public const string SupportUrl = Backend + "/support";

    public static string ManageSubscriptionUrl => DeviceInfo.Platform == DevicePlatform.iOS
        ? "https://apps.apple.com/account/subscriptions"
        : "https://play.google.com/store/account/subscriptions?package=com.georgiancarscanner.app&sku=com.georgiancarscanner.pro.monthly";

    public static Task OpenAsync(string url) => Launcher.Default.OpenAsync(url);
}
