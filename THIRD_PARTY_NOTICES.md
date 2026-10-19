# Third-party notices (app)

## OBDb — vehicle signal sets

The hybrid / EV battery request layouts (byte positions and scaling) in
`Services/ToyotaHybridBattery.cs`, `Services/HyundaiKiaBattery.cs`,
`Services/GmVoltBoltBattery.cs`, `Services/NissanLeafBattery.cs` and
`Services/MoreEvBatteries.cs`, `Services/MoreEvBatteries2.cs`, and the
recorded car responses used in `Tests/CarScanner.Tests/ToyotaHybridBatteryTests.cs`
`HyundaiKiaBatteryTests.cs`, `GmVoltBoltBatteryTests.cs`, `NissanLeafBatteryTests.cs` and `MoreEvBatteriesTests.cs`, are adapted from **OBDb**
(https://github.com/OBDb — Toyota-Prius, Toyota-RAV4-Hybrid,
Toyota-Camry-Hybrid, Toyota-Corolla, Toyota-C-HR, Kia-Niro,
Hyundai-Kona-Electric, Hyundai-IONIQ-Electric, Hyundai-Santa-Fe-Hybrid,
Chevrolet-Volt, Chevrolet-Bolt-EV, Chevrolet-Bolt-EUV, Nissan-Leaf, BMW-i3, Ford-Fusion-Hybrid, Ford-Mustang-Mach-E, Volkswagen-e-Golf, Audi-e-tron,
Haval-Jolion-HEV, Honda-CR-V-Hybrid, Honda-Civic, Jaguar-I-PACE, MG-ZS-EV, Porsche-Taycan and
related repositories), licensed under the Creative Commons Attribution-ShareAlike 4.0 International
licence (https://creativecommons.org/licenses/by-sa/4.0/).

Changes: the definitions were re-expressed as C# decoders with plausibility
checks; the assessment limits and scoring are OBD360's own.

Signal names and identifiers of OBDb (same licence, same credit) also appear in
the name rules `Services/Mileage/MileageSignalClassifier.cs` and
`Services/OemDiagnostics/ServiceMetricClassifier.cs`, in their tests
(`Tests/CarScanner.Tests/MileageHistoryTests.cs`, `ServiceOilSemanticsTests.cs`,
`TestData/obdb-distance-signal-names.txt`), and, counted and grouped, in the
research tables under `research/` (see `research/README.md`). Those tables are
offered under CC BY-SA 4.0. The CAN odometer fields listed there come from
opendbc (MIT, Copyright (c) 2020, Comma.ai, Inc.).

## PriusChat community

The 7E3 variant of the 2010–2015 block request (21 81, value × 79.99 / 65535)
follows the community-shared Torque PID list on PriusChat. The app tries the
OBDb-documented 7E2 address first and marks the 7E3 variant as not yet verified.

## Bundled offline diagnostic catalog (`Resources/Raw/DtcCatalog.sqlite.br`)

The app ships an offline SQLite catalog (Brotli-compressed, unpacked on first
launch) that backs DTC lookups (`Services/DtcCatalogService.cs`,
`Services/OemDiagnostics/ModuleDtcScanService.cs`) and the per-vehicle
UDS `$22`/`$21` signal list (`Services/OemDiagnostics/ModuleLiveDataService.cs`
and related readers). The catalog's own `source_catalogs` table records, for
every row in it, which of the sources below it came from, that source's
license, and its revision/commit. This section is the human-readable mirror
of that table and must be kept in sync with it whenever a source is added,
refreshed, or removed.

Two kinds of rights apply to each source below: a license over the **code**
used to extract/compile the data (not shipped — only its output is), and a
license over the **data/contents** itself (what is actually bundled and
queried at runtime). Only the data/contents license governs what OBD360 may
ship; the code license is noted for completeness.

| # | Source | Revision/commit | Data license | Code license | Imported into | Active rows | Used by |
|---|--------|------------------|---------------|---------------|----------------|-------------|---------|
| 1 | [Wal33D/dtc-database](https://github.com/Wal33D/dtc-database) | user-provided snapshot | MIT | MIT | `dtc_definitions` | 18,797 | DTC lookup |
| 2 | [AutoDiag2/database](https://github.com/autodiag2/database) | `11e73fd6…` | ODbL-1.0 (database) + DbCL-1.0 (contents) | GPL-3.0-or-later | `dtc_definitions`, `build_exclusions` | **0 — quarantined**, see below | — |
| 3 | [commaai/opendbc](https://github.com/commaai/opendbc) | user-provided snapshot | MIT | MIT | `ecu_firmware`, `vehicle_platforms`, `can_messages`, `can_signals`, `ecu_addresses` | 5,417 / 380 / 4,728 / 36,205 / 1,011 | CAN signal decode (`CatalogDbcSource.cs`); the rest currently unused (see "Unused imported tables" below) |
| 4 | [ludwig-v/arduino-psa-diag](https://github.com/ludwig-v/arduino-psa-diag) | user-provided snapshot | GPL-3.0 | GPL-3.0 | `dtc_definitions`, `ecu_addresses` | **0 — quarantined**, see below | — |
| 5 | [OBDb/obdb.community](https://github.com/OBDb/obdb.community) | `3fd5afe5ecf68a6d6b06beb7b4e86f23ee6df69b`, 2026-10-17 | aggregates many OBDb per-vehicle repos, each **CC BY-SA 4.0**; the Explorer wrapper repo itself is MIT | MIT | `pid_definitions`, `vehicle_generations`, `diagnostic_commands`, `vehicle_pid_support` | 157,229 / 2,138 / 99 / 116 | Mode `$22`/`$21` per-vehicle live-data signals; the generation/command/pid-support tables are currently unused |
| 6 | [mdabrowski1990/uds-address](https://github.com/mdabrowski1990/uds-address) | `2.0.0` | MIT | MIT | `ecu_addresses`, `vehicle_platforms` | 13 / 1 | Currently unused |
| 7 | [foerbsnavi/obdex](https://github.com/foerbsnavi/obdex) | `bc58b0eb…` | CC0-1.0 (data) | MIT (tooling) | `dtc_definitions`, `pid_definitions` | 9,532 / 132 | DTC lookup / Mode `$01` support |
| 8 | [openvehicles/Open-Vehicle-Monitoring-System-3](https://github.com/openvehicles/Open-Vehicle-Monitoring-System-3) | `92dcd0c1…` | majority MIT; "software which uses other licenses will be annotated appropriately" per the repo's own LICENSE — treated as mixed | majority MIT | `enhanced_pid_definitions`, `ecu_addresses`, `vehicle_platforms` | 1,003 / 162 / 45 | Currently unused |
| 9 | [evrenonur/obd2-elm327-pid-reference](https://github.com/evrenonur/obd2-elm327-pid-reference) | `03601e82…` | **no LICENSE file in the repository**; the README text alone claims MIT | same (README-only) | `research_reference_entries` only | 431 | Admin-only Research page; **never promoted to `dtc_definitions`/`pid_definitions`** because a README claim is not a reliable license grant |
| 10 | [mdabrowski1990/uds](https://github.com/mdabrowski1990/uds) | `46f5edd9…` | MIT | MIT | `uds_services`, `uds_negative_response_codes` | 27 / 59 | Internal UDS SID/NRC reference, not yet surfaced in UI |
| 11 | [openxc/uds-c](https://github.com/openxc/uds-c) | `e56aeb9c…` | BSD-3-Clause | BSD-3-Clause | `obd_modes`, `scanner_engine_references` | 11 / 1 | Internal reference |

### Quarantined sources (not shipped in `DtcCatalog.sqlite.br`)

Two sources were removed from the bundled catalog because OBD360 could not
confirm it may redistribute their data merged into a closed, proprietary
catalog. Nothing was deleted: every affected row was copied byte-for-byte
into a **safe-copy database kept in the repository but excluded from the
app package** — `Docs/ThirdPartyDataSnapshots/AutoDiag2_PSA_safe_copy.sqlite.br`
(see the README there for restore instructions and the exact licensing
analysis) — before being removed from the shipped file.

- **AutoDiag2/database** (22,172 `dtc_definitions` rows + 6,661
  `build_exclusions` bookkeeping rows). Its data is ODbL-1.0 (database) +
  DbCL-1.0 (contents). DbCL explicitly defers to ODbL for anything beyond bare
  facts (Open Data Commons, DbCL v1.0 §2.3), and ODbL's share-alike/notice
  conditions (§4.2, §4.4) apply once a Substantial part of the Contents is
  Publicly Conveyed — which bundling these rows into OBD360's closed app
  package is. OBD360 does not currently offer this slice of data under
  ODbL-compatible terms with the required notices, so it is excluded from
  production pending either (a) a confirmed-compliant separate distribution
  of just this slice, or (b) legal sign-off that the specific facts involved
  fall outside Database Right / DbCL's scope.
- **ludwig-v/arduino-psa-diag** (40,840 `dtc_definitions` rows from the
  repository's `dtc/*.md` files — one Markdown table per PSA/Stellantis ECU
  of DTC-code-to-short-description pairs — plus 515 `ecu_addresses` rows from
  `ECU_LIST.md`'s `CAN_EMIT_ID:CAN_RECV_ID` pairs). The repository is
  released as a whole under GPL-3.0 with no separate license found for the
  `dtc/` or root `*.md` files. GPL-3.0 does not draw ODbL's distinction
  between "database" and "contents" — it licenses "the Program" as released,
  which by default includes its documentation files. Shipping this content in
  a closed-source commercial catalog without releasing the corresponding
  program under GPL-3.0 is not confirmed compliant, so it is excluded from
  production pending legal confirmation that these specific
  code-to-description facts are usable independently of the repository's
  chosen license.

If either determination changes, the safe-copy database has everything
needed to re-import without re-downloading or re-parsing the original
repositories.

### Unused imported tables

`enhanced_pid_definitions` (1,003 rows), `ecu_firmware` (5,417),
`vehicle_platforms` (426), `vehicle_generations` (2,138),
`diagnostic_commands` (99), and `vehicle_pid_support` (116) are present in
the shipped catalog but not queried by any app code today (confirmed via
`grep -rln "FROM <table>" Services/`). They add bundle size with no current
functional benefit; see the project's open investigation into which of these
are safe to prune versus worth wiring up before any of them are removed.

## evrenonur/obd2-elm327-pid-reference — quarantine note

This source has no LICENSE file on GitHub; only its README text claims MIT.
Its content is kept solely in `research_reference_entries`, visible only on
the admin-only Research page (`Views/ResearchDataPage.xaml`), and is never
promoted into the trusted `dtc_definitions`/`pid_definitions` lookup tables
used for ordinary scans.
