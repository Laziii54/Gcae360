# OBD360 — Final Service research report

Written for `OBD360_SERVICE_MILEAGE_RESEARCH_BRIEF_2026-10-21.md`, section 10. This is a short, cross-referencing report, not a
second copy of the research: the detailed figures, per-source records and per-brand findings already exist in `research/` (dated
2026-10-17 to 2026-10-19) and in the uncommitted round-2 code changes this session audited but did not need to redo. Nothing in
this file was measured on a vehicle. No OEM command, identifier or payload was invented by this report or by the session that
wrote it.

## 1. What exists today (audited this session, not re-derived)

* **Catalog**: `Resources/Raw/service_procedures.json` — 165 guided procedures at the start of this session (166 after this
  session's one addition — the Kia EV6, §5 below), independently re-verified this session (a small
  script re-ran the duplicate-id, missing-field, year-range and verification-status checks `Services/ServiceProcedures/ServiceProcedureParser.cs`
  already enforces): 0 duplicate ids, 0 missing sources, 0 invalid year ranges, every record `SourceDocumented`. 89 are brand-wide
  (`model: "*"`), 76 model-specific, 3 are explicit generic fallbacks (`general.any.owners-manual` and two further catch-alls with
  no verified brand-specific method) and are labelled as such, not as a specific vehicle's procedure.
* **Diagnostic (ECU-write) resets**: `oem_operations.json` — `operations` is empty; 12 brand-family and 73 vehicle research
  candidates, every one recorded as non-executable. Confirmed by reading `Services/OemDiagnostics/ServiceDataReaderService.cs`:
  `ReviewedGmOilLifeResets` (the only table an automated write is ever read from) is `Array.Empty<ReviewedGmOilLifeReset>()`. **Zero
  vehicles have an automated oil-life reset today.**
* **50% vs. 100%** (brief section 5): the code already keeps these as two separate questions, correctly. `CanOfferOilLifeTarget(100)`
  needs a reviewed entry; `CanOfferOilLifeTarget(50)` additionally needs that entry to say `AllowsPercentTargets: true`. Since the
  reviewed table is empty, neither is offered for any vehicle today — this is the honest state the evidence supports, not a bug.
  The UI (`ViewModels/AdvancedViewModels.cs`) exposes `ShowOilResetUnavailable` so the card explicitly says "unavailable" rather than
  disappearing silently when `CanResetOilLife` is false.
* **The BMW/MINI/Toyota `22 DFE7` quarantine** (brief section 6.5, asked to be re-inspected): `Services/OemDiagnostics/KnownMislabelledDefinitions.cs`
  (new, uncommitted, audited this session) excludes this request from both the exact-model and brand-wide-fallback candidate paths
  in `ServiceDataReaderService.cs`, with the evidence inline (343 recorded replies, 49–74% within 50 km / 0.2% of a same-year
  odometer value per model). This session's own re-reading of `KnownMislabelledDefinitions.cs` and its two call sites confirms the
  exclusion is wired into both candidate-building passes and is logically sound: the quarantine is **not restored**, as the brief
  requires absent new evidence to the contrary. No new evidence was found this session.
* **Model-year evidence** (`ObdbYearEvidence.cs` / `ObdbYearEvidenceStore.cs`, uncommitted, audited this session): pure,
  fail-open (an unreadable or missing asset gives `Empty`, and the app behaves exactly as before the evidence existed), correctly
  distinguishes "applicable this year" / "still under test this year" / "a reply was recorded this year" from the catalog's single
  command-level `debug` flag. Wired into `ServiceDataReaderService.cs`'s candidate loader for both the exact-model and brand-wide
  paths.

## 2. Research already completed (2026-10-17 to 2026-10-19, before this session)

Summarised here; full detail in `research/research-log.md`, `research/primary-source-findings.md`, `research/source-register.json`
(35 sources, reshaped by domain into `OBD360_SERVICE_SOURCE_LEDGER.json`), `research/coverage-report.md`.

* **No OEM oil-life/service reset write command is publicly documented** for any manufacturer searched (GM, Ford, BMW,
  Mercedes, Stellantis, VAG, Mazda, Kia, Honda, Toyota) at Tier 1–4. Every maker's own bulletin or workshop document names the
  module and the scan-tool function, never the byte-level request. `arduino-psa-diag` and `l77rodeo/gmlan` were the only two
  open-source leads with partial relevant content (GMLAN `0x1F1` carries an oil-life reset, also a remote-start and immobilizer
  function — not reused because of the shared, security-relevant identifier; PSA's diag sketch has nothing about a service reset).
* **GM's own bulletins (12312A, 18-NA-125, PIP5282A, PIP6076, read in full by `pdftotext`)** confirm GM's own tool can set the oil
  life to a chosen value on 2010-2012 2.4L cars — a capability statement, not a published request. This is the only source anywhere
  in the research that supports "a chosen percentage" as a real OEM capability for any vehicle; it is not reduced to an app
  capability because the request itself is never published.
* **165 guided (dashboard/menu) procedures are shipped**, sourced from official owner's manuals, brand support pages and (for the
  SKYACTIV-D 2.2) the official Mazda owner method. Guided procedures exist for 84 brand names; 24 of those also have at least one
  Service-read definition in the shipped OBDb catalog.

## 3. New research this session

This session searched (WebSearch + WebFetch, primary-source-first) for owner's-manual-level guided (dashboard/menu)
service-reminder reset procedures for **Hyundai, Kia, Genesis, Nissan, Infiniti, Mitsubishi and Suzuki** — the brand
families `research/next-steps.md` flagged as having no catalog entry and no prior dedicated search. Findings are in
section 5 below. (A background research fork was launched first for this; it deviated into also writing this
session's own report files, was stopped, and its research was redone directly by this session instead — see the
session's own final report to the owner for that detail. Nothing from the fork's research output was used; every
claim below was independently found and read by this session.)

## 4. 50% / 100% honesty review (brief section 5, acceptance criterion 6)

Traced end to end: `ServicePage.xaml` → `AdvancedViewModels.cs` (`CanPressOilReset`, `CanSetOilLifeHalf`, `ShowOilResetUnavailable`) →
`ServiceDataReaderService.CanOfferOilLifeTarget` / `ResetGmOilLifeAsync` → `IObdService` GMLAN write → read-back (`ReadReviewedOilLifePercentAsync`)
before success is reported. Findings:

* No shipped vehicle can reach the automated-write branch at all (`ReviewedGmOilLifeResets` is empty), so no UI state currently shows
  "verified automated" for any vehicle — honestly, because none is verified.
* `ResetGmOilLifeAsync` refuses before any vehicle I/O when `_reviewedGmReset` is null, when the car is moving or module voltage is
  low, and refuses a 50%-style target specifically when `AllowsPercentTargets` is false — matching the brief's "a verified 100%
  reset is not proof 50% is supported" requirement exactly.
* A negative ECU reply and a read-back mismatch are both handled as failure, not as success (`OilLifeResetResult.NotSent` /
  the read-back comparison in `ResetGmOilLifeAsync`, read in full this session).
* Guided (manual) procedures are never represented as an executed ECU operation: `ServiceProcedureExecutionType.Manual` records
  carry no `oemOperationId` (enforced by `ManualWithOperationId`/`OemWithoutOperationId` in `ServiceProcedureValidator`), and the UI
  path for a guided procedure only ever shows instructions, never a "success" state tied to a read-back.

No change was made to this subsystem: the existing design already satisfies the brief's section 5 requirements.

## 5. Manufacturer coverage gap — Hyundai/Kia/Genesis, Nissan/Infiniti, Mitsubishi/Suzuki

One addition, scoped tightly to what the source actually documents:

* **Kia EV6 (model year 2023 only), catalog id `kia.ev6.2023.service-interval-reset`.** Source: Kia's own official
  owner's-manual web help, `ownersmanual.kia.com/full_webhelp/CV1/2023/en_US/topics/t00275.html` ("CV1" is Kia's
  internal platform/chassis code for the EV6), accessed 2026-10-21. It gives the exact driver-accessible menu path:
  **Setup → Vehicle → Cluster → Service Interval → Reset**, and that the "Service in" reminder appears once 900 mi
  / 1,500 km or 30 days remain. **Deliberately not extended to other model years**: the 2024 US manual page at the
  same URL slug no longer carries this path (Service Interval is only a navigation link there, pointing elsewhere),
  so a 2024+ EV6 is left with no entry rather than an assumed one. `executionType: Manual`,
  `verificationStatus: SourceDocumented` — identical evidentiary bar to the other 165 guides already in the catalog;
  no new field or exception was added to accommodate it.

Everything else searched came back negative or too weak to add, and is recorded as a negative finding rather than
silently omitted:

* **Hyundai**: every result (for Tucson, Santa Fe, Palisade, Sonata oil-life resets) was a third-party blog, a
  video-index page, or what looked like an SEO content-farm mirror (`api.bybrand.io`, `erp.blackboxoperations.com`
  and similar). No `hyundaiusa.com` or `hyundai.com` page with the actual steps was found in either a general or a
  `site:`-restricted search. Not added.
* **Kia, other models**: a parallel official source — Kia's own **India Seltos** manual page
  (`kia.com/content/dam/kia2/in/en/content/new-seltos-manual/topics/t00144.html`) — describes the Service Interval
  reminder but **does not document a driver-facing reset step at all**. A Kia TSB (NHTSA-hosted, `ELE209` Rev 3,
  already in the source register) says some models need the dealer's **KDS** tool to switch the cluster from
  "Workshop" to "Customer Mode" before the interval can even be set. This is direct evidence that the EV6's
  driver-menu path **does not generalise** to other Kia models or platforms, exactly the mistake brief §2.2 warns
  against — so no brand-wide Kia entry was added, only the one EV6/2023 entry the source actually supports.
* **Nissan**: the official regional owner's-manual pages for the Juke (`nissan.ie`, `nissan.com.mt`) confirm the
  oil-replacement indicator exists and must be reset manually after an oil change ("the indicator does not reset
  automatically"), but the step-by-step procedure is on a linked sub-page this tool could not successfully retrieve
  (the link target did not resolve through WebFetch). Third-party guides for the Altima, Sentra and Quest give
  mutually inconsistent menu paths and are not Nissan-sourced. Not added — the evidence that a procedure *exists* is
  solid, but the exact steps are not confirmed from an official source.
* **Mitsubishi**: no official manual or support page was found for any model (Outlander searched specifically); only
  third-party sites and what appear to be mirror/spam PDF hosts. Not added.
* **Genesis, Infiniti, Suzuki**: not reached in the time available for this pass: a genuine gap, not a negative
  finding, and listed as such in `OBD360_UNVERIFIED_CAPABILITIES.md`.

This addition was made directly into `Resources/Raw/service_procedures.json` (catalog version bumped 11 → 12, with
a `coverageNotes` entry recording both the addition and the negative findings above, following the same convention
the catalog already uses for "researched but not added" entries), with a matching regression test
(`Tests/CarScanner.Tests/ServiceCatalogContentTests.cs`, `ServiceGuideTextTests.cs`) confirming: the entry loads
without issues, is year-scoped (a 2024 EV6 does not get it), and is model-scoped (a Kia Sportage does not get it).
This is consistent with how the project's existing 165 entries were added by prior sessions directly, at the same
evidentiary bar — not a "redesign" requiring a separate proposal step.

## 6. What was not done

* No vehicle was connected; no capture was made; no reset or write was sent or designed.
* The six-brand gap research (section 5) is owner's-manual-level only; professional tool documentation (Techstream-equivalent for
  these brands: Hyundai/Kia GDS/KDS, Nissan Consult, Mitsubishi MUT-III) was not purchased or accessed — paywalled.
* The catalog refresh (`research/catalog-refresh-proposal.md`, item C1) is explicitly out of scope for this session: it is being
  worked by a separate concurrent session in this same checkout (`research/tools/build-candidate-catalog.js` and related files,
  left untouched).
