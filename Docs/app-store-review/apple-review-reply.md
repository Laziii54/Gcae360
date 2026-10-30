# Response to App Review — Guideline 2.1, Information Needed

**App:** OBD360 · **Bundle ID:** com.gcar360.app · **App Store Connect Apple ID:** 6817508639
**Subscription group:** OBD360 PRO Plans

Thank you for the opportunity to provide more information. We address each of your seven points
below.

---

## 1. Physical-device screen recording

Attached is a screen recording captured on a physical iPhone running the latest iOS, showing: app
launch; VIN Check (works without any hardware); connecting to a Bluetooth OBD2 adapter and running
a live trouble-code scan and live sensor data on a real vehicle; account sign-in; the PRO
subscription purchase flow with plan, price and terms; and Restore Purchases.

## 2. App purpose, target audience, problem solved, user value

OBD360 is a vehicle diagnostic app for drivers and car enthusiasts who want more than a basic
check-engine-light reader. Connected to a Bluetooth LE or Wi-Fi OBD2 adapter, it reads and clears
trouble codes, shows live sensor data and graphs, and — for supported vehicles — reaches beyond
the generic OBD2 layer into manufacturer-specific modules (ABS, SRS, transmission, and others) for
deeper diagnostics most consumer scan tools don't offer. It also includes an AI-assisted
explanation of diagnostic results, a VIN history lookup (accident/damage/mileage history from
third-party vehicle-data providers), and service/maintenance tracking.

The app does not require an account to browse or use its core OBD/VIN features. An account is
needed only to keep a PRO subscription synced across devices.

## 3. Setup instructions, main features, required credentials

**Setup:** the app requires a commercially available ELM327-compatible OBD2 adapter (Bluetooth LE
or Wi-Fi) connected to the vehicle's OBD2 port, and the vehicle's ignition on. No special hardware
beyond a standard, widely available adapter is needed.

**Main features**, reachable from the home screen without signing in: trouble-code scan and clear,
live sensor data, module-level diagnostics (where supported by the vehicle), fuel/service
tracking, and VIN history lookup. Signing in (email/password, Google, or Apple) is only required
to purchase or restore a PRO subscription.

**Credentials:** no test credentials are required — all core features (OBD2 scanning, live data,
module diagnostics, VIN history) are accessible without signing in. An account is only needed to
purchase or restore a PRO subscription.

## 4. External services, tools, and platforms

- **OBD360's own backend** (Azure-hosted) for accounts, VIN history, AI diagnostics, purchase
  verification, and crash/usage telemetry — all first-party, all over HTTPS.
- **Google Gemini**, called server-side only by our backend for AI diagnostic explanations — no AI
  provider key is ever stored in the app itself.
- **Sign in with Apple** and **Google Sign-In**, Apple's own requirement (4.8) already implemented:
  Google is only offered alongside Apple on iOS.
- **Apple StoreKit** for in-app purchases.
- No third-party analytics or crash-reporting SDK is used; crash reports and usage telemetry are
  both first-party, sent to our own backend only.

Full detail with file citations: `external-services-inventory.md`.

## 5. Regional differences

Core functionality (diagnostics, live data, module scanning, VIN lookup, AI explanations, PRO) is
identical worldwide and not gated by region. The only region-aware default is the measurement unit
system (Imperial defaults for a small number of device regions, Metric elsewhere), which the user
can change in Settings at any time. In-app purchase pricing follows the App Store's own standard
per-territory localized pricing; it is not something our app configures per region.

## 6. Licensing / regulated services

VIN history results draw on third-party automotive-data providers through our own backend (the app
itself never calls them directly). We hold the necessary data licensing agreements for these
providers at the backend/business level; this is not something the client app surfaces or claims
to users beyond showing the resulting vehicle history report. No other regulated or
rights-protected third-party material is used.

## 7. In-app purchase — what can be bought and how to access it

From the PRO tab (reachable from the home screen, or automatically offered when a PRO-gated
feature is opened), users can subscribe to one of three auto-renewing monthly plans within the
"OBD360 PRO Plans" group:

- **OBD PRO** — full OBD2 diagnostics (module scanning, live data, AI explanations)
- **VIN PRO** — VIN history reports
- **Full PRO** — both combined

Plus one-time VIN report credit packs (not subscriptions). Pricing, final local price, and the
renewal terms are shown by Apple's own purchase sheet before any charge.

---

We appreciate App Review's patience and are happy to provide any further detail needed.
