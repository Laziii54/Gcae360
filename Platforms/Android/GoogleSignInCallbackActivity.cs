using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace CarScanner;

/// <summary>
/// Receives Google's redirect (com.obd360.app:/oauth2redirect) after the person picks an account in the browser and hands
/// it to WebAuthenticator. The scheme is the application id, which Google requires for an Android OAuth client; the
/// authorization code in that redirect is useless to any other app because it is bound to this app's PKCE verifier.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "com.obd360.app")]
public class GoogleSignInCallbackActivity : WebAuthenticatorCallbackActivity
{
}
