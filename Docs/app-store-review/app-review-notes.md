# App Review Information — Notes field

Paste (and trim if needed) into App Store Connect's "App Review Information → Notes" field for
this submission.

---

OBD360 is a vehicle diagnostic app that connects to an external OBD2 adapter over Bluetooth LE or
Wi-Fi. **No account is required to launch the app, browse it, or buy the PRO subscription.**

## No sign-in is required to review this app

The app opens directly into its main interface. There is no login wall, no "continue as guest"
selection, and no registration step before any purchase.

**Apple subscriptions can be purchased without an OBD360 account.** On the PRO screen, tapping a
plan goes straight to Apple's own purchase sheet. The App Store verifies who the customer is; our
backend then verifies the Apple transaction with the App Store Server API and unlocks the features.
**Restore Purchases also works with no OBD360 account** — it restores from the Apple Account alone.

An OBD360 account is optional and only ever needed for the separate website subscription
(obd360.ge); it is not needed for anything Apple sells inside the app.

## How to review without a car or adapter

The home screen shows "OBD scanner" and "VIN Check". VIN Check (vehicle history lookup by VIN)
works fully without any hardware — it needs only a VIN and an internet connection. The OBD scanner
flow requires a connected OBD2 adapter and a vehicle; without one it shows a clear "adapter not
found" state rather than a blank or broken screen.

A screen recording of the physical adapter workflow is attached separately (see the reply letter).

## Main features

OBD2 trouble-code scan/clear, live sensor data, module-level diagnostics for supported vehicles,
AI-assisted explanation of results, fuel/service tracking, and VIN history lookup.

## In-app purchase — exactly what can be bought

From the PRO screen (also offered when a PRO-gated feature is opened) there are **three
auto-renewable monthly subscriptions** in the "OBD360 PRO Plans" group:

| Plan | What it unlocks | Product ID |
|---|---|---|
| OBD PRO | Diagnostics (all-module scan, live data, AI, pre-purchase check) | `com.georgiancarscanner.pro.monthly` |
| VIN PRO | VIN history reports | `com.georgiancarscanner.vin.monthly` |
| Full PRO | Both of the above | `com.georgiancarscanner.full.monthly` |

Plus one-time (consumable) VIN report credit packs, which are only offered to a signed-in account
because the credits are counted per account.

There is **no monthly/yearly selector and no annual plan in the app** — three monthly plans only.
The price shown on each card is the price Apple's StoreKit returns for that exact product in the
reviewer's own storefront; if StoreKit returns nothing, the app shows "—" and a reload button
rather than any other number. Apple's purchase sheet always shows and confirms the final price.

## Renewal and cancellation

The subscription renews automatically every month until cancelled in the App Store. The same
statement is on the PRO screen, next to links to the Privacy Policy and the Terms of Use, and
Settings → Manage Subscription opens Apple's own subscription management page.

## Sign-in options (all optional)

E-mail/password, Google, and Sign in with Apple. Because Google Sign-In is offered, Sign in with
Apple is offered beside it on iOS (Guideline 4.8). None of them is required to buy or restore an
Apple subscription.

- Apple In-App Purchase access is based on the verified Apple transaction.
- Website subscription access is based on the OBD360 account that bought it, after signing in.

## If a reviewer account would help

One is not required for the purchase flow. If you would like to see the account-only surfaces
(website subscription status, cross-device sync, account deletion), we can supply a dedicated
reviewer account on request — it is created and configured on our side and is **not** published in
this field.

## Regions

Core functionality is identical worldwide, not gated by region. In-app purchase pricing follows the
App Store's own per-territory localized pricing.
