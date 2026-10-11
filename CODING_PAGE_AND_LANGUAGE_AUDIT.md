# Coding page and language switching — audit and changes (2026-10-11)

No vehicle was connected and no app was run on a phone during this work. Everything below that says "tested" means
unit tests, source-reading tests, or a successful build; none of it proves ECU behaviour or on-screen appearance.

## 1. What the Coding page really does (before and after)

| Function | State |
|---|---|
| Full backup | **Real, read-only.** Asks every module that answers for standard UDS identification DIDs (`22 F187…F19E`, brand-specific read lists from the catalog) or GMLAN `1A` identifiers, and stores the bytes. Nothing is written. |
| Compare two backups | **Real.** Byte/bit differences between two stored backups. Needs no car. |
| Change a setting | **Code exists, unreachable today.** The write path (session `10 03`, `2E`, read-back, automatic restore, speed/engine/voltage/CAN checks, part-number gate, fresh backup first) only runs for an option the catalog marks `verified` for the exact brand, model, year, module address and part number. `coding_catalog.json` currently has **0 options**, so no write can happen on any car. |
| Restore a change | Real code, but history can only contain changes made through the path above: currently none. |
| Security access, flashing, programming, adaptations, actuator tests | **Not implemented and refused by design** (`SendCodingRequestAsync` accepts only `10 01/03`, `3E 00`, `22`, `2E`; a test pins it). |

Nothing was added that changes a vehicle. No new write command exists.

## 2. Defects found in the Coding code

1. **Foreign replies were accepted.** `CodingService.ParseReply` fell back to frames from *any* CAN id. Real captures
   (see `DIAGNOSTIC_MODULES_MILEAGE_SERVICE_AUDIT.md`) show the engine ECU's frames arriving for requests to 20+ other
   addresses. A backup could therefore store the engine's data under another module, and a foreign `6E <DID>` could be
   read as the acknowledgement of a write. Now only the asked module's reply id counts (`CodingReplyParser`); a reply
   from another id is reported as "reply from another module (0x…)".
2. **Stale module list.** The backup reused `ModuleDtcScanService.LastModules` from whichever car was scanned last. It is
   now reused only when `LastScanVehicleKey` (VIN, else make/model/year) equals the current car; otherwise the scan runs.
3. **An empty "backup" looked like a backup.** A run where no module returned any value was saved and reported as
   "Saved: N modules". It is now refused with the reason per module, and nothing is stored.
4. **A module that refuses service `22` was asked 13 times.** After `NRC 11` (service not supported) the remaining
   requests for that module are skipped.
5. **The Home tile did nothing without an adapter** (`GoToCoding` returned silently although its own comment says the
   page opens without one). It now opens; the page says "connect the adapter" and offers the button.

## 3. UI changes ([CodingPage.xaml](Views/CodingPage.xaml), [CodingViewModel.cs](ViewModels/CodingViewModel.cs))

* Vehicle card: name, VIN (last 6) or "VIN unknown", protocol, connection chip; "Connect the adapter" or "Check again".
* **Readiness** list (read-only checks): adapter, CAN, VIN, vehicle identified, speed, battery voltage (same 12.2 V limit
  as the write path). Unknown values are shown as unknown, not as OK.
* **What works for this car**: five rows (backup, compare, change a setting, restore, forbidden operations), each with a
  state and a reason. "Change a setting" says "Not verified for this car — the catalog has N verified setting(s) in total
  and none for this exact car, module and part number. Nothing will be written."
* Last-backup summary: how many modules gave values, and a line per module that did not (refused / silent / answered by
  another module).
* Error card with **Try again**; progress card with a spinner and Stop; buttons disabled while busy.
* Layout: one centred column (max 720), text wraps everywhere, action buttons moved below the text so rows do not
  overflow on narrow phones, destructive actions use the theme error colour (was a fixed red), pills/colours go through
  the theme palette for dark mode.
* The shared-file note says the file contains the VIN.

Pure logic lives in [CodingCapabilities.cs](Services/Coding/CodingCapabilities.cs) and
[CodingReplyParser.cs](Services/Coding/CodingReplyParser.cs) so it can be tested.

## 4. Language switching: root cause

The app translates every `Label` and `Button` through a hook appended to `LabelHandler.Mapper` /
`ButtonHandler.Mapper` for `Text` (`LocalizationService.RegisterDisplayTranslation`, called from `MauiProgram`).
MAUI's `Label` has `static Label() => RemapForControls();`, which runs `ReplaceMapping(nameof(Text), MapText)` the first
time `Label` is used — after `MauiProgram` had appended the hook. The replacement discards the hook, so label text was
never translated. What did translate in the screenshots (Entry placeholders, text built by view models with
`L(georgian, english)`, "Trim"/"Fuel") does not depend on the hook; every static or bound Georgian `Label` on the Home page
stayed Georgian in English and Spanish. This was established from MAUI's source (`Label.Mapper.cs`), the screenshots and
the app's code, not from a device.

**Fix:** `RuntimeHelpers.RunClassConstructor` for `Label` and `Button` before `AppendToMapping`, and a once-only guard.

## 5. Language data checked

* All Georgian text in XAML is covered by the dictionaries (3 strings were missing; added).
* English XAML text is in every language pack except names/units/addresses (RPM, OBD, PDF, CSV, JSON, PRO, VIN, e-mail
  addresses …); "ADAPTER" (de, pl), "Contact" (fr) and "Freeze Frame" (pl, es, pt, fr, it) were missing; added.
* Each of the seven packs covers at least 95 % of the English texts reachable from Georgian (about 97 %).
* Every translation row keeps the placeholders of its English text.
* All Coding page texts (capabilities, readiness, backup messages) and the module-scan diagnostics wording were added to
  all seven packs ([LanguagePacks.Extra10.cs](Services/Localization/LanguagePacks.Extra10.cs)) and, for the English-only
  diagnostics, to the Georgian pairs.

Tests: `LocalizationRegressionTests` (hook ordering, registration, dictionary and pack coverage, placeholders, Coding
texts), `CodingPageTests` (reply parser, capabilities, readiness, vehicle key, backup status, catalog invariants, coding
path allow-list).

## 6. Not checked / needs a device

* How the page looks on a phone, a tablet, in dark mode and at large font sizes (XAML builds; never rendered).
* That the language fix works on iOS and Android as read from MAUI's source (it should; it was not run).
* Other pages with texts that are built in code without `L(…)` and without a dictionary entry: a source scan found about
  750 Georgian literals not obviously covered (many are false positives: paired `en ? … : …` code), mostly in
  `InspectionChecklist`, `PrePurchaseCheckService`, `ModuleLiveDataService` and `AdvancedViewModels`. With the hook
  working they fall back to the same dictionary; the ones without an entry will stay in their source language. They
  need to be found on screen, page by page (send screenshots).
* Any ECU behaviour: backup reads, the module answers, the write path (which has no verified option to run).

## 7. First vehicle: Chevrolet Volt 2013 — readiness

| Question | Answer |
|---|---|
| Can the app change a coding setting on the Volt? | **No, and nothing can be added honestly.** The catalog has no verified option for any car, and the one real Volt capture contains no coding data. A setting needs a write + read-back verified on a real car, with the module address and part number. |
| Is the read-only backup ready? | **Code ready, untested on the car.** GM is recognised (Chevrolet/GMC/Cadillac/Buick/Opel → GMLAN). Modules come from the module scan (GM address table 0x241–0x25D, UDS `19`, then GMLAN `$A9`) or, if that finds nothing, a probe of 0x7E0–0x7E7 (`22 F190`, then GMLAN `1A 90`); each answering GMLAN module is read for 11 identification identifiers (`1A 90 99 B0 B4 C0–C4 CB CC`). |
| What does the Volt capture say about it? | The capture never asked a Volt module for `22 F1xx` or `1A`; `19 02 FF` got NO DATA from every address it tried (0x241–0x25D, 0x7E1–0x7E7, 29-bit 18DA17F1/18DA40F1). Only 0x7E0 answered (Mode 22 values). So it is **unknown** whether any Volt module answers the identification reads. |
| Gap found | The normal GM backup contains identification only. Settings are not stored there, so the "change it in the car's menu → second backup → compare" workflow would show **no difference**. Ford and VW have configuration identifiers in the catalog; GM has none. |
| What was added | **Extended read (GM)**: an opt-in, read-only sweep of GMLAN `$1A 00–FF` on each GMLAN module that already answered (stops on "service not supported", on 24 silent requests in a row, or on foreign replies; stores only what the module returns). Shown on the Coding page for GM cars, including when the make is not identified but "Chevrolet" was chosen before connecting (the choice only offers the button; it never matches a setting). The backup summary then states per module whether values were read, refused, silent or answered by another module. |
| What the first test should be | 1) Connect, ignition on / READY, car stopped. 2) Normal backup: read the summary — which modules gave values. 3) If some did: Extended read, then change ONE setting in the car's own menu (e.g. a dash/DIC option), Extended read again, select both and Compare. Share both files. That produces the evidence a catalog entry needs. 4) If the summary says "answered by another module" or "silent" for everything, that is the finding; do not repeat it with writes in mind. |
| Not covered | Whether `$1A` sweeping can disturb a Volt module (a read, so unlikely, but never tried); how long it takes (up to ~256 requests per answering module); VIN not read on the Volt (backups are then filed under "unknown" and restoring is blocked — the readiness list says so). |
