# Diagnostic Modules, Mileage and Service audit — 2026-10-11

Scope of this document: what was inspected, what was found in four real capture files, what was changed, and what still
needs a real vehicle. It states only what was observed or run. **No vehicle was connected during this work.** Nothing here
is real-vehicle verification of any code path; the four capture files are the only vehicle evidence, and they are
observations (`evidenceLevel: UnreviewedObservation`), not verification.

## 1. Evidence base

Four `gcar360.capture/1` files recorded by the app itself (read-only exchanges; no VIN or account data inside):

| File | Label in the file | Observed / kept | Notes |
|---|---|---|---|
| `…-20261010-131802` | CHEVROLET Volt 2013 (capture target `chevrolet-volt`) | 1946 / 184 | |
| `…-20261011-061738` | BMW 320i 2018 | 2066 / 99 | |
| `…-20261011-071632` | FORD Escape 2012 | 1158 / 213 | |
| `…-20261011-072604` | TOYOTA Camry, year "Unknown" | 1328 / 101 | |

All four: protocol `ISO 15765-4 (CAN 11/500)`, app version 1.0.0.

### 1.1 Vehicle identity is NOT established by these files

The make/model/year are labels the app attached when recording (from its own vehicle profile or the selected capture
target). The files contain no VIN and no ECU identification string: BMW identification DIDs F187–F195 and `19 04…` got
NO DATA; no other file contains an identification reply. The user's belief (BMW, Chevrolet, Ford, Toyota) is consistent
with the labels but is **not confirmed by anything in the data**. Mode 01 "supported PID" bitmaps do not separate the cars
either: BMW and Ford both report `BE3FA813`; Toyota `BE1FA813`; Volt `BE7FB813`. In this document the cars are therefore
called by their file label, and conclusions are never drawn from the make.

### 1.2 What the four captures establish

Classification used: **O** directly observed · **D** decoded by an existing, standard implementation (SAE J1979 PID
formulas, ISO 15765-2 framing, ISO 14229 record layout) · **H** reasonable but unverified hypothesis · **I** insufficient
evidence.

| # | Finding | Class | Files |
|---|---|---|---|
| 1 | Every positive or negative reply to a module-addressed request carried CAN id `0x7E8` (the engine/OBD ECU), whichever module was addressed. Of the module-addressed items that carry frames, the frames' id equals the item's recorded reply id in: Volt 45 (all tx 0x7E0), BMW 1, Ford 69, Toyota 21 (recorded as 0x7E8 — see 2); and differs from it in: Volt 5, BMW 2, Ford 25, Toyota 29. The recorded id is the expected one for the older recorder entries and the real sender for the newer ones, so these two counts are a lower bound for the cross-address effect. | O | all |
| 2 | In the Toyota file no CAN id other than 0x7E8 ever answered, although 24 request addresses were asked. Requests labelled tx 0x7C0 (`21 41`, 15 samples) returned a 4-frame engine-style reply whose values changed from sample to sample. | O | Toyota |
| 3 | The requests probably all reached one module (the engine ECU) and the adapter/bus did not separate the addressed modules (e.g. a header that was not applied). Not the only possible explanation (stale buffered frames, gateway behaviour). | H | all |
| 4 | Ford: the same 8-byte first frame `7E8 11 B7 59 02 FF 00 11 00` (UDS 19 02 reply, announced length 0x1B7 = 439 bytes) came back for 23 different request addresses; no consecutive frames were captured. | O | Ford |
| 5 | Toyota: `22 xxxx` → `7F 22 11`, `19 02 FF` → `7F 19 11`, `21 62`/`21 CE` → `7F 21 12` (all from 7E8), while `21 41`, `21 28`, `21 82` returned positive `61 xx` replies. | O | Toyota |
| 6 | Toyota `61 41` (21 data bytes), `61 28`, `61 82`: contents not interpretable with any verified definition in this repo. | I | Toyota |
| 7 | Mode 01 standard PIDs: Volt/BMW/Ford/Toyota report supported-PID bitmaps (01–20) and answered the listed PIDs (`valid`); PID A6 (odometer) was requested in all four and got NO DATA each time; PIDs 4D/4E NO DATA in Volt/BMW/Ford, valid in Toyota. | O | all |
| 8 | Mode 03/07/0A: Volt none; BMW `03` = P0440; Ford `03/07/0A` = P0171; Toyota none (`0A` NO DATA). | O/D | all |
| 9 | Ford Mode 22 to 0x7E0: 68 of 134 requests answered positively. `22 F40C` → `0B F4` (= 765 rpm by the J1979 PID 0C formula), `22 F40D` → `00` (0 km/h). These agree with a parked, idling car. | O/D | Ford |
| 10 | Ford `22 DD 01` → `62 DD 01 03 B5 8F` (3 data bytes = 243,087). What DD01 means and in which unit is not established anywhere in this repository. | O / I | Ford |
| 11 | Volt Mode 22 to 0x7E0: 45 of 81 answered positively; 7E4/7E7 (battery DIDs from the public catalog) all NO DATA; `19 02 FF` NO DATA everywhere. | O | Volt |
| 12 | BMW: probes with the BMW tester id (11-bit 0x6F1 plus a target byte: 26 of 27 NO DATA, the 27th carried a 7E8 frame `04 43 01 04 40`, a Mode 03 style frame, filed under 0x6F1→0x601; 29-bit 0x6F1xx: 11 of 11 NO DATA). `19 02 FF` to 7E0 → `59 02 FF 19 0F 08 68`. | O | BMW |
| 13 | `19 0F 08` is shown by the app as SAE code `P190F` (FTB 08, status 0x68 = confirmed). Whether this module uses the SAE format is not established; the same car reported P0440 via Mode 03. | D / I | BMW |
| 14 | No capture establishes a module's name, a mileage value, a service capability or a manufacturer-specific decoding rule. | – | all |

`NO DATA` is silence on the asked reply id. It does not mean the module is absent; the files cannot tell absent,
asleep, unreachable and "request never delivered to that address" apart.

## 2. Diagnostic Modules

### 2.1 Pipeline traced

Adapter session (`Elm327Protocol.RunDtcModuleSessionAsync` → `DtcModuleSession`: `ATSH` header, `ATCRA` receive filter,
software filter when the adapter rejects it, flow control, answer window) → `ModuleDtcScanService.ScanAsync` (candidate
list: ISO 15765-4 `0x7E0–0x7E7`, brand table for the detected brand, BMW tester `0x6F1` + target byte, candidates from the
bundled catalog; capped at 48) → per candidate: UDS `19 02 FF`, KWP `18 02 FF 00` when the module refused, GMLAN `$A9` for
GM → reports → `FullObdScanService` → `ModuleScanViewModel.AddModuleResults` → "Modules" card. The Bluetooth and Wi-Fi
adapters share this path (it sits on `Elm327Protocol`).

### 2.2 Defects found

1. **Replies of one module were indistinguishable from "no module".** `ReadModuleAsync` returned `null` for any
   non-answer, so silence, a reply from another CAN id, a transport error and an incomplete reply all vanished from the
   result. The user could not tell why a module was missing.
2. **A multi-frame reply that stopped after its first frame was treated as silence.** `Reassemble` dropped the unfinished
   message, `AnyReply` stayed false, and for non-GM cars the address was skipped (Ford capture: the engine module's
   `59 02` first frame). The module did answer.
3. **The session published to the capture recorder under the requested reply id and for every service** (not only reads).
   That is why the capture files show the engine's frames filed under 20+ other addresses. The reply's real id is now used,
   and only services 0x19/0x21/0x22 are published.
4. **Per-address exceptions were swallowed** (debug log only) and looked like "no module"; a lost connection kept failing all
   remaining addresses one by one.
5. **Module names come from our address tables** (e.g. 0x7B0 = "ABS" for Toyota) and were shown as facts. They are brand-level
   assumptions until the module reports its own name.
6. **DTC bytes were shown only as an SAE-style letter code**, hiding the module's real bytes.

What was **not** a defect: `ParseFrames` already ignored frames from an id other than the module's reply id, so the scan
never turned the engine's reply into another module. The captures differ from the scan's reading because the *recorder*
stored the unfiltered adapter text under the requested id.

### 2.3 Changes

* `Services/OemDiagnostics/ModuleReplyAnalysis.cs` (new, no hardware/MAUI dependencies): `Split` (own frames vs frames of
  another CAN id vs status words; compact and spaced formats; malformed tokens counted), `Assemble` (ISO-TP; incomplete
  messages kept as fragments; duplicated consecutive frames tolerated; impossible lengths counted), `ExchangeAsync` (one
  request; one retry with a longer window only for "response pending" or a multi-frame reply that stalled), `ModuleProbe`
  / `ModuleProbeState` (Answered, AnsweredNegativeOnly, AnsweredIncomplete, ForeignReplyOnly, NoReply, Error).
* `Services/OemDiagnostics/ModuleDtcProbe.cs` (new): the UDS → KWP → GMLAN decision tree as a pure function over send
  delegates. It can send exactly `19 02 FF`, `18 02 FF 00` and `A9 81 12`.
* `Services/OemDiagnostics/ModuleDtcParsers.cs` (new): `ParseUds/ParseKwp/ParseGmlan/FormatCode` moved out of the
  service unchanged in behaviour, plus `RawBytesHex`. `ModuleDtcScanService.ParseUds` etc. still exist and delegate.
* `ModuleDtcScanService`: uses the probe; keeps a `ModuleProbe` per address; new `ScanOutcome.Probes`; per-address errors
  recorded and the scan stops after three consecutive transport failures, keeping partial results; summary line describes
  silent / foreign-only / incomplete / failed addresses; no identification or snapshot reads are made for an incomplete
  reply.
* `ModuleDtcReport`: `CodeListComplete`, `Diagnostics` (e.g. "Refused the trouble-code request (0x19 → NRC 0x11
  serviceNotSupported)"), `RawResponse`, `NameBasis` ("standard" / "address-table"; a module-reported name still replaces the
  table name). `ModuleDtc.RawBytesHex`. `ObdScanReport.ModuleProbes`.
* UI (`ModuleScanViewModel.AddModuleResults`): an incomplete reply is amber "answered, code list incomplete", never "no
  faults"; the detail line says when the name is from the address table and shows the diagnostics; a code without a
  catalog description shows the module's bytes and status.
* `Elm327Protocol`: the session tap reports the real sender id and only read services; `RealSenderHeader` (tested).
* The DTC snapshot decoder no longer labels Ford DID `DD01` as "Odometer … km" (see §3).

### 2.4 Separation of layers

Generic OBD-II (`FullObdScanService`, Modes 01/03/07/0A) is untouched. Transport/framing: `ModuleReplyAnalysis`.
Protocol decision: `ModuleDtcProbe`. Code decoding: `ModuleDtcParsers`. Manufacturer-specific: the `BrandModules` tables in
`ModuleDtcScanService` (one entry per brand; adding a brand does not touch another brand's entry or the probe) and the
catalog candidates. The scan never claims that generic OBD-II reaches every ECU: addresses outside `0x7E0–0x7E7` are
asked only for the detected brand.

### 2.5 Not changed / open

* Why the addressed modules did not answer in the captures is **not determined**. The scan now says so when several
  addresses only heard one other sender ("the adapter probably did not separate the addresses").
* Brand tables are unverified address lists (see the existing `OemCaptureTargetTests.ModuleScanClaims_MatchTheCode`).
* The 48-candidate cap and the order (priority, then address) are unchanged.
* `ModuleLiveDataService` and `OdometerFinderService` share the CAN-id filter (`ParseFrames`) and are covered by it;
  they do not report incomplete multi-frame replies separately.

## 3. Mileage

### 3.1 Code paths inspected

`ActualMileageService` runs the providers: `GenericObdMileageProvider` (Mode 01 PID A6, only when advertised),
`GenericObdDistanceContextProvider` (PIDs 21/31/4D/4E, context only), `CatalogOdometerProvider` (public OBDb signals for the
exact model, read-only 0x22/0x21), `BroadcastOdometerProvider` / `GmBroadcastOdometerProvider` /
`VerifiedCan*Provider` (passive broadcast, exact-profile only), `VerifiedProfileMileageProvider` / `UdsMileageReader`
(profile lists are empty for UDS; two CAN profiles, Toyota Prius 2017 and Lexus CT200h 2018, from public DBC files),
`OdometerFinderService` (learned place on this phone). `MileageAnalyzer` compares only total-odometer readings;
`MileageSourceCompatibility` decides before anything is sent; `MileageViewModel` / `MileagePage` show it. Other places
that show a distance: `ObdScanReport.OdometerKm` (always null — nothing assigns it; the report writers would print it as
"km"), the pre-purchase check (uses `ActualMileageResult`), the DTC snapshot decoder.

Already correct and unchanged: unavailable ≠ 0 (all-ones raw value = not available; zero is rejected), implausible values
are flagged and never used, miles/metres/km kept in their reported unit, `MileageAnalyzer` never picks a side in a
conflict and compares only `TotalOdometer`, no "original mileage" wording (existing wording tests), standard A6 is not
requested unless the ECU advertises it.

### 3.2 Defects found and fixed

1. **A foreign module's reply was accepted as the asked module's odometer.** `OdometerResponseDecoder.ExtractPayload`
   fell back to *any* CAN id in the reply when the expected id was absent; a positive reply that echoes the same DID was
   then credited to the addressed module. The Ford capture contains exactly this: `22 DD 01` addressed to 0x7E1 (reply
   0x7E9) was answered `7E8 … 62 DD 01 03 B5 8F` by the engine ECU. Now: when the reply id is known, frames from any other
   id are rejected as `ForeignReply` (the attempt log says which id answered); without a known reply id the responder is
   named, and when ISO 15765-4 rules say it cannot be the addressed module (e.g. a cluster at 0x7C0 answered from 0x7E8)
   the evidence is labelled with the responder, not with the cluster. The same rule was applied to the EV battery reader
   and the Service-page data reader, which had copied the fallback. (The Coding page has a similar helper; it was
   deliberately not touched.)
2. **Ford `DD01` was labelled "Odometer … km" inside DTC freeze-frame snapshots** with no source for either the meaning or
   the unit. It is now shown as "DD01 value (Ford, unit not verified)" and the number carries no unit.

### 3.3 What is and is not verified

Verified decoders (standard, from the specification): SAE J1979 PID A6 (0.1 km/bit), 21/31 (1 km), 4D/4E (1 min).
Everything else is a *public definition* applied as published (OBDb signal, DBC profile) — documented, never verified on a
vehicle by OBD360. No capture contains a mileage value from a standard or catalog source. **Nothing in the four captures
shows a decoded odometer reading.**

## 4. Service pages and procedures

Inspected: `service_procedures.json` (161 entries: all `Manual`, all `SourceDocumented`, none `VehicleVerified`),
`ServiceProcedureParser`, `ServiceMatching`, `ServiceProcedureEngine` (kind, method, compatibility, verification),
`ServiceViewModel.Procedures`, the guided list in `ServiceViewModel`, `OemOperationService` + catalog (`operations` is
empty by design), `ServiceDataReaderService` (reads only), `OBDService.WritableServiceDids` (empty),
`WriteGmlanOilLifeByteAsync` (throws), `SendCodingRequestAsync` (Coding page only).

The shared representation the task asks for already exists and is used consistently: id, name, brand/model/year, engine
and fuel restrictions, control module, required adapter capabilities, preconditions, expected result, verification
method/status, tool-required flag, execution type, OEM operation reference, and a compatibility result
(Supported / ManualProcedureOnly / PartiallySupported / CompatibilityUnknown / NotSupported with reasons).

Gaps closed: `ServiceEffect` (`ShowsStepsOnly` / `ChangesVehicleState`) and `ServiceProcedureEntry.Effect` — an explicit,
tested statement of what the app itself sends. All 161 catalog procedures are `ShowsStepsOnly`.

Behaviour for the four captured cars (listing only; nothing executed): Volt 2013 → its own oil-life guide + the owner's
manual note, manual-only; Ford Escape 2012 → two generation-level oil-life guides (2008+ menu, 2005–2014 pedals) + owner's
manual note; BMW 320i 2018 → the BMW 2004+ CBS guide + owner's manual note; Toyota Camry with unknown year → three Toyota
guides, each *partially supported* (year unknown). None is "Supported"; none is `VehicleVerified`.

No procedure was executed, and no new command was added to any executable path.

## 5. Safety

* The module scan can send only `19 02 FF`, `18 02 FF 00`, `A9 81 12` (test-enforced). Clearing codes remains a separate
  user-initiated action (`ClearDetailedAsync`) that the scan never calls (test-enforced).
* The capture tap publishes only 0x19/0x21/0x22 on both module paths (test-enforced).
* `WritableServiceDids` is empty (test-enforced); no OEM operation exists in the production catalog.
* Newly seen commands from the captures were **not** added to any target list or executed.
* Coding code, signing, application ids, secrets, deployment configuration and production data were not touched.

## 6. Test and build record

Run on this Windows machine, 2026-10-11, after the last code change:

| Command (working directory) | Result |
|---|---|
| `dotnet test -nologo` (`Tests/CarScanner.Tests`) | **Passed 1384, failed 0, skipped 0** (1290 at the start of this task; 1343 after the module-discovery and mileage tests; 1384 after the service-safety and parser tests) |
| `dotnet build CarScanner.csproj -f net10.0-android -c Release -nologo -v q` (repository root) | **0 errors**, 264 warnings (existing XAML compiled-binding warnings), 3 min 44 s |
| Backend | not changed; it is built as a dependency of the test project (Debug) and compiled without error |

New test files: `ModuleDiscoveryTests` (reply splitting, ISO-TP assembly, exchange retries, the probe decision tree, capture attribution),
`ModuleDtcParserTests`, `MileageAttributionTests`, `ServiceSafetyTests`. Fixtures are
replies copied from the four capture files.

Not run: anything on a vehicle or a real adapter; the Android app on a device/emulator; iOS/Windows targets; the
backend deployment. No UI screenshot was taken.

## 7. Recommended real-vehicle validation (not done)

1. With a Toyota (and any car where one ECU answered everything): run the module scan with a second adapter model;
   compare the summary line. Add `ATSH` read-back (`ATDP`, `AT@1`) and an `ATMA` monitor window during one request to see
   which CAN ids actually carry the request and the reply.
2. Ford: repeat `19 02 FF` to 0x7E0 with `ATST FF` and an explicit `ATFCSM1/ATFCSD300000`, record whether consecutive frames
   arrive; compare with a request length shorter than 439 bytes (`19 02 08`).
3. Identify the cars: read VIN (Mode 09 02) and `22 F190`; record the answers in a capture.
4. Odometer: compare the instrument cluster with the Ford `22 DD 01` value (243,087) at a known mileage to establish the
   unit; only then consider a verified profile.
5. BMW: capture `19 02 FF` replies from 0x7E0 and compare with the workshop tool's code list to learn whether `190F08` is a
   BMW-format code.
6. When the remaining capture files arrive: re-run the per-address table (answered / negative / silent / foreign) and
   compare it with the numbers in §1.2.

## 8. Remaining issues (honest list)

* Root cause of the captured cross-address replies is not determined (hypothesis only).
* `ObdScanReport.OdometerKm` is dead data with a hard-coded "km" in the report writers; left as is (no producer).
* `EvEnergyReader` and `ServiceDataReaderService` now share the foreign-id rule but have no dedicated tests of their own
  (they need the catalog database); the shared helper `ObdResponseParser.IsOnlyFromOtherModules` is tested.
* The Android build/test results below are from this machine only.
