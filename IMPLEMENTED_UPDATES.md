# Implemented updates

## 0–100 km/h

- Home-screen measurement card with start, cancel, latest result and history.
- OBD vehicle-speed based timing, interpolation at the 100 km/h crossing and an explicit accuracy notice.
- Device-local `AccelerationRun` history stored in SQLite.
- A run starts only after a 0–3 km/h sample; a missing speed PID cannot manufacture a result.

## AI protection

- Promo codes were removed. PRO is granted only from the admin panel (Users → PRO-ს მიცემა).
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
