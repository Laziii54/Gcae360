# OBD360 — Subscription Compliance Audit (current build)

This supersedes the previous version of this file, which described a Monthly/Yearly selector, three
yearly products offered in the app, hard-coded Lari prices and a mandatory sign-in before purchase.
All four of those are gone. Everything below is marked **PASS** (verified from the code in this
working tree), **FAIL**, or **NEEDS MANUAL VERIFICATION** (only checkable in App Store Connect or on
a device).

Rejection being answered: **5.1.1(v)** — build 56 required registration before purchasing a
non-account-based subscription.

---

## Product catalog (mobile)

`Services/ProService.cs` — monthly only. `PurchaseService.SubscriptionProductIds` and
`PurchaseService.AllProductIds` contain no yearly id at all, so no yearly product can be requested
from StoreKit by the app.

| Plan | Product ID | Grants |
|---|---|---|
| OBD PRO | `com.georgiancarscanner.pro.monthly` | `GrantsObd`, not `GrantsVin` |
| VIN PRO | `com.georgiancarscanner.vin.monthly` | `GrantsVin`, not `GrantsObd` |
| Full PRO | `com.georgiancarscanner.full.monthly` | both |

Consumables kept (unchanged): `…vin.pack5`, `…vin.pack10`, `…vin.single` (the app offers the +5 and
+10 packs only, and only to a signed-in account).

Yearly ids still exist as constants because a **website** yearly purchase is a real entitlement the
app must understand (`ProductCatalog.GrantsObd/GrantsVin` on the backend). They are never sold in
the app.

## Purchase flow

| Item | Status | Evidence |
|---|---|---|
| Real StoreKit (not a stub) | **PASS** | `Platforms/iOS/Billing/iOSBillingService.cs` — `SKProductsRequest`, `SKMutablePayment`, transaction observer registered at launch in `AppDelegate`. |
| Buying needs no OBD360 account | **PASS** | `ProViewModel.OpenPlan` goes straight to `BuyPlan` on iOS; there is no `IsSignedIn` check and no `AccountPage` navigation in that branch. Pinned by `AccountlessIosFlowTests.BuyingOnIos_DoesNotRequireSigningIn`. |
| Purchase is verified server-side | **PASS** | `PurchaseService.SyncWithServerAsync` → `AppleEntitlementService.VerifyAsync` → `POST /api/store/apple/anonymous/verify` → `StoreVerificationService` (App Store Server API). |
| Transaction finished only after acceptance | **PASS** | `PurchaseAppleAsync` calls `CompleteTransaction(transactionId, accepted)` in `finally`; `accepted` is the server's answer. |
| Ask to Buy / deferred | **PASS** | `SKPaymentTransactionState.Deferred` → `"PENDING"`; background verifier on launch/resume. |
| Duplicate purchase blocked while in flight | **PASS** | `ProViewModel.BuyPlan` guards `IsBusy`; the CTA binds `!IsBusy`. |
| Product cannot change mid-purchase | **PASS** | The purchase uses `plan.ProductId` captured by the command; there is no billing-period toggle that could swap it. |
| Every error has a distinct message | **PASS** | `ProViewModel.PurchaseStatusPresentation` — cancelled, pending, already owned, product not found, linked-to-account, expired, revoked, verification failed. |

## Restore Purchases

| Item | Status | Evidence |
|---|---|---|
| Available and functional | **PASS** | `PurchaseService.RestoreDetailedAsync` + the "Restore Purchases" button on the PRO screen. |
| Works with no OBD360 account | **PASS** | No sign-in check; each restored transaction goes through the accountless verify path. Pinned by `AccountlessIosFlowTests.RestorePurchases_DoesNotRedirectToLogin`. |
| Distinguishes outcomes | **PASS** | `RestoreStatus` — Restored / NothingToRestore / Unreachable / VerificationFailed / Expired / Revoked / LinkedToAccount / Unsupported, each with its own text (`RestorePresentation`). |
| Exceptions not swallowed | **PASS** | Every branch maps to a status; only `Debug.WriteLine` is used for logging. |
| Existing account-linked purchases still work | **PASS** | `LINKED_TO_ACCOUNT` → falls back to `/api/store/verify` while signed in; the claim is never transferred away from the account. |

## Pricing and disclosure

| Item | Status | Evidence |
|---|---|---|
| Price comes from the store | **PASS** | `ProViewModel.StorePrice` reads only the `GetPricesAsync` result; `ProService` has **no** price constant left (pinned by a test). |
| Localized currency | **PASS** | `NSNumberFormatter` with `product.PriceLocale`. |
| Displayed product == purchased product | **PASS** | One cached `SKProduct` per id; the purchase reuses it (`LoadProductsAsync([productId])`). |
| No invented fallback price | **PASS** | Missing price → `—` + "Reload price"; `ShowPriceRetry` only after a real load attempt. |
| Android shows a real price too | **PASS** | `GET /api/web/prices` returns the website's own catalog (GEL in Georgia, USD elsewhere); the Android PRO page renders the identical layout with that number. |
| No fake anchor / discount | **PASS** | `DisplayStrikeText`, `ShowDisplayStrike`, `AnchorPriceText`, `DiscountBadgeText` removed from the model and from `ProPage.xaml`. |
| Duration disclosed correctly | **PASS** | Monthly only, so the disclosure can never contradict the selection: "renews automatically every month until you cancel it in the App Store (Settings → Apple Account → Subscriptions)". |
| Privacy Policy + Terms on the purchase screen | **PASS** | `ProPage.xaml`, `StoreLinks.PrivacyPolicyUrl` / `TermsOfUseUrl`. |
| Manage Subscription reachable | **PASS** | Settings page → `StoreLinks.ManageSubscriptionUrl` → `apps.apple.com/account/subscriptions` on iOS. |

## Out-of-app purchase content (3.1.1 / 3.1.3(b))

| Item | Status | Evidence |
|---|---|---|
| No website purchase link on iOS | **PASS** | `ProViewModel.OpenPlan` / `BuyPack` only reach `obd360.ge` off iOS. |
| No website price or cancel CTA on iOS | **PASS** | `ProViewModel.WebSubscription.ApplyWebSubscription`: `externalDetailsAllowed = !CanBuyInApp` gates both the price and `CanCancelWebSub`. Status and renewal date remain (that is management of an existing entitlement, not a purchase offer). |
| Paid full-VIN checkout hidden on mobile | **PASS** (unchanged) | `VinReportViewModel.ExternalCheckoutAllowed => false` under `#if ANDROID || IOS`. |
| No external purchase CTA in VIN flows | **PASS** (unchanged) | Quota prompts are plain alerts. |

## Entitlement / access control

| Item | Status | Evidence |
|---|---|---|
| Server-authoritative | **PASS** | `ProService` writes a cached value only from a server answer; `HasObd = IsPro && ServerVerified`. |
| Two sources combined, not overwritten | **PASS** | `ProService` keeps account and Apple flags separately; `ProServiceLogoutTests` pins the sign-out behaviour. |
| Sign-out keeps an App Store subscription | **PASS** | `ClearAccountEntitlementAsync` clears account fields only. |
| Sign-out clears an account entitlement | **PASS** | Same method clears `_accountObd`, the VIN preferences and the cached row. |
| Full reset clears both | **PASS** | `DeactivateAsync` also forgets the accountless session token. |
| One owner per store transaction (account rule) | **PASS** | `EntitlementStore.PurchaseClaims` — an account claim is exclusive; accountless installations never write to it. Verified by `AccountlessApplePurchaseTests`. |
| One Apple subscription on several devices | **PASS** | `StoreEntitlementLinks` is additive: iPhone A and iPhone B both link to the same subscription key and both stay active; restoring on B never revokes A. Verified by `IPhoneB_RestoresTheSameSubscription_AndBothDevicesKeepPro` and `RestoringOnB_DoesNotRevokeA`. |
| Link key is Apple's `originalTransactionId` | **PASS** | `VerifyAppleAsync` resolves `subscriptionKey` from Apple's `originalTransactionId`; `VerifiedPurchase.SubscriptionKey` names it; the endpoint links and stores that same value. Pinned by `DifferentTransactionIds_SameOriginalTransactionId_ShareOneSubscription` and `SubscriptionOwnership_IsKeyedByOriginalTransactionId_NotByTheTransactionId`. |
| Reinstall restores access | **PASS** | A reinstall is simply a new owner linked to the same subscription; pinned by `Reinstalling_AllowsRestoration`. |
| Refund/expiry reaches every device | **PASS** | The Apple notification handler applies the state to every linked owner; pinned by `ARenewalOrRefundNotification_ReachesEveryLinkedInstallation`. |
| Expired / revoked removes access | **PASS** | `ApplyApple` requires an unexpired date; `/refresh` re-reads Apple state and deactivates on EXPIRED/REVOKED. |

## Sign-in services (4.8)

| Item | Status | Evidence |
|---|---|---|
| Google Sign-In offered | **PASS** | `ExternalSignInService.GetGoogleCredentialAsync` (OAuth + PKCE). |
| Sign in with Apple offered beside it on iOS | **PASS (code)** / **NEEDS MANUAL VERIFICATION (build)** | `ShowGoogle` is false on iOS unless `ShowApple`, which requires the `APPLE_SIGNIN` define from `-p:EnableAppleSignIn=true`; `codemagic.yaml` sets it. The App ID capability and profile must be confirmed by hand. |
| Account deletion exists | **PASS** (unchanged) | `AccountPage` → Delete account. |
| Normal e-mail OTP unchanged | **PASS** | No bypass was added; the login code flow is untouched. |

## Needs manual verification (cannot be checked here)

- Whether each of the three monthly products exists, is auto-renewable and is "Ready to Submit" in
  App Store Connect, and the price it actually returns (intended: $1.99 / $1.99 / $2.99).
- The subscription group's level ordering (FULL PRO highest; OBD PRO and VIN PRO on the same level).
- Whether the App ID `com.gcar360.app` has the Sign in with Apple capability and a regenerated
  profile.
- Actual sandbox purchase / restore / renewal / refund behaviour on a device.
- `Stores:Apple:*` credentials present on the production App Service.
