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
| `vehicle-coverage-matrix.csv` | One row per vehicle of the catalog (556): the odometer sources, the history records and the service readings it has. Generated. |
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
| E3 | The maker's own procedure for an exact vehicle (owner's manual, bulletin) for a manual, dashboard-level reset. |
| E4 | Reviewed against a recorded exchange of that exact vehicle. |
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

3. Build the files (needs Node 22.5 or newer, for `node:sqlite`):

```bash
node research/tools/build-research-files.js --signals /tmp/obd360-export/classified-signals.json --obdb-sets /tmp/obd360-obdb-sets
```

`node research/tools/tally-obdb-services.js /tmp/obd360-obdb-sets` prints the services the signal sets use (the "all commands are reads" finding).

## Known limits

* The catalog has no model-year filter: OBDb's `filter` and `dbgfilter` are absent from all of its rows, and the year-range override files were not
  read. A row applies to the model whatever the year.
* About 60% of the catalog's rows are flagged `dbg` by the community (the schema: "whether to debug the signal responses").
* The catalog holds 556 vehicles; upstream OBDb defines more today.
* The module types (cluster, engine, ...) come from OBDb's `ecu` lists, which state a type for only some headers.
* Several sources were read only through search summaries; the register says which.
