# OBD360 — External Services Inventory

Verified from the source code in this repository. Each entry is marked **confirmed active**
(the app code actually calls it), **installed but not confirmed invoked** (present as a
dependency, no confirmed call site), or **server-side only** (visible in this monorepo's backend
code but never reached directly by the mobile app — included here for completeness, not because
Apple needs to know about a backend's own upstream vendors).

## 1. OBD360 backend API — confirmed active

One first-party backend, `Services/AIConfig.cs`:

```
https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net
```

(An Azure Web App in the Germany West Central region — distinct from the public marketing site
`obd360.ge`, which only serves the Privacy Policy, Terms of Use, and the website purchase flow.)
The app calls this backend for: accounts/auth (`/api/accounts/*`), VIN history (`/api/vin/*`), AI
diagnostics (`/api/ai/diagnose`, `/api/ai/chat`), usage telemetry (`/api/telemetry/heartbeat`),
crash reports, opt-in ECU data collection (`/api/ecu-observations*`), purchase/entitlement
verification (`/api/store/verify`, `/api/entitlements/{id}`), and avatar upload. All first-party,
all one domain, all over HTTPS (several services explicitly refuse to send anything if the
resolved scheme isn't `https`).

## 2. AI diagnostics — confirmed active, proxied

The app never talks to an AI provider directly. `AI/AiDiagnosticService.cs` only calls the
backend's own `/api/ai/diagnose` and `/api/ai/chat`. `AIConfig.cs` states explicitly: *"No AI key
is stored in the MAUI app."* The upstream model, called server-side only, is **Google Gemini**
(`gemini-3.8-flash` per the backend's own default-model config, visible in this monorepo but not
in the mobile app).

## 3. Authentication providers — confirmed active

`Services/ExternalSignIn/ExternalSignInService.cs` implements real (non-stub) OAuth/PKCE for
**Google Sign-In** and native **Sign in with Apple**. Apple Sign-In requires the iOS build to be
compiled with `-p:EnableAppleSignIn=true`; the project's one iOS CI pipeline
(`codemagic.yaml`, workflow `maui-ios`) always passes this flag. Email/password sign-in against
the OBD360 backend is also available and does not require either provider.

## 4. In-app purchases — confirmed active (iOS), none (Android)

iOS uses native **StoreKit** (`Platforms/iOS/Billing/iOSBillingService.cs`) — no third-party
billing SDK. Android has no in-app purchase integration at all; PRO bought on the website is
unlocked by signing in to the same account. See `subscription-compliance-audit.md` for the
purchase/entitlement flow itself.

## 5. Crash reporting — confirmed first-party, not a third-party SDK

`Services/CrashReportingService.cs`: *"Persists an anonymous, sanitized crash envelope locally and
sends it on the next launch. Vehicle data, account data and exception messages are never
included."* The envelope is exception type, a sanitized stack trace, app version, device
platform/OS version, a category, and a fatal flag — uploaded to the OBD360 backend only. No
Crashlytics, Sentry, App Center, or similar SDK exists anywhere in the project.

## 6. Usage/analytics reporting — confirmed first-party

`Services/UsageReportService.cs` posts a heartbeat (app version/build, platform, OS version,
device model, language, notification setting) to the app's own `/api/telemetry/heartbeat`, at
most every 6 hours. It is identified by a random per-install device ID, sent anonymously while
signed out; once the user is signed in, the same request also carries their auth token, so it
becomes account-linked at that point. No third-party analytics SDK is present.

## 7. Anonymous ECU data collection — confirmed genuinely opt-in

`Services/DataCollection/DataCollectionService.cs` and `ConsentPromptService.cs`: off by default,
only starts after the user explicitly grants consent; disabling it both stops collection locally
and sends a server-side delete request for previously-collected data. Uploads carry a per-device
ID but **no account token and no VIN** — the data is pseudonymous (the server can hash the device
ID), not fully anonymous, and the app's own code comment says so. What's collected: sanitized OEM
module responses and OBD2 PID/DTC-mode summaries, never raw measured values.

## 8. VIN history provider

**App side — confirmed backend-only**: `VinCheck/Services/VinServices.cs` calls only the OBD360
backend's `/api/vin/*`. **Server side — present in this repo's `Backend/` project, never reached
directly by the mobile app**: the backend's own VIN data service references several named
upstream vehicle-data providers (Apibara, VehicleDatabases.com, apiauctions.io, VinAudit) plus
NHTSA's public vPIC/recalls APIs. These are the backend's own vendor relationships, included here
for completeness; the app itself has no direct dependency on them.

## 9. Third-party packages with a real external footprint

The `<PackageReference>` list in `CarScanner.csproj` is modest. Items with any real external-service
or notable on-device-SDK character:

- `Xamarin.GooglePlayServices.MLKit.Text.Recognition` — **confirmed active, on-device only**
  (Android-specific; used for on-device OCR of speedometer digits during the acceleration/
  performance-video feature, `Platforms/Android/PerformanceVideo/AndroidPerformanceCameraController.cs`).
  Not iOS-relevant.
- `Xamarin.Google.Android.Play.Review(.Ktx)` — Google Play's in-app review prompt. Android-only,
  not iOS-relevant.
- `Plugin.BLE`, `Plugin.LocalNotification`, `sqlite-net-pcl`, `CommunityToolkit.Mvvm`,
  `Microsoft.Maui.Controls` and the AndroidX libraries are device-local libraries, not external
  services.
- No Firebase, Crashlytics, Sentry, App Center, maps SDK, or third-party analytics SDK exists
  anywhere in the project — consistent with items 5 and 6.

## 10. Payment processor (context only, not iOS-relevant)

"BOG" (Bank of Georgia) appears throughout the backend's website-checkout code. This is the
payment gateway for the **website** purchase flow (`obd360.ge/pro`), used by Android users and by
anyone paying by card on the web — it is not used by, and has no bearing on, the iOS in-app
purchase flow, which goes entirely through StoreKit.

## Summary for the Apple reply

Every external service the app itself calls is either (a) the app's own first-party backend, over
HTTPS, with no AI/analytics/crash-reporting key embedded in the client, or (b) a native Apple/Google
sign-in and purchase mechanism (Sign in with Apple, Google Sign-In, StoreKit). There is no
third-party analytics or crash SDK collecting data outside the app's own control.
