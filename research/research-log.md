# Research log

What was searched, fetched and counted, in order, with what came of it. Negative results are kept: they are findings. Nothing was run on a
vehicle. "Search summary" means that the web-search tool's summary of a page was read, not the page.

## 2026-10-17 to 2026-10-18: public data

| Step | Result |
|---|---|
| Cloned `commaai/opendbc` (commit `48c7f06c6f27c41e7169242acb03ef9775658deb`, 2026-10-17) and listed every odometer-named signal. | Odometer fields for Ford, Honda, Hyundai/Kia, Toyota, VW (MQB, MEB, MLB, PQ), GM (low-speed bus), Rivian and others. The app's `BroadcastOdometerProvider` already listens to eleven of the 36 odometer-named entries (a field that several DBC files repeat counts once per file). |
| Listed the organisation `OBDb` through the GitHub API (9 pages). | 891 repositories: 884 CC-BY-SA-4.0, 6 without a licence, 1 Apache-2.0. The 880 vehicle repositories are all CC-BY-SA-4.0; the non-CC ones are infrastructure (`.meta`, the website, editor, log-formatter and pid-hunter repositories, `vscode-obdb`). |
| Downloaded `signalsets/v3/default.json` of every repository in the list (885 names; 880 vehicle sets read, 5 non-vehicle repositories without a signal set). | 226,798 signals, of which 186,617 use service 22, 39,074 service 21 and 1,107 service 01: every command is a read. 609 vehicles define at least one signal. |
| Classified the names of the distance, oil and service signals. | 185 oil-life signals in 103 repositories at that time; the Stellantis family publishes oil *degradation*, the opposite scale. The odometer vocabulary holds many names that are not the odometer (history records, installation records, component counters, deltas); this found the false-conflict defect that the mileage work corrected. |

## 2026-10-19: web research

| Query / page | What was found |
|---|---|
| Search: GM oil life reset, scan tool, Mode 22, bulletins. | NHTSA-hosted GM bulletins (search summary): a scan tool restores the oil life to the percentage recorded before an ECM reprogramming; the monitor has three calculation pathways. A forum post shows one Mode 22 request on a Bosch ECM, unverified. **No Mode 22 PID for oil life and no reset request are published.** |
| Search: BMW CBS reset, UDS routine, ISTA, open source. | Workshop and vendor pages: reset by cluster or diagnostic unit; one BMW bulletin names ISTA; a commercial tool resets CBS oil service. **No routine id or payload; no open-source project.** The project's own candidate cites EDIABAS description files (0x2E plus save routine `31 01 F0 ED`); the documentation repository behind it has no licence and was not opened. |
| Search: Mercedes ASSYST reset. | Blogs that disagree (XENTRY service function, cluster key sequences). **No official or open routine.** |
| Search: UDS service-interval reset 0x2E, open source. | General UDS references only (0x2E is write-by-identifier; session and security access usually needed). **No oil-service identifier is public**; they are manufacturer-defined. |
| Search: `arduino-psa-diag`. | GPL-3.0 (API). An Arduino sketch for raw UDS / KWP frames to PSA modules; **nothing about a service reset** in the part described. Quarantined in the catalog; not used. |
| Search: Ross-Tech service interval reset; fetch of the SRI Reset page. | VCDS writes the cluster's service channels; oil service, time/distance service and inspection are separate; newer UDS clusters use an ESI function; the generic option fails on many 2008+ Audis; the interval is a separate setting in km. Procedure level, **no raw payload**. |
| Search: FORScan oil life reset. The first summary named EcoSport, Ka/Figo, Focus Mk4 and Mondeo Mk4 in 2.3.40. | **That summary was wrong.** The primary page (`forscan.org/changes_history.html`, read in full) lists the PCM service procedures 'Reset oil change indicator' and 'Reset Engine Oil Deterioration Learned Value' for other vehicles and versions (Ranger/Everest, EcoSport EU, Edge, Ka, Fiesta Mk7, Galaxy/S-Max, Territory; Mazda 3/5/6/CX-3/CX-5/CX-7/Bongo; F-Series 6.7 Powerstroke; 2.1.14 to 2.3.67). The first part's 2.3.40 section was reported as cut off. A fetch of a forum thread about 2.3.40 returned 404. Existence only, **no payload**. |
| Search: Toyota / Lexus maintenance required reset, Techstream. | Low-quality pages describing the odometer-button sequence. **Nothing on a dealer-tool request.** |
| Search: UDS 0x19 snapshot, odometer in freeze-frame data. | **No source says snapshots carry the odometer**; the content is manufacturer-defined. The project's own BMW capture got NO DATA for `19 04`. |
| Search: odometer stored in several control units, manipulation detection. | A Bosch tester function lists the mileage stored in various control units (seat memory and the electronic parking assistant named) and compares it with the speedometer (eeNews, 2021-01-26, fetched). A blog guide names engine, ABS and body modules and gives a case where a transmission module read about three times the dashboard value and was judged a corrupted counter. Patents on odometer protection exist (not opened). **Supports: a difference is an observation, not a verdict.** |
| Fetch: Wikipedia *OBD-II PIDs* (three times). | A6 odometer: 4 bytes, divided by 10, km. 21 and 31: 2 bytes, km. 4D: minutes. 5B hybrid battery life %. 5C engine oil temperature. **No PID for oil life, a service interval or maintenance.** Text CC BY-SA 4.0. |
| Fetch: OBDb organisation profile and a vehicle repository; fetch of the template repository. | Layout `signalsets/v3/default.json` plus optional year-range files; CC BY-SA 4.0; the pages do not say how definitions are validated or what `dbg` means. |
| Read: `OBDb/.schemas/signals.json`. | `dbg`: "Whether to debug the signal responses". `filter` and `dbgfilter` restrict by year. This is the only documented meaning; the app words the flag as "still marked as under test" and no stronger. |
| GitHub API licence checks: `ludwig-v/arduino-psa-diag`, `commaai/opendbc`, `emdzej/ediabasx-docs-sgbd`, `l77rodeo/gmlan`, `OBDb/*`. | GPL-3.0; MIT (Copyright (c) 2020, Comma.ai, Inc.); none; none; CC-BY-SA-4.0. |

## 2026-10-19: the shipped catalog

| Step | Result |
|---|---|
| Decompressed `DtcCatalog.sqlite.br` and listed its tables. | 157,229 OBDb rows (services 22 and 21) for 556 vehicles, 94,977 of them flagged debug; opendbc `can_signals` 36,205 rows. The catalog records two different OBDb revisions (`source_catalogs`: 3fd5afe5..., 2026-10-17; `meta`: bc13d91a..., 2026-10-03). |
| Looked for OBDb's year filters in the rows. | None in any row (`filter`, `dbgfilter` absent). A row applies to the model whatever the year. |
| Exported every service- or distance-named row with the app's own classifiers (`CatalogResearchExportTests`). | 4,296 rows; after the maintenance fix below, 4,271. |
| Read the Service page's loader. | Normal refresh sends the non-debug definitions of the exact model plus the brand-wide identifiers of the make (kinds: oil life, oil degradation, distance since oil change, distance and days to service, inspection), labelled research-level. Other debug definitions only on request. |
| Read the Mileage provider. | Requests the exact model's definitions whether or not they are flagged debug, and did not say so on the page. Now it does. |
| Read `SERVICE_RESET_RESEARCH.md` (2026-09, the project's own comparison of 14 scanner apps and its verification audit of 2026-09-28). | Reaches the same conclusions from other sources: reset is not part of SAE J1979, apps send only the verified command of the identified car, no open database of reset commands exists; the generic `22 -> 2E` write for GM `4DC0`, Ford `054B` and Mazda `0519` and the inferred GMLAN `$3B` write-back were disabled in the audit. |
| Read the project's shipped catalogs. | `service_procedures.json`: 164 guides (165 after round 2 added the Mazda one), all manual and source-documented. `oem_operations.json`: `operations` is empty; 12 family and 73 vehicle candidates, all blocked; the Volt candidate says reset research is 'BLOCKED_UNTIL_EXACT_WIRE_CAPTURE'. |

## Round 2 (2026-10-19): the documents themselves

The first round read search summaries for several sources. This round went to the documents.

| Step | Result |
|---|---|
| Downloaded and read in full, with `pdftotext`: GM bulletins 12312A (twice, same text), 18-NA-125, PIP5282A, PIP6076; BMW SI B62 06 13 (two revisions); Kia TSB ELE209 (two issues) and ENG241; Stellantis STAR case S2309000009. | The makers name the module and tool function of each reset and publish no request. GM 12312A documents that GM's own tool restores a chosen oil-life percentage on the 2.4L cars of 2010-2012 (a capability, not a request). BMW's bulletin shows that CBS is held in the instrument cluster on F-series. Stellantis runs an IPC routine and a PCM routine. Kia sets the service reminder in the cluster with KDS and resets OLMS from the head unit. |
| Read the Mazda owner's manual page (official), the Ross-Tech pages on cluster adaptation, the EDIABAS description of a diesel DME (job index and `CBS_RESET`), the Car Scanner page, four FORScan threads (two returned 403). | Mazda's official owner method for the SKYACTIV-D 2.2 (now a shipped guide). Ross-Tech: items by name, no bytes. BMW `CBS_RESET`: the structure of the record (item, availability, number of services, target month and year, remaining distance, forecast intervals), the DIDs `4050`, `404F`, `3701` and routine `F0ED`, **not** the identifier it writes. FORScan threads: no raw data; on Mazda diesels the learned value is oil dilution. |
| Cloned `arduino-psa-diag` and searched it; read the `l77rodeo/gmlan` README; grepped every opendbc DBC for oil, service and maintenance. | PSA: no service reset. GMLAN: 0x1F1 carries the oil-life reset request and also a remote-start request and the immobilizer password. opendbc: the Ford mechanism (cluster message 0x430 bit `EngOilLife_B_RqReset`, answers in 0x179 and 0x17D); nothing for GM or BMW oil life. |
| GitHub repository search (ten queries), web searches for byte-level resets (GM, BMW, Mercedes, VAG, Ford, Hyundai/Kia, Toyota, Honda, Mazda). | Nothing usable. Two motorcycle service-reset tools turned up. |
| Read OBDb's own tooling (`.schemas`: schema, `signals.py`, `loader.py`, `signals_testing.py`) and downloaded all 860 vehicle repositories. | The exact year semantics of `filter` and `dbgfilter`; the layout of the recorded tests per model year; the catalog's `debug` column equals the command-level `dbg` only (61,598 rows have a `dbgfilter` and are marked not debug). |
| `research/tools/evidence-obdb.js` over the repositories. | 3,415 signals analysed. By evidence: 334 vehicles with a recorded current-odometer reply, 140 with two modules recorded in one model year, 116 with a tracking pair; 38 vehicles with a recorded oil-life reply and 2 with an oil-degradation reply (see `obdb-evidence-report.md`). **A definition that holds something else than its name says:** the BMW "Distance since last oil change" (`22 DFE7`) follows the odometer in its 343 recorded replies. |
| Per-vehicle capability matrix for the 106 vehicles with an oil-life or degradation definition. | 40 with a recorded reply; 0 with an automatic reset; 0 with an automatic chosen percentage; 3 where the maker's own tool is documented to set a chosen percentage (GM 2.4L cars). |

### Mistakes of the first round found by this one

* The first round said OBDb's year filters had to be imported to give "year filters". They are also what separates "not debug" from "verified": the catalog's flag is not the verification state of the car's model year.
* "Non-debug" was used as if it meant verified. By model-year evidence, 38 of 95 oil-life vehicles have a recorded reply, not 39 "non-debug" ones, and the sets are different vehicles.
* The Mileage page's "requestable" figure counted definitions. Recorded odometer replies exist for 334 of 461 vehicles with a definition.

## Catalog refresh, item C1 (2026-10-19): how the OBDb rows were made, and a candidate

| Step | Result |
|---|---|
| Read `Services/DtcCatalogService.cs`, `CatalogProvenanceAndLicensingTests`, the catalog's `source_catalogs`, `meta`, `documentation`, `source_snapshots` tables. | The OBDb rows came from "OBDb/obdb.community" (the OBDb Explorer front end), not from the vehicle repositories. Source 5 records a commit, a zip and a second commit that do not agree. |
| Read the Explorer's repository through the API and raw files (`scripts/extract_data.py`, `validate_json.py`, `matrix_data_schema.json`, the daily workflow). Fetched `public/data/matrix_data.json` at the two commits the catalog names. | The shipped rows are that file at `3fd5afe5` (2026-10-17 09:00 UTC) kept for services 22/21 with a make and a model: 157,229 rows, 556 vehicles, 94,977 debug, all equal. `bc13d91a` (2026-10-03) is an older matrix of 68,298 rows / 278 vehicles. The Explorer drops `filter`, `dbgfilter`, `rax`, `tst`, `pri`, `proto`, the `ecu` list and signal descriptions, skips repositories with a dot in the name and the standalone make repositories. The Explorer's repository declares no licence file (the README says MIT); the data in it derives from CC-BY-SA-4.0 repositories. |
| Read OBDb's published schema (`.schemas/signals.json`, `python/can/signals.py`), the organisation README, `.make-template`, and the Apache-2.0 `vscode-obdb` (`debugFilterCalculator.ts`, `commandSupportUtils.ts`, the linter) for what `filter`, `dbgfilter`, `ecu` and the year-range files mean. | `default.json` is the fallback for years no override file covers; filter rules as in the proposal; `ecu` entries match by header, `eax`, `rax`, year, most specific wins; a `dbgfilter` is written from the model years that have a recorded reply. The two filter implementations differ for `from == to`; no filter has that. |
| Snapshot of the organisation (880 CC-BY-SA-4.0 repositories, commit of each), `fetch-obdb-snapshot.js`. Shallow `git clone` was tried first and dropped: GitHub throttled it to about 6 s a repository; the source archive endpoint takes 1 s and carries the commit id. | `obdb-2026-10-19-7b0a265c8d5d`. Some OBDb test-case file names contain `\|`, which Windows cannot create, so archives are read in memory. |
| Importer that reproduces the Explorer's output (Python-compatible JSON text, scaling text, signal groups). | Serializer writes the 162,048-entry matrix back byte-identical; 378 untouched repositories give 106,890 identical entries. |
| Candidate catalog and diff against the shipped one. | 553 vehicles (shipped 556: +2, −5 renamed/removed upstream), 157,684 rows, 99.66 % of the shipped rows byte-identical; 18,439 rows with a year restriction, 62,265 with a `dbgfilter`, 50,985 with a module type. The earlier "880 repositories, 609 with signals" counted 71 make-level repositories (56 with signals, 68,289 rows, never in the catalog). |
| Checked what a `dbgfilter` means against the repositories' own tests (90 repositories, 127,485 command-years). | 99.40 % of the years the filter calls verified have a recorded reply; only 39.65 % of the years with a recorded reply are called verified. |
| Found, while working, that another session had added `ObdbYearEvidence*.cs`, `obdb_year_evidence.json` and `evidence-obdb.js` for the same purpose. | Left alone; the proposal compares the two (section 5.5). |
| Tests. | 19 Node unit tests of the importer; 5 C# tests of the candidate (run only with `OBD360_CANDIDATE_CATALOG`; 3 of them fail on the shipped file, as intended); the whole suite on a scratch copy of the working tree: 2,226 passed, 0 failed, with the shipped catalog and with the candidate. |

Not done in this item: nothing was installed on a device, no page was changed, `Resources/Raw/DtcCatalog.sqlite.br` was not replaced, nothing was committed, the recorded-reply years were not imported, and the make-level repositories were not evaluated.

## Defects found by the research (and fixed in the app)

1. Oil degradation (a value that grows) would have been shown as oil life remaining on Stellantis vehicles (earlier in this work).
2. History snapshots, installation records, component counters and deltas were treated as the odometer, producing false conflicts (earlier).
3. 'Maintenance' in the name of the 12 V voltage maintenance device (Peugeot, Jeep) and of the parking brake's maintenance state (Volvo) made 25 catalog rows look like service counters. They are no longer service signals.
4. A source comment in `ServiceDataReaderService` said the Volt reset had been tested on a real car (write, read-back, dashboard). `SERVICE_RESET_RESEARCH.md` (2026-09) also says the Volt reset was "done", but no capture or evidence file was kept, the audit of 2026-09-28 closed the write until an exact request and response are documented, and the candidate file records the reset as blocked. The comment now says what the repository can show.
5. The Mileage page gave no sign that 44% of its current-odometer definitions are flagged as under test. Each such reading now says so.
6. The "under test" label, and the Service page's "real ECU read" badge, used the command-level flag only. They now use the flag of the car's model year (OBDb's dbgfilter) when the year is verified, a request OBDb says does not apply to the year is not sent, and a reading says when OBDb holds a recorded reply for the year.
7. The BMW "Distance since last oil change" (`22 DFE7`) is the odometer; the Service page showed it as an oil-change distance on every BMW and MINI. It is no longer shown.

## Not done

* No vehicle was connected. No capture was made. No reset or write was sent or designed.
* The standards (SAE J1979, ISO 14229-1) and the makers' diagnostic documentation (ISTA, GDS2, FDRS, Techstream, i-HDS, KDS, ODIS, wiTECH) were not read: they are paid or proprietary. The EDIABAS pages were read to 300,000 of 618,000 characters.
* The recorded replies of OBDb were not compared with a vehicle. No capture was made, no reset or write was sent or designed, and no vehicle was connected.
* The production server's reset recordings (`/admin/reset-recordings`) were not looked at: they need the production admin login.
* Hyundai/Kia/Genesis, Nissan/Infiniti and Mitsubishi/Suzuki: the catalog has no service or oil-life definition; no further source was searched for them beyond the project's owner's-manual guides.
