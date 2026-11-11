# Response to App Review — Guideline 5.1.1(v) (build 56)

**App:** OBD360 · **Bundle ID:** `com.gcar360.app` · **App Store Connect Apple ID:** 6817508639
**Submission:** `1509192a-5394-44f0-8d1c-0afc68fb50e3` · **Subscription group:** OBD360 PRO Plans

Thank you for the review. You are right, and this build changes the purchase flow rather than
explaining it away.

## The issue you raised

> The application requires users to register with personal information before purchasing
> subscriptions that are not account-based.

That was correct for build 56: tapping a plan while signed out sent the user to a Create Account
screen, and our backend only accepted a purchase tied to an OBD360 account.

## What changed in this build

**1. The app opens straight into its main interface.** There is no login screen, no "continue as
guest" choice and no registration step anywhere in the purchase path.

**2. An Apple subscription can be bought with no OBD360 account.** Tapping a plan opens Apple's own
purchase sheet immediately. After the purchase completes, our backend verifies the transaction
against the App Store Server API (transaction lookup, subscription status, bundle id, expiry,
revocation) and unlocks the features. Registration is not a prerequisite and is never requested in
that flow.

**3. Restore Purchases works with no OBD360 account.** StoreKit returns the Apple transactions, our
backend verifies them the same way, and PRO is reactivated. There is no sign-in prompt and no
redirect to registration.

**4. The subscription is still fully verified.** Nothing is granted from a local flag. A transaction
is only completed after the server accepted it, and an unfinished transaction is retried on the
next launch or resume.

**4a. One subscription, all of the customer's devices.** The subscription belongs to the customer's
Apple Account, as Apple intends. Restoring it on a second iPhone or iPad gives that device its own
independent, verified entitlement and does **not** revoke the first — there is no device transfer
and no "only one device at a time" rule. A reinstall restores the same way.

**5. Three monthly plans only.** The Monthly/Yearly selector and the annual cards are gone from both
the iOS and the Android app; the app offers OBD PRO, VIN PRO and Full PRO as monthly
auto-renewable subscriptions. (Annual plans remain on our website, which is a separate product
surface and is not offered or linked from inside the iOS app.)

**6. Prices are Apple's, not ours.** Each plan shows the localized price StoreKit returns for that
exact product in the reviewer's storefront. The previous build displayed a hard-coded amount and a
struck-through "was" price with a percentage discount; both are removed. If StoreKit returns no
price, the screen says so and offers a reload instead of showing any number.

**7. No outside-purchase content on iOS.** The iOS PRO screen no longer shows a website price or a
website cancel button. It may show that an existing website subscription is active (plan name,
renewal date) — status only, no price and no call to action. Subscription management for App Store
purchases opens Apple's own page.

An OBD360 account is now genuinely optional: it exists for the separate website subscription at
obd360.ge and for cross-device sync of that website plan.

## Still true from our earlier reply (Guideline 2.1)

**Purpose.** OBD360 turns a phone and a common ELM327-compatible OBD2 adapter into a diagnostic
tool: trouble codes with explanations, live sensor data, module-level diagnostics on supported
vehicles, AI-assisted explanation of results, VIN history lookup, and service/maintenance tracking.

**Setup.** A commercially available Bluetooth LE or Wi-Fi OBD2 adapter connected to the vehicle's
OBD2 port, ignition on. No special hardware.

**Reviewing without hardware.** VIN Check works with only a VIN and an internet connection. The OBD
scanner flow needs an adapter and a vehicle; without one it shows a clear "adapter not found"
state.

**Recording.** A physical-device screen recording was supplied with the previous reply showing
launch, adapter pairing and connection, a live trouble-code scan, module diagnostics and live data
on a real vehicle. Nothing about it is claimed here beyond what it shows.

**External services.** Our own HTTPS backend (accounts, VIN data, AI, purchase verification,
crash/usage telemetry — all first-party); Google Gemini called server-side only; Sign in with Apple
and Google Sign-In; Apple StoreKit. No third-party analytics or crash SDK. Full detail with file
citations: `external-services-inventory.md`.

**Regions.** Functionality is identical worldwide. In-app purchase pricing follows the App Store's
own per-territory localized pricing; the app does not configure it.

**Licensing.** VIN history draws on licensed third-party automotive-data providers through our own
backend. The app never calls them directly.

## Accounting and disclosure on the PRO screen

Plan name, the StoreKit-localized monthly price, the monthly billing period, the auto-renewal
statement, and links to the Privacy Policy and Terms of Use are all on the purchase screen, above
or beside the purchase button.

---

We would be glad to supply a dedicated reviewer account if you want to see the optional
account-only surfaces, but it is not needed for purchasing or restoring. Thank you for your
patience.
