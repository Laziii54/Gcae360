# OBD360 — Google & Apple sign-in: what exists and what you must configure

Status of this document: written 2026-10-18 with the implementation. Nothing here has been deployed, published or committed.
The code is covered by unit tests only (see "Verified vs. not verified"). **No sign-in has been tried on a real phone, with a real
Google account or with a real Apple ID.**

## How it works (short)

1. The app asks the provider for an **ID token** — Google through the system browser (OAuth code flow + PKCE, no client secret),
   Apple through the native iOS sheet.
2. The app sends only that token (and, for Google, the random nonce it asked for) to `POST /api/auth/google` / `/api/auth/apple`.
3. The **server verifies the token itself**: RS256 signature against Google's / Apple's published keys, issuer, audience (our own
   client ids), expiry, nonce, one-time use. An e-mail address sent beside the token is never read.
4. The provider identity `(provider, subject)` is looked up in the new table `AccountExternalIdentities`:
   * **already linked** → normal device-bound sign-in of that account (same session token, same 90-day life, same bans, same
     "account is on another phone → move it?" rule as e-mail sign-in);
   * **new person** → an account is created without any form (random unknown password, e-mail marked verified because the provider
     vouched for it), bound to this phone;
   * **e-mail already belongs to an OBD360 password account** → nothing is merged. The server answers `link_required` with a
     10-minute single-use ticket. The person must enter that account's **password** and, when mail is configured, the **6-digit code
     e-mailed to that account**; only then is the identity linked.
5. PRO is untouched: it belongs to the account id / store receipt, never to how someone signed in.

## Files

New: `Backend/.../Services/ExternalIdentityVerifier.cs`, `ExternalAuthService.cs`; `Services/ExternalSignIn/ExternalOAuth.cs`,
`ExternalSignInService.cs`; `Platforms/Android/GoogleSignInCallbackActivity.cs`; `Platforms/iOS/Entitlements.AppleSignIn.plist`;
`Resources/Images/google_g.svg`, `apple_logo_black.svg`, `apple_logo_white.svg`; `Tests/.../ExternalSignInTests.cs`,
`ExternalSignInClientTests.cs`; this file.

Changed: `AccountStore.cs` (new table, four new methods, the device-bound tail of password login moved unchanged into
`CompleteDeviceBoundSignInAsync`, identity cleanup on account deletion), `EmailVerificationStore.cs` (`MarkVerifiedAsync`),
`Program.cs` (DI + `GET /api/auth/providers`, `POST /api/auth/{google|apple}`, `POST /api/auth/link`), backend `.csproj`
(+`Microsoft.IdentityModel.JsonWebTokens` 8.14.0), `appsettings.example.json`, `AccountService.cs`, `AccountPage.xaml(.cs)`,
`MauiProgram.cs`, `CarScanner.csproj` (opt-in Apple switch), `AndroidManifest.xml` (`<queries>` for Custom Tabs), `Info.plist`
(URL scheme), `LanguagePacks.Extra33.cs` (translations).

Database: one new table, created with `CREATE TABLE IF NOT EXISTS` when the backend starts — **nothing is dropped or rebuilt**, existing
rows are not touched. It is created in the production database the first time the new backend version starts (that is the "migration";
this session did not run it anywhere).

## What you must configure (nothing below was done by me)

### A. Google Cloud Console (https://console.cloud.google.com)

1. Create or choose a project → **APIs & Services → OAuth consent screen**: User type *External*, app name *OBD360*, support e-mail,
   privacy-policy link. Scopes: only `openid` and `email` (non-sensitive, no Google review needed). Move the app to **In production**
   (in *Testing* only listed test users can sign in).
2. **Credentials → Create credentials → OAuth client ID → Android**
   * Package name: `com.obd360.app` (the NEW Play app; its Play App Signing SHA-1 differs from the old `com.gcadr360.app` app)
   * SHA-1: the certificate that signs the build people run. For Play builds that is the **Play App Signing** certificate (Play Console →
     App integrity); add your upload / debug certificate as extra Android clients while testing.
   * Under *Advanced settings* turn on **Enable custom URI scheme** (Google disables it by default; this app's redirect is
     `com.obd360.app:/oauth2redirect`).
   * Copy the client id (`…apps.googleusercontent.com`) into `ExternalSignInConfig.GoogleAndroidClientId`
     (`Services/ExternalSignIn/ExternalOAuth.cs`). It is a public identifier, not a secret.
3. **OAuth client ID → iOS**: bundle id `com.gcar360.app`. Copy its id into `ExternalSignInConfig.GoogleIosClientId`, **and** replace
   `REPLACE_WITH_IOS_CLIENT_ID_PREFIX` in `Platforms/iOS/Info.plist` with that id **without** the `.apps.googleusercontent.com` ending
   (`1234-abc.apps.googleusercontent.com` → scheme `com.googleusercontent.apps.1234-abc`).
4. There is **no client secret** to create or store: Android and iOS clients have none, PKCE protects the code. If a console screen
   offers to download a JSON with a secret, you picked the wrong client type (Web / Desktop).

### B. Apple Developer (https://developer.apple.com/account)

1. **Certificates, Identifiers & Profiles → Identifiers → App IDs → `com.gcar360.app` → enable "Sign in with Apple"** (primary App ID).
2. Regenerate the provisioning profiles that use this App ID (or let Xcode / your CI signing refresh them).
3. Build the iOS app with `-p:EnableAppleSignIn=true`. That switch (a) signs with `Platforms/iOS/Entitlements.AppleSignIn.plist`
   (`com.apple.developer.applesignin = Default`) and (b) defines `APPLE_SIGNIN`, which is what makes a Release build show the Apple
   button. Without it the default signing is exactly as before and the Apple button stays hidden in Release (shown in Debug so the
   missing setup is visible). Adding the entitlement before step 1 makes signing fail — that is why it is opt-in.
4. No Services ID, no key (.p8) and no client secret are needed: the app uses the native sheet and the server only verifies the token
   against Apple's public keys.
5. Private-relay / hidden e-mail: works as is (the relay address in the token is used as the account e-mail; it is marked verified
   because Apple says so). Apple sends the name only on the first sign-in; this app never asks for or stores a name.

### C. Azure App Service (Configuration → Application settings) — **not changed by me**

| Name | Value |
| --- | --- |
| `GCS_GOOGLE_CLIENT_IDS` | `ANDROID_CLIENT_ID.apps.googleusercontent.com,IOS_CLIENT_ID.apps.googleusercontent.com` (public ids, comma-separated) |
| `GCS_APPLE_CLIENT_IDS` | `com.gcar360.app` |

(`Auth__Google__ClientIds` / `Auth__Apple__ClientIds` work too.) With either unset, that provider answers `503 not_configured` and the
app says so. `GET /api/auth/providers` shows which are on. Deploying the backend is a separate step that is yours to take; the table
is created when the new version starts.

## Verified vs. not verified

**Implemented and covered by automated tests (unit / in-memory SQLite / locally signed test tokens — 78 new tests, 2042 in the suite, all green):**
valid / expired / not-yet-valid / wrong-audience / wrong-issuer / unknown-key / tampered / `alg:none` / HS256-with-public-key /
missing-`sub` / garbage tokens; Google nonce required and matched, Apple nonce optional but checked when present; key rotation (one
forced refresh); provider not configured → fails safe; first-time sign-up; second sign-in same account id; provider e-mail change;
token replay; Apple relay address; missing / unverified e-mail; no merge on e-mail match; linking needs password **and** e-mailed code,
ticket single-use / expiry / device-bound / attempt-limited; one identity can belong to one account (unique key); device binding and the
7-day move cooldown; banned accounts; account deletion frees the identity; e-mail + password login unchanged (including device rules);
PKCE against the RFC 7636 vector, state check, cancellation vs. failure, no client secret, no token storage or logging in the app code,
Apple iOS-only, Apple entitlement off by default, identifiers unchanged, UI order and double-tap guard, translation rows.
`dotnet build -f net10.0-android -c Release` succeeds.

**Not verified (needs real hardware / accounts / a Mac):**
* any real Google or Apple sign-in end to end; the Android custom-scheme redirect actually returning to the app (it depends on the
  Google console switch above); Google's token endpoint answer on a real device;
* the iOS code path (`#if IOS` in `ExternalSignInService.cs`, `AppleSignInAuthenticator`) — it was written against the MAUI API listing
  but **could not be compiled on this Windows machine**; an iOS build is still required;
* how the buttons look on a phone (dark / light, small screens). The Google "G" and Apple logo are SVG files drawn from public brand
  shapes — compare them with Google's and Apple's current brand guidelines before release; the Apple button uses the logo + "Continue
  with Apple" label, not Apple's native `ASAuthorizationAppleIDButton`;
* the translations are machine-written (not reviewed by native speakers); Georgian text is mine.

## Remaining risks / decisions (honest list)

1. **Apple account deletion.** Apple expects apps with Sign in with Apple to revoke the user's Apple tokens when the account is deleted.
   This app never exchanges Apple's authorization code (no refresh token exists to revoke), so there is nothing to revoke, but App Review
   may still ask; implementing it needs a server-side Apple key (.p8) and the code exchange. Not done.
2. **Pre-hijack edge case.** If someone creates an account with a provider e-mail they control but the real owner never used OBD360
   (e.g. a Google Workspace admin minting `name@company.com`), the real owner later registers → "already exists" → resets the password
   by e-mail (proves mailbox control) and gets an account that still has the other person's Google identity linked. Mitigation not
   implemented: unlink provider identities on password reset. Say if you want it.
3. **In-memory state.** Link tickets and the used-token list live in the backend's memory: a restart or a second App Service instance
   simply makes the person tap again (fails safe). They are not shared across instances.
4. **Google on iOS needs Apple next to it** (App Store guideline 4.8). The code already hides Google on iOS Release builds when Apple
   is not enabled.
5. Android sign-in uses a **custom URI scheme** redirect (the only option without extra libraries). Another app could register the same
   scheme, but the code in the redirect is useless without this app's PKCE verifier. Google's newer native Credential Manager would be
   stronger but needs additional native packages; not added ("minimal dependencies").
6. The apps' `Release` Google button is hidden until `GoogleAndroidClientId` / `GoogleIosClientId` are filled in (Debug builds show it and
   explain what is missing).
