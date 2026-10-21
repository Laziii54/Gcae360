# OBD360 — Final Original Mileage research report

Written for `OBD360_SERVICE_MILEAGE_RESEARCH_BRIEF_2026-10-21.md`, section 10. Like its Service counterpart, this cross-references
the detailed work already in `research/` rather than duplicating it. Nothing here was measured on a vehicle; no DID, request or
scaling factor was invented.

## 1. What exists today (audited this session)

* **`ActualMileageService.cs` / `CatalogOdometerProvider.cs` / `MileageEvidence.cs`**: read (not re-derived in full this session —
  the prior round's `research/coverage-report.md` and `research/mileage-source-catalog.json` already trace this path in detail).
  Confirmed this session: the Mileage page requests the exact model's definitions whether or not OBDb flags them debug, and labels
  a reading "under test" or "a reply was recorded for this model year" using the per-signal `obdb_year_evidence.json`, not the
  catalog's single command-level flag — matching brief section 6.1's "find opportunities to use existing verified data better
  before adding new identifiers" directly.
* **The shipped catalog**: 556 vehicles, 157,229 OBDb rows (revision `3fd5afe5ec`, 2026-10-17). 457 vehicles (82.2%) have a
  current-odometer definition the Mileage page can request; 343 of those are non-debug; 302 vehicles (54.3%) have two or more
  modules, so a cross-module comparison is possible. Figures re-confirmed from `research/vehicle-coverage-matrix.csv` this
  session (programmatic re-check, see `OBD360_MILEAGE_COVERAGE_MATRIX.csv` below); not re-generated from the catalog from
  scratch, since the generator (`research/tools/build-research-files.js`) is shared with the sibling catalog-refresh work
  presently in progress in this checkout and was not re-run to avoid touching files in flight.
* **The BMW/MINI/Toyota `22 DFE7` candidate** (brief 6.5): confirmed quarantined on the Service side only
  (`KnownMislabelledDefinitions.cs`); it was never an active Mileage-page candidate (the Mileage page never requested this
  identifier), so there is nothing to additionally quarantine there. It remains a plausible further Mileage-page module
  (343 recorded replies, odometer-sized) **pending a real BMW confirming it against the dashboard** — not restored to any
  production path by this session, per the brief's explicit instruction.
* **Distinguishing measurement kinds** (brief 6.2): confirmed in code — `MileageMeasurement.TotalOdometer` vs. `OdometerAtEvent`
  (history) vs. `ModuleInstallRecord` vs. `ComponentDistance` are kept as separate enum values throughout `MileageEvidence.cs` and
  its consumers; the new "Enter manually" matcher (`Services/Mileage/MileageManualMatch.cs`, uncommitted, audited this session)
  restricts itself to `TotalOdometer` rows only (`Rank()`'s `.Where` clause), so a trip counter or a "since DTC clear" distance can
  never be offered as a match for a typed dashboard odometer value.

## 2. Research already completed (2026-10-17 to 2026-10-19)

* Cloned `commaai/opendbc` (MIT) and OBDb's full organisation (880 repositories, CC BY-SA 4.0): 226,798 signals, 609 vehicles with
  at least one signal; of odometer-named CAN broadcast fields, 36 exist and 11 are already consumed by `BroadcastOdometerProvider`.
* Built per-model-year evidence from OBDb's own recorded test replies (not just the community's "debug" flag): 334 of 461
  vehicles with an odometer definition have at least one recorded reply; 140 have two modules recorded in the same model year; 116
  of those form a tracking pair. 38 of 95 oil-life vehicles have a recorded reply (not 39 "non-debug" ones — a correction the
  round-2 research made to its own round-1 figure; both sets are different vehicles, see `research/research-log.md`, "Mistakes of
  the first round").
* Found and documented that 47 recorded module pairs (≥5 values each, same model year) disagree more than 90% of the time —
  VW 7E0/7E1/714, Honda DA10/DA60, Toyota Prius, Ford F-150 Lightning among them — which `research/next-steps.md` item C.1d flags
  as possibly a definition problem, not a genuine mileage conflict. Left as an open research item; not acted on this session (it
  would change how a "sources differ" finding is worded, which needs a decision, not just a data fix).

## 3. What this session added

* Independently re-derived `OBD360_MILEAGE_COVERAGE_MATRIX.csv` (556 rows) from the existing, already-generated
  `research/vehicle-coverage-matrix.csv`, reshaped into the exact column set brief section 10.2 asks for. No new count was
  computed; every number in it traces back to the existing generated file, which traces back to the shipped catalog via
  `research/tools/build-research-files.js` and the app's own classifiers (`MileageSignalClassifier`).
* Re-verified, by reading the code directly rather than trusting the prior report, that `ObdbYearEvidenceStore` fails open (an
  unreadable asset gives empty evidence, not an exception) and that the asset is picked up by the `.csproj`'s existing
  `MauiAsset Include="Resources\Raw\**"` glob — no project-file change was needed or made.
* No new Mileage candidate was added. The research gap this session could address without a vehicle (owner's-manual-level
  guided procedures for under-covered brands) is a Service-side question, not a Mileage-side one — the Mileage page's provider is
  generic across the catalog (`CatalogOdometerProvider`), so there is no equivalent "missing brand" gap to research without new
  OBDb signal data, which is the sibling catalog-refresh session's scope (item C1), not this session's.

## 4. What was not done

* No vehicle was connected; no capture was made.
* Item C1 (the catalog refresh — "the biggest gain", per `research/next-steps.md`) is being worked by a separate concurrent
  session (`research/tools/build-candidate-catalog.js`, `check-explorer-equivalence.js`, `fetch-obdb-snapshot.js`,
  `obdb-import-lib.js`, `render-diff-report.js`, `verify-dbgfilter-semantics.js`, and `Tests/CarScanner.Tests/CandidateCatalogCompatibilityTests.cs`,
  all untracked and left untouched by this session).
* The module-tracking-disagreement question (section 2 above) and the Honda `22 7060` unit question (`research/next-steps.md`
  item B.1) both need either a real vehicle or an explicit wording decision from the owner; neither was decided this session.
