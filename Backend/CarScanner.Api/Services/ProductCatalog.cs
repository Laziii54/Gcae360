namespace CarScanner.Api.Services;

/// <summary>
/// Every product sold in the app. Subscriptions unlock features; the
/// one-time VIN products add report credits that never expire.
/// </summary>
public static class ProductCatalog
{
    // Subscriptions (monthly only).
    public const string ObdMonthly = "com.georgiancarscanner.pro.monthly";
    public const string VinMonthly = "com.georgiancarscanner.vin.monthly";
    public const string FullMonthly = "com.georgiancarscanner.full.monthly";

    // One-time VIN credits.
    public const string VinPack5 = "com.georgiancarscanner.vin.pack5";
    public const string VinPack10 = "com.georgiancarscanner.vin.pack10";
    public const string VinSingle = "com.georgiancarscanner.vin.single";

    /// <summary>VIN reports included in one month of VIN PRO / Full PRO.</summary>
    public const int VinMonthlyQuota = 10;

    public static bool IsSubscription(string? productId) =>
        productId is ObdMonthly or VinMonthly or FullMonthly;

    public static bool IsConsumable(string? productId) =>
        productId is VinPack5 or VinPack10 or VinSingle;

    public static bool IsKnown(string? productId) =>
        IsSubscription(productId) || IsConsumable(productId);

    /// <summary>Credits added by a one-time product.</summary>
    public static int CreditsFor(string? productId) => productId switch
    {
        VinPack5 => 5,
        VinPack10 => 10,
        VinSingle => 1,
        _ => 0
    };

    /// <summary>Packs are sold only to VIN / Full subscribers.</summary>
    public static bool RequiresVinSubscription(string? productId) =>
        productId is VinPack5 or VinPack10;

    public static bool GrantsObd(string? productId, string? source) =>
        IsPromo(source) || productId is ObdMonthly or FullMonthly;

    public static bool GrantsVin(string? productId, string? source) =>
        IsPromo(source) || productId is VinMonthly or FullMonthly;

    private static bool IsPromo(string? source) =>
        string.Equals(source, "promo", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the account can use right now.</summary>
public sealed record AccessInfo(
    bool HasObd,
    bool HasVin,
    string Tier,
    DateTimeOffset? ObdExpiresAt,
    DateTimeOffset? VinExpiresAt)
{
    public static AccessInfo None { get; } = new(false, false, "none", null, null);
}
