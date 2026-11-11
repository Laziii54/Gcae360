# Accountless App Store purchases — architecture

**Status:** implemented in this working tree (not yet built for TestFlight, not yet submitted).
**Reason:** App Review rejected build 56 under Guideline 5.1.1(v) — a subscription could not be
bought without registering an OBD360 account.

This document describes what the code now does, so the review reply and any future change start
from the same facts. Nothing here claims an App Store Connect or Apple-side change was made.

---

## 1. The two entitlement sources

`Services/ProService.cs` is the only place the app decides whether a PRO feature is unlocked. It
combines two sources and never lets one overwrite the other:

| Source | Where it comes from | When it applies | Cleared by |
|---|---|---|---|
| **OBD360 account** | `/api/entitlements/{accountId}` (website purchase, account-linked store purchase, admin/promo plan) | only while signed in | sign-out, account deletion |
| **Accountless App Store** | `/api/store/apple/anonymous` with an opaque session token in the iOS keychain | always on iOS, signed in or not | uninstall, `DeactivateAsync`, an expired/revoked subscription |

`HasObd = IsPro && ServerVerified`, and `ServerVerified` is true only when the backend confirmed at
least one source. A cached value is trusted only while it is unexpired **and** was written from a
server-verified answer — never from a local boolean alone.

Signing out (`ProService.OnAccountChanged` → `ClearAccountEntitlementAsync`) clears the account
fields, clears the `pro.hasVin`/`pro.vinExpires` preferences, and leaves `_appleObd` / `_appleVin` /
the keychain token untouched. Full reset (`DeactivateAsync`) clears both, including the token.

Each installation keeps its own row here, so two devices on the same Apple Account are both active
at the same time — see §4 "Ownership".

## 2. Client flow — purchase

```
PRO screen → tap a plan
   → PurchaseService.BeginSubscriptionPurchaseAsync(plan.ProductId)
   → iOSBillingService.PurchaseProductAsync  (StoreKit 1, cached SKProduct)
   → Apple's purchase sheet
   → PurchaseService.PurchaseAppleAsync
   → SyncWithServerAsync
        → AppleEntitlementService.VerifyAsync      (no account, no bearer token)
             → POST /api/store/apple/anonymous/verify
        → ProService.ApplyAppleEntitlementAsync    (publish + cache)
   → transaction FINISHED only if the backend accepted it
```

No step in that chain reads `AccountService.IsSignedIn`. The only purchase that still needs an
account is a **VIN credit pack**, because credits are counted per account: the app says so and
offers to sign in instead of charging.

If an older app version already linked the same transaction to an OBD360 account, the accountless
call answers `LINKED_TO_ACCOUNT`; while that account is signed in the app falls back to the
account-scoped `/api/store/verify`, so the purchase keeps working and never changes owner.

## 3. Client flow — Restore Purchases

```
Restore Purchases → PurchaseService.RestoreDetailedAsync
   → SKPaymentQueue.RestoreCompletedTransactions (all periods, all products)
   → each restored transaction → the same SyncWithServerAsync above
   → ProService.RefreshAsync
   → RestoreResult.Restored | NothingToRestore | Unreachable |
                    VerificationFailed | Expired | Revoked | LinkedToAccount | Unsupported
```

Each outcome has its own user-facing text (`ProViewModel.RestorePresentation`). No exception is
swallowed and there is no sign-in prompt.

## 4. Backend — the accountless endpoints

`Backend/CarScanner.Api/Program.cs`:

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/store/apple/anonymous/verify` | none (rate-limited) | verify one StoreKit transaction and bind it to an installation |
| POST | `/api/store/apple/anonymous/refresh` | `X-GCS-Anon-Token` | re-read renewal / expiry / revocation from Apple |
| GET | `/api/store/apple/anonymous` | `X-GCS-Anon-Token` | current entitlement, no Apple round-trip |

Unchanged and still bearer-token-only: `POST /api/store/verify` (account-linked purchases and the
website), `GET /api/entitlements/{userId}`.

### Verification is the existing implementation, reused

The accountless path calls the same `StoreVerificationService` the account path uses — App Store
Server API with an ES256 JWT, production + sandbox hosts, bundle-id check, transaction lookup,
`/inApps/v1/subscriptions/{originalTransactionId}` for the current state, revocation and
grace-period handling, and the existing App Store Server Notifications V2 webhook. The product and
expiry always come from Apple's answer; the client's `productId` is only a hint.

### Ownership

`EntitlementStore.PurchaseClaims` already allowed exactly one owner per store transaction. An
accountless owner is namespaced:

```
anon:<32 hex chars>
```

so it can never collide with a real account id and can never be resolved by an account endpoint
(`EntitlementStore.IsAnonymousOwner`).

- A transaction that already belongs to an **OBD360 account** is never transferred. The endpoint
  answers `409 { code: "LINKED_TO_ACCOUNT" }` and the app tells the user to sign in to that account.
- An **accountless** subscription belongs to the customer's **Apple Account**, not to a device. The
  same verified subscription is legitimately used on every iPhone/iPad signed in to that Apple
  Account, so the relationship is **additive and many-to-many**
  (`StoreEntitlementLinks(Source, SubscriptionKey, OwnerId)`): the device that bought it, a restore
  on a second device, and a reinstall each get their own owner + their own session + their own
  `Subscriptions` row for the same subscription key.

  **`SubscriptionKey` is Apple's `originalTransactionId` — never a per-transaction id.** Apple uses
  two identifiers and they are not interchangeable: every purchase, restore and renewal has its own
  `transactionId`, while the subscription keeps one `originalTransactionId` for its whole life.
  `VerifyAppleAsync` resolves the key from Apple's `originalTransactionId` (the client's hint is
  accepted only when it agrees with Apple), `VerifiedPurchase.SubscriptionKey` names it explicitly,
  and the same value is written to `Subscriptions.TransactionId` so a revocation by key matches the
  row. Keying on the per-transaction id would give each device its own "subscription" and would stop
  renewals and refunds from reaching both.

  **Restoring on one device never revokes another.** Only `PurchaseClaims` (account ownership) has
  the one-owner rule; accountless links deliberately do not use it, and the anonymous verify path
  never writes to it. `transactionId` is still used where it belongs: as the idempotency key for a
  one-time (consumable) purchase, so a VIN pack can never be credited twice.

- Expiry and refund are applied to **every** linked installation. The
  `/api/store/apple/notifications` handler resolves the subscription's linked owners and applies
  Apple's state to all of them, and each device's `/refresh` re-reads Apple's answer anyway.

### The session token

- 32 random bytes, base64url, generated server-side.
- The client keeps it in `SecureStorage` (iOS keychain) under `apple_store_session_token`.
- Only `SHA-256(token)` is stored, in `AnonymousStoreSessions.TokenHash`. The database alone cannot
  be used to read an entitlement, and a token cannot be derived from a transaction id.
- `POST …/verify` accepts an optional token so a renewal/upgrade lands on the entitlement the
  installation already has instead of creating a new owner.

### Abuse limits

- `apple-anon-verify-ip` — 40 attempts / 15 min per IP.
- `apple-anon-verify-owner` — 30 / 15 min per owner.
- `apple-anon-refresh` — 30 / 5 min per owner.
- A consumable (VIN pack) is refused with `409 ACCOUNT_REQUIRED`; credits are never granted to an
  accountless owner.
- Nothing is granted from a client claim: every grant is preceded by an Apple round-trip.

## 5. Prices

`PurchaseService.GetPricesAsync()` → `iOSBillingService.GetLocalizedPricesAsync(AllProductIds)`:

- one `SKProductsRequest` for the whole set, cached for 10 minutes per product id;
- the cached `SKProduct` object is the same one the purchase request uses, so the displayed price
  always belongs to the product being bought;
- `NSNumberFormatter` with `product.PriceLocale`, so the currency follows the reviewer's storefront;
- a product StoreKit does not resolve is simply absent — the card shows "—" and the screen offers
  "Reload price". There is no compiled-in fallback amount anywhere (`ProService` has no price
  constants at all).

Android has no billing of its own, so it shows the price the **website catalog** would charge
(`GET /api/web/prices`, lari in Georgia and US dollars elsewhere — the same rule the checkout uses).
The page renders identically on both platforms; only the source of the number differs, and neither
platform ever shows an amount that did not come from its own authoritative catalog.

## 6. Website (unchanged behaviour, one new switch)

`GET /pro` accepts `source=android`, which renders **monthly plans only** and omits the period
toggle. The Android app appends it (`StoreLinks.ProUrlFor(email, fromAndroidApp: true)`). An
independent visitor still sees monthly **and** yearly, with the same bank checkout as before.

## 7. What still needs a human

- App Store Connect product prices/levels (see `manual-verification-checklist.md`).
- A TestFlight purchase + restore on a real device with a sandbox Apple Account.
- The physical-device recording Apple asked for.

## 8. Backend configuration this depends on

Read from configuration/environment; **no secret values belong in this repository**:

| Setting | Purpose |
|---|---|
| `Stores:Apple:IssuerId` / `GCS_APPLE_ISSUER_ID` | App Store Server API JWT issuer |
| `Stores:Apple:KeyId` / `GCS_APPLE_KEY_ID` | App Store Server API key id |
| `Stores:Apple:PrivateKeyP8` / `GCS_APPLE_PRIVATE_KEY_P8` | App Store Server API private key |
| `Stores:Apple:BundleId` / `GCS_APPLE_BUNDLE_ID` | must be `com.gcar360.app` |
| `Stores:Apple:Environment` / `GCS_APPLE_ENVIRONMENT` | `auto` (production, then sandbox) |
| `GCS_APPLE_ROOT_CA_SHA256` | optional override of the pinned Apple Root CA G3 fingerprint |

If those are unset, `CreateAppleApi()` returns null, every verification fails, and the app reports
`ERROR:SERVER_VERIFICATION_FAILED`. That is a configuration problem, not a code problem — check it
first if a purchase "does nothing" on production.

No database migration is required to deploy this: two new tables
(`AnonymousStoreSessions`, plus the existing `PurchaseClaims`/`Subscriptions`) are created with
`CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS` at startup, and existing rows are
untouched.
