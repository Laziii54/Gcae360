# Research: Service-page operations and multi-module mileage

This folder holds the research behind two parts of OBD360 and nothing else:

1. **Service page**: reading the oil life, resetting it, setting a chosen value, and resetting the Service Due / Maintenance Required /
   inspection / oil-change reminders, per vehicle.
2. **Mileage page**: reading the distance the different modules of a car keep, keeping the readings apart, and comparing only what can be
   compared.

It changes no behaviour of the app. The app changes made with it are listed in the final report of this work, each with its reason.
**Nothing in this folder was measured on a vehicle.** Every figure counts public definitions; a definition says where a value is supposed to
be, and only a positive reply from a real car shows that it is.

## Files

| File | What it is |
|---|---|
| `coverage-report.md` | The numbers in one readable report: what the shipped catalog can read per make, how many vehicles can be compared, the limits. Generated. |
| `service-operation-coverage.json` | The Service operations (read, reset, set, clear, interval), what the app does with each today, the evidence per brand family, the guided procedures and the research candidates the app already ships. Curated text + generated figures. |
| `oil-life-coverage.json` | Oil life remaining, oil degradation (the opposite scale), service distance and days, per make and per request. Generated. |
| `mileage-source-catalog.json` | Every place a car keeps distance (standard PIDs, module copies, history records, installation records, component counters, CAN broadcasts, candidates), the evidence, the comparison rules, and the CAN odometer fields of opendbc with an assessment of each. Curated text + generated figures. |
| `vehicle-coverage-matrix.csv` | One row per vehicle of the catalog (556): the odometer sources, the history records and the service readings it has. Generated. Counts of definitions. |
| `obdb-evidence-report.md`, `obdb-evidence-summary.json`, `obdb-evidence-signals.json` | The same signals by **model-year evidence**: for every odometer-like and service-like definition, the years in which OBDb says it applies, still flags it debug, lists it as supported and has a reply recorded in its tests; which definitions hold something else than their name says; which odometer modules track each other. Generated. This is the figure to use for "confirmed" support. |
| `oil-life-capability-matrix.json` / `.csv` | For every vehicle with an oil-life or oil-degradation definition: can it be read (and with what evidence), reset to 100% (guided procedures, automatic: none), set to a chosen percentage, reset as a reminder only, and what the adapter has to do. Generated. |
| `primary-source-findings.md` | Round 2: what the makers' own bulletins, manuals and legitimate open data say about who executes each reset, which tool function does it, how the result can be verified, and what is still missing. |
| `catalog-refresh-proposal.md` | Item C1: the proposal for a refreshed OBDb part of the bundled catalog (additive schema, one revision, model years, module types), how the pages could use it, what needs a decision. Nothing adopted; the candidate catalog is outside the repository. |
| `catalog-refresh-diff-report.md`, `catalog-refresh-report.json`, `catalog-refresh-explorer-equivalence.json`, `catalog-refresh-dbgfilter-semantics.json` | The numbers behind it: candidate against the shipped catalog (vehicles, rows, debug share, rows that gain a year restriction or a module type), how the shipped data was made and proof that the importer reproduces it, what a `dbgfilter` means. The Markdown is generated from the JSON by `tools/render-diff-report.js`. |
| `source-register.json` | Every source consulted: licence, what was read, evidence level, what was done with it. |
| `research-log.md` | What was searched and fetched, including what was not found and what was wrong. |
| `next-steps.md` | The remaining research plan. |
| `tools/` | The scripts that produce the generated files, and the curated inputs (`tools/curated/`). |

## Evidence levels

| Level | Meaning |
|---|---|
| E0 | Nothing found. A negative finding. |
| E1 | A lead: the function exists (closed tool, vendor claim, forum post), no protocol detail is public. |
| E2 | A public definition (standard or community data) that says where a value is. Not reviewed against a vehicle by this project. |
| E3 | The maker's own document names the procedure or tool function for an exact vehicle (owner's manual, service bulletin). For a dashboard-level reset that is the whole procedure; for a scan-tool function it names the module and the function but does not publish the request. |
| E4 | A reply to the request was recorded for that model year (OBDb's own tests, or a capture of that exact vehicle). Recorded by someone else; not by OBD360. |
| E5 | Verified on that exact vehicle by this project: write or routine, read-back and dashboard result, with hashed evidence. |

No operation that changes a car reaches E5 anywhere in the repository, so none is offered. A read needs E2 for the exact model.

## Rules this research kept

* No identifier, command, byte or formula was invented. Where a source gives a name but no payload, the entry says "no payload".
* Reading and writing are separate. A definition to read a value never authorises writing it.
* A reset to 100% and the setting of a chosen value (for example 50%) are different capabilities; the second needs its own evidence.
* Oil life **remaining** (100 = new, shrinks) and oil **degradation** (0 = new, grows) are opposite scales and are never mixed.
* Mileage values of different kinds (current copies, records of the past, installation records, component counters, deltas) are kept apart and
  only compared within a kind. No finding says that a car was tampered with, and none names an original mileage.
* A source's licence was checked before anything was derived from it (`source-register.json`). GPL, ODbL and unlicensed material is a lead only.

## Licence and attribution

* The OBDb signal definitions (https://github.com/OBDb, the repositories `OBDb/<Make>-<Model>`) are licensed
  **CC BY-SA 4.0** (https://creativecommons.org/licenses/by-sa/4.0/). `oil-life-coverage.json`, `mileage-source-catalog.json`,
  `service-operation-coverage.json`, `vehicle-coverage-matrix.csv` and `coverage-report.md` contain signal names, identifiers and counts
  derived from them (changes: classified, filtered, counted and grouped). Those files are therefore offered under the same licence,
  CC BY-SA 4.0, with credit to the OBDb contributors.
* `catalog-refresh-proposal.md`, `catalog-refresh-diff-report.md` and the `catalog-refresh-*.json` files contain repository names, signal counts and
  statements derived from OBDb and are offered under the same licence. The candidate catalog they describe is an adaptation of OBDb data (CC BY-SA 4.0,
  share-alike applies); it carries the credit, the licence and the changes made in its `documentation` and `source_catalogs` tables.
* The CAN odometer fields listed in `mileage-source-catalog.json` come from **opendbc**, Copyright (c) 2020, Comma.ai, Inc., MIT licence
  (https://github.com/commaai/opendbc).
* Standard PID facts were confirmed on Wikipedia's *OBD-II PIDs* (text CC BY-SA 4.0); no text was copied.
* The hand-written text and the tools in this folder are the project's own.
* The downloaded OBDb files themselves are not in this repository and must not be added to it.

## Regenerating the generated files

The classification is done by the app's own code (`MileageSignalClassifier`, `ServiceMetricClassifier`), run by a test that exports what it
decides; the script only counts. The shipped catalog `Resources/Raw/DtcCatalog.sqlite.br` is the data.

1. Export the classified signals (writes `classified-signals.json`; without the variable the test does nothing):

```bash
OBD360_RESEARCH_EXPORT_DIR=/tmp/obd360-export dotnet test Tests/CarScanner.Tests/CarScanner.Tests.csproj --filter "FullyQualifiedName~CatalogResearchExportTests"
```

2. Optional, for the module types OBDb states: download the default signal sets to a folder **outside** the repository.

```bash
node research/tools/fetch-obdb-sets.js /tmp/obd360-obdb-sets
```

3. Build the coverage files (needs Node 22.5 or newer, for `node:sqlite`):

```bash
node research/tools/build-research-files.js --signals /tmp/obd360-export/classified-signals.json --obdb-sets /tmp/obd360-obdb-sets
```

`node research/tools/tally-obdb-services.js /tmp/obd360-obdb-sets` prints the services the signal sets use (the "all commands are reads" finding).

The model-year evidence and the oil-life matrix need the full repositories (with their tests), not only the default signal sets. Each vehicle repository can be fetched
as a tarball without the API quota (`https://codeload.github.com/OBDb/<Repo>/tar.gz/refs/heads/main`); put them one folder per vehicle outside this repository, then:

```bash
node research/tools/evidence-obdb.js --repos /tmp/obd360-obdb-repos --signals /tmp/obd360-export/classified-signals.json --asset Resources/Raw/obdb_year_evidence.json
node research/tools/build-capability-matrix.js --list --out /tmp/obd360-capability
OBD360_RESEARCH_EXPORT_DIR=/tmp/obd360-capability OBD360_RESEARCH_VEHICLES_JSON=/tmp/obd360-capability/vehicles-for-guides.json dotnet test Tests/CarScanner.Tests/CarScanner.Tests.csproj --filter "FullyQualifiedName~ServiceGuideMatchExportTests"
node research/tools/build-capability-matrix.js --guides /tmp/obd360-capability/guide-matches.json
```

`evidence-obdb.js` also writes the compact file the app reads, `Resources/Raw/obdb_year_evidence.json` (with `--asset`). The guide matching is done by the app's own
`ServiceProcedureCatalogView`, through the second export test, so the matrix and the Service page agree.

The catalog refresh (item C1; the candidate catalog, its numbers and the proposal) uses a snapshot of the OBDb repositories with the commit of each, outside this repository,
and needs no API quota (source archives from codeload.github.com):

```bash
node research/tools/fetch-obdb-snapshot.js /tmp/obd360-obdb-snapshot
node research/tools/build-candidate-catalog.js --shipped Resources/Raw/DtcCatalog.sqlite.br --snapshot /tmp/obd360-obdb-snapshot --out /tmp/obd360-candidate
node research/tools/render-diff-report.js --report /tmp/obd360-candidate/candidate-report.json
node --test research/tools/test/obdb-import-lib.test.js
```

`check-explorer-equivalence.js` and `verify-dbgfilter-semantics.js` produce the two evidence files; their commands are at the end of `catalog-refresh-diff-report.md`.
`Tests/CarScanner.Tests/CandidateCatalogCompatibilityTests.cs` checks a candidate when `OBD360_CANDIDATE_CATALOG` names it and does nothing otherwise.

## Known limits

* The catalog rows have no model-year filter: OBDb's `filter` and `dbgfilter` are absent from all of them (61,598 rows have a `dbgfilter` upstream and are marked "not debug").
  The model-year evidence file above restores them; the coverage tables count definitions, the evidence tables count replies recorded per model year.
* About 60% of the catalog's rows are flagged `dbg` by the community (the schema: "whether to debug the signal responses"); another 39.5% have a `dbgfilter` and are debug in some model years only, so
  only 0.4% are unflagged in every year (`catalog-refresh-diff-report.md`, section 7).
* The catalog holds 556 vehicles. Upstream has 553 model repositories with Mode 22 / 21 definitions today (`catalog-refresh-diff-report.md`, section 4: the earlier "880 repositories, 609 with signals"
  counted 71 make-level repositories as well); the make-level ones are not in the catalog and the Explorer skips them.
* The module types (cluster, engine, ...) come from OBDb's `ecu` lists, which state a type for only some headers: for 20.6% of the modules that hold an odometer-named row.
* Several sources were read only through search summaries; the register says which.
