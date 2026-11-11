# Manual Verification Checklist — before resubmitting

Everything here needs you personally, in App Store Connect or on a physical device. None of it can
be done from the source tree. **Nothing in this list has been done by the code change** — no App
Store Connect setting, price, product, capability or build was touched.

---

## 1. App Store Connect — subscription products

The mobile app now sells **three monthly plans and nothing else**. Each must exist in App Store
Connect under the **"OBD360 PRO Plans"** subscription group, in a state StoreKit will resolve
("Ready to Submit" or "Approved" — not "Missing Metadata" or "Waiting for Review").

| Plan | Product ID | Intended base USD price |
|---|---|---|
| OBD PRO | `com.georgiancarscanner.pro.monthly` | **$1.99 / month** |
| VIN PRO | `com.georgiancarscanner.vin.monthly` | **$1.99 / month** |
| Full PRO | `com.georgiancarscanner.full.monthly` | **$2.99 / month** |

- [ ] All three exist, spelled exactly as above (Product IDs are case-sensitive).
- [ ] Each is an **auto-renewable subscription**, not "non-renewing" and not consumable.
- [ ] Each has a localized display name and description (used by Apple's purchase sheet).
- [ ] The base price is set as above; App Store Connect derives other territories itself.
- [ ] **Record the price each product actually returns** and compare it with the table. The app now
      shows Apple's returned price verbatim, so a mismatch is visible on the PRO screen — it is a
      configuration issue to fix in App Store Connect, not in code. If a product is more expensive
      than intended, change it in App Store Connect; do not "fix" it in the app.
- [ ] `com.georgiancarscanner.vin.single` is **not** needed by the mobile app any more (the app
      offers only the +5 and +10 packs). Leave it if it exists; do not create it for iOS.

### Annual products

- [ ] **Do not delete** `com.georgiancarscanner.pro.yearly`, `…vin.yearly`, `…full.yearly`. The
      website still sells them and existing web customers must keep renewing.
- [ ] Confirm they are **not** referenced anywhere in the app's offering. They are not: the app's
      `SubscriptionProductIds` and `AllProductIds` contain no yearly id, and the PRO screen has no
      period selector. A source test pins this.
- [ ] If App Store Connect shows a yearly product in this submission's In-App Purchase list, remove
      it from **this submission's** product list only (not from the account). Apple reviews what
      the build can reach; the app cannot reach a yearly product.

### Subscription levels (important — this is a commercial decision, not a code one)

The app sells three plans that are **alternatives, not a stack**: a customer picks one. In App
Store Connect, the level ordering inside the group must express that:

- [ ] **FULL PRO** → highest level (level 1 in Apple's ordering).
- [ ] **OBD PRO** → one level below FULL PRO.
- [ ] **VIN PRO** → the **same** level as OBD PRO.

Apple only allows one active subscription per group, which matches the intended model: buying VIN
PRO while OBD PRO is active replaces it (an upgrade/downgrade/crossgrade, prorated by Apple), it
does not stack. The backend grants features per product (`GrantsObd`/`GrantsVin`), so whichever one
is active determines the tier — a Full PRO subscriber gets both, an OBD PRO subscriber only OBD, a
VIN PRO subscriber only VIN.

- [ ] If the three yearly products stay in the **same** group, give each one the **same level as
      its monthly counterpart** (yearly OBD PRO → OBD PRO's level, yearly FULL PRO → FULL PRO's
      level). That keeps a web yearly customer on the same tier and keeps upgrade paths sane. Their
      presence does not make them purchasable in the app.

## 2. Sign in with Apple (Guideline 4.8)

Google Sign-In exists, so Sign in with Apple must ship beside it on iOS. In code this is coupled:
`ExternalSignInService.ShowGoogle` is false on iOS unless `ShowApple` is true, which requires the
build flag.

- [ ] Apple Developer → Certificates, Identifiers & Profiles → Identifiers → `com.gcar360.app` has
      the **Sign in with Apple** capability enabled.
- [ ] The App Store distribution provisioning profile was **regenerated after** enabling it.
- [ ] The Xcode/Codemagic signing for the archive uses that profile.
- [ ] The Codemagic workflow still passes `-p:EnableAppleSignIn=true` (it does — `codemagic.yaml`),
      which switches `CodesignEntitlements` to
      `Platforms/iOS/Entitlements.AppleSignIn.plist`.
- [ ] On the TestFlight build, the Account page shows **both** "Continue with Google" and
      "Continue with Apple" on an iPhone. If only e-mail appears, the capability/profile is wrong
      (the app deliberately hides Google rather than shipping Google without Apple).

## 3. New build

`CarScanner.csproj` currently says `ApplicationDisplayVersion` 1.1.1 and `ApplicationVersion` 67.
The rejected build was 56; bump the build number for the upload.

- [ ] Push the branch to GitHub (nothing was pushed from here — no GitHub or network action taken).
- [ ] Run the Codemagic workflow `OBD360 iOS` (`maui-ios`) on the branch. It builds with
      `-p:EnableAppleSignIn=true` and `-p:ApplicationVersion=$BUILD_NUMBER`.
- [ ] It uploads to App Store Connect / TestFlight automatically on success.
- [ ] In App Store Connect → the version → Build, select the new build.
- [ ] Fill "App Review Information → Notes" with `app-review-notes.md`.
- [ ] Attach the physical-device recording (or link it) and reference it in the reply.
- [ ] Paste the reply letter (`apple-review-reply.md`) as the response to the rejection.
- [ ] Submit. **Do not** submit automatically from CI.

Windows cannot produce a signed IPA. The iOS target does compile here (`dotnet build -f
net10.0-ios` succeeds) but archiving and uploading requires macOS + Xcode.

## 4. Backend

- [ ] `Stores:Apple:*` (or the `GCS_APPLE_*` environment variables) are set on the production App
      Service: issuer id, key id, private key `.p8`, bundle id `com.gcar360.app`, environment
      `auto`. Without them every verification fails and the app shows "verification could not be
      completed".
- [ ] App Store Server Notifications V2 URL is still configured in App Store Connect and points at
      `/api/store/apple/notifications`.
- [ ] No database migration is needed: `AnonymousStoreSessions` is created with
      `CREATE TABLE IF NOT EXISTS` at startup. Existing `PurchaseClaims`, `Subscriptions`,
      `Entitlements` and `WebOrders` rows are untouched.
- [ ] After deploying, confirm `POST /api/store/apple/anonymous/verify` answers `401
      {"code":"NOT_VERIFIED"}` for a junk transaction id (i.e. it reaches Apple and refuses), not a
      500.

## 5. Physical device — sandbox purchase and restore

Do this on a real iPhone with a **sandbox Apple Account** (Settings → Developer → Sandbox Account,
or the tester created in App Store Connect → Users and Access → Sandbox Testers).

- [ ] Fresh install, **not signed in to any OBD360 account.**
- [ ] PRO screen opens with no login prompt anywhere.
- [ ] Three cards, prices come from the App Store, no strike-through, no percentage badge, no
      Monthly/Yearly toggle.
- [ ] Buy OBD PRO → Apple sheet → confirm → the app reports success and a PRO-gated feature opens.
- [ ] Kill and relaunch the app **while still signed out** → PRO is still active (the accountless
      entitlement surviving a restart).
- [ ] Delete the app, reinstall, **stay signed out** → tap Restore Purchases → PRO comes back.
- [ ] **Two devices, same Apple Account** (an iPhone and a second iPhone or iPad): buy on device A,
      then Restore Purchases on device B while A is still installed. **Both must be active at the
      same time.** Re-open A and confirm it was not revoked. Then let a sandbox subscription lapse
      (or refund it in App Store Connect → sandbox) and confirm both devices lose access.
- [ ] Sign in with an OBD360 account afterwards → PRO still active (both sources combined).
- [ ] Sign out again → PRO still active (the Apple source must survive a sign-out).
- [ ] Full reset (account deletion on a throwaway account) **does** clear it, and Restore brings it
      back.
- [ ] A VIN pack while signed out → the app asks to sign in instead of charging.
- [ ] Privacy Policy, Terms of Use and Settings → Manage Subscription all load.
- [ ] Account deletion works on a throwaway account.

## 6. Android regression (must not break)

- [ ] PRO screen shows three monthly plans, **no** year/month toggle, no numeric price (payment
      happens on the website).
- [ ] Tapping a plan while signed out opens sign-in; while signed in it opens
      `obd360.ge/pro?email=…&source=android`.
- [ ] That page shows **monthly plans only** and no period toggle.
- [ ] A website purchase made there is recognised after returning to the app.
- [ ] Opening `https://obd360.ge/pro` **in a normal browser** still shows monthly **and** yearly.

## 7. Content / licensing (unchanged from the earlier audit)

- [ ] VIN-history provider agreements (Apibara, VehicleDatabases.com, apiauctions.io, VinAudit —
      server-side only) are current, in case Apple asks again.

## 8. Do not do

- Do not delete annual products from App Store Connect.
- Do not publish or commit Apple credentials, `.p8` keys, or a reviewer password.
- Do not add a general "skip the sign-in code" switch: normal accounts keep their e-mail OTP. None
  was added in this change, and none is needed — Apple never has to sign in to buy or restore.
