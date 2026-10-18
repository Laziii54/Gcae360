# Georgian Car Scanner — release checklist

## Source-complete in this package

- [x] Generic OBD-II DTC read: Stored / Pending / Permanent
- [x] DTC clear positive-response validation and post-clear verification
- [x] Timeout / transport / unsupported states remain distinct from zero DTCs
- [x] DTC catalog cache + automatic public generic catalog refresh
- [x] PID support discovery and decoded/failed/unsupported statistics
- [x] Adaptive live-data architecture with one engine
- [x] Structured freeze-frame and advanced-mode raw-test records
- [x] VIN / ECU identification
- [x] Scan-session persistence
- [x] TXT / JSON / CSV reports and sharing
- [x] Adapter diagnostics and low-voltage warnings
- [x] Privacy export / local deletion
- [x] Dark / Light / System theme handling
- [x] Long-screen scrolling / virtualized diagnostic collections
- [x] Backend-only Gemini AI
- [x] PRO gating for AI / Fuel / Mileage
- [x] Store purchase client integration + server verification endpoints
- [x] Persistent server entitlement storage
- [x] GM/Volt OEM profile framework + capability model + fail-closed safety gates (no executable OEM address enabled without verified evidence)
- [x] UDS response/NRC parser + ISO-TP safety limits
- [x] Parser/unit-vector test suite
- [x] Natural Georgian generic DTC catalog: 9,530 codes / 67,074 translated fields
- [x] Full DTC catalog regression test (YAML integrity, counts, empty fields, foreign-script and known mixed-text checks)
- [x] Real HTTPS `AIConfig.BackendBaseUrl` configured for the deployed Azure API
- [x] `GEMINI_API_KEY` stored on the backend only (not bundled in the mobile app)
- [x] Deployed backend `/health` endpoint verified successfully (2026-09-09)
- [x] Clean Android Release build: 0 errors / 0 warnings (revalidated 2026-09-15)
- [x] Automated regression suite: 108 / 108 passed (revalidated 2026-09-15)
- [x] Backend Release build: 0 errors / 0 warnings (revalidated 2026-09-15)
- [x] Isolated iOS Release compile: 0 errors / 0 warnings (2026-09-15; signing/archive and device tests still require macOS)
- [x] Compiled XAML bindings validated for list items and page commands
- [x] Detailed scan sessions load on History and are included in CSV export
- [x] History full delete removes legacy and all structured scan child records atomically
- [x] Optional PRO restore account: register/login/logout, secure local token, authenticated purchase transfer and in-app account deletion
- [x] Anonymous first-party managed crash capture, next-launch upload, backend validation, rate limiting and 90-day/5,000-report retention cap
- [x] Static Georgian/English long-form UI localization cleanup and persisted language selection
- [x] 73 manufacturer capability profiles with fail-closed MetadataOnly state until an exact reviewed OEM definition exists
- [x] iOS source integration: BLE-only OBD path, Bluetooth privacy strings, StoreKit purchase/restore bridge, Apple server verification and privacy manifest

## Environment / store configuration required before publishing

- [x] Resolve purchase restoration after reinstall with authenticated account ownership and explicit authenticated transaction transfer.
- [x] History > Delete clears legacy and structured diagnostic history atomically after explicit confirmation.

- [ ] Create `com.georgiancarscanner.pro.monthly` in Google Play and App Store Connect.
- [ ] Configure Google Play service-account credentials on the backend.
- [ ] Verify the backend uses `GCS_ANDROID_PACKAGE_NAME=com.obd360.app` exactly.
- [ ] Configure Apple App Store Server API credentials on the backend.
- [ ] Deploy and confirm `/health/configuration` reports `readyForStoreRelease: true` without exposing secret values.
- [ ] Confirm `/health/configuration` reports `persistentStorageConfigured: true`; keep Azure at one instance while the API uses SQLite.
- [ ] Complete sandbox purchase/restore/renewal/refund tests.
- [ ] Test real BLE and Classic Bluetooth adapters on supported Android versions.
- [ ] Test iOS with a BLE adapter.
- [ ] Capture verified GM/Volt OEM ECU addresses and positive read-only responses on a real vehicle. The 2026-09-07 Volt capture verifies 32 generic PIDs but contains no OEM ECU address/response, so ABS/SRS/BECM executable definitions remain safely disabled.
- [ ] Obtain equivalent reviewed ECU definitions/captures for each manufacturer before enabling its OEM executable profile; metadata selection alone never enables OEM commands.
- [ ] Build/sign/archive on macOS and complete iPhone BLE + StoreKit sandbox testing. Windows cannot produce or validate the iOS archive.
- [ ] Release signing, store metadata and production monitoring/dashboard operations.

The application deliberately does not fabricate proprietary OEM command payloads. Unsupported OEM write/security/programming operations stay disabled until a verified vehicle profile and documented routine are available.
