# OBD360 — Full PRO Subscription Compliance Audit

Subscription group per the rejection context: **"OBD360 PRO Plans"**, product **"Full PRO"**.
Every item is marked **PASS** (verified correct from code), **FAIL** (a confirmed, concrete
problem), or **NEEDS MANUAL VERIFICATION** (can only be checked in App Store Connect or on a
physical device). Prices/durations below are quoted directly from the app's own source constants
and its own published Terms of Use — nothing here is invented.

## Product catalog (as implemented in code)

`Services/ProService.cs` defines three subscription tiers, each with monthly and yearly product
IDs, plus one-time VIN-credit packs:

| Plan | Monthly product ID | Monthly price (fallback shown if store price unavailable) | Yearly product ID | Yearly price (fallback) |
|---|---|---|---|---|
| OBD PRO | `com.georgiancarscanner.pro.monthly` | 4.99 ₾ | `com.georgiancarscanner.pro.yearly` | 47.99 ₾ |
| VIN PRO | `com.georgiancarscanner.vin.monthly` | 5.99 ₾ | `com.georgiancarscanner.vin.yearly` | 57.99 ₾ |
| **Full PRO** | `com.georgiancarscanner.full.monthly` | 7.99 ₾ | `com.georgiancarscanner.full.yearly` | 67.99 ₾ |

Plus one-time VIN credit packs (`vin.pack5`, `vin.pack10`, `vin.single`) — not subscriptions.

## Update: yearly products exist in App Store Connect — investigation of the mapping instead

**The owner has confirmed the three yearly products already exist in App Store Connect.** The
code comment this audit originally quoted (*"Not yet created in App Store Connect"*) was stale and
has been corrected; it is no longer evidence of anything. This section was re-investigated as a
follow-up task specifically to find the real cause of a possible `PRODUCT_NOT_FOUND`.

### How a purchase actually requests a product (traced end to end)

1. `ProViewModel.ActiveProductId(plan)` (`ViewModels/AdvancedViewModels.cs`) picks
   `plan.YearlyProductId` when `IsYearlyBilling` is true and the plan has one, else
   `plan.ProductId`. Each `ProPlanItem` is constructed with its own matching pair — Full PRO gets
   `FullProductId`/`FullYearlyProductId`, VIN PRO gets `VinProductId`/`VinYearlyProductId`, OBD PRO
   gets `ObdProductId`/`ObdYearlyProductId`. **Confirmed correct** — no plan can end up requesting
   another plan's id, and the billing-period toggle correctly picks between the two.
2. That id is passed straight through `PurchaseService.BeginSubscriptionPurchaseAsync` →
   `PurchaseAppleAsync` → `iOSBillingService.PurchaseProductAsync` →
   `LoadProductAsync(productId, …)`.
3. `LoadProductAsync` (`Platforms/iOS/Billing/iOSBillingService.cs`) builds a **fresh
   `SKProductsRequest` for exactly that one id, on every call** — there is no pre-loaded, static
   product list the purchase flow depends on. Whatever id reaches this point is the id StoreKit is
   actually asked to resolve.

**Conclusion: the id-selection code path (steps 1–2) is correct, and the actual StoreKit request
(step 3) always carries the exact id the plan/billing-period combination implies.** There is no
code bug in this path that would make a *correctly configured* yearly product fail to load. A
`PRODUCT_NOT_FOUND` for a product that genuinely exists in App Store Connect, with the app
requesting the exact right id, would have to come from outside this code — most commonly an exact
string mismatch between the id in `Services/ProService.cs` and the Product ID as typed into App
Store Connect (including case — Apple Product IDs are case-sensitive), or the product not yet
being in a state StoreKit will resolve (e.g. still "Waiting for Review"/"Missing Metadata" rather
than "Ready to Submit"/"Approved"). **Neither of those can be checked from this repository** — see
"Exact identifiers to compare" below.

### 🟠 Two real, confirmed bugs found and fixed in the same area

Not the root cause of `PRODUCT_NOT_FOUND` (the purchase request itself was already using the right
id — see above), but both are genuine, confirmed mapping bugs around yearly products that this
task's own checklist asked to look for, so they're fixed:

1. **`PurchaseService.IsCovered(productId)`** only matched the three *monthly* constants in its
   `switch`; any yearly id fell through to `_ => false`. Effect: a user who already owns a plan via
   its yearly id never got the "PRO_ALREADY_ACTIVE" short-circuit when `BeginSubscriptionPurchaseAsync`
   was called again with that yearly id — the app would start a real purchase attempt (and, after
   it completed, the post-purchase `IsCovered(productId) ? "SUCCESS" : "ERROR:ENTITLEMENT_NOT_ACTIVE"`
   check would also wrongly report failure) instead of recognizing the existing entitlement. Fixed:
   each case now matches `MonthlyId or YearlyId`.
2. **`PurchaseService.SubscriptionProductIds`** (used by `GetPricesAsync()` to fetch real store
   prices for display) listed only the three monthly ids, so yearly plans could never show a real
   App Store price even if that code path were wired up. Fixed: all six ids are now listed.
   *(Separately noted, not fixed as part of this task since it's pre-existing and affects monthly
   and yearly equally: `GetPricesAsync()` itself is never actually called from `ProViewModel` —
   the `_prices` dictionary it would populate is always empty, so the PRO page currently always
   shows the hardcoded fallback price, never a live store price, for every plan. Apple's own
   purchase sheet still shows the real price before any charge, so this isn't a purchase-safety
   issue, but it's worth wiring up at some point.)*

Both fixes are in `Services/PurchaseService.cs`; see the final report for the complete file list
and test results.

### Exact identifiers to compare against App Store Connect

Since this audit has no App Store Connect access, here are the exact nine identifiers currently in
code (`Services/ProService.cs`) — please confirm each one, including case, matches ASC exactly:

| Plan | Monthly | Yearly |
|---|---|---|
| OBD PRO | `com.georgiancarscanner.pro.monthly` | `com.georgiancarscanner.pro.yearly` |
| VIN PRO | `com.georgiancarscanner.vin.monthly` | `com.georgiancarscanner.vin.yearly` |
| Full PRO | `com.georgiancarscanner.full.monthly` | `com.georgiancarscanner.full.yearly` |

Plus VIN credit packs (one-time, not subscriptions): `com.georgiancarscanner.vin.pack5`,
`com.georgiancarscanner.vin.pack10`, `com.georgiancarscanner.vin.single`.

For each, please also confirm its **status** in App Store Connect (Ready to Submit / Approved,
not Missing Metadata or Waiting for Review) and that it sits under the **"OBD360 PRO Plans"**
subscription group with bundle id `com.gcar360.app`.

### 🟡 Still needs manual verification: which *monthly* products exist in ASC

The original rejection context named only **"Full PRO"** as the configured subscription product.
Code defines three separate monthly products. If `com.georgiancarscanner.pro.monthly` or
`com.georgiancarscanner.vin.monthly` don't actually exist yet, those two plans' purchase buttons
would fail the same way, independent of the yearly investigation above.

## Pricing, duration, and disclosure

| Item | Status | Evidence |
|---|---|---|
| Subscription duration stated to users | **PASS** | App's own published Terms of Use (obd360.ge/terms-of-use, §3, fetched and read as part of this audit): *"PRO is a monthly auto-renewable subscription."* |
| Local/final price shown before purchase | **PASS** (by platform default) | iOS purchases go through StoreKit (`Platforms/iOS/Billing/iOSBillingService.cs`), which always shows Apple's own localized price confirmation sheet before charging — this is Apple's own UI, not something the app can misstate. |
| App does not hardcode a price into the purchase itself | **PASS** | `PurchaseService.BeginSubscriptionPurchaseAsync` always passes the real product id to StoreKit; the GEL figures in the table above are only a *display* fallback (`ProService.cs` comment: "Shown only when the store price cannot be loaded") used in the redesigned PRO page's UI, never the charged amount. |
| "Cancel anytime" or similar unsupported claim | **PASS (none made)** | The app's subscription info text says the subscription *"renews every month until you cancel it in the App Store"* — accurate, no unsupported claim. |
| Renewal/cancellation terms disclosed | **PASS** | Terms of Use §3, read directly: renews automatically until cancelled via the store account settings; a link to Apple's own subscription-management page is shown in-app (`StoreLinks.ManageSubscriptionUrl` → `https://apps.apple.com/account/subscriptions` on iOS). |

## Purchase flow

| Item | Status | Evidence |
|---|---|---|
| Real StoreKit integration (not a stub) | **PASS** | `Platforms/iOS/Billing/iOSBillingService.cs`, 652 lines, uses `StoreKit`'s product request and transaction APIs directly. |
| Server-side receipt verification (no locally-manufactured entitlement) | **PASS** | Every purchase result is sent to the backend (`POST /api/store/verify`) before the StoreKit transaction is finished (`PurchaseService.PurchaseAppleAsync`); the transaction is only marked complete once the server accepts it. `ProService` never sets `IsPro = true` from a purchase result alone — only from a server-verified entitlement load. |
| Interrupted / "Ask to Buy" purchases handled | **PASS** | `iOSBillingService.SetServerVerifier` + `ProcessUnfinishedAsync`, called both after a purchase and on app resume (`SynchronizeOwnedPurchaseAsync`), re-submits any transaction StoreKit redelivers. |
| Duplicate purchase prevented while one is in flight | **PASS** | `ProViewModel.BuyPlan` guards on `IsBusy`; the CTA button's `IsEnabled` is bound to `!IsBusy`. |
| Purchase error states are clear and distinct | **PASS**, with the one fix noted above | `PurchaseStatusPresentation` (`ViewModels/AdvancedViewModels.cs`) maps every StoreKit/server error code to a specific, localized message (cancelled, pending/Ask-to-Buy, already owned, product not found, verification failed, etc.) — not a generic failure string. |

## Restore Purchases

| Item | Status | Evidence |
|---|---|---|
| Restore Purchases available and functional on iOS | **PASS** | `PurchaseService.RestorePurchasesAsync` calls `iOSBillingService.RestoreAsync`, re-verifies every restored transaction with the server, then reloads the entitlement. Bound to a visible "Restore Purchases" button on the PRO page. |

## Entitlement / PRO access control

| Item | Status | Evidence |
|---|---|---|
| PRO status is server-authoritative | **PASS** | `ProService.LoadAsync`/`LoadServerEntitlementAsync` always treats the backend's `/api/entitlements/{id}` response as the source of truth; a cached local flag is only trusted when it was previously `VerifiedByServer`. |
| PRO features correctly gated | **PASS** | `AppShell.xaml.cs`'s `IsProFeatureRoute` intercepts navigation to AI, Mileage, Diagnostic Report, and Fuel pages and requires `EnsureProAsync()`. |
| Sign-out / account deletion correctly revokes local PRO | **PASS** | `ProService.OnAccountChanged` deactivates the local entitlement the moment the account signs out (with an explicit comment explaining why this check exists — see the file). |

## Legal links

| Item | Status | Evidence |
|---|---|---|
| Privacy Policy link present and working | **PASS** | `StoreLinks.PrivacyPolicyUrl` → `https://obd360.ge/privacy-policy`; fetched directly as part of this audit, loads real, dated (Oct 8, 2026) content in English and Georgian. |
| Terms of Use link present and working | **PASS** | `StoreLinks.TermsOfUseUrl` → `https://obd360.ge/terms-of-use`; fetched directly, loads real, dated (Oct 1, 2026) content covering the subscription terms quoted above. |
| Both links visible on the purchase screen | **PASS** | Present on the PRO page (`Views/ProPage.xaml`), as plain secondary buttons below the purchase CTA. |

## What I could not verify (needs you / App Store Connect / a device)

- Whether `com.georgiancarscanner.full.monthly` itself is correctly configured (price, display
  name, subscription group level, localization) in App Store Connect — I have no App Store Connect
  access.
- Whether OBD PRO and VIN PRO monthly products exist in ASC (see above).
- Whether the yearly products exist in ASC (confirmed from code that they did **not**, as of
  whenever that comment was written — verify current state).
- Actual on-device purchase/restore behavior — the code path is sound by inspection, but was not
  exercised on a real iPhone/sandbox account as part of this audit.
