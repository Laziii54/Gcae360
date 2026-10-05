# Georgian Car Scanner — Store release

## Fixed identifiers

- App/package/bundle ID: `com.gcar360.app`
- PRO monthly subscription: `com.georgiancarscanner.pro.monthly`
- Current internal-test version: `0.5.0` (`versionCode` 50)
- Android target API: 36
- Android minimum: API 24
- Google Play Billing Library: 8.0.0 (supported for new releases through 2027-08-31)
- iOS minimum: 15.0

Do not change the package/bundle ID after the first store upload. Increase
`ApplicationVersion` for every uploaded build. Change the public display
version to `1.0.0` only after device and store-sandbox acceptance tests pass.

## Required backend deployment

Deploy `Backend/CarScanner.Api` before store review. The deployed backend must
serve:

- `/privacy-policy`
- `/terms-of-use`
- `/support`
- `/health/configuration` (must report `readyForStoreRelease: true`)
- `/api/store/verify`
- `/api/entitlements/{userId}`
- `/api/accounts/register`, `/api/accounts/login`, `/api/accounts/me`
- `/api/telemetry/crash`
- the existing AI endpoints

`/api/store/webhook` is a trusted relay endpoint, not a raw Google/Apple
notification receiver. The relay must authenticate with `X-GCS-Webhook-Secret`
and send a unique `eventId`, `store`, installation `userId`, `productId`,
`transactionId`, plus `action=verify|refresh|revoke`. Revocation is accepted only
when the store and transaction match that user's active entitlement.

Production secrets belong only in the backend environment:

- `GEMINI_API_KEY`
- `GCS_GOOGLE_SERVICE_ACCOUNT_JSON`
- `GCS_ANDROID_PACKAGE_NAME=com.gcar360.app`
- `GCS_APPLE_ISSUER_ID`
- `GCS_APPLE_KEY_ID`
- `GCS_APPLE_PRIVATE_KEY_P8`
- `GCS_APPLE_ENVIRONMENT=auto`
- `GCS_APPLE_BUNDLE_ID=com.gcar360.app`
- `GCS_STORE_WEBHOOK_SECRET`

Never put these values in the mobile app, repository, screenshots or store
listing.

Server-side entitlement, purchase-claim, promo and webhook state is stored in
one SQLite database. On Azure App Service the API automatically places it under
the persistent `HOME/data/georgian-car-scanner` volume. On another host set
`GCS_DATA_DIRECTORY` to a persistent directory outside the deployed application
files. Keep the App Service at one instance while SQLite is used; move this
state to a managed database before scaling to multiple API instances.

## Google Play

1. Create the app with package ID `com.gcar360.app`.
2. Enrol in Play App Signing and create/back up one upload keystore.
3. Create the monthly auto-renewing subscription with product ID
   `com.georgiancarscanner.pro.monthly` and at least one active base plan.
4. Add the backend Google service account to Play Console and grant only the
   permissions needed to view subscription orders.
5. Complete App content, Data safety, content rating, target audience, ads,
   account-access and privacy-policy sections.
6. Upload the signed AAB to Internal testing first and add licence testers.
7. Test purchase, cancellation, pending purchase, restore, expiry and reinstall.
8. Promote only the exact tested artefact to production.

Create the upload key with Visual Studio's Android signing identity dialog or
`keytool`. Keep the keystore and password in two backed-up secure locations.
Then publish without exposing the password in command history:

```powershell
.\StoreRelease\Publish-Android.ps1 `
  -KeyStorePath 'D:\secure\gcs-upload.keystore' `
  -KeyAlias 'gcs-upload' `
  -PasswordFile 'D:\secure\gcs-upload-password.txt'
```

The password file and keystore must stay outside the repository.

## Apple App Store

The iOS archive must be compiled and signed on a Mac with Xcode and an active
Apple Developer membership.

1. Register bundle ID `com.gcar360.app`.
2. Create the App Store Connect app and monthly auto-renewable subscription
   `com.georgiancarscanner.pro.monthly`.
3. Complete Agreements, Tax and Banking.
4. Create an App Store Connect API key for the backend and configure the Apple
   environment variables above.
5. Build/archive `net10.0-ios` on the Mac with distribution signing.
6. Upload to TestFlight and test purchase, restore, expiry and reinstall with a
   sandbox tester.
7. Complete App Privacy, age rating, review notes and subscription metadata.
8. Submit the exact TestFlight-tested build for review.

StoreKit returns a transaction identifier to the app. The backend verifies it
with Apple before PRO is granted. Production and TestFlight/sandbox lookup are
both supported when `GCS_APPLE_ENVIRONMENT=auto`.

## Mandatory acceptance tests

- Fresh install; Bluetooth denied, then granted.
- Classic Bluetooth and BLE adapter connection where supported.
- Adapter absent, ignition off, ECU unavailable and connection loss.
- VIN/year/fuel fallback states contain no demo values.
- Quick scan with zero DTC, active/pending/permanent DTC and freeze frame.
- Every unsupported PID/module/action shows unavailable, never fabricated data.
- Metric/Imperial and Georgian/English persist after restart.
- PRO purchase, restore and entitlement expiry on both stores.
- Privacy Policy, Terms and Manage Subscription open successfully.
- Public Support URL opens successfully.
- Offline launch and graceful backend/AI failure.
- Android internal test on API 24 and a current API 36 device.
- iPhone/iPad TestFlight smoke test on the supported iOS range.

## Current external blockers

Authenticated PRO account ownership and the explicit transaction-transfer policy
are implemented. The remaining purchase work requires real store products,
credentials and sandbox accounts.

- A permanent Android upload keystore and its owner-selected password.
- Google Play Console account, product/base-plan and service-account access.
- Mac/Xcode, Apple Developer/App Store Connect access and signing certificates.
- Apple subscription and server API key configuration.
- Final vehicle/device regression testing and store reviewer approval.
