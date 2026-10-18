using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CarScanner.Api.Services;

public static class ExternalProviders
{
    public const string Google = "google";
    public const string Apple = "apple";

    public static string? Normalize(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        Google => Google,
        Apple => Apple,
        _ => null
    };
}

/// <summary>What a verified Google/Apple ID token says about the person. Built only from a token whose signature checked out.</summary>
public sealed record ExternalIdentityClaims(
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified,
    string TokenFingerprint,
    DateTimeOffset ExpiresAt);

public enum ExternalVerifyStatus
{
    Ok,
    NotConfigured,
    Invalid
}

public sealed record ExternalVerifyResult(ExternalVerifyStatus Status, ExternalIdentityClaims? Claims = null);

/// <summary>
/// Server-side sign-in settings. Client ids are public identifiers (not secrets) of the Google OAuth clients
/// (Android, iOS) and of the Apple app (its bundle id). Empty list = that provider is switched off and every
/// attempt is refused. Nothing here is a secret; there is no client secret anywhere in this feature.
/// </summary>
public sealed class ExternalAuthOptions
{
    public IReadOnlyList<string> GoogleClientIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AppleClientIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ClientIdsFor(string provider) =>
        provider == ExternalProviders.Google ? GoogleClientIds
        : provider == ExternalProviders.Apple ? AppleClientIds
        : Array.Empty<string>();

    public bool IsConfigured(string provider) => ClientIdsFor(provider).Count > 0;

    public static ExternalAuthOptions From(IConfiguration config) => new()
    {
        GoogleClientIds = Split(config["Auth:Google:ClientIds"] ?? Environment.GetEnvironmentVariable("GCS_GOOGLE_CLIENT_IDS")),
        AppleClientIds = Split(config["Auth:Apple:ClientIds"] ?? Environment.GetEnvironmentVariable("GCS_APPLE_CLIENT_IDS"))
    };

    private static string[] Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>The provider's current public signing keys (JWKS).</summary>
public interface IExternalSigningKeys
{
    /// <param name="forceRefresh">True after a token named a key we do not have (the provider rotated keys).</param>
    Task<IReadOnlyList<SecurityKey>> GetKeysAsync(string provider, bool forceRefresh, CancellationToken ct);
}

/// <summary>Downloads and caches Google's / Apple's public keys. Refresh on demand is rate limited so bad tokens cannot make us hammer the provider.</summary>
public sealed class HttpExternalSigningKeys : IExternalSigningKeys
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinForcedRefresh = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _http;
    private readonly ILogger<HttpExternalSigningKeys> _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (IReadOnlyList<SecurityKey> Keys, DateTimeOffset FetchedAt)> _cache = new();

    public HttpExternalSigningKeys(IHttpClientFactory http, ILogger<HttpExternalSigningKeys> log, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private static string? UrlFor(string provider) => provider switch
    {
        ExternalProviders.Google => "https://www.googleapis.com/oauth2/v3/certs",
        ExternalProviders.Apple => "https://appleid.apple.com/auth/keys",
        _ => null
    };

    public async Task<IReadOnlyList<SecurityKey>> GetKeysAsync(string provider, bool forceRefresh, CancellationToken ct)
    {
        var url = UrlFor(provider);
        if (url is null)
            return Array.Empty<SecurityKey>();

        await _gate.WaitAsync(ct);
        try
        {
            var now = _clock();
            if (_cache.TryGetValue(provider, out var cached))
            {
                var age = now - cached.FetchedAt;
                if (age < CacheFor && (!forceRefresh || age < MinForcedRefresh))
                    return cached.Keys;
            }

            try
            {
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var json = await client.GetStringAsync(url, ct);
                var keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
                if (keys.Count > 0)
                {
                    _cache[provider] = (keys, now);
                    return keys;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Type only; keep serving the last good keys if we have them.
                _log.LogWarning("Could not refresh {Provider} signing keys ({ErrorType}).", provider, ex.GetType().Name);
            }

            return cached.Keys ?? Array.Empty<SecurityKey>();
        }
        finally { _gate.Release(); }
    }
}

/// <summary>
/// Verifies a Google / Apple ID token on the server. The token is the only thing believed: signature (RS256 against
/// the provider's published keys), issuer, audience (our own client ids), expiry, nonce. An e-mail address the app
/// sends beside the token is never used. Tokens, claims and keys are never logged.
/// </summary>
public sealed class ExternalIdentityVerifier
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxTokenAge = TimeSpan.FromHours(25);

    private readonly ExternalAuthOptions _options;
    private readonly IExternalSigningKeys _keys;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _used = new();

    public ExternalIdentityVerifier(ExternalAuthOptions options, IExternalSigningKeys keys, Func<DateTimeOffset>? clock = null)
    {
        _options = options;
        _keys = keys;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsConfigured(string provider) => _options.IsConfigured(provider);

    public async Task<ExternalVerifyResult> VerifyAsync(string provider, string? idToken, string? nonce, CancellationToken ct)
    {
        if (!_options.IsConfigured(provider))
            return new ExternalVerifyResult(ExternalVerifyStatus.NotConfigured);

        // Cheap shape checks before any crypto: a JWT is three dot-separated parts and never huge.
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 8192 || idToken.Count(c => c == '.') != 2)
            return Invalid();

        // Google: our nonce is mandatory (the app always sends one). Apple: the platform sign-in API cannot carry a
        // custom nonce, so it is checked only when the token has one.
        var nonceRequired = provider == ExternalProviders.Google;
        if (nonceRequired && (string.IsNullOrEmpty(nonce) || nonce.Length < 16 || nonce.Length > 256))
            return Invalid();

        var parameters = new TokenValidationParameters
        {
            ValidIssuers = provider == ExternalProviders.Google
                ? new[] { "https://accounts.google.com", "accounts.google.com" }
                : new[] { "https://appleid.apple.com" },
            ValidAudiences = _options.ClientIdsFor(provider),
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = false   // checked below against the injected clock
        };

        var handler = new JsonWebTokenHandler();
        TokenValidationResult? result = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            parameters.IssuerSigningKeys = await _keys.GetKeysAsync(provider, forceRefresh: attempt == 1, ct);
            if (parameters.IssuerSigningKeys is null || !parameters.IssuerSigningKeys.Any())
                return Invalid();

            result = await handler.ValidateTokenAsync(idToken, parameters);
            if (result.IsValid)
                break;
            // A key we do not know yet (rotation) is worth one forced refresh; anything else is final.
            if (result.Exception is not (SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException))
                return Invalid();
        }

        if (result is not { IsValid: true } || result.SecurityToken is not JsonWebToken jwt)
            return Invalid();

        var now = _clock();
        var expires = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        if (jwt.ValidTo == DateTime.MinValue || expires + ClockSkew <= now)
            return Invalid();   // expired
        if (jwt.ValidFrom != DateTime.MinValue && new DateTimeOffset(jwt.ValidFrom, TimeSpan.Zero) - ClockSkew > now)
            return Invalid();   // not valid yet
        if (jwt.IssuedAt != DateTime.MinValue && new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero) + MaxTokenAge < now)
            return Invalid();   // absurdly old even if exp is far out

        var subject = ClaimText(jwt, "sub");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
            return Invalid();

        var tokenNonce = ClaimText(jwt, "nonce");
        if (!NonceMatches(provider, nonce, tokenNonce, nonceRequired))
            return Invalid();

        var email = ClaimText(jwt, "email");
        var emailVerified = ClaimFlag(jwt, "email_verified");

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idToken)));
        if (_used.ContainsKey(fingerprint))
            return Invalid();   // the same token already completed a sign-in

        return new ExternalVerifyResult(
            ExternalVerifyStatus.Ok,
            new ExternalIdentityClaims(provider, subject, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), emailVerified, fingerprint, expires));
    }

    /// <summary>Marks a token as used once it has produced a session or a link ticket. A replay of it is refused until it would have expired anyway.</summary>
    public void MarkUsed(ExternalIdentityClaims claims)
    {
        var now = _clock();
        _used[claims.TokenFingerprint] = claims.ExpiresAt + ClockSkew;
        if (_used.Count > 2000)
        {
            foreach (var pair in _used)
            {
                if (pair.Value <= now)
                    _used.TryRemove(pair.Key, out _);
            }
        }
    }

    private static bool NonceMatches(string provider, string? requested, string? inToken, bool required)
    {
        if (string.IsNullOrEmpty(inToken))
            return !required;   // Apple may issue no nonce; Google must have one

        if (string.IsNullOrEmpty(requested))
            return false;       // the token is bound to a nonce the caller does not know

        if (FixedEquals(requested, inToken))
            return true;
        // Apple can carry the SHA-256 of the nonce instead of the nonce itself.
        var hashed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requested))).ToLowerInvariant();
        return provider == ExternalProviders.Apple && FixedEquals(hashed, inToken.ToLowerInvariant());
    }

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string? ClaimText(JsonWebToken jwt, string name) =>
        jwt.TryGetPayloadValue<string>(name, out var value) ? value : null;

    private static bool ClaimFlag(JsonWebToken jwt, string name)
    {
        if (jwt.TryGetPayloadValue<bool>(name, out var flag))
            return flag;
        // Apple sends "true"/"false" as text.
        return jwt.TryGetPayloadValue<string>(name, out var text) && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static ExternalVerifyResult Invalid() => new(ExternalVerifyStatus.Invalid);
}
