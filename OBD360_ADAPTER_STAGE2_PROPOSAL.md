# OBD360 — Adapter compatibility: Stage 2 engineering proposal

**Status (updated 2026-10-21): Stage 2A is IMPLEMENTED — items F-1, D and E, see "Stage 2A: what was built" below. Items A, B and C
(and F-2 … F-5) are still PROPOSALS: none of them is implemented.** Every remaining item needs the owner's approval, and the ones
marked **HW** also need real-adapter logs first. Everything implemented is uncommitted and verified by unit/simulator tests and an
Android emulator only; nothing has run on a phone or a car.

Date: 2026-10-20 (proposal), 2026-10-21 (Stage 2A) · Branch `feature/obd360-deep-scan-expansion` · Working tree, uncommitted.

## How to read the evidence labels

| Label | Meaning |
|---|---|
| **[V]** | **Verified fact** — read directly from the current source code (file and line given). |
| **[S]** | **Simulator result** — a deterministic test that runs the real production code against a *software* adapter. It proves what the code does, not what any real adapter or car does. |
| **[H]** | **Hypothesis** — plausible, consistent with the evidence, not proven. |
| **[HW]** | **Hardware evidence required** before the decision can be made. |

Line numbers are those of the working tree on the date above and will drift.

## The two field problems this serves (unchanged, still unexplained)

* **Chevrolet Volt 2012** — expensive adapter reads the hybrid battery; cheap adapter does not.
* **Ford Escape** — expensive adapter fails the Original Mileage read; cheap adapter succeeds.

The root causes are **not confirmed**. The proposals below are what the code and the simulator say *could* produce such
differences, ranked by how little they risk. Stage 0/1 added the logging that will tell us which one it is.

## Ranking

| Rank | ID | Proposal | Impact | Evidence today | Risk | Where it can be validated |
|---|---|---|---|---|---|---|
| 1 | **F-1** | BLE: stop re-reading a field that a link loss has just cleared | Low–medium (clear link-loss errors) | **[S]** confirmed on the real BLE code | Very low | Simulation |
| 2 | **E** | Hybrid battery: say *which* thing failed | **High** (turns a single vague message into a diagnosis) | **[S]+[V]** the messages converge | Low (text only) | Simulation |
| 3 | **D** | Original Mileage: keep the distinctions the adapter layer already knows | Medium (diagnosis, not behaviour) | **[S]+[V]** | Very low (trace text only) | Simulation |
| 4 | **A** | Module session: do not require `ATS0` | Potentially high for cheap adapters | Mechanism **[S]**; cause on a real adapter **[H]** | Medium | Simulation, then **HW gate** |
| 5 | **C** | Original Mileage: how to treat a value another ECU answered | Potentially high for the Escape | Mechanism **[S]** + one real capture **[V]**; the right policy **[HW]** | Medium–high (could hide a correct number) | **HW gate + owner decision** |
| 6 | **B** | Frame-aware parsing of joined replies | Unknown for these two cars; real for other multi-frame battery packs | Defect **[S]** | Medium (shared parser) | Simulation |

**Suggested order:** *Stage 2a* = F-1, D, E (no vehicle command changes, no dependence on hardware) →
*Stage 2b* = A and C **after** the logs from the field tests → *Stage 2c* = B, on its own.

Things that stay untouched in every proposal: ECU addresses, the Original Mileage command, the hybrid-battery request
sequence, the retry policy, protocol selection, BLE packet size / write type / MTU, TCP handling, adapter detection.

## Stage 2A: what was built (2026-10-21) — and where it differs from the proposals below

| Item | Built | Where it differs from the text below |
|---|---|---|
| **F-1** BLE lost link | `BluetoothService.SendCommandAsync` keeps its own `pending` response for the whole request (no re-read of the shared field); `MarkBleDisconnected` takes the field out with one `Interlocked.Exchange`. A lost link now reaches the caller as the `IOException` the code always set. 35 new tests; on the old code 31 of 55 BLE tests fail (all 25 repeated runs gave `NullReferenceException`), on the new code 55/55 pass. | **BLE only.** The Android Bluetooth Classic path (`SendClassicCommandAsync`, `#if ANDROID`) has the same re-read pattern, but nothing clears the field during a classic request, so no failure path was confirmed there and it was left unchanged (it cannot be run in the test project). The retry loop of the protocol layer still ends a dropped link as a timeout after its retries (F-2, unchanged by design). |
| **E** Hybrid battery failure reporting | `HybridReadDiagnosis` (pure) + evidence collected while `ReadGmPackAsync` runs: findings `SetupNotAccepted`, `ModuleSilent`, `OtherModuleAnswered`, `UnreadableReply`, `TooFewCells`, `TransportLost`; anything else keeps the old sentence. Six new sentences in all 9 languages. | **No `Elm327Protocol` change** (the proposal suggested a read-only property there). A lost link is decided from the exception type or from `OBDService.IsAdapterConnected` after the read. **No "not READY" finding exists**: the code has no reliable way to establish it, and a failed request alone does not show it. A negative response, a bus error and mixtures stay with the old sentence. |
| **D** Original Mileage trace | `MileageReadDiagnosis` (pure) words for the technical log: `[no reply]`, `[negative response]` (with NRC), `[adapter/transport]`, `[malformed reply]`, `[other ECU]`, `[unverified]`; written into the log lines and each attempt's `Detail` text only. The state of the link when a request failed is added, because the protocol layer turns a dropped link into a timeout. | **No `Elm327Protocol` or `OBDService` change.** Outcomes, evidence, confidence, the mileage selection and the decoder are untouched (pinned by tests). The page does not show an attempt's detail, so nothing on screen changed; the copied log is longer. |

What Stage 2A did NOT do (still proposals): A (`ATS0` fallback), B (frame-aware parsing), C (policy for a value another ECU answered),
F-2 … F-5. No ECU address, request, protocol-selection or retry change; no active header probe.

---

## A. Module-session compatibility (`ATS0` is required)

1. **Confirmed evidence**
   * **[V]** `DtcModuleSession.PrepareAsync` requires `ATL0`, `ATS0` and `ATH1` — `Services/OBD/Elm327Protocol.cs:885–889`
     (`RequireOkInternalAsync("ATS0")`). The *physical read* path treats `ATS1/ATD0/ATCFC1` as optional
     (`TrySetupAsync`, same file) but the session does not.
   * **[S]** `AdapterCompatibilityScenarioTests.OlderChip_TheSamePhysicalRead_Works_ButTheModuleSessionCannotStart`: on a chip that
     answers `?` to `ATS0`/`ATS1`, the same adapter reads a physical value and cannot start a session; the request is never sent.
   * **[S]** `RealVoltReaderTests.AdapterThatRefusesAts0_…` (direct, Wi-Fi, BLE): the real Volt reader throws
     `Elm327CommandException`, no cell is requested.
   * **[V]** Because the exception is not caught in the scan service, the Volt page shows the technical text
     (`HybridHealthViewModel.cs:319`: "Check failed: Adapter does not support required command 'ATS0'.").
   * **[S]** Single-frame replies parse identically with spaces on or off (`HvPackProtocol.ExtractData` strips spaces), so a
     spaces-on session would still read Volt cells.
2. **Hardware evidence required** **[HW]** — Does the *cheap* adapter actually refuse `ATS0`? The Stage 0 log answers it in one
   line: `Setup: required command ATS0 NOT accepted (reply "?" …)` and `Init ATS1: NOT accepted …`. Without that line, this
   proposal does not apply and should not be built.
3. **Files that would change** `Services/OBD/Elm327Protocol.cs` (`PrepareAsync`; a field recording whether spaces are off);
   tests only otherwise.
4. **Smallest safe implementation** Keep `ATS0` first. **Only if the adapter refuses it**, continue with the adapter's default
   (spaces on), record that in the log, and rely on the parsers' existing space handling. Adapters that accept `ATS0` send
   exactly the same commands as today.
   *Dependency:* multi-frame replies with spaces on are only safe after proposal **B** (frame-aware parsing). Until then the
   fallback would be limited to single-frame reads, which is all the Volt cells need.
5. **Tests required before the change**
   * New: a simulated chip that refuses `ATS0` — the Volt reader returns 96 cells over direct, Wi-Fi and BLE (the current
     tests that expect the exception are flipped on purpose).
   * Guard: the differential command-transcript harness (see "Safety net") shows **byte-identical** command sequences for every
     adapter that accepts `ATS0`.
   * Guard: `ATS1` is restored after the session on both paths.
6. **Regression risks** A mixed state (spaces on while the code assumes compact frames) in a code path that is not
   space-tolerant — the multi-frame case above; mitigated by limiting the fallback to the refusal case.
7. **Rollback** One hunk in `PrepareAsync`; revert it, the rest is unchanged.
8. **Success criterion** In simulation: the refusing chip reads all 96 cells and every other personality's command transcript
   is unchanged. On hardware: the cheap adapter produces cells on the Volt (if and only if `ATS0` was its problem).

**Validation:** mechanism in **simulation**; whether it matters for the owner's cheap adapter needs **hardware logs**.

---

## B. Frame-aware response parsing

1. **Confirmed evidence**
   * **[V]** `CleanResponse` joins an adapter's lines with a space (`Elm327Protocol.cs:2954`), so a multi-frame reply reaches
     callers as one line.
   * **[V]** `HvPackProtocol.ExtractData` (`Services/HvBatteryCore.cs:77`) and the session's `ApplySoftwareFilter`
     (`Elm327Protocol.cs:934`) work **line by line**. By contrast `ModuleReplyAnalysis.Split/Assemble` (used by the module scan)
     tokenises by whitespace and handles joined text.
   * **[S]** `HybridBatteryReplyVariationTests` (real parser, made-up multi-frame fixtures):
     a **2-frame** reply joined into one line → **rejected** (`null`); a **3-frame** reply joined into one line → **wrong bytes of
     the right length** (first frame's part right, the rest read from the later frames' header characters); a valid frame that
     follows an echo or another module's frame **on the same joined line** → not found.
   * **[S]** The same joined text *is* reassembled correctly by `ModuleReplyAnalysis` — two parsers in the app disagree.
   * **[S]** `RealVoltReaderTests`: on a chip with no receive filters, if the engine ECU also answers, every Volt cell is lost
     because the joined line starts with the engine's frame.
2. **Hardware evidence required** **[HW]** — Whether any multi-frame battery read (Toyota / Hyundai / Kia / Nissan packs) is
   failing or returning wrong numbers in the field today. The Volt's cell reads are single-frame, so this does **not** explain
   the Volt problem **[V]**.
3. **Files that would change** `Services/HvBatteryCore.cs` (`ExtractData`), `Services/OBD/Elm327Protocol.cs`
   (`ApplySoftwareFilter` only); tests.
4. **Smallest safe implementation** Keep today's line-based logic first. If the text is a **single line that contains the
   receive id more than once**, parse it with the existing `ModuleReplyAnalysis.Split` + `Assemble` instead. Nothing changes for
   text that has one frame per line (all existing fixtures).
5. **Tests required before the change** The six existing battery fixture suites unchanged and green; the three
   `Characterization_…` tests flipped on purpose to the correct bytes; a randomised property test: for any N frames, the
   line-per-frame text and the space-joined text give identical data.
6. **Regression risks** The shared parser serves Toyota, Hyundai/Kia, Nissan, Volt and others. A change that returns *different*
   bytes for text that used to "work" (the 3-frame case) is intended, but must be checked against real captures for each family.
7. **Rollback** Revert the two methods; behaviour returns to today's.
8. **Success criterion** All existing fixtures green and identical; the joined-line cases return exactly the bytes the
   per-line text returns; no change to any command sent.

**Validation:** entirely **simulation** (pure parsing). Real-car confirmation only for families where captures exist.

---

## C. Foreign ECU response handling (Original Mileage)

1. **Confirmed evidence**
   * **[V]** The module that is asked for the odometer may be answered by another ECU when the adapter does not apply the
     requested header. `MileageAttributionTests` documents a **real capture**: `22 DD 01` addressed to 0x7E1 was answered by
     the engine ECU on 0x7E8.
   * **[V]** When the reply id is **not** known, the decoder accepts the value and **names** the responder
     (`OdometerResponseDecoder.ExtractPayload`, `IsResponderForeign`, `OdometerResponseDecoder.cs:147`). The provider then adds it
     as **ordinary evidence** — confidence 0.8/0.7, status *Ok*, label "ECU 0x7E8 (request was addressed to 7E1)"
     (`CatalogOdometerProvider.Build`, line 284). It is named; it is not set apart.
   * **[S]** `OriginalMileagePipelineTests` (direct, Wi-Fi, BLE): with an adapter that ignores `ATSH` and no reply id, the
     engine's frames arrive, the number is extracted, the responder is 0x7E8 and flagged foreign.
   * **[S]** When the reply id **is** known, the foreign frames never reach the decoder: a hardware filter drops them, or
     `KeepOnlyFramesFrom` turns them into `NO DATA`. They look exactly like silence at the mileage layer; only the Stage 0 log
     line (`frames from 7E8x1; ForeignReply [ecu]`) shows them — and only when no hardware filter hides them.
   * **[S]** Arbitrary frames from another module are never a mileage when the reply id is known
     (`ArbitraryFramesFromAnotherModule_AreNeverAMileage_WhateverTheyContain`).
2. **Hardware evidence required** **[HW]** — For the Escape, with both adapters in the same car state: the `Read 22DD01 … ForeignReply`
   lines, the value each adapter returns, and **whether the engine ECU's number equals the dashboard**. If it does, the
   engine's answer may be *correct* and discarding it would be wrong. This is the deciding fact.
3. **Files that would change** `Services/Mileage/CatalogOdometerProvider.cs` (`Build`), possibly `MileageAnalyzer.cs` and the
   mileage wording/localization; tests.
4. **Smallest safe implementation** *(needs the owner's policy decision)* Keep reading and showing the value, but mark
   evidence whose responder is not the addressed module as **"answered by another module"** and do not let it be the *primary*
   figure unless another source agrees. No command changes.
5. **Tests required before the change** `MileageAttributionTests` unchanged; new analyzer tests for the marked evidence; the
   pipeline tests above as the contract.
6. **Regression risks** Removing or demoting a mileage that is in fact right (the Ford capture above may be exactly that).
   Also user-visible wording in nine languages.
7. **Rollback** Revert `Build`; no stored data format changes (to be re-checked against `MileageHistory` before implementing).
8. **Success criterion** With ground truth from the dashboard: the figure is shown as before when it is right, and attributed to
   the module that actually answered; a wrong-ECU figure never becomes the headline number on its own.

**Validation:** needs **hardware ground truth** and an **owner decision**. Not safe to build from simulation alone.

---

## D. Diagnostic error classification (Original Mileage)

1. **Confirmed evidence**
   * **[V]** `OdometerResponseDecoder.ExtractPayload` returns `Negative` for `NO DATA`, `?` and *any* explicit refusal
     (`OdometerResponseDecoder.cs:67`, and the 7F scan after it). The provider maps `Negative → ReadingUnavailable`,
     `NotPositiveForRequest → InvalidReading`, everything else → `ModuleNotResponding` (`CatalogOdometerProvider.cs:130, 238`).
   * **[S]** `OriginalMileagePipelineTests`: silence, `7F 22 31`, `7F 22 11` and a never-ending `7F 22 78` all land in the same
     bucket.
   * **[V]** **Any exception** from the read — adapter refused a setup command, timeout, lost link, an internal fault — becomes
     `ModuleNotResponding` with only the exception *type name* (`CatalogOdometerProvider.cs:120–126`).
   * **[V]** The adapter layer already knows the distinctions: `AdapterReadFailure.ClassifyReply/Classify`
     (`Services/OemDiagnostics/AdapterReadFailure.cs:97`) separates no reply, NO DATA, ECU refusal with its code, another
     module, incomplete, unsupported adapter command, bus error.
2. **Hardware evidence required** **[HW]** — Only to see which distinction the Escape logs actually show.
3. **Files that would change** `Services/OBD/Elm327Protocol.cs` (a read-only property holding the last read's foreign senders,
   like `LastPhysicalReadSetupNote`), `Services/OBDService.cs` (pass-through), `CatalogOdometerProvider.cs` (the *detail
   string* of the trace attempt only).
4. **Smallest safe implementation** Do **not** change the outcome enum, the evidence, or any command. Enrich the human-readable
   detail of `MileageAttempt` with what `ClassifyReply` already says ("ECU refused: Request out of range (NRC 0x31)", "only
   frames from 7E8 arrived", "adapter does not accept ATH1").
5. **Tests required before the change** Extend `OriginalMileagePipelineTests`; confirm no test asserts the old detail text
   (`MileageSimulatorTests`, `MileageHistoryTests` read the trace).
6. **Regression risks** Very low: trace text only. Check consumers of `MileageAttempt.Detail` for string matching.
7. **Rollback** Revert the detail strings.
8. **Success criterion** Each scenario in `OriginalMileagePipelineTests` yields a different detail; the evidence list and
   outcome enums are identical to today for all of them.

**Validation:** **simulation**.

---

## E. Hybrid battery failure reporting

1. **Confirmed evidence**
   * **[S]** `RealVoltReaderTests`, with the real reader: *every* one of these returns `null` and so reaches the page as the same
     sentence — car off / module silent, header ignored (engine answers instead), echo on, negative reply, 0 V cells, fewer
     than 80 cells, and (because `AskAsync` swallows every non-cancellation exception) timeouts or a lost link mid-scan.
   * **[V]** That sentence is "The battery did not answer. Switch the car on (READY) and try again."
     (`HybridHealthService.cs:112–118`, for a Volt/Bolt whose pack is `null`).
   * **[S]+[V]** The one exception: a refused `ATS0` throws and the page prints the raw text
     "Check failed: Adapter does not support required command 'ATS0'." (`HybridHealthViewModel.cs:319`).
   * **[V]** Stage 0 already computes what is needed: the module-session summary (`frames from 7E8 x3; 3 replies came only from
     other modules`, kinds, timeouts) is in the connection log.
2. **Hardware evidence required** **[HW]** — Only to tune wording; the mapping itself is testable in simulation.
3. **Files that would change** `Services/HybridHealthService.Gm.cs` (record *why* it returned `null`; signature unchanged),
   `Services/HybridHealthService.cs` (pick the sentence), `ViewModels/HybridHealthViewModel.cs`, `Services/OBD/Elm327Protocol.cs`
   (a read-only property exposing the last session's summary), `LocalizationService.Extra.cs` and a new pack batch (nine languages).
4. **Smallest safe implementation** A small internal finding (`ModuleSilent`, `TooFewCells`, `OtherModuleAnswered`,
   `SetupNotAccepted`, `Unknown`) derived from data the code already has. `Unknown` keeps today's sentence. Proposed wording
   (English; no jargon, details stay in the copied log):
   * Setup not accepted — "This adapter does not accept a setting the battery check needs. Try a different adapter."
   * Other module answered — "The adapter replied with data from another module, not from the battery. It may not support
     addressing a specific module."
   * Too few cells — "Only {0} of 96 battery cells answered. Keep the car in READY and try again."
   * Otherwise — unchanged.
5. **Tests required before the change** `RealVoltReaderTests` as the contract (each scenario → finding → sentence); localization
   regression tests; the existing strings unchanged.
6. **Regression risks** A wrong diagnosis shown with confidence — hence `Unknown` falls back to the old sentence and the
   wording says "may". Nine-language text.
7. **Rollback** Revert; additive.
8. **Success criterion** Each simulated scenario shows its own sentence; the request transcript of every scenario is
   byte-identical to today.

**Validation:** **simulation**; wording reviewed after the first field logs.

---

## F. Transport hardening

Confirmed defects are separated from things that are only possible.

### F-1 (confirmed) — BLE: a lost link during a request surfaces as `NullReferenceException`

1. **Confirmed evidence** **[V]** `BluetoothService.SendCommandAsync` waits on `_pendingResponse.Task`, then **reads the field
   again** (line 705: `await _pendingResponse.Task`); `MarkBleDisconnected` sets that field to `null` immediately after failing
   the response with an `IOException` (line 1760). **[S]** On the *real* BLE code,
   `BleTransportScenarioTests.Characterization_ALostLink_…` observes a `NullReferenceException` (it is a race; an `IOException`
   is possible). The retry loop then sees "not connected" and the request ends as `Elm327TimeoutException … failed after N
   attempt(s)` — the failure kind reads *NoReplyInTime*, not *link lost*
   (`Characterization_AtTheProtocolLayer_ALostLinkDuringACommand_…`).
2. **Hardware evidence required** None for the defect. A field log with `BLE link event: … (was connected: True)` would show
   how often it happens.
3. **Files** `Services/Bluetooth/BluetoothService.cs` (`SendCommandAsync`, the three uses of `_pendingResponse.Task`).
4. **Smallest safe implementation** Copy the field into a local once, right after it is assigned, and use the local.
   Timing, retries and packet handling are unchanged; only the exception type becomes the `IOException` the code intended.
5. **Tests before** Flip the BLE characterization test to require `IOException`; run it 100×; the rest of the BLE tests unchanged.
6. **Regression risks** Very low. Cannot be exercised on a phone from here — a single manual check (unplug the adapter during a
   read) is enough.
7. **Rollback** One hunk.
8. **Success criterion** The lost-link test sees `IOException` every time; `AdapterReadFailure.Classify` gives `AdapterLinkLost`.

**Validation:** **simulation**; optional single phone check.

### F-2 (observation, not proposed for change) — the retry loop treats a lost link as retryable
The final error is a timeout. Changing which errors are retried is a **retry-policy change** and is out of scope. If wanted
later: keep the policy, but carry the first exception as the final error's `InnerException`. Needs a separate decision.

### F-3 (possible, not proposed) — a late fragment can complete the next command
Characterized in `BleTransportScenarioTests` (a bare remainder arriving while the next command waits is returned as its reply;
the protocol layer rejects a complete reply to another PID but not a bare fragment). The existing resync/settle logic already
bounds it. A fix needs a request/response tagging scheme the ELM327 does not offer. **No change recommended.**

### F-4 (Wi-Fi) — no confirmed defect
Split replies, six terminator styles, mid-reply closure, a vanished adapter, cancellation and the silent-adapter limit all
behave as designed in the tests.

### F-5 — BLE packet size (20-byte chunks), write type, MTU, TCP settings, adapter detection
Pinned by `CommandsAreWrittenInChunksOfAtMostTwentyBytes_AsTheyAlwaysWere`. **No evidence** for changing any of them; no change
proposed.

---

## Safety net to put in place before any Stage 2 change

1. **Command-transcript differential harness** (used for this task's validation; currently in the session's scratch area).
   It runs 45 scenarios plus Wi-Fi transcripts against two builds of the protocol layer and diffs *what was sent and returned*.
   A deliberate one-character change was detected. **Recommendation:** commit it as tooling so each Stage 2 step is proven to
   change only what it claims.
2. The full test suite (2,500+ tests) and the Android build after every step; the pre-existing failure stays documented, not hidden.
3. One proposal per change set, so each can be reverted alone.

## Decisions needed from the owner

1. ~~Approve Stage 2a (F-1, D, E)?~~ Done on 2026-10-21 (built, uncommitted). Still to decide: commit it, and the optional single phone check of F-1 (unplug the adapter during a read).
2. Run the field tests (see the hardware guide) before A and C?
3. For C: if the engine ECU's mileage matches the dashboard, may it stay the headline number (attributed to the engine ECU)?
4. Commit the differential harness as repository tooling?
