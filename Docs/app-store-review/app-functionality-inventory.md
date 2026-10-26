# OBD360 — App Functionality Inventory

Verified from the source code in this repository. Every item below cites the file(s) it was
confirmed in. "Confirmed" means the code was read and does what is described; it does not mean
the feature was exercised on a physical device — that distinction is called out per item.

## 1. App launch and onboarding

No tutorial, walkthrough, or permission-priming screen. The first screen is
[`Views/StartPage.xaml`](../../Views/StartPage.xaml), a two-card menu:

- **OBD scanner** → `Views/HomePage.xaml` (`StartPage.xaml.cs`, `OnObdTapped`)
- **VIN Check** → `VinCheck/Views/VinLookupPage.xaml` (`StartPage.xaml.cs`, `OnVinTapped`)

plus a language picker. **No account or sign-in is required to reach either path** —
`HomePage.OnAppearing` runs its connection/VIN summary refresh with no auth check.

## 2. Account system

[`Services/AccountService.cs`](../../Services/AccountService.cs) implements real server-backed
account management against the OBD360 backend (`/api/accounts/*`):

- Email/password registration and login, plus email verification codes (`VerifyLoginCodeAsync`).
- **Google Sign-In and Sign in with Apple** via
  [`Services/ExternalSignIn/ExternalSignInService.cs`](../../Services/ExternalSignIn/ExternalSignInService.cs).
  On iOS, Google is only offered alongside Apple (`ShowGoogle` requires `ShowApple`), which is the
  app's own implementation of Apple's Guideline 4.8 ("any third-party login must offer Apple's
  too"). Apple Sign-In itself is compiled in only when the iOS build passes
  `-p:EnableAppleSignIn=true` — confirmed set in the project's only iOS build pipeline,
  `codemagic.yaml` (see `external-services-inventory.md`).
- **In-app account deletion** — `AccountService.DeleteAccountAsync()` sends a real
  `DELETE /api/accounts/me`, then clears the local session. Wired to a visible button:
  `Views/AccountPage.xaml` → `DeleteAccountButton` → `OnDeleteAccountClicked`
  (`AccountPage.xaml.cs`), which shows two confirmation dialogs, an iOS-specific note that an App
  Store subscription is **not** auto-cancelled by account deletion, and resets PRO status to free
  afterward. This directly satisfies Apple Guideline 5.1.1(v) (apps that support account creation
  must support in-app account deletion).
- An account is **optional** for the core OBD scanning and VIN-check browsing; it is required
  only to keep a PRO entitlement synced across devices and to buy PRO at all.

## 3. OBD2 adapter connectivity

Two transports are implemented and registered (`MauiProgram.cs`): Bluetooth LE via
`Services/Bluetooth/BluetoothObdTransport.cs` (Plugin.BLE) and Wi-Fi via
`Services/Bluetooth/WifiObdTransport.cs`, unified behind `ObdConnectionService`.

- `Views/BluetoothPage.xaml` drives pairing/connection.
- A real, worded empty state exists for "no adapter found" / a failed scan
  (`ViewModels/BluetoothViewModel.cs`) rather than a blank screen on failure.
- **No demo or simulator mode exists** in the app (confirmed: no `Demo`/`Simulator`/`Mock` class
  outside the Bluetooth service and the test project). A reviewer without a real, compatible OBD2
  adapter and a car cannot exercise live scanning, live data, or module diagnostics directly —
  see `manual-verification-checklist.md` and the recording checklist, which exist specifically to
  cover this gap with a real demonstration instead of relying on the reviewer's own hardware.

## 4. Diagnostic scans and trouble codes

Real, implemented read-and-clear flow at two levels:

- General OBD: `ViewModels/DiagnosticsViewModel.cs` (`ClearCodes()` → `OBDService.ClearDtcWithResultAsync()`).
- Per-module: `ViewModels/AdvancedViewModels.cs` (`ModuleScanViewModel.ClearDtc()`, same underlying call).

## 5. Live Data and sensor monitoring

Backed by `Services/LiveDataEngine.cs`, `Services/LiveDataRecorder.cs`, and
`Services/OemDiagnostics/ModuleLiveDataService.cs`, with dedicated pages `LiveDataPage`,
`SensorsPage`, and `GraphStudioPage` (all routed in `AppShell.xaml.cs`). Confirmed as real
telemetry plumbing (registered services, a recorder, a dedicated page per surface) rather than
placeholder screens; the rendered UI itself was not walked through interactively as part of this
audit.

## 6. Module (multi-ECU) diagnostics

A substantial, built-out feature: `Services/OemDiagnostics/*.cs` totals roughly 11,700 lines
across identification, DTC, freeze-frame, live-data, coding-backup, and module-discovery services,
all registered in `MauiProgram.cs`, with `ModuleScanPage` and `ModuleLiveDataPage` as the user-facing
surfaces. This is not a stub — confirmed by code volume and the number of distinct, wired-up
services, not by running a scan against a real vehicle.

## 7. AI diagnostic feature

[`Services/AIConfig.cs`](../../Services/AIConfig.cs) points the app at the OBD360 backend only:

```
BackendBaseUrl = "https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net"
DiagnosePath   = "/api/ai/diagnose"
ChatPath       = "/api/ai/chat"
```

`AI/AiDiagnosticService.cs` only ever calls those two paths — confirmed no AI provider key or
endpoint is present anywhere in the mobile app itself. The upstream model (Google Gemini) is
called server-side; see `external-services-inventory.md`. The AI feature is PRO-gated
(`AppShell.xaml.cs`, `IsProFeatureRoute` includes `AiPage`).

## 8. Error handling and empty/loading states

A consistent pattern is used everywhere it was checked: command handlers wrap their logic in
try/catch and report failures through `CommandErrors.Report(ex, "Context")`
(`Services/CommandErrors.cs`), and Bluetooth has a real, worded empty state (§3). This was
spot-checked across several pages (Start, Home, Bluetooth, Account's delete flow), not
exhaustively audited across all ~60 pages in the app.

## 9. Release blockers

No `throw new NotImplementedException`, and no `TODO`/`FIXME` comments, were found anywhere in
`Views/`, `ViewModels/`, `Services/`, or `AI/` (Tests and Backend excluded from this check). This
rules out *marked* incompleteness; it cannot rule out a feature that is silently unfinished
without a UI walkthrough.

## 10. Regional consistency

No feature, screen, or content is gated by country, region, or IP anywhere outside localization
and unit formatting. The only region-aware behavior found is the default measurement system —
`Services/UnitSystemService.cs` defaults to Imperial for US/Liberia/Myanmar device regions and
Metric everywhere else, and the user can change it. Core functionality (diagnostics, live data,
module scanning, VIN check, PRO) does not vary by region; pricing displays in the store's local
currency per standard App Store/Google Play behavior, and the website checkout (not used on iOS)
separately prices in GEL for Georgia and USD elsewhere — see `subscription-compliance-audit.md`.

## Summary for the Apple reply

The most relevant fact for a Guideline 2.1 ("need more information") rejection is **item 3**: the
app's core value depends on hardware (a real OBD2 adapter) and often a real vehicle, which an App
Review session cannot supply. That is almost certainly why Apple asked for a screen recording and
a clearer explanation — not evidence of a missing or broken feature. The functionality itself,
account system, and purchase flow are all real, implemented, and reasonably complete based on this
audit.
