using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarScanner.Services.ExternalSignIn;

/// <summary>
/// Public identifiers for Google / Apple sign-in. Nothing in this file is a secret: an OAuth client id of a mobile app
/// is public by design, and a mobile app never holds a client secret (none exists for Android / iOS clients, the
/// authorization code is protected by PKCE instead). Empty = that sign-in is not set up in this build; the buttons are
/// then hidden in Release and explain themselves in Debug. See SIGN_IN_SETUP.md for where each value comes from.
/// </summary>
public static class ExternalSignInConfig
{
    /// <summary>Google Cloud Console → Credentials → OAuth client of type "Android" (package com.obd360.app + SHA-1). Ends with ".apps.googleusercontent.com".</summary>
    public const string GoogleAndroidClientId = "892443922366-rnu2o0efhf2m4oj6cscm4km9ql59o5in.apps.googleusercontent.com";

    /// <summary>Google Cloud Console → Credentials → OAuth client of type "iOS" (bundle id com.gcar360.app). Ends with ".apps.googleusercontent.com".</summary>
    public const string GoogleIosClientId = "892443922366-h9536o1n7read3knpng8hjbgpl91199u.apps.googleusercontent.com";

    /// <summary>The Android application id; Google requires it as the redirect scheme of an Android client.</summary>
    public const string AndroidRedirectScheme = "com.obd360.app";

    public static string GoogleClientIdForThisPlatform()
    {
#if ANDROID
        return GoogleAndroidClientId;
#elif IOS
        return GoogleIosClientId;
#else
        return string.Empty;
#endif
    }

    public static string GoogleRedirectUriForThisPlatform()
    {
#if IOS
        return GoogleOAuth.IosRedirectUri(GoogleIosClientId) ?? string.Empty;
#else
        return GoogleOAuth.AndroidRedirectUri(AndroidRedirectScheme);
#endif
    }
}

public enum ExternalSignInFailure
{
    /// <summary>The person closed the browser / sheet or declined. Never shown as an error.</summary>
    Cancelled,
    /// <summary>No client id in this build, or the platform cannot do it.</summary>
    NotConfigured,
    /// <summary>Anything else: network, a bad answer from the provider, a changed callback.</summary>
    Failed
}

public sealed class ExternalSignInException : Exception
{
    public ExternalSignInException(ExternalSignInFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;

    public ExternalSignInFailure Failure { get; }
}

/// <summary>What the app sends to our own server: the provider's ID token and the nonce it was requested with.</summary>
public sealed record ProviderCredential(string Provider, string IdToken, string? Nonce);

public sealed record GoogleAuthRequest(string AuthorizeUrl, string CodeVerifier, string State, string Nonce, string RedirectUri);

/// <summary>
/// Google's OAuth 2.0 authorization-code flow with PKCE for an installed app (RFC 7636 / RFC 8252). Pure functions, no
/// platform code, so the security-relevant parts (PKCE, state, what is sent where) are unit-tested.
/// Only the ID token is used. The access / refresh tokens in the token answer are never read, stored or logged.
/// </summary>
public static class GoogleOAuth
{
    public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ClientIdSuffix = ".apps.googleusercontent.com";

    public static string AndroidRedirectUri(string applicationId) => applicationId + ":/oauth2redirect";

    /// <summary>"123-abc.apps.googleusercontent.com" → "com.googleusercontent.apps.123-abc" (the URL scheme Google expects for an iOS client). Null when the id is empty or malformed.</summary>
    public static string? IosRedirectScheme(string? iosClientId)
    {
        if (string.IsNullOrWhiteSpace(iosClientId) || !iosClientId.EndsWith(ClientIdSuffix, StringComparison.Ordinal))
            return null;
        var prefix = iosClientId[..^ClientIdSuffix.Length];
        return prefix.Length == 0 ? null : "com.googleusercontent.apps." + prefix;
    }

    public static string? IosRedirectUri(string? iosClientId) =>
        IosRedirectScheme(iosClientId) is { } scheme ? scheme + ":/oauth2redirect" : null;

    public static GoogleAuthRequest CreateRequest(string clientId, string redirectUri, Func<int, byte[]>? random = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
            throw new ExternalSignInException(ExternalSignInFailure.NotConfigured, "Google sign-in is not configured.");

        random ??= RandomNumberGenerator.GetBytes;
        var verifier = Base64Url(random(32));      // 43 characters, inside RFC 7636's 43..128
        var state = Base64Url(random(24));
        var nonce = Base64Url(random(24));

        var query = new (string Key, string Value)[]
        {
            ("client_id", clientId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", "openid email"),
            ("code_challenge", CodeChallenge(verifier)),
            ("code_challenge_method", "S256"),
            ("state", state),
            ("nonce", nonce),
            ("prompt", "select_account")
        };

        var url = AuthorizeEndpoint + "?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return new GoogleAuthRequest(url, verifier, state, nonce, redirectUri);
    }

    /// <summary>RFC 7636 S256: BASE64URL(SHA256(ASCII(verifier))).</summary>
    public static string CodeChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>
    /// The authorization code from the redirect, after checking that it answers OUR request (state). Throws
    /// <see cref="ExternalSignInException"/>: Cancelled when the person declined, Failed for anything unexpected.
    /// </summary>
    public static string ReadAuthorizationCode(IEnumerable<KeyValuePair<string, string>> callback, string expectedState)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in callback)
            values[pair.Key] = pair.Value;

        if (values.TryGetValue("error", out var error))
        {
            throw error is "access_denied" or "user_cancelled" or "consent_required"
                ? new ExternalSignInException(ExternalSignInFailure.Cancelled, "Google sign-in was cancelled.")
                : new ExternalSignInException(ExternalSignInFailure.Failed, "Google refused the sign-in.");
        }

        if (!values.TryGetValue("state", out var state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
        {
            throw new ExternalSignInException(ExternalSignInFailure.Failed, "The Google answer did not match the request.");
        }

        if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code) || code.Length > 2048)
            throw new ExternalSignInException(ExternalSignInFailure.Failed, "Google sent no authorization code.");

        return code;
    }

    /// <summary>The form posted to Google's token endpoint. No client secret: PKCE's code_verifier is the proof.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> TokenRequestForm(string clientId, string code, string codeVerifier, string redirectUri) =>
        new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("grant_type", "authorization_code")
        };

    /// <summary>Reads only "id_token" from the token endpoint's answer; everything else in it is ignored. Null when absent.</summary>
    public static string? ReadIdToken(string? tokenResponseJson)
    {
        if (string.IsNullOrWhiteSpace(tokenResponseJson) || tokenResponseJson.Length > 65_536)
            return null;

        try
        {
            using var document = JsonDocument.Parse(tokenResponseJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("id_token", out var idToken) &&
                idToken.ValueKind == JsonValueKind.String)
            {
                var value = idToken.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
