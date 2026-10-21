# OBD360 — Final Release-Readiness Report: Service & Original Mileage (2026-10-21)

Required final deliverable of `OBD360_SERVICE_MILEAGE_RESEARCH_BRIEF_2026-10-21.md`, section 14. Written by the
session that ran this brief end to end. **Nothing in this report was measured on a physical vehicle.** Every claim
below is one of: a fact read directly from the code or catalog, a fact from an automated test run, a fact from a
build, or a cited external source — and each is labelled as such, never blended.

## 1. Executive summary

The brief asked for a full audit-plus-research pass on OBD360's Service (oil-life/maintenance reset, the 50%/100%
question) and Original Mileage (odometer evidence/provider validation) systems, evidence-backed improvements only,
expanded tests, a build, and a set of research ledgers/matrices/reports. This session found the existing
implementation (built up across several prior, concurrent, mostly-uncommitted sessions on 2026-10-17 to 2026-10-21)
to already be unusually rigorous and brief-compliant: the 50%/100% honesty logic, the year-evidence system, and the
BMW/Toyota `22 DFE7` mislabelling quarantine were all already correctly implemented and wired in. This session's own
contribution was: (1) an independent audit confirming all of that (not just reading the prior session's own claims
about itself), (2) one small, evidence-backed Service catalog addition (Kia EV6 2023, from an official source) after
fresh research into six previously-unresearched manufacturer families, (3) the eight named research
deliverables this brief requires, built from the existing research plus this session's own findings, and (4) a
three-times-repeated full regression run plus an Android Release build, both passing.

**No automated (ECU-write) reset is enabled for any vehicle**, exactly as before this session. **No vehicle was
connected.** Nothing was committed, pushed, deployed, or published.

## 2. Current-state architecture findings (audited, not re-derived)

* Service: UI (`ServicePage.xaml`/`AdvancedViewModels.cs`) → catalog (`Resources/Raw/service_procedures.json`,
  parsed/validated by `Services/ServiceProcedures/ServiceProcedureParser.cs`) → for the one physically-reachable
  write path, `Services/OemDiagnostics/ServiceDataReaderService.cs`'s GM/GMLAN oil-life branch. The 50%/100%
  separation (`CanOfferOilLifeTarget`), the safety checks before any write (`CheckWriteSafetyAsync`), and the
  read-back-before-success rule were all read directly in this session and found already correct — see
  `OBD360_FINAL_SERVICE_RESEARCH.md` §2 and §4 for the full trace.
* Mileage: UI (`MileagePage`/`MileageViewModel.cs`) → `Services/Mileage/ActualMileageService.cs` →
  `CatalogOdometerProvider.cs` → `MileageEvidence.cs`'s nine-way measurement-kind separation →
  `Services/OemDiagnostics/ObdbYearEvidence.cs`/`ObdbYearEvidenceStore.cs` (per-model-year evidence, fail-open) and
  `KnownMislabelledDefinitions.cs` (the DFE7 quarantine). See `OBD360_ORIGINAL_MILEAGE_RESEARCH.md` §1-§2.
* Both subsystems keep a clean separation between "a definition exists" (community data, OBDb) and "a car answered"
  (a recorded test reply) — the distinction the brief's §2.4 honesty rule requires, and it is enforced in the type
  system (`MileageSourceKind`, `ServiceVerification`/`verificationStatus`), not only in prose.

## 3. Service catalog audit — total results

* **166 guided procedures** (165 at the start of this session + 1 added, see §5), **0** automated/`OemDiagnostic`
  procedures, **0 duplicate ids**, **0 missing sources**, **0 invalid year ranges**, **every record
  `SourceDocumented`**, **0 `VehicleVerified`** — independently re-counted with a standalone script against the
  project's own `ServiceProcedureValidator` rules (`MissingId`, `DuplicateId`, `MissingBrand`, `MissingModel`,
  `MissingName`, `InvalidYearRange`, `OemWithoutOperationId`, `ManualWithOperationId`, `ManualWithoutSteps`,
  `EmptyStep`, `InvalidStepOrder`, `UnknownKind`, `MissingSource`, `UnknownVerificationStatus`).
* 83 distinct brand names (82 before this session's addition); 90 brand-wide entries, 76 model-specific.
* **Diagnostic (ECU-write) resets enabled in production: 0**, unchanged. `oem_operations.json`'s `operations` array
  is empty; `ServiceDataReaderService.ReviewedGmOilLifeResets` is `Array.Empty<...>()`.
* Full detail: `OBD360_FINAL_SERVICE_RESEARCH.md`, `OBD360_SERVICE_COVERAGE_MATRIX.csv` (166 rows, one per catalog
  entry, with make/model/year-range/operation/50%-status/100%-status/classification/evidence/source/test-status/
  limitations columns per brief §10.1).

## 4. Research sources and the source ledger

* `OBD360_SERVICE_SOURCE_LEDGER.json` (32 sources) and `OBD360_MILEAGE_EVIDENCE_LEDGER.json` (15 sources) —
  re-packaged, under the brief's §7.2 evidence vocabulary (`OEM_VERIFIED`/`CORROBORATED`/`GUIDED_VERIFIED`/
  `COMMUNITY_LEAD`/`PARTIAL_YEAR_SUPPORT`/`RESEARCH_ONLY`/`QUARANTINED`/`UNSUPPORTED`), from the existing
  `research/source-register.json` (the 2026-10-17..19 research round's own 35-source register) plus the one new
  source this session added (the Kia EV6 owner's manual).
* Full search-by-search narrative, including negative findings: `research/research-log.md`,
  `research/primary-source-findings.md`.
* No paywalled or licence-restricted document was bypassed; no large copyrighted document was stored in the repo
  (`research/README.md`'s existing licence notice covers the folder; the one new citation this session added is a
  URL + a short quoted claim, not a document copy).

## 5. 50% / 100% support findings (brief section 5)

Traced end to end this session (not re-stated from a prior report): both percentages resolve to the same honest
state for every vehicle today — **unavailable, with a clear reason shown** (`ShowOilResetUnavailable` /
`OilResetUnavailableText`, localized Georgian+English) — because the reviewed-entry table that would allow either
is empty. The code already enforces, and this session verified by reading it rather than trusting a prior claim:
a verified 100% reset does not imply 50% is supported (`AllowsPercentTargets` is a separate, per-entry flag,
defaulting false); the vehicle must be stationary and the battery ≥ 12.0 V before any write is attempted; a write
targets only the reviewed physical module address, never a broadcast; a negative ECU reply or a failed read-back is
reported as failure, never as success. **No change was made to this logic** — it already matched the brief.
Full trace: `OBD360_FINAL_SERVICE_RESEARCH.md` §4.

## 6. New Service capability actually implemented

One addition, meeting the exact evidentiary bar the other 165 pre-existing entries already meet:

* **`kia.ev6.2023.service-interval-reset`** — Kia EV6, model year 2023 only. Source: Kia's own official
  owner's-manual web help (`ownersmanual.kia.com/full_webhelp/CV1/2023/en_US/topics/t00275.html`, accessed
  2026-10-21). Driver-accessible menu path: Setup → Vehicle → Cluster → Service Interval → Reset.
  `executionType: Manual`, `verificationStatus: SourceDocumented`. Deliberately **not** extended to other EV6 model
  years (the 2024 manual page at the same URL no longer carries this path) or to other Kia models (a Kia TSB shows
  some models need the dealer's KDS tool instead, and Kia's own India Seltos manual describes the reminder with no
  driver reset step at all). Catalog version bumped 11 → 12 with a `coverageNotes` entry recording both this
  addition and the negative findings for Hyundai/Nissan/Mitsubishi below, following the catalog's own existing
  documentation convention.
* Regression tests added: `Tests/CarScanner.Tests/ServiceCatalogContentTests.cs`
  (`Catalog12_HasTheKiaEv6ServiceIntervalGuide_SourceDocumentedAndYearScoped`, plus the existing catalog-version
  assertion updated 11 → 12) and `Tests/CarScanner.Tests/ServiceGuideTextTests.cs`
  (`AKiaEv6_2023_GetsTheServiceIntervalGuide_ButOtherYearsDoNot`, which also proves the entry does **not** apply to
  a 2024 EV6 or to a Kia Sportage).
* Research that came back negative, and was **not** added (honest negative results, per brief §2.4): Hyundai (every
  source found was a third-party blog or an apparent SEO content-farm mirror; no official manual page with the
  actual steps), Nissan (the official Juke manual confirms the reminder but the step-by-step sub-page could not be
  retrieved), Mitsubishi (no official source found). Genesis, Infiniti and Suzuki were not reached in the time
  available — a gap, not a negative finding. Full detail: `OBD360_FINAL_SERVICE_RESEARCH.md` §5,
  `OBD360_UNVERIFIED_CAPABILITIES.md` §4.

## 7. Original Mileage — candidate/source research and coverage improvements actually implemented

* **No new Mileage candidate or identifier was added.** The Mileage page's provider is generic across the whole
  catalog (`CatalogOdometerProvider`), so the "no catalog entry for this brand" gap that exists on the Service side
  has no equivalent on the Mileage side — new catalog-wide coverage there is the sibling catalog-refresh session's
  scope (item C1 in `research/next-steps.md`), explicitly left untouched by this session.
* The BMW/Toyota `22 DFE7` quarantine (brief §6.5, specifically required to be re-inspected) was re-inspected: the
  evidence (343 recorded replies, odometer-sized) and the code (`KnownMislabelledDefinitions.cs`, wired into both
  the exact-model and brand-wide paths of `ServiceDataReaderService.cs`) were both read in full this session. **Not
  restored to production anywhere**, including the Mileage page, per the brief's explicit instruction — the
  evidence supports "this looks like an odometer" but not "a car's dashboard has confirmed it," which is what §6.5
  requires before promotion.
* Coverage figures (457/556 vehicles with a current-odometer definition, 343 non-debug, 302 with 2+ comparable
  modules, 85 "total odometer" rows excluded for safety) were independently re-derived from
  `research/vehicle-coverage-matrix.csv` and cross-checked against `research/coverage-report.md`; both agreed
  exactly. Full detail and the required column-shaped matrix: `OBD360_ORIGINAL_MILEAGE_RESEARCH.md`,
  `OBD360_MILEAGE_COVERAGE_MATRIX.csv` (556 rows, brief §10.2 columns).

## 8. Unsupported / unverified / quarantined items

Full register: `OBD360_UNVERIFIED_CAPABILITIES.md`. Highest-impact items: every OEM reset write request for every
manufacturer (GM, Ford, BMW, Mercedes, Stellantis, VAG, Mazda, Kia, Honda, Toyota — all researched in the prior
round, re-verified this session as still having no public byte-level request); the BMW/Toyota DFE7 Mileage
candidate; the Volt `119F` byte-scaling ambiguity; the Honda `22 7060` unit question; 85 Mileage-page rows
deliberately never requested.

## 9. Source licensing / provenance findings

Re-verified, not re-researched: OBDb (CC-BY-SA-4.0, attribution already in `THIRD_PARTY_NOTICES.md`), opendbc (MIT),
Wikipedia OBD-II PIDs text (CC BY-SA 4.0), `arduino-psa-diag` (GPL-3.0, quarantined — not used), `l77rodeo/gmlan` and
`emdzej/ediabasx-docs-sgbd` (no licence — leads only, never a basis for a shipped identifier). This session's one new
source (Kia's owner's manual) is manufacturer-published documentation, cited for its facts only — no bulk text was
copied into the repository.

## 10. Test results

* **Phase 0 baseline** (before any change): `dotnet test` → **2764 passed, 1 failed, 2765 total**. The one failure
  is the known, pre-existing, explicitly-preserved `ObdbYearEvidenceTests.TheMileagePage_SaysWhenARecordedReplyExistsForTheModelYear`
  (brief §9.4 names this test by name as the one to preserve). Confirmed by **three consecutive runs**, byte-identical
  result each time (durations 1m47s / 1m57s / 2m05s).
* **After this session's one catalog addition and two new tests**: `dotnet test` → **2766 passed, 1 failed, 2767
  total** — exactly +2 passed (the two new tests) and the same one pre-existing failure, untouched. No other test
  changed outcome.
* **Targeted re-run** of the Service-catalog test files alone, before the fix to the `fuelTypes` field (see §11):
  caught a real classification mismatch this session introduced (`PartiallySupported` instead of the expected
  `ManualProcedureOnly`) — fixed in the data, not in the test, since the data was wrong (an unnecessary
  single-powertrain restriction on an EV-only model caused the engine to report "missing fuel info" rather than a
  full match). This is reported here as a genuine catch, not glossed over.
* No test was deleted, weakened, or had its assertion loosened to pass.

## 11. Android build and artifact verification

* `dotnet build CarScanner.csproj -c Release -f net10.0-android` → **0 errors, 145 warnings** (all resource-merge
  notes from `aapt2`, none about this session's changes). Build time 24.7s (incremental against an already-restored
  tree from a prior session's build).
* **No new installable APK was produced or needed**: this session's only functional change is one data-only catalog
  entry plus two unit tests — already fully exercised by the test suite; it does not change compiled app behaviour
  in a way a new on-device install would additionally verify.
* **Prior verified Release artifacts, confirmed byte-for-byte unchanged by this session** (SHA-256, re-hashed):
  | Artifact | SHA-256 |
  |---|---|
  | `bin/DiagnosticApk/com.obd360.app-Signed.apk` | `937171505227961386836f1ab883c3b94535cd470fd5ab2f392518daa222554c` |
  | `bin/DiagnosticApk-Stage2A/com.obd360.app-Signed.apk` | `8caf5a446d5e540ddff647573edb96412a882b404e960d87749e734904e79d02` |
  | `bin/DiagnosticApk-Stage2C/com.obd360.app-Signed.apk` | `6e5f97c37bedcbe2db667e652b66f7e5eeb83fe2114936a0f293f5bb46493ef4` |
* Package identity confirmed unchanged by this session: `ApplicationId com.obd360.app` (Android) /
  `com.gcar360.app` (iOS/Mac Catalyst), `ApplicationDisplayVersion 1.0.0`, `ApplicationVersion 62` —
  `CarScanner.csproj`'s diff for this session is empty (the file shows as modified in `git status` only because of
  other, pre-existing sessions' uncommitted work, not this one's).

## 12. Exact files changed by this session, and why

| File | Change | Why |
|---|---|---|
| `Resources/Raw/service_procedures.json` | +1 procedure (`kia.ev6.2023.service-interval-reset`), `catalogVersion` 11→12, `catalogGenerated` date updated, one `coverageNotes` entry added | The one evidence-backed Service addition this session made (§6) |
| `Tests/CarScanner.Tests/ServiceCatalogContentTests.cs` | Renamed/updated the catalog-version test (11→12), added `Catalog12_HasTheKiaEv6ServiceIntervalGuide_SourceDocumentedAndYearScoped` | Regression coverage for the new entry |
| `Tests/CarScanner.Tests/ServiceGuideTextTests.cs` | Added `AKiaEv6_2023_GetsTheServiceIntervalGuide_ButOtherYearsDoNot` | Regression coverage proving year/model scoping |
| `OBD360_FINAL_SERVICE_RESEARCH.md`, `OBD360_ORIGINAL_MILEAGE_RESEARCH.md`, `OBD360_SERVICE_COVERAGE_MATRIX.csv`, `OBD360_MILEAGE_COVERAGE_MATRIX.csv`, `OBD360_SERVICE_SOURCE_LEDGER.json`, `OBD360_MILEAGE_EVIDENCE_LEDGER.json`, `OBD360_UNVERIFIED_CAPABILITIES.md`, `OBD360_FINAL_RELEASE_READINESS_REPORT.md` (this file) | New files | The brief's required section-10/14 deliverables |

No other file was modified, moved, or deleted by this session. No code outside `Services/ServiceProcedures` test
coverage and the one catalog file was touched. Mode 05/06 was not touched. No commit, push, deploy, or publish
action was taken.

## 13. Quantified before/after coverage

| Measure | Before this session | After |
|---|---|---|
| Service guided procedures | 165 | 166 |
| Service brands with a catalog entry (of the 7 researched this session) | 1 (Hyundai, TPMS only — a different system) | 2 (+ Kia, EV6 2023 Service Interval) |
| Service automated (ECU-write) operations enabled | 0 | 0 (unchanged) |
| Service catalog issues (duplicate/missing/invalid) | 0 | 0 (unchanged) |
| Mileage candidates added | — | 0 (unchanged; out of this session's reach, see §7) |
| Mileage vehicles with a current-odometer definition | 457 / 556 | 457 / 556 (unchanged) |
| Full-suite tests passing | 2764 / 2765 | 2766 / 2767 |
| Research deliverable files required by brief §10 | 0 present | 8 / 8 present |

## 14. Known limitations and required physical validation

Everything in `OBD360_UNVERIFIED_CAPABILITIES.md` needs a physical vehicle, an owner decision, or a paywalled source
this project does not have access to. Highest-priority items if the owner wants to invest further:
a Chevrolet Volt capture (read path already implemented; write path blocked pending evidence), one BMW confirming
`22 DFE7` against its own dashboard, and Genesis/Infiniti/Suzuki research (not reached this session).

## 15. Final Git status and confirmation that unrelated work remains intact

Branch `feature/obd360-deep-scan-expansion`, up to date with `origin/feature/obd360-deep-scan-expansion`, HEAD at
`a336417`. This session created 8 new files (§12) and modified exactly 3 existing files (§12) — nothing else. The
substantial pre-existing uncommitted work from other, concurrent sessions — the Stage 0/1/2A adapter-compatibility
work (`Services/HybridReadDiagnosis.cs`, `Services/Mileage/MileageReadDiagnosis.cs`,
`Services/OBD/AdapterDiagnosticCapture.cs`, `Services/OBD/AdapterExchangeClassifier.cs`, and their tests under
`Tests/CarScanner.Tests/Adapter*`, `Tests/CarScanner.Tests/Simulation/`), the round-2 Service/Mileage research
(`Services/OemDiagnostics/ObdbYearEvidence*.cs`, `KnownMislabelledDefinitions.cs`, `Resources/Raw/obdb_year_evidence.json`),
the "Enter manually" search feature (`Services/OemDiagnostics/ServiceSearchMatching.cs`,
`Services/Mileage/MileageManualMatch.cs`, `LanguagePacks.Extra34-38.cs`), and the sibling catalog-refresh session's
in-progress files (`research/catalog-refresh-*`, `research/tools/*.js`,
`Tests/CarScanner.Tests/CandidateCatalogCompatibilityTests.cs`) — were all read where directly relevant to this
brief, and **none of them was modified, staged, or discarded** by this session. `git status --porcelain` at the end
of this session shows exactly this session's 8 new files and 3 modified files beyond the pre-existing state recorded
at the start.

## 16. Verified vs. simulated vs. hardware-confirmed — the one distinction that matters most

* **Source-verified** (an external document says this): every claim in §9 and §4; the Kia EV6 menu path (§6); the
  negative findings for Hyundai/Nissan/Mitsubishi (§6).
* **Code/test-verified** (read directly or confirmed by an automated run in this session): the 50%/100% logic (§5),
  the catalog audit counts (§3), the test results (§10), the build result (§11), the artifact hashes (§11), the git
  status (§15).
* **Hardware-confirmed**: **nothing**. No vehicle was connected during this session or the research it builds on.
  Every number in this report that sounds like a capability is a documented or coded *possibility*, not an observed
  fact from a real car — exactly the distinction the brief's §2.4 requires, and it is preserved end to end.
