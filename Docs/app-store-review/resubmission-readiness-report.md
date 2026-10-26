# OBD360 — Apple App Review Resubmission Readiness Report

## Executive summary

Apple rejected iOS build 52 under Guideline 2.1 (Information Needed), not for a specific bug —
Apple's seven questions are all about *understanding* the app, which this audit's new
documentation (`docs/app-store-review/`) now answers with verified, cited facts. Separately, this
audit found one concrete, confirmed bug in the subscription flow that is worth fixing regardless
of the 2.1 request, because it could cause App Review to hit a real purchase failure.

The app itself — account system, OBD2 diagnostics, AI explanations, VIN history, and the Full PRO
purchase flow — is substantially real and well-built. The main genuine risk for a Guideline-2.1
style rejection is structural, not a bug: core features need a physical OBD2 adapter and often a
real vehicle, which App Review cannot supply on its own. That's what the recording and
documentation in this folder are for.

## Critical blockers (must resolve before resubmitting)

1. **Unverified: exact Product ID match between code and App Store Connect, for all nine
   products.** The owner has confirmed the yearly products already exist in ASC (a stale code
   comment claiming otherwise has been corrected). A traced, line-by-line read of the purchase
   path found no remaining code bug that would cause a correctly-configured product to fail to
   load — the exact id the UI implies is the exact id StoreKit is asked to resolve. If
   `PRODUCT_NOT_FOUND` is still seen on a real device, the most likely remaining cause is outside
   this repository: an exact string/case mismatch between a Product ID in `Services/ProService.cs`
   and App Store Connect, or a product not yet in a "Ready to Submit"/"Approved" state. The nine
   exact identifiers to compare are listed in `subscription-compliance-audit.md`.
2. **Unconfirmed: whether OBD PRO and VIN PRO monthly products exist in ASC at all** — the
   rejection context names only "Full PRO." If they don't exist, the same failure applies to two
   of the three plan buttons. This needs your confirmation in App Store Connect; it cannot be
   checked from the code.
3. **No physical-device recording exists yet.** Apple explicitly asked for one;
   `physical-device-recording-checklist.md` is the script to follow — nothing was or can be
   recorded in this environment.

## Confirmed issues (fixed in this engagement)

- **`ViewModels/AdvancedViewModels.cs`, `PurchaseStatusPresentation`**: the `PRODUCT_NOT_FOUND` /
  `ITEM_UNAVAILABLE` / etc. error message unconditionally said *"This offer is not available in
  Google Play yet"* — including on iOS, where this exact path is reachable. Now uses the
  already-existing `StoreName` property, so it correctly says "App Store" or "Google Play"
  depending on platform. New localized strings added to
  `Services/Localization/LanguagePacks.Extra39.cs` for all 7 added languages (tr/de/pl/es/pt/fr/it).
- **`Services/PurchaseService.cs`, `IsCovered(productId)`**: only recognized the three *monthly*
  product ids; a yearly id always fell through to "not covered," so a user who already owned a
  plan via its yearly id would never get the "already active" short-circuit and could be sent
  through a real purchase attempt again. Fixed to recognize both ids of each plan.
- **`Services/PurchaseService.cs`, `SubscriptionProductIds`**: listed only the three monthly ids;
  now lists all six (monthly + yearly), so the (currently unused, see below) price-fetch path can
  look up yearly prices too.
- **`Services/ProService.cs`**: corrected the stale comment claiming yearly products "are not yet
  created in App Store Connect" — the owner confirmed they exist; the comment now points at the
  verification this still needs (exact id match) instead of asserting something false.
- Five new regression tests added, `Tests/CarScanner.Tests/SubscriptionProductMappingTests.cs`,
  pinning the monthly/yearly id pairing, the `IsCovered` fix, and `ActiveProductId`'s selection
  logic, following this codebase's existing source-reading test convention for MAUI-dependent
  services (same approach as `ProServiceLogoutTests.cs`/`ProTierTests.cs`).

**Noted but not fixed** (pre-existing, out of this task's scope, affects monthly and yearly
equally): `PurchaseService.GetPricesAsync()` — which would fetch real App Store prices for
display — is never actually called anywhere in the app. The PRO page's displayed prices are
always the hardcoded GEL fallback, never a live store price, for every plan. This doesn't affect
purchase safety (Apple's own purchase sheet always shows the real price before any charge), so it
was left alone rather than wiring up a new code path under a "fix the yearly mapping" request —
flag if you'd like it addressed separately.

Everything else flagged during the audit (unverifiable App Store Connect product configuration,
the lack of a recording) requires your action, not a code change, and is tracked in
`manual-verification-checklist.md`.

## Potential issues (flagged, not changed — your call)

- The three-tier, non-strictly-nested subscription structure (OBD PRO / VIN PRO / Full PRO, each
  with its own monthly+yearly pair) is more complex than a typical single-tier subscription. Worth
  a deliberate check of the ASC subscription group's level/ordering configuration so
  upgrade/downgrade behaves as intended — this is an App Store Connect setting, not app code.
- No in-app demo/simulator mode exists for OBD2 features. This is a legitimate product decision
  (not a bug), but it's the most likely reason App Review needs the recording in the first place.
  Not changed here since it wasn't asked for and would be a real feature addition, not a fix.

## What was inspected

Full detail and file citations are in the other six documents in this folder. In summary:
onboarding/launch, the account system (including in-app account deletion), OBD2 connectivity and
its empty/error states, DTC scan/clear, live data and module diagnostics, the AI diagnostic
feature and its backend, every external service and SDK the app actually calls, and the complete
Full PRO purchase/restore/entitlement/legal-links flow.

## Test and build results

- `dotnet build CarScanner.csproj -f net10.0-android -c Debug` — **0 errors** (ran twice during
  this engagement, both clean; Android, since no Mac is available here to build iOS).
- `dotnet test Tests/CarScanner.Tests` — run repeatedly through this engagement. **Final, current
  result: 2778 passed, 1 failed, 2779 total** (the 5-test increase from earlier runs is the new
  `SubscriptionProductMappingTests`). The one failure,
  `ObdbYearEvidenceTests.TheMileagePage_SaysWhenARecordedReplyExistsForTheModelYear`, predates this
  engagement entirely and is unrelated to subscriptions, icons, or App Review.
  One earlier full run (overlapping with other test/build commands still finishing in the
  background at the same time) additionally reported
  `BleTransportScenarioTests.Characterization_AtTheProtocolLayer_ALostLinkDuringACommand_SurfacesAsATimeoutAfterTheRetries`
  failing on a retry-count assertion — a timing-sensitive test under system load, not a code
  issue: it passed both alone (55/55) and in every other full run, including the final one above.
  Not caused by anything changed in this session (no Bluetooth/BLE/transport code was touched).
- iOS was not built or tested — no Mac/Xcode toolchain is available in this environment. The iOS
  code paths (StoreKit, Apple Sign-In) were verified by reading the source, not by building or
  running them.

## What you must do manually

See `manual-verification-checklist.md` for the complete list. The short version: confirm/create
the missing App Store Connect products, record the device video, decide on a reviewer test
account, and re-read the two reply documents to fill in their placeholders before resubmitting.

## Readiness assessment

**Not yet ready to resubmit.** Two things must happen first, neither of which is a code change:
(1) resolve the App Store Connect product-configuration gap (blockers #1–2), and (2) record the
physical-device video (blocker #3). Once those are done, the documentation in this folder directly
answers all seven of Apple's questions with verified, specific information, which should resolve
the Guideline 2.1 rejection itself.
