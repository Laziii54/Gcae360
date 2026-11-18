using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarScanner.Api.Services;
using CarScanner.Services.Entitlements;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// The signed PRO entitlement token, tested end to end with real cryptography:
/// every token here is actually signed by <see cref="EntitlementTokenService"/>
/// and verified by the exact validator the mobile app uses
/// (<see cref="EntitlementTokenVerifier"/>). Nothing is mocked.
///
/// This is the security boundary of the whole PRO system: if any of these
/// assertions regress, editing the local SQLite database or the preferences file
/// would once again be enough to unlock paid features.
/// </summary>
public sealed class EntitlementTokenTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _kid = "test-k1";
    private readonly string _publicKeyBase64;

    public EntitlementTokenTests()
    {
        _publicKeyBase64 = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    }

    public void Dispose() => _key.Dispose();

    // ---- helpers -----------------------------------------------------------

    private EntitlementTokenService Service(
        string? keyId = null,
        string? issuer = null,
        string? audience = null,
        int lifetimeDays = EntitlementTokenService.DefaultLifetimeDays,
        bool withKey = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Entitlements:Signing:KeyId"] = keyId ?? _kid,
            ["Entitlements:Signing:LifetimeDays"] = lifetimeDays.ToString(),
        };

        if (withKey)
            settings["Entitlements:Signing:PrivateKeyP8"] = _key.ExportPkcs8PrivateKeyPem();
        if (issuer is not null)
            settings["Entitlements:Signing:Issuer"] = issuer;
        if (audience is not null)
            settings["Entitlements:Signing:Audience"] = audience;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new EntitlementTokenService(configuration, NullLogger<EntitlementTokenService>.Instance);
    }

    private IReadOnlyDictionary<string, string> Keys(string? kid = null) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [kid ?? _kid] = _publicKeyBase64
        };

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---- happy path --------------------------------------------------------

    [Fact]
    public void AValidToken_IsAccepted_WithItsClaims()
    {
        var token = Service().Issue("acc-1", hasObd: true, hasVin: false);
        Assert.NotNull(token);

        var claims = EntitlementTokenVerifier.Verify(token, Keys());

        Assert.NotNull(claims);
        Assert.Equal("acc-1", claims!.Subject);
        Assert.True(claims.HasObd);
        Assert.False(claims.HasVin);
        Assert.Equal("obd", claims.Tier);
        Assert.Equal(_kid, claims.KeyId);
        Assert.NotEmpty(claims.TokenId);
    }

    [Fact]
    public void FullPro_GrantsBothFlags()
    {
        var claims = EntitlementTokenVerifier.Verify(
            Service().Issue("acc-full", hasObd: true, hasVin: true), Keys());

        Assert.NotNull(claims);
        Assert.True(claims!.HasObd);
        Assert.True(claims.HasVin);
        Assert.Equal("full", claims.Tier);
    }

    [Fact]
    public void ANoEntitlementToken_IsStillValid_ButGrantsNothing()
    {
        // A lapsed or revoked subscription must produce a *valid* token that
        // says "false" — that is how revocation reaches the device.
        var claims = EntitlementTokenVerifier.Verify(
            Service().Issue("acc-2", hasObd: false, hasVin: false), Keys());

        Assert.NotNull(claims);
        Assert.False(claims!.HasObd);
        Assert.False(claims.HasVin);
        Assert.Equal("none", claims.Tier);
    }

    [Fact]
    public void AnAnonymousOwner_IsSupported()
    {
        var claims = EntitlementTokenVerifier.Verify(
            Service().Issue("anon:abc123", hasObd: true, hasVin: true, deviceId: "install-1"), Keys());

        Assert.NotNull(claims);
        Assert.Equal("anon:abc123", claims!.Subject);
        Assert.Equal("install-1", claims.DeviceId);
    }

    [Fact]
    public void TokenLifetime_IsConfigurable_AndClamped()
    {
        var service = Service(lifetimeDays: 7);
        Assert.Equal(7, service.LifetimeDays);

        var issued = DateTimeOffset.UtcNow;
        var claims = EntitlementTokenVerifier.Verify(
            service.Issue("acc-3", true, false, now: issued), Keys());

        Assert.NotNull(claims);
        Assert.Equal(issued.AddDays(7).ToUnixTimeSeconds(), claims!.ExpiresAt.ToUnixTimeSeconds());

        Assert.Equal(30, Service(lifetimeDays: 500).LifetimeDays);   // clamped
        Assert.Equal(1, Service(lifetimeDays: 0).LifetimeDays);      // clamped
    }

    // ---- rejection paths ---------------------------------------------------

    [Fact]
    public void ATamperedPayload_IsRejected()
    {
        // Issued without VIN PRO, then forged to claim it — the classic attack
        // against an unsigned or unsigned-verified entitlement.
        var token = Service().Issue("acc-1", hasObd: true, hasVin: false)!;
        var parts = token.Split('.');

        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(
            parts[1].Replace('-', '+').Replace('_', '/').PadRight(
                parts[1].Length + (4 - parts[1].Length % 4) % 4, '=')));
        var forged = payload.Replace("\"hasVin\":false", "\"hasVin\":true");
        Assert.NotEqual(payload, forged);

        var tampered = parts[0] + "." + B64Url(Encoding.UTF8.GetBytes(forged)) + "." + parts[2];

        Assert.Null(EntitlementTokenVerifier.Verify(tampered, Keys()));
    }

    [Fact]
    public void ATokenSignedByAnotherKey_IsRejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entitlements:Signing:PrivateKeyP8"] = other.ExportPkcs8PrivateKeyPem(),
                ["Entitlements:Signing:KeyId"] = _kid
            })
            .Build();

        var foreign = new EntitlementTokenService(configuration, NullLogger<EntitlementTokenService>.Instance)
            .Issue("acc-1", true, true);

        Assert.Null(EntitlementTokenVerifier.Verify(foreign, Keys()));
    }

    [Fact]
    public void AnExpiredToken_IsRejected()
    {
        var service = Service(lifetimeDays: 7);
        var old = service.Issue("acc-1", true, true, now: DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Null(EntitlementTokenVerifier.Verify(old, Keys()));
    }

    [Fact]
    public void ATokenThatIsNotYetValid_IsRejected()
    {
        var service = Service();

        // Signed now for a time well in the future: "nbf" is one minute before
        // "now", so a token issued 2 hours ahead must be refused.
        var future = service.Issue("acc-1", true, true, now: DateTimeOffset.UtcNow.AddHours(2));

        Assert.Null(EntitlementTokenVerifier.Verify(future, Keys(), now: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AWrongIssuer_IsRejected()
    {
        var token = Service(issuer: "somebody-else").Issue("acc-1", true, true);

        Assert.Null(EntitlementTokenVerifier.Verify(token, Keys()));
    }

    [Fact]
    public void AWrongAudience_IsRejected()
    {
        var token = Service(audience: "another-app").Issue("acc-1", true, true);

        Assert.Null(EntitlementTokenVerifier.Verify(token, Keys()));
    }

    [Fact]
    public void AnUnknownKeyId_IsRejected()
    {
        var token = Service(keyId: "rotated-out").Issue("acc-1", true, true);

        // Only the current key is trusted, so a token under any other kid fails —
        // but it still verifies while the old key is deliberately kept in the set.
        Assert.Null(EntitlementTokenVerifier.Verify(token, Keys()));
        Assert.NotNull(EntitlementTokenVerifier.Verify(token, Keys("rotated-out")));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS256")]
    [InlineData("ES384")]
    public void AnUnsupportedAlgorithm_IsRejected(string algorithm)
    {
        var token = Service().Issue("acc-1", true, true)!;
        var parts = token.Split('.');

        var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = algorithm,
            typ = "JWT",
            kid = _kid
        }));

        Assert.Null(EntitlementTokenVerifier.Verify(header + "." + parts[1] + "." + parts[2], Keys()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("..")]
    public void AMalformedToken_IsRejected(string token)
    {
        Assert.Null(EntitlementTokenVerifier.Verify(token, Keys()));
    }

    [Fact]
    public void ANullOrBlankToken_IsRejected()
    {
        Assert.Null(EntitlementTokenVerifier.Verify(null, Keys()));
        Assert.Null(EntitlementTokenVerifier.Verify("   ", Keys()));
    }

    [Fact]
    public void AMissingEntitlementClaim_DefaultsToFalse()
    {
        // A signed token that simply omits hasObd/hasVin must never be read as PRO.
        var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "JWT", kid = _kid }));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = EntitlementTokenVerifier.ExpectedIssuer,
            ["aud"] = EntitlementTokenVerifier.ExpectedAudience,
            ["sub"] = "acc-1",
            ["exp"] = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["jti"] = "x"
        }));

        var signingInput = header + "." + payload;
        var signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var claims = EntitlementTokenVerifier.Verify(signingInput + "." + B64Url(signature), Keys());

        Assert.NotNull(claims);
        Assert.False(claims!.HasObd);
        Assert.False(claims.HasVin);
        Assert.Equal("none", claims.Tier);
    }

    [Fact]
    public void AKeyRotatedIn_VerifiesWhileTheOldOneIsStillPublished()
    {
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entitlements:Signing:PrivateKeyP8"] = next.ExportPkcs8PrivateKeyPem(),
                ["Entitlements:Signing:KeyId"] = "k2"
            })
            .Build();

        var service = new EntitlementTokenService(configuration, NullLogger<EntitlementTokenService>.Instance);

        var oldToken = Service().Issue("acc-1", true, true);   // kid = test-k1
        var newToken = service.Issue("acc-1", true, true);     // kid = k2

        // Both keys published during the rotation window.
        var keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [_kid] = _publicKeyBase64,
            ["k2"] = Convert.ToBase64String(next.ExportSubjectPublicKeyInfo())
        };

        Assert.NotNull(EntitlementTokenVerifier.Verify(oldToken, keys));
        Assert.NotNull(EntitlementTokenVerifier.Verify(newToken, keys));
    }

    [Fact]
    public void TheAppEmbedsAPublicKeyAndNoPrivateKey()
    {
        // The shipped trust set must contain public keys only.
        Assert.NotEmpty(EntitlementTokenKeys.Trusted);
        Assert.Contains(EntitlementTokenKeys.CurrentKeyId, EntitlementTokenKeys.Trusted.Keys);

        foreach (var key in EntitlementTokenKeys.Trusted.Values)
        {
            var bytes = Convert.FromBase64String(key);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(bytes, out _);   // throws if it is not an SPKI public key
            Assert.NotEqual(0, ecdsa.KeySize);
        }
    }

    // ---- backend service ---------------------------------------------------

    [Fact]
    public void WithoutASigningKey_NothingIsIssued()
    {
        var service = Service(withKey: false);

        Assert.False(service.IsConfigured);
        Assert.Null(service.Issue("acc-1", true, true));
        Assert.Null(service.PublicKeyBase64);
    }

    [Fact]
    public void AMalformedKey_DisablesIssuanceInsteadOfCrashing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entitlements:Signing:PrivateKeyP8"] = "not-a-pem"
            })
            .Build();

        var service = new EntitlementTokenService(configuration, NullLogger<EntitlementTokenService>.Instance);

        Assert.False(service.IsConfigured);
        Assert.Null(service.Issue("acc-1", true, true));
    }

    [Fact]
    public void TheServiceExposesThePublicKeyButTheTokenNeverCarriesThePrivateOne()
    {
        var service = Service();
        var token = service.Issue("acc-1", true, true)!;

        Assert.NotNull(service.PublicKeyBase64);
        Assert.Equal(_publicKeyBase64, service.PublicKeyBase64);

        // No part of the JWS may contain anything from the PKCS#8 private key.
        var privateKeyBody = _key.ExportPkcs8PrivateKeyPem()
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();

        Assert.DoesNotContain(privateKeyBody[..40], token);
    }

    [Fact]
    public void EachIssuedToken_HasItsOwnJti()
    {
        var service = Service();
        var first = EntitlementTokenVerifier.Verify(service.Issue("acc-1", true, true), Keys());
        var second = EntitlementTokenVerifier.Verify(service.Issue("acc-1", true, true), Keys());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.TokenId, second!.TokenId);
    }
}
