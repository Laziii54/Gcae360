using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarScanner.Api.Services;

/// <summary>
/// Issues short-lived, cryptographically signed PRO entitlement tokens.
///
/// A token is a JWS in compact form (<c>header.payload.signature</c>) signed
/// with ES256 (ECDSA P-256 + SHA-256). The mobile app only ever holds the
/// matching public key and verifies signatures locally, so a tampered local
/// database or preference file can no longer fake PRO: without the private key
/// nobody can mint a token the app will accept.
///
/// The private key is never logged, never returned by an endpoint and never
/// committed. It is read from configuration / environment only:
///
///   Entitlements:Signing:PrivateKeyP8   (or GCS_ENTITLEMENT_PRIVATE_KEY_P8)
///   Entitlements:Signing:KeyId          (default "k1"; supports rotation)
///   Entitlements:Signing:LifetimeDays   (default 7)
///   Entitlements:Signing:Issuer         (default "obd360-api")
///   Entitlements:Signing:Audience       (default "obd360-app")
///
/// With no private key configured the service reports
/// <see cref="IsConfigured"/> = false and issues nothing: verification then
/// fails closed on the client (PRO stays locked rather than being granted).
/// </summary>
public sealed class EntitlementTokenService
{
    public const string DefaultKeyId = "k1";
    public const string DefaultIssuer = "obd360-api";
    public const string DefaultAudience = "obd360-app";
    public const int DefaultLifetimeDays = 7;

    /// <summary>Hard floor/ceiling for the configured lifetime, in days.</summary>
    private const int MinLifetimeDays = 1;
    private const int MaxLifetimeDays = 30;

    private readonly ILogger<EntitlementTokenService> _log;
    private readonly string? _privateKeyPem;

    public EntitlementTokenService(IConfiguration configuration, ILogger<EntitlementTokenService> log)
    {
        _log = log;

        _privateKeyPem = Normalize(
            configuration["Entitlements:Signing:PrivateKeyP8"]
            ?? Environment.GetEnvironmentVariable("GCS_ENTITLEMENT_PRIVATE_KEY_P8"));

        KeyId = Clean(configuration["Entitlements:Signing:KeyId"]
                      ?? Environment.GetEnvironmentVariable("GCS_ENTITLEMENT_KEY_ID"))
                ?? DefaultKeyId;

        Issuer = Clean(configuration["Entitlements:Signing:Issuer"]) ?? DefaultIssuer;
        Audience = Clean(configuration["Entitlements:Signing:Audience"]) ?? DefaultAudience;

        var days = int.TryParse(
            configuration["Entitlements:Signing:LifetimeDays"]
            ?? Environment.GetEnvironmentVariable("GCS_ENTITLEMENT_LIFETIME_DAYS"),
            out var parsed)
            ? parsed
            : DefaultLifetimeDays;

        LifetimeDays = Math.Clamp(days, MinLifetimeDays, MaxLifetimeDays);

        if (_privateKeyPem is null)
        {
            _log.LogWarning(
                "Entitlement signing key is not configured (Entitlements:Signing:PrivateKeyP8 / " +
                "GCS_ENTITLEMENT_PRIVATE_KEY_P8). Entitlement tokens cannot be issued and the app " +
                "will keep PRO locked.");
            return;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(_privateKeyPem);
            PublicKeyBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
            IsConfigured = true;
        }
        catch (Exception ex)
        {
            // A malformed key must not crash startup; it disables issuance and is
            // reported once, loudly, without the key material itself.
            _log.LogError("Entitlement signing key could not be loaded: {Error}", ex.GetType().Name);
        }
    }

    /// <summary>True when a usable private key is loaded.</summary>
    public bool IsConfigured { get; }

    public string KeyId { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public int LifetimeDays { get; }

    /// <summary>SPKI DER, base64 — public data, safe to expose and to embed in the app.</summary>
    public string? PublicKeyBase64 { get; }

    /// <summary>
    /// Issues one entitlement token for an owner (an OBD360 account id, or an
    /// accountless <c>anon:</c> owner). Returns null when signing is not
    /// configured — callers must then grant nothing.
    /// </summary>
    public string? Issue(
        string? subject,
        bool hasObd,
        bool hasVin,
        string? deviceId = null,
        DateTimeOffset? now = null)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(subject))
            return null;

        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddDays(LifetimeDays);

        var tier = hasObd && hasVin ? "full" : hasObd ? "obd" : hasVin ? "vin" : "none";

        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "JWT",
            ["kid"] = KeyId
        };

        var payload = new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = subject.Trim(),
            ["hasObd"] = hasObd,
            ["hasVin"] = hasVin,
            ["tier"] = tier,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            // A minute of back-dated "not before" absorbs small clock drift
            // between the App Service and the phone.
            ["nbf"] = issuedAt.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        if (!string.IsNullOrWhiteSpace(deviceId) && deviceId.Length <= 64)
            payload["dev"] = deviceId.Trim();

        var signingInput = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header))
                           + "."
                           + Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(_privateKeyPem!);

            var signature = ecdsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            return signingInput + "." + Base64Url(signature);
        }
        catch (Exception ex)
        {
            _log.LogError("Entitlement token signing failed: {Error}", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Config values sometimes arrive with escaped newlines (Key Vault / JSON).</summary>
    private static string? Normalize(string? pem) =>
        string.IsNullOrWhiteSpace(pem)
            ? null
            : pem.Replace("\\n", "\n").Trim();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
