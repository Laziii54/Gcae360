#if ANDROID

namespace CarScanner.Platforms.Android.Billing;

/// <summary>
/// Google Play Billing is not used: the Android app has no in-app purchases.
/// PRO bought on the website is unlocked by signing in to the account.
///
/// This empty type only exists because MauiProgram still registers it in DI.
/// Once that registration is removed, delete this file (or exclude
/// Platforms\Android\Billing\** from the build in CarScanner.csproj).
/// </summary>
public sealed class AndroidBillingService
{
}

#endif
