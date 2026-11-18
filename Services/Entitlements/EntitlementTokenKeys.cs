namespace CarScanner.Services.Entitlements;

/// <summary>
/// The PUBLIC keys the app trusts for PRO entitlement tokens.
///
/// Only public keys belong here — never the private signing key, which lives
/// exclusively on the backend (Azure App Service setting
/// <c>Entitlements__Signing__PrivateKeyP8</c>, ideally sourced from Key Vault).
///
/// Each entry is <c>kid → base64(SPKI DER)</c> and matches the backend's
/// <c>Entitlements:Signing:KeyId</c>. Keeping more than one entry is what makes
/// key rotation possible: publish the new key pair, have the backend start
/// issuing under the new <c>kid</c>, and keep the old public key here until every
/// token signed with it has expired (at most <c>LifetimeDays</c>, default 7).
///
/// To rotate:
///   1. generate an ECDSA P-256 key pair;
///   2. put the private key in App Service as <c>Entitlements__Signing__PrivateKeyP8</c>
///      and set <c>Entitlements__Signing__KeyId</c> to the new id;
///   3. add the new public key below (keep the old one);
///   4. after one token lifetime, delete the old entry and rebuild.
/// </summary>
public static class EntitlementTokenKeys
{
    /// <summary>kid of the current signing key (production <c>k1</c>).</summary>
    public const string CurrentKeyId = "k1";

    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        // k1 — ECDSA P-256 public key (SPKI, base64).
        // Its private half must exist only as the backend's configured secret.
        [CurrentKeyId] =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEEGebbZrDOdWTyQyFVlmdhYhyGtBz" +
            "ECBy6oiIRL5MvLEXibkgG8pneVHxhBp7Hw7PSETPzDvHMfizY/T3ou4UKA=="
    };

    /// <summary>The trusted key set, keyed by <c>kid</c>.</summary>
    public static IReadOnlyDictionary<string, string> Trusted => Keys;
}
