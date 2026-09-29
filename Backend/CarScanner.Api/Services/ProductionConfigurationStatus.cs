namespace CarScanner.Api.Services;

public sealed record ProductionConfigurationStatus(
    bool AiConfigured,
    bool GooglePlayConfigured,
    bool AppleStoreConfigured,
    bool WebhookConfigured,
    bool PersistentStorageConfigured,
    bool AndroidPackageValid,
    bool AppleBundleValid,
    bool ReadyForStoreRelease,
    IReadOnlyList<string> Issues)
{
    private const string ExpectedAndroidPackageId =
        "com.Gcar360.app";

    private const string ExpectedAppleBundleId =
        "com.Gcar360.app";

    public static ProductionConfigurationStatus Read(
        IConfiguration configuration)
    {
        var geminiKey = Get(
            configuration,
            "Gemini:ApiKey",
            "GEMINI_API_KEY");

        var googleCredentials = Get(
            configuration,
            "Stores:Google:ServiceAccountJson",
            "GCS_GOOGLE_SERVICE_ACCOUNT_JSON");

        var androidPackage = Get(
            configuration,
            "Stores:Google:PackageName",
            "GCS_ANDROID_PACKAGE_NAME")
            ?? ExpectedAndroidPackageId;

        var appleIssuer = Get(
            configuration,
            "Stores:Apple:IssuerId",
            "GCS_APPLE_ISSUER_ID");

        var appleKeyId = Get(
            configuration,
            "Stores:Apple:KeyId",
            "GCS_APPLE_KEY_ID");

        var applePrivateKey = Get(
            configuration,
            "Stores:Apple:PrivateKeyP8",
            "GCS_APPLE_PRIVATE_KEY_P8");

        var appleBundle = Get(
            configuration,
            "Stores:Apple:BundleId",
            "GCS_APPLE_BUNDLE_ID")
            ?? ExpectedAppleBundleId;

        var webhookSecret = Get(
            configuration,
            "Stores:WebhookSecret",
            "GCS_STORE_WEBHOOK_SECRET");

        var configuredDataDirectory = Get(
            configuration,
            "Storage:DataDirectory",
            "GCS_DATA_DIRECTORY");

        var persistentStorageConfigured =
            !string.IsNullOrWhiteSpace(configuredDataDirectory) ||
            (
                !string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(
                        "WEBSITE_SITE_NAME")) &&
                !string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(
                        "HOME"))
            );

        var aiConfigured =
            IsRealSecret(geminiKey);

        var androidPackageValid =
            string.Equals(
                androidPackage.Trim(),
                ExpectedAndroidPackageId,
                StringComparison.Ordinal);

        var appleBundleValid =
            string.Equals(
                appleBundle.Trim(),
                ExpectedAppleBundleId,
                StringComparison.Ordinal);

        var googleConfigured =
            IsRealSecret(googleCredentials) &&
            androidPackageValid;

        var appleConfigured =
            IsRealSecret(appleIssuer) &&
            IsRealSecret(appleKeyId) &&
            IsRealSecret(applePrivateKey) &&
            appleBundleValid;

        var webhookConfigured =
            IsRealSecret(webhookSecret);

        var issues =
            new List<string>();

        if (!aiConfigured)
        {
            issues.Add(
                "AI provider key is not configured.");
        }

        if (!androidPackageValid)
        {
            issues.Add(
                $"Google Play package ID does not match the app package ID. Expected: {ExpectedAndroidPackageId}");
        }
        else if (!googleConfigured)
        {
            issues.Add(
                "Google Play service-account credentials are not configured.");
        }

        if (!appleBundleValid)
        {
            issues.Add(
                $"Apple bundle ID does not match the app bundle ID. Expected: {ExpectedAppleBundleId}");
        }
        else if (!appleConfigured)
        {
            issues.Add(
                "Apple App Store Server API credentials are not configured.");
        }

        if (!webhookConfigured)
        {
            issues.Add(
                "Store webhook secret is not configured.");
        }

        if (!persistentStorageConfigured)
        {
            issues.Add(
                "A persistent server data directory is not configured.");
        }

        var ready =
            aiConfigured &&
            googleConfigured &&
            appleConfigured &&
            webhookConfigured &&
            persistentStorageConfigured;

        return new ProductionConfigurationStatus(
            aiConfigured,
            googleConfigured,
            appleConfigured,
            webhookConfigured,
            persistentStorageConfigured,
            androidPackageValid,
            appleBundleValid,
            ready,
            issues);
    }

    private static string? Get(
        IConfiguration configuration,
        string configurationKey,
        string environmentKey)
    {
        return configuration[configurationKey]
            ?? Environment.GetEnvironmentVariable(
                environmentKey);
    }

    private static bool IsRealSecret(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed =
            value.Trim();

        return
            !trimmed.Equals(
                "SET_ON_SERVER_ONLY",
                StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith(
                "YOUR_",
                StringComparison.OrdinalIgnoreCase);
    }
}