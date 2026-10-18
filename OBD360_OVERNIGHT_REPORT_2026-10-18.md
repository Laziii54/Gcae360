# OBD360 overnight work — 2026-10-18

Nothing here was committed, pushed, deployed or built into an APK. Test results are **unit / simulator / source-reading only**; nothing below was tried on a phone, an adapter or a car unless it says so.

## 1. Profile photos — admin page, Android and iPhone

**Admin (`/admin?tab=avatars`, new tab "პროფილის ფოტოები")**
- Lists every stored profile picture (waiting-for-approval first): thumbnail, account e-mail, status, upload time, size.
- Per picture: **Approve** (only when it is waiting) and **Delete** (with confirmation). Both are written to the admin audit log (`avatar_approve`, `avatar_delete`).
- The admin-only image address `/admin/avatars/{id}/image` needs the admin session and is never cached; the public address `/api/avatars/{id}` serves approved pictures only.
- The ranking table in the admin "აჩქარების რეიტინგი" tab also has a "ფოტოს წაშლა" button per result.
- Admin pages' Content-Security-Policy now allows images from their own origin (`img-src 'self' data:`); everything else is unchanged.
- Files: `Backend/CarScanner.Api/Services/AvatarStore.cs` (list / read-any / set-status), `AdminPages.cs`, `AdminService.cs`, `Program.cs`.

**Android and iPhone** (checked by decompiling MAUI 10.0.101's own `MediaPickerImplementation` for both platforms)
- Android: the picker uses the AndroidX system photo picker (or the ordinary "choose a picture" screen on older systems). It requests **no permission** (permissions are requested only for capturing with the camera). `MainActivity` is a `MauiAppCompatActivity`, which the photo picker needs.
- iOS: iOS 14+ uses `PHPickerViewController`, which needs **no permission**. The app's minimum iOS is 15. `NSPhotoLibraryUsageDescription` was added to `Platforms/iOS/Info.plist` anyway (MAUI's documentation lists it).
- The app now calls `PickPhotosAsync` with `SelectionLimit = 1` (the single-photo method is obsolete in .NET 10). Photos are rotated, shrunk to 256 px and re-encoded as JPEG on the phone, which also drops EXIF data (for example GPS) before upload.
- **Not verified:** the picker and the upload on a real Android phone and a real iPhone; iOS cannot be built on this Windows PC.

Tests: `Tests/CarScanner.Tests/AvatarTests.cs`, `AvatarAppWiringTests.cs`.

## 2. Service resets for more cars, oil percentage

**Short answer:** an automatic ECU reset for *all* cars cannot be added safely, and nobody publishes one. What I could do honestly was done.

What the research found
- There is **no open dataset** of oil-life reset commands (UDS 0x2E / 0x31 or GMLAN) for all makes on GitHub or elsewhere. Searches for such a dataset returned nothing usable; commercial scanner apps keep theirs closed (already written down in `SERVICE_RESET_RESEARCH.md`).
- The bundled OBDb catalog (CC BY-SA 4.0, `obdb.community`) already gives **read-only** values: production-grade oil-life % rows exist for Ford (28 models), Chevrolet (5), GMC (4), Cadillac (1), Lincoln (1); GM and Mazda oil-life rows are marked *debug/research* by OBDb itself, so normal Service refresh does not send them. Many more makes have production "distance / days to service" rows (Toyota 48, Ford 42, Audi 32, VW 30, Honda 27, Peugeot 15, Volvo 15 ...).
- **No automatic oil reset is enabled for any car today** (not even the Volt): `ReviewedGmOilLifeResets` has been empty since the 2026-09-28 safety audit, and `oem_operations.json` has `operations: []` by its own rule (exact make/model/year/ECU, exact request, exact response, read-back verified on a real car, hashed evidence). I did **not** add unverified write commands: a wrong write can change another setting or damage a module, and your own promotion checklist forbids it.

What was changed
- **Oil page ("Reminders → Oil")** now shows **every applicable dashboard guide (up to three, each with its title)** instead of only the first one. A driver usually does not know whether the car has the button cluster or the menu cluster. (`Services/ServiceProcedures/ServiceGuideText.cs`, `ViewModels/MaintenancePartViewModel.cs`.) Tool-only notes, tyre-pressure and inspection guides are never shown there.
- The oil card said "use the guide below" after the Procedures/Help blocks were removed; it now says where the steps are ("tap Oil under Reminders"). Translated into the 7 packs (machine-written).
- **Catalog 10** (`Resources/Raw/service_procedures.json`, 164 guides): Mercedes-Benz **Sprinter (906, 2007-2018) with and without steering-wheel controls**, from the official *Mercedes-Benz Vans Canada "MB Reset Service Indicator Guide - Sprinter"*; a **Fiat / Abarth cluster-menu** oil reminder guide (two independent sources). Four tool/dealer-only notes (Porsche, Haval, BYD, Lada/Daewoo/UAZ/SsangYong) are now flagged `toolRequired`, so they are never offered as a hand guide. All new records are `SourceDocumented`, none claims a real-car test.
- Researched and **not** added because no reliable source exists: Fiat Grande Punto trip-button / pedal sequences (contradictory, an owner reported it failed), Ford Transit / Transit Custom, Nissan Qashqai J10, Land Rover / Jaguar (official owner pages are dynamic), Volvo (official pages describe the message, not the reset).
- Tests: `ServiceGuideTextTests.cs` (+ updated `ServiceCatalogContentTests.cs`).

To get more real resets later: each car needs a captured, read-back-verified write on a real vehicle first (see the promotion checklist in `oem_operations.json`). The "Reset recorder" that collected such captures was hidden together with the Help block at your request.

## 3. Fuel / energy trip keeps recording with the screen off

**Short answer:** the base mechanism already existed (Android foreground service + wake lock, iOS background location). I audited every way it could stop silently and fixed what the code showed. **Nothing here was run on a phone** — whether it survives hours with the screen off on *your* phone can only be seen on a real drive (checklist at the end).

What the audit found, and what changed
1. **Android 12+ refuses a foreground service once the app is hidden** (documented: `ForegroundServiceStartNotAllowedException`). The service was requested only *after* the PRO check and the adapter handshake (several seconds). Press Start, lock the phone at once → the request came too late, the error was swallowed, the trip recorded only while the screen was on. **Now:** the service is requested at the tap, before the handshake (only if an adapter is connected, so nothing is asked for a start that cannot happen) and taken down again if the start fails.
2. **Android 14+ `connectedDevice` service prerequisite** (Android documentation): the app must have declared one of `CHANGE_NETWORK_STATE` / `CHANGE_WIFI_STATE` / … in the manifest *or* been granted a Bluetooth runtime permission. The manifest declared neither, and a **Wi-Fi adapter user never grants a Bluetooth permission** → the service was refused. **Now:** `CHANGE_NETWORK_STATE` (a normal permission, no prompt) is declared. Checked in the merged manifest of the Android compile: permission present, service has `foregroundServiceType="connectedDevice"`.
3. **A refusal was silent** (`Debug.WriteLine` only). **Now** the Fuel page shows a line under the status: "Background recording could not start. Keep the app open and the screen on, or the trip may stop." (iOS: "allow Location While Using the App"). 7-language rows added.
4. **Polling restart after an adapter drop was one-shot.** If the resume failed once (the car's ECU still waking up, the handshake timed out) or raced the old loop's shutdown, polling never came back and the trip froze for the rest of the drive. **Now** `LiveDataEngine` has a trip watchdog (5 s, only while a trip records and only while the adapter link is up) that starts polling again. Real-time tests against the software adapter; *with the watchdog switched off the main test fails* (checked).
5. **Swiping the app away** left an orphan service and notification with nobody to stop it. **Now** it counts as "closing the app" (your wording): the service and wake lock end, the checkpoint is saved first, the trip stays as a **paused** trip (and as "interrupted trip" if the process is killed).
6. The **wake lock** had a hard 12 h cap. It is now pushed forward while readings keep arriving, and still ends by itself if nothing arrives (car off, adapter gone).
7. The **notification kept showing the last distance** when the car stopped sending. After about half a minute without data (25 s, checked every 20 s) it now says "waiting for data from the car…".
8. If **the system dropped the service while the app was away**, nothing restored it. On resume / when the Fuel page appears it is started again (a moment when Android allows it).
9. **Android battery optimisation** can stop background apps whatever the app does. Once, after a trip has started and only if optimisation is on for OBD360, a dialog offers to open the system battery screen (no special permission, nothing is changed automatically).

Unchanged on purpose: leaving the Fuel page never stops a trip (already so); Pause stops the background recording too; Stop ends everything; no distance or fuel is invented for periods without data (they count as gaps).

Limits / not verified
- **iOS:** needs Location "While Using the App" (the Info.plist text and the `location` + `bluetooth-central` background modes were already there). With it denied the system suspends the app when the screen turns off — the page now says so. iOS code could not be compiled on this Windows PC; I used only API calls the file already used.
- Phones with aggressive battery managers can still kill the app; the one-time hint is the only remedy the app can offer.
- A trip that the system killed cannot continue; the 20-second checkpoint lets you save what was recorded.

Files: `Services/Background/TripKeepAlive.cs`, `Platforms/Android/TripForegroundService.cs`, `Platforms/Android/AndroidManifest.xml`, `Platforms/iOS/TripKeepAlive.iOS.cs`, `Services/LiveDataEngine.cs`, `ViewModels/FuelViewModel*.cs`, `Views/FuelPage.xaml`, `App.xaml.cs`, pack rows in `LanguagePacks.Extra33.cs`.
Tests: `Tests/CarScanner.Tests/TripBackgroundTests.cs` (20 new). Full suite **1910/1910**, Android Debug compile **0 errors** — unit / simulator / source checks only.

Phone checklist (please try once): (a) Android 14/15 with a **Wi-Fi** adapter: Start, lock the phone within 2 s, drive 10 min, unlock — did distance and fuel keep counting? (b) the same with a Bluetooth adapter; (c) unplug the adapter for a minute and plug it back — does the page return to "running"? (d) swipe the app away, reopen — "paused trip" shown? (e) iPhone: deny location → the orange line appears; allow it → drive with the screen off.

## 4. Cheap ELM327 vs Konnwei — root-cause audit (Chevrolet Volt 2012 oil life: `ATSH 7E0`, `ATCRA 7E8`, `22 119F`)

**Short answer.** I found and fixed a real defect that hits exactly this kind of adapter, and reproduced it in a simulation. **I could not prove that it is what your cheap adapter hits**: there is no physical adapter or car here and no raw log from either of your adapters. Everything below is marked **simulated**, **code-read** or **vendor documentation**; nothing is marked "verified on hardware", because nothing was.

### 4.1 The pipeline that was traced (Volt oil life)
| Step | Where | What happens |
|---|---|---|
| UI | `Views/ServicePage` → `ServiceViewModel.RefreshServiceValuesAsync` | asks the reader; shows the value, or a "why not" line |
| Vehicle gate | `ServiceDataReaderService.ReadCoreAsync` → `ShouldProbeGmOilLife` | probes only GM-family cars / the verified Gen-1 Volt profile (identity comes from the VIN, Mode 09 `0902`, a multi-frame read) |
| Command generation | `TryReadFirstGenerationVoltOilLifeAsync` | DIDs `0x119F`, then `0x4DC0`, request `22 DD DD` to TX `7E0` / RX `7E8`; fallback: functional `22 DD DD` |
| Adapter layer | `OBDService.ReadPhysicalCanReadOnlyAsync` → `Elm327Protocol.SendPhysicalCanReadOnlyPayloadAsync` | one indivisible session: `ATL0, ATS1, ATH1, ATCAF1, ATCFC1, ATD0, ATSH7E0, ATCRA7E8`, the request, then back to generic settings |
| Transport | `ObdConnectionService` → `BluetoothService` (Classic RFCOMM / BLE) or `WifiObdTransport` | writes `cmd\r`, reads until the `>` prompt |
| Raw → text | `Elm327Protocol.CleanResponse` | removes `>` and joins lines with spaces |
| Parsing | `ServiceDataReaderService.TryExtractDidData` (`ObdResponseParser`, ISO-TP reassembly) | accepts only a positive `62 11 9F xx` from the asked CAN id; value = `xx × 100 / 255` |
| UI binding | `RebuildServiceValuePresentation` | oil card, or the failure line |

### 4.2 Findings (each with its evidence and status)
1. **The physically addressed read path required adapter commands that older/cheap chips do not have.** `ATS1`, `ATCFC1`, `ATD0` and `ATCRA 7E8` were all *required*: the first `?` aborted the whole read with "Adapter does not support required command". Elm Electronics' own **version history** (datasheet) lists `S0/S1`, `D0/D1` and `CRA hhh` as new in **v1.3** and `CRA` without argument in **v1.4b**; cheap "v1.5" adapters are widely reported to run older microcode, and the same datasheet says `?` is the answer to a command the chip does not know. *Status: confirmed defect in the code; reproduced in the simulation (the old behaviour fails with exactly that error; the same test passes now). Whether your cheap adapter really answers `?` to these: not determined.* The module scan path (`RunDtcModuleSessionAsync`) had already been made tolerant for `ATD0`/`ATCFC1`/`ATCRA`; this path was the one left strict.
2. **The functional fallback (`22 119F` to the broadcast header, which needs none of those commands) was unreachable in exactly that case.** Any exception from the physical read skipped it. *Confirmed in the code.*
3. **A failing clean-up hid a good answer.** The restore step (`ATS1` again, `ATSH7DF`, `ATH0` …) ran in a `finally` and could throw away an answer that had already been read, or hide the real error. It also left the adapter marked "not initialised" ("connect the adapter first" on the next call). *Confirmed in the code; simulated.*
4. **The page called an adapter problem an "ECU reply".** `ECU reply: Elm327CommandException …` blamed the car for something the adapter refused. *Confirmed in the code.*
5. Not defects, checked: **parsing** (frames are separated by CAN id, ISO-TP assembled, headerless fallback; foreign-module frames are never attributed), **echo/spacing** differences, **UDS needs** (a plain `0x22` in the default session — no session control or security is sent), **Wi-Fi vs Bluetooth** termination (both read to the `>` prompt).

### 4.3 What was changed (smallest set, nothing else touched)
- `Elm327Protocol` physical read path: `ATS1`, `ATCFC1`, `ATD0` are now **optional** (they are the adapter's defaults; a refusal is recorded and the read goes on). `ATL0`, `ATH1`, `ATCAF1`, `ATSH` stay **required**. `ATCRA hhh` → if refused: for the standard OBD pairing (7E0–7E7 → 7E8–7EF) the adapter's Auto Receive follows the header (datasheet "AR"), so nothing more is set; for other modules `ATCF`+`ATCM` (every firmware) and, if even that fails, no hardware filter. In every case **the app keeps only the asked module's frames** (a filter-less adapter reports `NO DATA` if only another module answered), exactly as a hardware filter would. A filter that cannot be cleared again (neither `ATCRA` nor `ATAR`) forces a full re-initialisation, as the module scan already does. The clean-up can no longer throw away an answer or mask the real error.
- `ATS1` in the shared restore step is optional too (a chip that rejects it never had its spaces changed).
- `ServiceDataReaderService`: when the physical read fails because the **adapter refused one of its addressing commands**, the functional read-only request is now tried (not after timeouts or a lost link: they would only wait twice). Notes carry a **category tag** and the **setup note** ("setup: not accepted: ATS1 → ?, ATD0 → ?; filter: …").
- `AdapterReadFailure` (new, pure): sorts a failure into *adapter command unsupported / adapter link lost / no reply in time / ECU did not answer / ECU refused (NRC text) / bus or protocol error / reply from another module / incomplete reply / unexpected reply / undetermined*. Nothing is remembered: **one failure never marks an adapter or a car incompatible**; every read tries its own setup afresh (tested).
- The Service page says **"Adapter: …"** instead of "ECU reply: …" when every failed attempt was on the adapter's side. Pack row added (7 languages, machine-written).
- Files: `Services/OBD/Elm327Protocol.cs`, `Services/OBDService.cs`, `Services/OemDiagnostics/ServiceDataReaderService.cs`, `Services/OemDiagnostics/AdapterReadFailure.cs`, `ViewModels/AdvancedViewModels.cs`, `LanguagePacks.Extra33.cs`; tests `Tests/CarScanner.Tests/AdapterCloneCompatibilityTests.cs`, simulator `Simulation/SimulatedElm327.cs`.

### 4.4 Competing hypotheses that remain (not changed — no evidence)
- **Response pending (`7F 22 78`)**: the datasheet says the chip detects it and extends the timeout only from **v2.1**; an older chip hands the pending frame to the app and the final answer is lost. The physical path does not retry on it. *If it happens, the note now shows "the ECU asked for more time (response pending)".*
- **Timing**: adaptive timing (`ATAT`) needs v1.2+; without it the answer window is the chip's fixed default (about 200 ms). A slow first answer would look like `NO DATA`.
- **Vehicle identification**: the VIN read (`0902`) is multi-frame; if a clone mishandles it the car is not recognised as GM/Volt and the DID is never asked (the page then shows an identity message, not the "Adapter:/ECU reply:" line — so the two are distinguishable).
- **Bluetooth transport quality** of a cheap adapter (partial/late replies, drops): the app resynchronises and reconnects, but a radio problem cannot be told apart from a command problem without a log.
- **The car itself**: `NO DATA` (ECM asleep, ignition off) or `7F 22 31` (identifier not available) would affect *both* adapters; both are now labelled as ECU-side.
- Same strict pattern still exists in the ISO-TP OEM path (`ConfigureCanCoreAsync`: Toyota hybrid ECU, OEM UDS services). **Deliberately not changed**: those features are gated by the Adapter Center test, which already tells the user which command the adapter refused, and nothing reported a failure there.
- Cost: on an old chip each physical read now spends a few extra (refused) round trips.

### 4.5 How to settle it with your two adapters (please do once; ~5 minutes each)
1. Same car, same cable position, ignition on. Adapter A (cheap), then adapter B (Konnwei).
2. **Adapter Center → TEST**: screenshot the whole list. It shows, per command, Passed / Failed (`ATS1`, `ATD0`, `ATCFC1`, `ATCAF1`, `ATSH`, `ATCRA`, receive-filter check, multi-frame `0902`).
3. **Service page**: pull to refresh. If no oil value shows, copy the line under the status: it now contains the raw reply of each attempt, the category tag in brackets and `setup: …` (which commands the adapter did not accept, how replies were limited).
4. Reading the result: `setup: not accepted: …` plus a value now = hypothesis 1 confirmed for that adapter; `[NO DATA …]` on both = the car/ignition; `[ECU refused: Request out of range]` = the car does not offer this identifier; `[the ECU asked for more time …]` = response pending; no "ECU reply/Adapter" line at all and an identity message = the VIN step.

Results: `AdapterCloneCompatibilityTests` 36 test cases (older-microcode clone, full-featured adapter, two adapters side by side, filter ladder, cross-talk, restore failure, failure categories, no state-changing command is ever sent, no memory of a refusal); mutation check: with the old strict `ATS1` the clone test fails with `Adapter does not support required command 'ATS1'`. Full suite **1946/1946** (was 1890 before tonight: +20 trip, +36 adapter). **All of this is simulation / unit level — no adapter, no car, no phone.**