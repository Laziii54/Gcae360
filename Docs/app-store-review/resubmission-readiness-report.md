# OBD360 — Apple App Review Resubmission Readiness Report

**Build being prepared:** 1.1.1 (csproj currently 67 — bump the build number for the upload)
**Rejection being answered:** submission `1509192a-5394-44f0-8d1c-0afc68fb50e3`, build 56,
Guideline **5.1.1(v)** (registration required before a non-account-based purchase).

---

## Executive summary

The rejection was valid, and this change fixes the cause rather than the wording around it: an
App Store subscription can now be bought, verified, activated, kept across restarts and restored
**with no OBD360 account at all**. The account still exists for the website subscription; it is no
longer a gate in front of anything Apple sells.

Three other things that would very likely have been the next rejection were fixed in the same pass,
because they sit in the same screen:

1. **Prices were hard-coded Lari amounts**, with a struck-through "was" price and a percentage
   discount. The PRO page now shows only the localized price StoreKit returns, and shows "—" with a
   reload button when it returns none.
2. **The Monthly/Yearly selector offered three yearly products** whose App Store Connect state was
   contradictory in the repository. Both apps now sell **three monthly plans only**.
3. **The iOS PRO page displayed a website subscription price and a website cancel button.** On iOS
   it now shows status and renewal date only.

The code is ready. What remains is manual: App Store Connect product/price/level configuration, a
TestFlight sandbox purchase test, and the physical-device recording Apple asked for.

## What changed (code)

Full detail: `accountless-purchase-architecture.md`. Summary:

| Area | Change |
|---|---|
| Purchase gate | `ProViewModel.OpenPlan` no longer checks `IsSignedIn` on iOS and no longer routes to the Account page. |
| Backend | New `/api/store/apple/anonymous/{verify,refresh}` + `GET /api/store/apple/anonymous`. Same `StoreVerificationService` (App Store Server API) as before. |
| Ownership | `EntitlementStore.AnonymousStoreSessions` holds only SHA-256 hashes of opaque tokens; `anon:` owners cannot collide with accounts; account-linked transactions are never transferred. |
| Restore | `PurchaseService.RestoreDetailedAsync` returns one of eight statuses; no sign-in prompt; no swallowed exceptions. |
| Entitlement | `ProService` keeps account and Apple sources separately; sign-out clears only the account source; `DeactivateAsync` clears both. |
| Prices | `PurchaseService.GetPricesAsync` is now actually called; one batched, cached `SKProductsRequest`; no price constant exists in `ProService` any more. |
| Plans | Yearly selector, yearly cards and all yearly ids removed from the mobile purchase paths. |
| iOS web card | No external price, no external cancel CTA. |
| Website | `/pro?source=android` renders monthly plans only; the independent page is unchanged and still sells monthly + yearly. |
| Android | Unchanged apart from the same three-monthly-plans PRO screen and the `source=android` marker. No Google Play Billing was added. |

## Test and build results (actually run in this environment)

| Command | Result |
|---|---|
| `dotnet build Backend/CarScanner.Api/CarScanner.Api.csproj` | **succeeded**, 0 errors |
| `dotnet build CarScanner.csproj -f net10.0-android -c Debug` | **succeeded**, 0 errors, 151 warnings (pre-existing: AAPT2 merge notes, one nullable warning in `Mileage/CatalogOdometerProvider.cs`) |
| `dotnet build CarScanner.csproj -f net10.0-ios -c Debug` | **succeeded**, 0 errors, 5 warnings — this compiles the StoreKit and Apple Sign-In code on Windows; it does **not** produce a signed IPA |
| `dotnet test Tests/CarScanner.Tests` | **2838 passed, 0 failed, 0 skipped** (~2 min) |

Previous baseline in this repository was 2779 tests. The suite grew by 59: the rewritten
`SubscriptionProductMappingTests` and `ProServiceLogoutTests`, a new `AccountlessApplePurchaseTests`
(real `EntitlementStore` against a throw-away SQLite database, including the multi-device and
distinct-transaction-id cases) and a new `AccountlessIosFlowTests`.

**iOS signing/archiving was not performed** — it requires macOS and Xcode. Nothing was uploaded,
submitted, pushed to GitHub, or deployed to Azure.

## New tests that matter for this rejection

- Buying on iOS contains no `IsSignedIn` check and no Account-page navigation.
- Restore contains no Account-page navigation and maps every failure to a message.
- One Apple subscription works on several devices at once: iPhone A buys, iPhone B restores, both
  stay active, and B's restore does not revoke A. A reinstall restores too.
- The account-claim table still allows exactly one owning account; an accountless subscription is
  never claimed by an account, and an account-owned one is never linked to an installation.
- A refund/expiry notification revokes access on every linked installation.
- The raw session token is never in the database (only its SHA-256 hash).
- VIN PRO unlocks VIN only, OBD PRO unlocks OBD only, Full PRO unlocks both; an expired row unlocks
  nothing; an account entitlement and an Apple entitlement do not overwrite each other.
- `SubscriptionProductIds` / `AllProductIds` contain no yearly id; the PRO page has no period
  toggle, no strike-through and no discount badge; `ProService` has no price constant.

## Critical blockers before resubmitting (all manual)

1. **App Store Connect products** — confirm the three monthly ids exist, are auto-renewable,
   "Ready to Submit", and priced at $1.99 / $1.99 / $2.99 (or record what they actually are; the app
   shows Apple's number, so a wrong price is now visible rather than hidden).
2. **Subscription levels** — FULL PRO highest, OBD PRO and VIN PRO on the same level; yearly
   variants (if kept in the group) on the matching level. See `manual-verification-checklist.md`.
3. **Sign in with Apple** — App ID capability + regenerated profile, so the TestFlight build shows
   Google *and* Apple.
4. **Physical-device recording** — `physical-device-recording-checklist.md`; nothing was recorded.
5. **Backend credentials** — `Stores:Apple:*` on the production App Service.
6. **Sandbox purchase + restore on a real iPhone**, signed out, including a reinstall.

## Readiness assessment

**Technically ready; not yet submittable.** Every code-side cause of the 5.1.1(v) rejection is
fixed and covered by tests, and both platform targets compile. Submission still depends on the six
manual items above. Nothing here guarantees Apple's approval.
