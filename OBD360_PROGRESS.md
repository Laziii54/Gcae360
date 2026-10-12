# OBD360 overnight work — progress and evidence (2026-10-12)

Branch: `feature/obd360-overnight-improvements`. **Nothing was committed, pushed, deployed or published.** The working tree has
about 150 changed files; one file was already modified before this work and was not touched on purpose:
`Backend/CarScanner.Api/wwwroot/assets/logo.png`.

Legend: DONE (changed and covered by a test here) · DONE-STATIC (changed, verified by reading code / source tests only) ·
PARTIAL · PENDING-APPROVAL (proposal only, nothing built) · NOT-DETERMINED · HISTORICAL (the code in the report no longer exists)

**What "tested" means here.** xUnit tests on a software ELM327, loopback TCP sockets and source-reading checks. None of them
proves behaviour on an iPhone, an Android phone or a real car. Items that need a device are listed at the end.

## B — administrator-reported errors

| # | Report | Root cause found | Files | Test | Status | Needs a device |
|---|---|---|---|---|---|---|
| 3.1 | `AggregateException` ×24 | Unobserved task exceptions arrive wrapped in an `AggregateException` that has no stack; the report said nothing about the cause | `Services/CrashExceptionFlattener.cs`, `Services/CrashReportingService.cs` | `CrashRegressionTests` (flatten, de-duplicate, cancellation dropped, category) | DONE for reporting. The real inner causes show up in the next reports | yes — new reports must be read |
| 3.2 | iOS Shell `SendPopping` ("nav stack consistency") | Overlapping or root-level pops and double taps raced inside Shell | `Services/NavigationGate.cs`, `Services/SafeNavigation.cs`, `AppShell.xaml.cs` and 35 callers | `CrashRegressionTests` (gate rules, no back at root) | DONE in code | **yes — iOS** |
| 3.3 | iOS `TitleView` `MissingCtor` ×2 | Inside MAUI's own `ShellPageRendererTracker`; no app UIKit type is involved | — | — | **NOT-DETERMINED**; only fewer overlapping transitions as a mitigation | **yes — iOS** |
| 3.4 | `MileageFinder` `InvalidOperationException` ×2 | The reported methods no longer exist in the code | — | — | HISTORICAL | no |
| 3.5 | `OBDService.InitializeAsync` from `FuelViewModel.Start` / `LiveDataEngine.StartAsync` | Start without a connected adapter threw a bare `InvalidOperationException` and left a half-started trip | `Services/LiveDataStartPolicy.cs`, `OBDService.cs`, `LiveDataEngine.cs`, `ViewModels/FuelViewModel.cs`, `UserError.cs` | `CrashRegressionTests` (typed exception, nothing sent, gate released, concurrent init) | DONE | no |
| 3.6 | Android `AppBarLayout` theme crash | Fixed earlier: the manifest must not redeclare `MainActivity` (explained in the manifest comment); theme comes from the `[Activity]` attribute | `Platforms/Android/AndroidManifest.xml`, `MainActivity.cs` | — | HISTORICAL / guarded | recommended smoke test |
| 3.7 | Android `TextureView` listener `NotSupportedException` | A listener without a usable Java peer (no activation constructor) | `Platforms/Android/PerformanceVideo/AndroidPerformanceCameraController.cs` (`SurfaceListener`) | source test in `CrashRegressionTests` | DONE-STATIC | **yes — Android** |
| 3.8 | `ServiceViewModel` collection changed off the UI thread | Collections changed from a background thread | `ViewModels/AdvancedViewModels.cs` | source test | DONE-STATIC | yes |
| 3.9 | Error-report quality | Wrapper noise, duplicates, no category | `CrashReportingService.cs` | `CrashRegressionTests` | DONE. No new data is collected: still the type, a sanitised stack, source, version, platform and a fatal flag | no |

## C — localization

Languages advertised: Georgian, English, Turkish, German, Polish, Spanish, Portuguese, French, Italian. Georgian ↔ English use
the core dictionaries; the seven added languages use packs (`Services/Localization/LanguagePacks*.cs`).
All pack texts are **machine-written and not reviewed by native speakers**.

Measured by scanning the source (script and the new tests `EnglishTextsInViewModelsAndServices_…` and
`AdjacentGeorgianEnglishPairsInCode_…`):

| Text source | Unique English texts | Translated in every added language |
|---|---|---|
| `L(ka, en)` and `IsEnglish ? en : ka` in code | 1 682 | 99.6 – 99.9 % (the rest are words that are the same in that language, e.g. "Service", "Start") |
| Adjacent `"ka", "en"` literals in records / tuples (checklist, VIN check, market parts …) | 2 089 | 98.0 – 98.3 % (the rest are identifiers, URLs and half-parsed interpolations, not UI text) |
| English text in XAML | all | 100 % (test `EveryEnglishTextInXaml_IsInEveryLanguagePack`) |
| Coding page texts | all | 100 % (existing test) |

Not proven: Georgian-only text typed directly into C# without a pair (a heuristic finds about 795 of 4 193 such literals not in a
dictionary; most are in files that carry an English variant in a different shape, so this is an upper bound, not a defect count);
the Georgian ↔ English dictionaries; layout of long German/French texts; right-to-left (none of the languages needs it).
Runtime switching: the Label/Button handler hook is pinned by `LocalizationRegressionTests` (static-constructor order). **It still
needs a check on an iPhone** (the original screenshots showed the hook not running).
Changing the UI language never changes the measurement units: test `ChangingTheLanguage_NeverTouchesTheMeasurementUnits`.

## D — OBD360 rebranding

User-visible text, notification texts, report headers, share titles, store-facing text in code, pack texts and exported file
names now say OBD360. `ApplicationTitle` is OBD360. Kept on purpose, because other systems depend on them:

* application / bundle id `com.gcar360.app`, Play subscription skus, codemagic bundle identifier, signing;
* `DataProtection` purposes (`GCar360.AccountDelete.v1`, `GCar360.Admin.v2`) — changing them invalidates tokens;
* payment descriptors sent to the bank (`GCar360 PRO`, `GCar360 VIN …`);
* hash salts (`gcar360-ranking:`, `gcar360-video:`, `gcar360-device:`), capture schema id `gcar360.capture/1`, channel id
  `gcar360_trip`, handler-thread and wake-lock tags, animation key `gcar360-telemetry`;
* support address `gcar360support@gmail.com` (the mailbox exists under that name) and the Play Store URL;
* the DTC catalog file headers and the `.vs` folder.

Manual work outside the code: a new support mailbox and its forwarding, store listings and descriptions, bank/merchant
descriptors, GitHub repository / cloud resource names, domain/DNS. **The approved OBD360 logo was not in the repository, so no
logo was invented**; the launcher icons are unchanged.

## E — navigation, lifecycle, Wi-Fi OBD

* Rapid navigation: `NavigationGate` (one transition at a time, same route refused for 700 ms, back refused at the root),
  `SafeNavigation` for every route call. DONE, tested.
* Event leaks: three view models created on every page open (`AccelerationHistory`, `AccelerationLeaderboard`,
  `TechnicalInspection`) subscribed to long-lived services and were never released. They now use `WeakEventRelay`. DONE, tested
  with garbage collection (`WeakEventRelayTests`). Other subscribers are singletons.
* Wi-Fi OBD: audited `WifiObdTransport` — one command at a time, stale-reply drain, resync after a timeout, monitor mode, closed
  link detection were already right. **Fixed one real gap:** an unplugged adapter left the transport "connected" while every
  command timed out. Now three timeouts in a row where the adapter does not even answer the resync end the link
  (`ObdConnectionLostException`, `ConnectionChanged(false)`); a slow but alive adapter never counts. Eight loopback tests
  (`WifiObdTransportTests`). Not tested: a real Wi-Fi adapter (clones differ in the prompt, `ATI` text and timing).
* White screens: no blank-page cause was proven. Timers were audited (three, all with stop paths); pages' fallback states were not
  redesigned.

## F — design

* Pull-to-refresh with one light haptic only after a real, finished refresh: **Service** and **Battery** pages
  (`PullRefreshRunner`, `RefreshHaptics`, `PullRefreshTests`). A pull while a refresh runs is dropped. Not applied to pages whose
  "refresh" is a long scan or a write (Coding, Mileage, Vehicle Health, Hybrid load test).
* Font sizes: 254 of 1 100 `FontSize` declarations in XAML are below 12. **Not changed** — a global change needs screenshots on
  devices (clipped fixed-height labels). Proposed: type tokens in `Resources/Styles`, migrated page by page.
* Dark/light and icon consistency were not reviewed visually (no device screenshots available).

## G — page correctness

| Item | Result |
|---|---|
| Mileage: zero or implausible value | Already correct and tested: zero, implausible and malformed values are "flagged, not used" (`MileageAnalyzer`, `MileageDecoderTests.ZeroKilometres_…`). Source classification (not advertised / no definition / not requested / no answer …) already exists |
| All Sensors progress 33 → 38 | **Fixed**: the scan reported `n / 33` and then `n / 38` because status-type PIDs were counted after the first part. One total from the first report; test `AllSensorsProgress_KeepsOneTotal_AndNeverMovesBackwards` |
| Battery Charging | Duplicate "High Voltage Battery" card removed (the Hybrid Battery page owns it). Voltage verdicts now depend on a 12 V battery type chosen by the owner (standard, AGM/EFB, hybrid/EV auxiliary, lithium) — `BatteryVoltageGuide`, 17 tests. The type is not read from the car |
| Service: 50 % / 100 % reset | **Fixed**: the page only bound the 100 % command. Both buttons are shown only when the car supports the checked write-back; the confirmation dialog states the target |
| Acceleration units | Heading, ranking row and share file/title follow the unit setting (0–100 km/h or 0–60 mph) instead of a fixed "0–100"; history, leaderboard and default test already did |
| Graph Studio | Tap any graph (separate or grid view) to enlarge it; close **X at the upper-left**; the enlarged chart binds the live series, so it keeps updating |
| Fuel (per powertrain), Hybrid Battery states, ECU Explorer, Inspection, Emissions | Reviewed; the texts and states already exist from earlier work and are translated. Larger redesigns are proposals (H) |
| Report navigation (I) | One entry on Home — "Vehicle report" → the full report page (PDF, QR, text, earlier reports, scan history). The generic "Report" tile is gone. The older text-only `VehicleJournalPage` is no longer linked (route and code kept); its text export is covered by the report page's **Text** button |

Mode 05 and Mode 06 pages, view models, parsers and definitions were **not modified** (only pack translations contain their
words). The Coding page was **not redesigned**; its two changed lines are a brand word in a backup note and the navigation call.

## H — visual proposals

`OBD360_VISUAL_PROPOSALS.html` — 14 pages with wireframes and open decisions. **Nothing in it is implemented; no approval is
assumed.**

## J — tests and builds (actual results)

* `dotnet test Tests/CarScanner.Tests` — 1 544 passed, 0 failed, 0 skipped, twice in a row (about 2 min 30 s). A capture test
  (`StandardObdCaptureThroughProtocolTests…BackendIsDown…`) failed in about one full parallel run in four because tests share the
  static OBD exchange tap; test parallelization is now switched off for the assembly (`AssemblyBehavior.cs`) and the failure has
  not come back. The root cause was not traced to a single test.
* Android Release build (`net10.0-android`, final sources) — 0 errors, 265 warnings (existing XAML binding hints), 4 min 25 s.
  This produces the build output only; no APK/AAB was signed or installed.
* iOS cannot be built on this Windows machine.

## Still open

1. Device checks: iPhone (Shell back / pop, `TitleView`, language hook on Home), Android phone (camera surface, Service page,
   Battery picker), a real Wi-Fi adapter, a real car for the 12 V verdicts.
2. Native-speaker review of the seven added languages.
3. Approved OBD360 logo and icons; support mailbox; store listings.
4. Decisions on the 14 proposals.
5. Typography tokens and a visual pass (needs screenshots).
6. 3.3 (`TitleView` `MissingCtor`) needs a symbolicated iOS crash log.

---

# Second run — follow-up work and the emoji-icon removal

## Follow-up priorities (this run)

* **Flaky capture test — root cause found.** `DiagnosticsKeepWorking_WhenTheBackendIsDown_AndWhenCollectionIsDisabled` asserted the
  queue before the pipeline's background consumer had drained it (missing `pipe.DrainAsync()`). Fixed in the test; assembly-wide
  parallelism is back (`AssemblyBehavior.cs` deleted). Evidence: 8 of 30 runs failed without the drain, 0 of 60 with it; five full-suite
  runs under 24 busy CPU threads all passed (1 583/1 583).
* iOS `MissingCtor`: one real defect fixed (`CameraPreviewView` lacked the `(NativeHandle)` constructor); the Shell `TitleView`
  root cause is still unproven (`OBD360_IOS_TITLEVIEW_INVESTIGATION.md`; a symbolicated crash log is needed).
* Localization: dialogs, notifications and native pickers now go through `LocalizationService.T`
  (`PageDialogExtensions`, `LocalizedTextConverter`); packs Extra19–28 added. Machine-written; native review still needed.
* Small text: `OBD360_SMALL_TEXT_AUDIT.md`. **Mode 05 / Mode 06 pages were briefly touched by that pass and have been restored to
  their original content; `git diff` shows no change in any Mode 05/06 file.**

## Emoji-style icon removal (this run)

Policy: **NO EMOJI-STYLE ICONS IN THE OBD360 USER INTERFACE** — written up in `DEVELOPMENT_GUIDE.md`.

* New shared system (no new dependency): `Controls/IconCatalog.cs` (21 outline icons, 24 grid), `Controls/Icon.cs`,
  `Controls/IconButton.cs`; based on the existing `RowIconStyle` recipe.
* Removed emoji and symbol glyphs from XAML, view models, services, notifications, reports, language packs (keys and values kept
  consistent), tests, the backend web/e-mail/admin pages and the store-listing headings. Language flags became `KA/EN/TR/DE/PL/ES/PT/FR/IT`
  code badges. Service-procedure JSON now names car buttons in words.
* Status marks became icon kinds (`check`, `warning`, `close`, `circle`, `approx`, `info`) drawn by `Icon`; icon-only buttons have
  accessibility labels (translated in `LanguagePacks.Extra28.cs`). Decorative icons were removed rather than replaced.
* `OBD360_VISUAL_PROPOSALS.html` revised: no emoji, shared inline icon sprite, new "Icon direction" section and an "Icons" note per proposal.
* Guard: `Tests/CarScanner.Tests/IconPolicyTests.cs` + `IconPolicy.cs` (XAML, every C# string literal, bundled JSON, proposals page,
  store listings, catalog consistency).
* Verified: `dotnet test` 1 622 passed / 0 failed; Android Release build 0 errors. **Not verified:** how any screen looks on a device
  (light/dark rendering of the new controls was only checked for the same paths in the HTML proposals); iOS was not built.
