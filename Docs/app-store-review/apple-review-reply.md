# Response to App Review — build 68 (1.1.2)

**App:** OBD360 · **Bundle ID:** `com.gcar360.app` · **App Store Connect Apple ID:** 6817508639
**Previous submission:** `1509192a-5394-44f0-8d1c-0afc68fb50e3` (build 56, rejected 10 Nov 2026)
**Subscription group:** OBD360 PRO Plans (3 monthly products; the yearly products are removed)

Thank you for the review. Both issues are addressed in this build — the purchase flow was changed,
not explained away, and the demo video you asked for is linked below.

---

## Guideline 2.1 — demo video

A demo video of the current build on a physical Apple device, paired with the OBD2 adapter, is
available here:

**▶ [DEMO VIDEO LINK — paste the shareable link here]**

It shows, in one continuous recording on a physical iPhone (not a simulator):

1. Launching OBD360 and reaching the main screen with no sign-in.
2. Pairing the app with a physical ELM327-compatible Bluetooth OBD2 adapter plugged into the
   vehicle's OBD2 port, ignition on — the pairing dialogue, the connection step and the adapter
   being selected.
3. The full workflow with that hardware: live sensor data, a trouble-code scan on the connected
   vehicle, module-level diagnostics, and VIN read from the vehicle.

The same recording also shows a purchase and a Restore Purchases with no account, which covers the
5.1.1(v) section below.

---

## Guideline 5.1.1(v) — Legal: Data Collection and Storage

> The app requires users to register before purchasing In-App Purchase products that are not
> account based.

That was true of build 56: tapping a plan while signed out sent the reviewer to a Create Account
screen, and the backend accepted a purchase only when it was tied to an OBD360 account.

### What changed

**1. The app opens straight into its main interface.** No login screen, no "continue as guest"
choice, no registration step anywhere in the purchase path. The app's first screen is the OBD360
main screen.

**2. An Apple subscription is bought with no OBD360 account.** Tapping a plan opens Apple's purchase
sheet immediately. When the transaction completes, our backend verifies it against the App Store
Server API (transaction lookup, subscription status, bundle id, expiry, revocation) and the app
verifies a short-lived signed entitlement token issued by that backend. Registration is not a
prerequisite and is never requested in that flow. The PRO screen states this explicitly:
*"PRO unlocks right away — no OBD360 account needed."*

**3. Restore Purchases works with no OBD360 account.** StoreKit returns the Apple transactions, the
backend verifies them the same way, and PRO is reactivated. No sign-in prompt, no redirect to
registration.

**4. One subscription, all of the customer's devices.** The subscription belongs to the customer's
Apple Account, as Apple intends. Restoring it on a second iPhone or iPad gives that device its own
independent, verified entitlement and does **not** revoke the first — there is no device transfer
and no "one device at a time" rule. A reinstall restores the same way.

**5. Nothing is granted from a local flag.** A purchase is only completed after the server accepted
it, and an unfinished transaction is retried on the next launch or resume. The previous build
trusted local database values; that path is removed, and the old values are cleared on first launch
of this build.

### About the VIN credit packs

VIN credit packs (+5 / +10 / single report) are **account-based by nature**: a pack is a balance of
VIN reports that follows the customer across devices and across platforms (iOS, Android, website),
and each report after the first month is deducted from that balance on our server. They are not a
content unlock that could be restored from the Apple Account alone, so they are tied to an OBD360
account — which is optional and only needed if the customer chooses to buy that particular product.
Subscription purchases and Restore Purchases never require it.

### What the reviewer can verify in five minutes

| Step | Expected result |
|---|---|
| Launch the app | Main screen opens; no sign-in |
| Open PRO | Three monthly plans with Apple-localized prices; no yearly toggle |
| Tap a plan | Apple's purchase sheet opens immediately |
| Complete or cancel | No account screen appears at any point |
| Tap "Restore Purchases" | StoreKit restore runs without a sign-in prompt |
| Sign in (optional, any time) | Only then does an OBD360 account become relevant, for the website plan and cross-device sync |

---

## Still true from our earlier reply (functionality summary)

**Purpose.** OBD360 turns a phone and a common ELM327-compatible OBD2 adapter into a diagnostic
tool: trouble codes with explanations, live sensor data, module-level diagnostics on supported
vehicles, AI-assisted explanation of results, VIN history lookup, and service/maintenance tracking.

**Setup.** A commercially available Bluetooth LE or Wi-Fi OBD2 adapter connected to the vehicle's
OBD2 port, ignition on. No special hardware.

**Reviewing without hardware.** VIN History works with only a VIN and an internet connection. It is a
row at the end of the TECHNICAL section of the main screen. The OBD scanner flow needs an adapter
and a vehicle; without one it shows a clear "adapter not found" state.

**External services.** Our own HTTPS backend (accounts, VIN data, AI, purchase verification,
crash/usage telemetry — all first-party); Google Gemini called server-side only; Sign in with Apple
and Google Sign-In; Apple StoreKit. No third-party analytics or crash SDK. Full detail with file
citations: `external-services-inventory.md`.

**Regions.** Functionality is identical worldwide. In-app purchase pricing follows the App Store's
own per-territory localized pricing; the app does not configure it.

**Licensing.** VIN history draws on licensed third-party automotive-data providers through our own
backend. The app never calls them directly.

---

## Accounting and disclosure on the PRO screen

Plan name, the StoreKit-localized monthly price, the monthly billing period, the auto-renewal
statement, and links to the Privacy Policy and Terms of Use are all on the purchase screen, above
or beside the purchase button. There is no struck-through "was" price and no percentage discount.

---

An OBD360 account is now genuinely optional: it exists for the separate website subscription at
obd360.ge, for VIN credit packs, and for optional cross-device sync. It is never required to
purchase, restore or use the app. Thank you for your patience.
