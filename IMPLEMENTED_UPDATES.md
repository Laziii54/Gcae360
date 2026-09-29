# Implemented updates

## 0–100 km/h

- Home-screen measurement card with start, cancel, latest result and history.
- OBD vehicle-speed based timing, interpolation at the 100 km/h crossing and an explicit accuracy notice.
- Device-local `AccelerationRun` history stored in SQLite.
- A run starts only after a 0–3 km/h sample; a missing speed PID cannot manufacture a result.

## Promo and AI protection

- Promo codes are configured only on the server through `GCS_PROMO_CODES`; no production promo code is embedded in the app source.
- Server storage keeps only a SHA-256 code digest, enforces expiry, a redemption limit and one redemption per user ID.
- Example format: `WELCOME30|30|500|2027-01-31`.
- AI requests now include the app's user identifier and the backend serves them only to an active PRO entitlement.

## Reliability and interface

- Fixed the Nordic UART BLE notify UUID so compatible BLE OBD adapters can initialize.
- Android supports BLE and Bluetooth Classic discovery; iPhone correctly presents BLE-only OBD support because iOS cannot use arbitrary Bluetooth Classic serial adapters.
- Device rows expose the transport type and connection remains single-adapter at a time.
- Added global Light/Dark/System resources used by pages, cards, text, borders and standard buttons.
- Localization no longer overwrites bound view-model values when language changes, and preserves original XAML strings for reliable switching.

## Still requires physical/release validation

- Real Android and iPhone tests with supported BLE adapters and vehicles.
- Google Play/App Store credentials, signing, StoreKit purchase implementation for iOS, and production database/backup configuration.
- Full translator QA for every existing legacy string before declaring a store release.
