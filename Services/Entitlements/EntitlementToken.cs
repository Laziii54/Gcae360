using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarScanner.Services.Entitlements;

/// <summary>Claims of a PRO entitlement token that already passed signature and claim validation.</summary>
public sealed record EntitlementTokenClaims(
    string Subject,
    bool HasObd,
    bool HasVin,
    string Tier,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string TokenId,
    string KeyId,
    string? DeviceId)
{
    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt <= now;
}

/// <summary>
/// Verifies the backend's signed PRO entitlement tokens (JWS compact, ES256).
///
/// This is the only thing that may grant PRO locally. It deliberately contains
/// no MAUI dependency so the exact same code runs in the app and in the tests.
///
/// Rejected, always: a token whose signature does not verify against the
/// embedded public key, an unknown key id, any algorithm other than ES256 (so
/// "alg: none" and HMAC-confusion attacks fail), a wrong issuer or audience,
/// an expired or not-yet-valid token, a structurally broken token.
/// </summary>
public static class EntitlementTokenVerifier
{
    public const string ExpectedIssuer = "obd360-api";
    public const string ExpectedAudience = "obd360-app";
    public const string RequiredAlgorithm = "ES256";

    /// <summary>Clock drift allowance for "not before" only.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private const int MaxTokenLength = 8192;

    /// <summary>
    /// Returns the validated claims, or null when the token must not grant
    /// anything. Never throws.
    /// </summary>
    public static EntitlementTokenClaims? Verify(
        string? token,
        IReadOnlyDictionary<string, string>? trustedKeys = null,
        DateTimeOffset? now = null,
        string expectedIssuer = ExpectedIssuer,
        string expectedAudience = ExpectedAudience)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var jwt = token.Trim();
        if (jwt.Length > MaxTokenLength)
            return null;

        var parts = jwt.Split('.');
        if (parts.Length != 3 ||
            parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return null;
        }

        JsonDocument header;
        JsonDocument payload;
        byte[] signature;
        try
        {
            header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            signature = Base64UrlDecode(parts[2]);
        }
        catch
        {
            return null;
        }

        using (header)
        using (payload)
        {
            var h = header.RootElement;
            var p = payload.RootElement;
            if (h.ValueKind != JsonValueKind.Object || p.ValueKind != JsonValueKind.Object)
                return null;

            // ---- header: algorithm and key id ----
            if (!h.TryGetProperty("alg", out var alg) ||
                alg.ValueKind != JsonValueKind.String ||
                !string.Equals(alg.GetString(), RequiredAlgorithm, StringComparison.Ordinal))
            {
                return null;
            }

            if (!h.TryGetProperty("kid", out var kidElement) ||
                kidElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var keyId = kidElement.GetString();
            if (string.IsNullOrWhiteSpace(keyId))
                return null;

            var keys = trustedKeys ?? EntitlementTokenKeys.Trusted;
            if (!keys.TryGetValue(keyId!, out var publicKeyBase64) ||
                string.IsNullOrWhiteSpace(publicKeyBase64))
            {
                return null;
            }

            // ---- signature ----
            if (!HasValidSignature(parts[0] + "." + parts[1], signature, publicKeyBase64!))
                return null;

            // ---- claims ----
            var nowValue = now ?? DateTimeOffset.UtcNow;

            var issuer = ReadString(p, "iss");
            if (!string.Equals(issuer, expectedIssuer, StringComparison.Ordinal))
                return null;

            var audience = ReadString(p, "aud");
            if (!string.Equals(audience, expectedAudience, StringComparison.Ordinal))
                return null;

            var subject = ReadString(p, "sub");
            if (string.IsNullOrWhiteSpace(subject))
                return null;

            if (!TryReadUnixSeconds(p, "exp", out var expiresAt) || expiresAt <= nowValue)
                return null;

            if (TryReadUnixSeconds(p, "nbf", out var notBefore) && notBefore > nowValue + ClockSkew)
                return null;

            TryReadUnixSeconds(p, "iat", out var issuedAt);

            var hasObd = ReadBool(p, "hasObd");
            var hasVin = ReadBool(p, "hasVin");
            var tier = ReadString(p, "tier") ?? (hasObd && hasVin ? "full" : hasObd ? "obd" : hasVin ? "vin" : "none");
            var tokenId = ReadString(p, "jti") ?? string.Empty;

            return new EntitlementTokenClaims(
                subject!,
                hasObd,
                hasVin,
                tier!,
                issuedAt,
                expiresAt,
                tokenId,
                keyId!,
                ReadString(p, "dev"));
        }
    }

    private static bool HasValidSignature(string signingInput, byte[] signature, string publicKeyBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);

            return ecdsa.VerifyData(
                Encoding.ASCII.GetBytes(signingInput),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch
        {
            // Bad key material, bad signature length, anything else: reject.
            return false;
        }
    }

    private static string? ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool TryReadUnixSeconds(JsonElement payload, string name, out DateTimeOffset value)
    {
        value = default;
        if (!payload.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out var seconds))
        {
            return false;
        }

        // Guard against absurd values that DateTimeOffset would throw on.
        if (seconds < 1_000_000_000 || seconds > 4_102_444_800)
            return false;

        value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight(
            normalized.Length + (4 - normalized.Length % 4) % 4,
            '='));
    }
}
