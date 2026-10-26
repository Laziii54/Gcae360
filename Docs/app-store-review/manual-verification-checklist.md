# Manual Verification Checklist — before resubmitting

Everything here needs you personally, in App Store Connect or on a physical device. None of it
could be confirmed from the source code alone.

## App Store Connect

- [ ] **Confirm `com.georgiancarscanner.full.monthly` ("Full PRO") is Approved/Ready to Submit**,
      with correct price, display name, and description, under the "OBD360 PRO Plans" group.
- [ ] **Confirm whether `com.georgiancarscanner.pro.monthly` (OBD PRO) and
      `com.georgiancarscanner.vin.monthly` (VIN PRO) exist in ASC.** The rejection context names
      only "Full PRO" — if the other two don't exist, their purchase buttons will fail on iOS.
      Either create them, or (tell us and) we'll adjust the app to only show plans that actually
      exist.
- [ ] **Compare all nine Product IDs character-for-character** (the exact strings are listed in
      `subscription-compliance-audit.md`) against what's typed into App Store Connect — Product
      IDs are case-sensitive, and the purchase code was traced end to end and found to correctly
      request whatever id the UI implies, so a mismatch at the ASC end (not in this app's code) is
      the most likely remaining explanation if a yearly (or any) purchase still fails.
- [ ] **Confirm each product's status is "Ready to Submit" / "Approved"**, not "Missing Metadata"
      or "Waiting for Review" — StoreKit may not resolve a product that isn't in a ready state.
- [ ] Confirm the subscription group's tier/level ordering makes sense for three plans that aren't
      strictly nested (OBD PRO, VIN PRO, Full PRO) — this affects how StoreKit presents
      upgrade/downgrade/crossgrade between them.
- [ ] Confirm the app's current build number in ASC. The rejected submission was build 52; this
      repository's `CarScanner.csproj` currently has `ApplicationVersion` 65 — the codebase has
      moved on since the rejected build, so a fresh build/number is needed for resubmission
      regardless.
- [ ] Re-check the App Review Information notes field, screenshots, and app description are still
      accurate for the current build.

## Physical device

- [ ] Record the screen-recording video per `physical-device-recording-checklist.md`.
- [ ] Manually test: sign-in (email, Google, Apple), OBD2 connect, DTC scan, live data, AI
      explanation, PRO purchase (sandbox), Restore Purchases, account deletion (on a throwaway
      account), both legal links.
- [ ] Confirm **Sign in with Apple actually appears** on the build you test — this depends on the
      iOS build having been produced with `-p:EnableAppleSignIn=true`. Our only iOS CI pipeline
      (`codemagic.yaml`) always sets this flag, so it should be present, but verify on the actual
      installed build rather than assuming.
- [ ] Confirm VIN Check and any other non-hardware feature work correctly without an OBD2 adapter
      connected, exactly as App Review would experience them.

## Account / test credentials

- [ ] Decide whether to give App Review a dedicated test account (recommended, so they don't need
      their own vehicle/adapter to get past sign-in) and, if so, create it and add its credentials
      to `app-review-notes.md` yourself — none were invented in these documents.
- [ ] If you provide a test account, consider granting it a PRO entitlement server-side so
      reviewers can see PRO-gated screens even without completing a sandbox purchase.

## Content / licensing

- [ ] Confirm your data-licensing agreements with the VIN-history providers your backend uses
      (Apibara, VehicleDatabases.com, apiauctions.io, VinAudit — found server-side in this repo,
      see `external-services-inventory.md`) are current, in case Apple asks a follow-up question
      about data rights.

## Before you resubmit

- [ ] Re-read `apple-review-reply.md` and fill in the two placeholders (the recording, and test
      credentials if you're providing them).
- [ ] Re-read `app-review-notes.md` and fill in test credentials if applicable.
- [ ] Nothing in this engagement pushed a build, changed signing, or submitted anything to Apple —
      that's still entirely up to you.
