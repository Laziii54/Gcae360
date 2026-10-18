using System.Text.RegularExpressions;
using CarScanner.Services.ExternalSignIn;

namespace CarScanner.Tests;

/// <summary>
/// The app side of Google / Apple sign-in. The first half exercises the pure OAuth helpers (PKCE, state, what is sent where).
/// The second half reads the source and platform files to pin the security decisions (no secrets, no token storage or logging,
/// Apple on iOS only, Apple entitlement off by default). None of this opens a browser, a Google account or an iPhone:
/// it proves the code and files say what they should, not that sign-in works on a device.
/// </summary>
public sealed class ExternalSignInClientTests
{
    private const string AndroidClient = "1234567890-abcdefg.apps.googleusercontent.com";
    private const string Redirect = "com.obd360.app:/oauth2redirect";

    // ---- PKCE / request ----

    [Fact]
    public void CodeChallenge_MatchesTheRfc7636ExampleVector()
    {
        // RFC 7636, appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", GoogleOAuth.CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void TheAuthorizeUrl_AsksForACodeWithS256Pkce_State_AndNonce_AndNeverCarriesASecret()
    {
        var request = GoogleOAuth.CreateRequest(AndroidClient, Redirect);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(request.AuthorizeUrl).Query);

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", request.AuthorizeUrl);
        Assert.Equal(AndroidClient, query["client_id"]);
        Assert.Equal(Redirect, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email", query["scope"]);          // nothing beyond what the account needs
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(GoogleOAuth.CodeChallenge(request.CodeVerifier), query["code_challenge"]);
        Assert.Equal(request.State, query["state"]);
        Assert.Equal(request.Nonce, query["nonce"]);
        Assert.DoesNotContain("secret", request.AuthorizeUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(request.CodeVerifier, request.AuthorizeUrl);   // only its hash leaves the phone at this step
    }

    [Fact]
    public void EveryRequest_UsesFreshRandomValues_OfSufficientLength()
    {
        var a = GoogleOAuth.CreateRequest(AndroidClient, Redirect);
        var b = GoogleOAuth.CreateRequest(AndroidClient, Redirect);

        Assert.NotEqual(a.CodeVerifier, b.CodeVerifier);
        Assert.NotEqual(a.State, b.State);
        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.InRange(a.CodeVerifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", a.CodeVerifier);
        Assert.True(a.Nonce.Length >= 16);   // the server refuses shorter nonces
    }

    [Theory]
    [InlineData("", Redirect)]
    [InlineData(AndroidClient, "")]
    [InlineData("  ", Redirect)]
    public void WithoutAClientId_OrRedirect_TheRequestRefusesToStart(string clientId, string redirect)
    {
        var ex = Assert.Throws<ExternalSignInException>(() => GoogleOAuth.CreateRequest(clientId, redirect));
        Assert.Equal(ExternalSignInFailure.NotConfigured, ex.Failure);
    }

    // ---- redirect handling ----

    private static Dictionary<string, string> Callback(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void AMatchingCallback_YieldsTheCode()
    {
        Assert.Equal("4/abc", GoogleOAuth.ReadAuthorizationCode(Callback(("code", "4/abc"), ("state", "S1")), "S1"));
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public void AStateThatIsNotOurs_IsRefused(string state)
    {
        var ex = Assert.Throws<ExternalSignInException>(() => GoogleOAuth.ReadAuthorizationCode(Callback(("code", "4/abc"), ("state", state)), "S1"));
        Assert.Equal(ExternalSignInFailure.Failed, ex.Failure);
    }

    [Fact]
    public void ACallbackWithoutState_IsRefused()
    {
        var ex = Assert.Throws<ExternalSignInException>(() => GoogleOAuth.ReadAuthorizationCode(Callback(("code", "4/abc")), "S1"));
        Assert.Equal(ExternalSignInFailure.Failed, ex.Failure);
    }

    [Fact]
    public void WhenThePersonDeclines_ItIsACancellation_NotAnError()
    {
        var ex = Assert.Throws<ExternalSignInException>(() => GoogleOAuth.ReadAuthorizationCode(Callback(("error", "access_denied"), ("state", "S1")), "S1"));
        Assert.Equal(ExternalSignInFailure.Cancelled, ex.Failure);
    }

    [Fact]
    public void AnotherProviderError_IsAFailure()
    {
        var ex = Assert.Throws<ExternalSignInException>(() => GoogleOAuth.ReadAuthorizationCode(Callback(("error", "server_error"), ("state", "S1")), "S1"));
        Assert.Equal(ExternalSignInFailure.Failed, ex.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ACallbackWithoutACode_IsRefused(string code)
    {
        Assert.Throws<ExternalSignInException>(() => GoogleOAuth.ReadAuthorizationCode(Callback(("code", code), ("state", "S1")), "S1"));
        Assert.Throws<ExternalSignInException>(() => GoogleOAuth.ReadAuthorizationCode(Callback(("state", "S1")), "S1"));
    }

    // ---- token exchange ----

    [Fact]
    public void TheTokenRequest_ProvesItselfWithTheVerifier_AndHasNoClientSecret()
    {
        var form = GoogleOAuth.TokenRequestForm(AndroidClient, "4/abc", "VERIFIER", Redirect).ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("VERIFIER", form["code_verifier"]);
        Assert.Equal("4/abc", form["code"]);
        Assert.Equal(Redirect, form["redirect_uri"]);
        Assert.False(form.ContainsKey("client_secret"));
    }

    [Fact]
    public void OnlyTheIdToken_IsTakenFromTheTokenAnswer()
    {
        var json = "{\"access_token\":\"ya29.SECRET\",\"refresh_token\":\"1//SECRET\",\"id_token\":\"aaa.bbb.ccc\",\"expires_in\":3599}";
        Assert.Equal("aaa.bbb.ccc", GoogleOAuth.ReadIdToken(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"access_token\":\"x\"}")]
    [InlineData("{\"id_token\":123}")]
    [InlineData("{\"id_token\":\"\"}")]
    public void ABadTokenAnswer_YieldsNoToken(string? json)
    {
        Assert.Null(GoogleOAuth.ReadIdToken(json));
    }

    // ---- iOS redirect scheme ----

    [Fact]
    public void TheIosRedirectScheme_IsTheReversedClientId()
    {
        Assert.Equal("com.googleusercontent.apps.1234-abc", GoogleOAuth.IosRedirectScheme("1234-abc.apps.googleusercontent.com"));
        Assert.Equal("com.googleusercontent.apps.1234-abc:/oauth2redirect", GoogleOAuth.IosRedirectUri("1234-abc.apps.googleusercontent.com"));
        Assert.Null(GoogleOAuth.IosRedirectScheme(""));
        Assert.Null(GoogleOAuth.IosRedirectScheme("not-a-google-client-id"));
        Assert.Null(GoogleOAuth.IosRedirectScheme(".apps.googleusercontent.com"));
    }

    [Fact]
    public void TheAndroidRedirect_UsesTheApplicationId()
    {
        Assert.Equal("com.obd360.app:/oauth2redirect", GoogleOAuth.AndroidRedirectUri(ExternalSignInConfig.AndroidRedirectScheme));
    }

    // ---- what the repository files say ----

    private static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "CarScanner.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir) ?? dir;
            }

            throw new DirectoryNotFoundException("repository root not found");
        }
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()));

    [Fact]
    public void NoGoogleClientSecret_IsEverEmbeddedInTheApp()
    {
        foreach (var file in new[]
                 {
                     Path.Combine("Services", "ExternalSignIn", "ExternalOAuth.cs"),
                     Path.Combine("Services", "ExternalSignIn", "ExternalSignInService.cs"),
                     Path.Combine("Services", "AccountService.cs"),
                     Path.Combine("Views", "AccountPage.xaml.cs"),
                     Path.Combine("Platforms", "Android", "GoogleSignInCallbackActivity.cs")
                 })
        {
            var source = Read(file);
            // The only place the word appears is the test-visible comment explaining there is none.
            Assert.DoesNotMatch(new Regex(@"client_secret\s*""?\s*[,=:]", RegexOptions.IgnoreCase), source);
            Assert.DoesNotContain("GOCSPX-", source);   // the prefix of every Google client secret
        }
    }

    [Fact]
    public void ProviderTokens_AreNeverStoredOrLogged()
    {
        var service = Read("Services", "ExternalSignIn", "ExternalSignInService.cs");
        var oauth = Read("Services", "ExternalSignIn", "ExternalOAuth.cs");
        foreach (var forbidden in new[] { "Preferences", "SecureStorage", "Debug.Write", "Console.Write", "Trace.Write", "ILogger", "CommandErrors", "File.Write" })
        {
            Assert.DoesNotContain(forbidden, service);
            Assert.DoesNotContain(forbidden, oauth);
        }

        // The page passes the credential to the account service and keeps nothing: no field holds it.
        var page = Read("Views", "AccountPage.xaml.cs");
        Assert.DoesNotContain("ProviderCredential _", page);
        Assert.DoesNotContain("IdToken", page);
    }

    [Fact]
    public void TheOnlyThingStoredAfterSignIn_IsTheAppSessionToken_InSecureStorage()
    {
        var account = Read("Services", "AccountService.cs");
        var external = account[account.IndexOf("private async Task<ExternalSignInResult> PostExternalAsync", StringComparison.Ordinal)..];
        external = external[..external.IndexOf("private static string ExternalFailureMessage", StringComparison.Ordinal)];

        Assert.DoesNotContain("Preferences", external);
        Assert.DoesNotContain("SecureStorage", external);
        Assert.Contains("ApplySessionAsync", external);   // the same session code as the password login
        Assert.Contains("SecureStorage.Default.SetAsync(AccessTokenKey", account);
    }

    [Fact]
    public void SigningOut_StillClearsTheSessionToken()
    {
        var account = Read("Services", "AccountService.cs");
        var logout = account[account.IndexOf("public async Task LogoutAsync", StringComparison.Ordinal)..];
        logout = logout[..logout.IndexOf("public async Task<AccountActionResult> DeleteAccountAsync", StringComparison.Ordinal)];

        Assert.Contains("SecureStorage.Default.Remove", logout);
        Assert.Contains("AccountIdKey", logout);
    }

    [Fact]
    public void Apple_IsIosOnly_AndHiddenOnAndroid()
    {
        var service = Read("Services", "ExternalSignIn", "ExternalSignInService.cs");
        Assert.Contains("OperatingSystem.IsIOSVersionAtLeast(13)", service);
        Assert.Contains("#if IOS", service);   // the Apple call itself is compiled for iOS only

        var android = Directory.GetFiles(Path.Combine(Root, "Platforms", "Android"), "*.cs", SearchOption.AllDirectories);
        Assert.DoesNotContain(android, file => File.ReadAllText(file).Contains("AppleSignIn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Google_OnIos_IsOnlyOfferedBesideApple()
    {
        var service = Read("Services", "ExternalSignIn", "ExternalSignInService.cs");
        Assert.Matches(new Regex(@"!OperatingSystem\.IsIOS\(\)\s*\|\|\s*ShowApple"), service);
    }

    [Fact]
    public void TheAppleEntitlement_IsOffByDefault_AndOnlyAddedByTheExplicitBuildSwitch()
    {
        Assert.DoesNotContain("applesignin", Read("Platforms", "iOS", "Entitlements.plist"));
        Assert.Contains("com.apple.developer.applesignin", Read("Platforms", "iOS", "Entitlements.AppleSignIn.plist"));

        var csproj = Read("CarScanner.csproj");
        Assert.Matches(new Regex(@"Condition=""'\$\(TargetFramework\)' == 'net10\.0-ios' And '\$\(EnableAppleSignIn\)' == 'true'"""), csproj);
        Assert.Contains("APPLE_SIGNIN", csproj);
    }

    [Fact]
    public void PackageAndBundleIdentifiers_AreUnchanged_AndMatchTheAndroidRedirect()
    {
        var csproj = Read("CarScanner.csproj");
        Assert.Contains("<ApplicationId>com.obd360.app</ApplicationId>", csproj);
        Assert.Contains(">com.gcar360.app</ApplicationId>", csproj);   // iOS bundle id is unchanged
        Assert.Equal("com.obd360.app", ExternalSignInConfig.AndroidRedirectScheme);

        var activity = Read("Platforms", "Android", "GoogleSignInCallbackActivity.cs");
        Assert.Contains("DataScheme = \"com.obd360.app\"", activity);
        Assert.Contains("WebAuthenticatorCallbackActivity", activity);
    }

    [Fact]
    public void TheIosUrlScheme_ForGoogle_IsRegistered()
    {
        var plist = Read("Platforms", "iOS", "Info.plist");
        Assert.Contains("CFBundleURLSchemes", plist);
        Assert.Contains("com.googleusercontent.apps.", plist);
    }

    [Fact]
    public void TheAccountPage_OffersGoogleFirst_AppleSecond_ThenEmail_AndGuardsAgainstDoubleTaps()
    {
        var xaml = Read("Views", "AccountPage.xaml");
        var google = xaml.IndexOf("x:Name=\"GoogleButton\"", StringComparison.Ordinal);
        var apple = xaml.IndexOf("x:Name=\"AppleButton\"", StringComparison.Ordinal);
        var email = xaml.IndexOf("x:Name=\"EmailToggleButton\"", StringComparison.Ordinal);
        Assert.True(google > 0 && google < apple && apple < email, "Google, then Apple, then Continue with Email");

        var code = Read("Views", "AccountPage.xaml.cs");
        var provider = code[code.IndexOf("private async Task ProviderSignInAsync", StringComparison.Ordinal)..];
        Assert.Contains("if (_busy)", provider[..200]);                 // a second tap while one is running does nothing
        Assert.Contains("SetBusy(true)", provider);
        Assert.Contains("GoogleButton.IsEnabled = !busy", code);
        Assert.Contains("AppleButton.IsEnabled = !busy", code);
    }

    [Fact]
    public void AClosedSheet_IsSilent()
    {
        var code = Read("Views", "AccountPage.xaml.cs");
        Assert.Contains("if (credential is null)", code);
        Assert.Contains("the person closed the sheet", code);
    }

    [Fact]
    public void EveryNewSignInString_HasATranslationRow()
    {
        var packs = string.Concat(Directory.GetFiles(Path.Combine(Root, "Services", "Localization"), "LanguagePacks*.cs").Select(File.ReadAllText));
        foreach (var english in new[]
                 {
                     "Continue with Google", "Continue with Apple", "Continue with Email", "Cancel linking", "Link and sign in",
                     "Google sign-in is not set up in this version yet.", "Sign in with Apple is not available on this device.",
                     "Your account was created. Welcome!", "Confirm linking", "This sign-in is already linked to another OBD360 account.",
                     "The linking request has expired. Start again.", "The sign-in could not be verified. Try again."
                 })
        {
            Assert.Contains("R(\"" + english + "\"", packs);
        }
    }
}
