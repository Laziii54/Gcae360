using System.Net.Http.Json;

namespace CarScanner.Services.ExternalSignIn;

/// <summary>
/// Gets a Google / Apple ID token from the platform. It does the provider part only: our own server verifies the token,
/// finds or creates the account and issues the app session (<see cref="AccountService.SignInWithProviderAsync"/>).
/// Tokens exist only in memory for the length of this call and are never written to storage or to a log.
/// </summary>
public sealed class ExternalSignInService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Google is offered when this build has a client id for the platform. Debug builds show it anyway so the missing setup
    /// is visible. On iOS it is only offered next to Sign in with Apple (App Store guideline 4.8: an app that offers a
    /// third-party sign-in must offer Apple's too).
    /// </summary>
    public bool ShowGoogle
    {
        get
        {
#if DEBUG
            return OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
#else
            return !string.IsNullOrWhiteSpace(ExternalSignInConfig.GoogleClientIdForThisPlatform())
                   && (!OperatingSystem.IsIOS() || ShowApple);
#endif
        }
    }

    /// <summary>
    /// Sign in with Apple exists on iOS 13+ only; Android never shows it. A Release build offers it only when it was built
    /// with the entitlement (-p:EnableAppleSignIn=true), so a build without the App ID capability never shows a button
    /// that cannot work.
    /// </summary>
    public bool ShowApple
    {
        get
        {
            if (!OperatingSystem.IsIOSVersionAtLeast(13))
                return false;
#if DEBUG || APPLE_SIGNIN
            return true;
#else
            return false;
#endif
        }
    }

    public async Task<ProviderCredential?> GetGoogleCredentialAsync(CancellationToken token = default)
    {
        var clientId = ExternalSignInConfig.GoogleClientIdForThisPlatform();
        var redirectUri = ExternalSignInConfig.GoogleRedirectUriForThisPlatform();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
            throw new ExternalSignInException(ExternalSignInFailure.NotConfigured, "Google sign-in is not configured in this build.");

        var request = GoogleOAuth.CreateRequest(clientId, redirectUri);

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(request.AuthorizeUrl),
                CallbackUrl = new Uri(redirectUri),
                PrefersEphemeralWebBrowserSession = false
            });
        }
        catch (TaskCanceledException)
        {
            return null;   // the person closed the browser
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new ExternalSignInException(ExternalSignInFailure.Failed, "The Google sign-in page could not be opened.", ex);
        }

        string code;
        try
        {
            code = GoogleOAuth.ReadAuthorizationCode(result.Properties, request.State);
        }
        catch (ExternalSignInException ex) when (ex.Failure == ExternalSignInFailure.Cancelled)
        {
            return null;
        }

        try
        {
            using var content = new FormUrlEncodedContent(GoogleOAuth.TokenRequestForm(clientId, code, request.CodeVerifier, redirectUri));
            using var response = await Http.PostAsync(GoogleOAuth.TokenEndpoint, content, token);
            if (!response.IsSuccessStatusCode)
                throw new ExternalSignInException(ExternalSignInFailure.Failed, "Google did not accept the sign-in.");

            var idToken = GoogleOAuth.ReadIdToken(await response.Content.ReadAsStringAsync(token));
            return idToken is null
                ? throw new ExternalSignInException(ExternalSignInFailure.Failed, "Google sent no identity token.")
                : new ProviderCredential("google", idToken, request.Nonce);
        }
        catch (ExternalSignInException)
        {
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new ExternalSignInException(ExternalSignInFailure.Failed, "Google could not be reached.", ex);
        }
    }

    public async Task<ProviderCredential?> GetAppleCredentialAsync(CancellationToken token = default)
    {
#if IOS
        if (!OperatingSystem.IsIOSVersionAtLeast(13))
            throw new ExternalSignInException(ExternalSignInFailure.NotConfigured, "Sign in with Apple needs iOS 13 or newer.");

        try
        {
            // Only the e-mail scope: the person's name is never needed (and Apple sends it only on the very first sign-in).
            var result = await AppleSignInAuthenticator.AuthenticateAsync(new AppleSignInAuthenticator.Options
            {
                IncludeEmailScope = true,
                IncludeFullNameScope = false
            });

            var idToken = result?.IdToken;
            if (string.IsNullOrWhiteSpace(idToken) && result is not null && result.Properties.TryGetValue("id_token", out var fromProperties))
                idToken = fromProperties;

            // The platform API offers no custom nonce; the server accepts Apple tokens without one (and checks one if present).
            return string.IsNullOrWhiteSpace(idToken)
                ? throw new ExternalSignInException(ExternalSignInFailure.Failed, "Apple sent no identity token.")
                : new ProviderCredential("apple", idToken, null);
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (ExternalSignInException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExternalSignInException(ExternalSignInFailure.Failed, "Sign in with Apple did not complete.", ex);
        }
#else
        await Task.CompletedTask;
        throw new ExternalSignInException(ExternalSignInFailure.NotConfigured, "Sign in with Apple is only available on iPhone and iPad.");
#endif
    }
}
