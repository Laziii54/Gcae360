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
| Read the project's shipped catalogs. | `service_procedures.json`: 164 guides, all manual and source-documented. `oem_operations.json`: `operations` is empty; 12 family and 73 vehicle candidates, all blocked; the Volt candidate says reset research is 'BLOCKED_UNTIL_EXACT_WIRE_CAPTURE'. |

## Defects found by the research (and fixed in the app)

1. Oil degradation (a value that grows) would have been shown as oil life remaining on Stellantis vehicles (earlier in this work).
2. History snapshots, installation records, component counters and deltas were treated as the odometer, producing false conflicts (earlier).
3. 'Maintenance' in the name of the 12 V voltage maintenance device (Peugeot, Jeep) and of the parking brake's maintenance state (Volvo) made 25 catalog rows look like service counters. They are no longer service signals.
4. A source comment in `ServiceDataReaderService` said the Volt reset had been tested on a real car (write, read-back, dashboard). `SERVICE_RESET_RESEARCH.md` (2026-09) also says the Volt reset was "done", but no capture or evidence file was kept, the audit of 2026-09-28 closed the write until an exact request and response are documented, and the candidate file records the reset as blocked. The comment now says what the repository can show.
5. The Mileage page gave no sign that 44% of its current-odometer definitions are flagged as under test. Each such reading now says so.

## Not done

* No vehicle was connected. No capture was made. No reset or write was sent or designed.
* The GM bulletin PDFs, the EDIABAS documentation pages, the standards (SAE J1979, ISO 14229-1) and the OBDb year-range override files were not read.
* Hyundai/Kia/Genesis, Nissan/Infiniti and Mitsubishi/Suzuki: the catalog has no service or oil-life definition; no further source was searched for them beyond the project's owner's-manual guides.
