# Privacy and store disclosure working sheet

This file documents the current codebase. Re-audit it whenever analytics, ads,
authentication, crash reporting or another SDK is added.

## Data processed

| Data | Purpose | Linked | Tracking | Notes |
| --- | --- | --- | --- | --- |
| Random local user ID | App functionality | Yes | No | Anonymous entitlement lookup before sign-in |
| Email/account ID | Account management | Yes | No | Optional PRO restore account; password stored only as a salted hash |
| Purchase token/transaction ID | App functionality | Yes | No | Sent to backend for store verification |
| VIN and diagnostic scan context | App functionality | Yes | No | Sent only when required by the selected backend/AI feature |
| User's AI diagnostic question | App functionality | Yes | No | Sent to backend to generate the requested answer |
| Language, units, theme, local history | App functionality | No | No | Stored locally unless included in an explicit report/action |
| Anonymous crash diagnostics | App functionality | No | No | App/OS version, platform, exception type and sanitized stack only; excludes VIN, readings, email, IDs, secrets and location |
| Usage signal (every ~6 h) | Analytics (aggregate counts), fraud prevention | Yes when signed in | No | Hashed install ID, app/OS version, device model, language, notification permission; account ID if signed in |
| Website card payment (BOG/TBC) | Purchases | Yes | No | Card entered on the bank page; backend keeps order ID, product, amount, status, bank payment ID |
| QR report | App functionality | No (public link) | No | Stored 30 days: make/model/year, last 6 VIN characters, scan summary, codes, readiness, owner-entered service history |
| Acceleration leaderboard result (0–100, 0–60 mph, ¼ mile) | App functionality | Yes | No | Public: display name, vehicle model, time; VIN stored only as a one-way hash |
| Profile photo (optional) | App functionality | Yes | Shared with Google (Gemini) for an automatic offensive-content check before it is shown | Public on the ranking next to the result; small JPEG (256 px) chosen by the user from the gallery; deleted with the account or on request in the app |
| Precise location (GPS) during a measured run | App functionality, fraud prevention | Yes | No | Optional; foreground only while measuring. Sent only when a result is published; coordinates kept only for results under review, deleted 30 days after the review |
| Review video of a run | App functionality, fraud prevention | Yes | No | Only for top-place / unusual results, uploaded by the user; seen only by the administrator; deleted 30 days after the review |
| Service history, trips, reminders | App functionality | No | No | Stored only on the device; reminders are local notifications |

The current project contains no advertising or analytics SDK. Crash reporting
uses the first-party backend rather than a third-party tracking SDK. Bluetooth scan
permissions are used to find/connect to an OBD adapter. Legacy Android location
permission is limited to Android 11 and below for Bluetooth discovery; the app
must not use it to collect or transmit physical location.

## Google Play Data safety draft

- Data shared for core functionality: diagnostic/user content sent to the app's
  backend and AI provider only when the user invokes those features.
- Purchase information: processed for subscription verification and website card orders (BOG/TBC).
- Data encrypted in transit: Yes; cleartext traffic is disabled on Android.
- Account creation: Optional; account deletion is available inside the PRO screen.
- Deletion request contact: `obd360support@gmail.com`.
- Ads and cross-app tracking: No.

Final answers in Play Console must match the deployed backend, retention policy
and every dependency in the exact release bundle.

## Apple App Privacy draft

- User ID: linked, app functionality, not tracking.
- Other user content/diagnostic context: linked, app functionality, not tracking.
- Purchase history: linked, app functionality, not tracking.
- Crash data: not linked, app functionality, not tracking.
- Product interaction / usage data: linked when signed in, analytics, not tracking.
- Other user content (QR report, leaderboard name): linked, app functionality, not tracking.
- Tracking: No.

`Platforms/iOS/Resources/PrivacyInfo.xcprivacy` declares these categories and
the UserDefaults required-reason API (`CA92.1`). Validate the final archive's
generated privacy report on the release Mac before submission.
