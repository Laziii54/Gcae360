# Next steps

What is left, in the order that pays off most. Nothing below has been started. Items that need a vehicle say so; this research did not have one.

## A. Before any reset or set operation can be offered (Service page)

The project's own promotion checklist (`oem_operations.json`) is the gate; this research found no way around it.

0. **Check the production server first.** The app already uploads reset recordings (`/admin/reset-recordings` on the production API). If any exist for the vehicles below, they may hold the missing request; this research could not look (production admin login).
1. **Pick the first vehicles where capturing is cheap and the tool is free.** Candidates in order:
   * **Ford / Mazda diesels that FORScan lists** (Ranger and Everest 2.2 / 3.2 / 2.0 TDCi, EcoSport EU 1.5 TDCi, Edge 2.0 TDCi, Ka 1.5 TDCi; Mazda 3 / 6 / CX-5 oil-deterioration reset). The tool exists and does the PCM reset; a passive capture of its exchange on that exact vehicle would give the request, the positive reply and the read-back in one session. Check the tool's terms of use first.
   * **Chevrolet Volt 2012 / 2013**: the read path (`22 119F`, then `4DC0`) is implemented; the project's candidate lists what is missing (the reset request from GDS2 / Tech2, session, security access, post-reset read-back).
   * **VAG with a UDS cluster** (Ross-Tech documents the cluster adaptation items; the identifiers depend on the cluster's data set, so the capture must name the cluster).
2. **Capture protocol** (one file per vehicle, never shared between vehicles): VIN-verified identity, adapter and bus, every request and reply of the reset session with timestamps, the dashboard before and after (photo), the read-back, the hash of the capture, the tool and its version. Store it next to the candidate in `oem_operations.json`; do not edit the catalog from a capture without the review step.
3. **Decide the 'set a chosen percentage' question separately.** Only GM's bulletin hints that a tool can set a chosen value. Test it on a Volt only after the plain reset has evidence, and record whether the module accepts arbitrary values (`AllowsPercentTargets`) as its own finding.
4. **What each capture has to show** (from `primary-source-findings.md`): GM: the scan-tool request that sets the Engine Oil Life Remaining (and, for a chosen percentage, with which value), on a 2.4L car of 2010-2012, plus a first read of its oil life (nothing is recorded for these cars). Ford: the PCM routine behind FORScan's 'Reset oil change indicator' on a listed diesel, with `22 054B` before and after. Stellantis: the IPC 'Reset Service Information' and PCM 'Reset oil change indicator' routines on a Fiat 500X or Jeep Renegade, with `22 3813` before and after. BMW: the identifier `CBS_RESET` writes. VAG: 'ESI: Resetting ESI' and the two 'FIX' items on one cluster, with its part number.
5. **Read the sources that were only summarised**: the Ford TSBs 22-2484 and SSM 49728, the Toyota, Honda and Mercedes documents (summaries only in `source-register.json`).

## B. Reading (Service page)

1. Honda / Acura `Maintenance, <item>` rows (`22 7060` at DA60; recorded for the Accord, Civic and Clarity, values 0 to 9,999) have no unit in the definition. Find out what the number is (remaining life, a code, a flag) from one car before the page shows more than the raw number.
1a. The Volt's `22 119F`: OBDb decodes the byte as a raw percentage, the app as `A*100/255`. Both are debug and nothing is recorded. One read on the Volt, next to the dashboard, settles which.
1b. Mazda diesels: the "learned oil value" is the oil dilution. Decide whether the Service page should show it as its own kind (FORScan shows it in grams) instead of leaving it unread.
2. Decide whether `Time since last oil change` (Ford, Mazda, Lincoln), `Oil change frequency in days` and `Oil service interval in days` (Renault) deserve their own kinds. Today they are 'Other'. `Oil change interval in months` (unit 'scalar') shares the kind of the kilometre interval.
3. `Maintenance status` is caught by the earlier `maintenance` rule in `ServiceMetricClassifier`, so the `ServiceStatus` kind is never produced. Harmless today (no such signal exists in the catalog); fix when one appears.
4. The brand-wide fallback (the same identifier tried on another model of the make) is labelled research-level. Count, on real cars, how often it answers correctly before widening it.

## C. Mileage

1. **Catalog refresh pipeline** (the biggest gain, in progress in a separate session): record one authoritative OBDb revision (the catalog's `source_catalogs` and `meta` tables disagree); import the model-year filters (`filter`, `dbgfilter`) and the year-range signal sets into the catalog itself (the app now reads them from `Resources/Raw/obdb_year_evidence.json` instead, derived by `evidence-obdb.js`; the asset becomes redundant once the catalog carries them); import OBDb's module types per header so the page can name a module from the definition instead of from a header guess (`ModuleName` in `CatalogOdometerProvider`). **Prepared, not adopted:** a candidate catalog (outside the repository), the importer, the diff report and a schema proposal are in `catalog-refresh-proposal.md` and `catalog-refresh-diff-report.md`; the decisions it asks for are in section 8 of the proposal. Correction to the earlier wording: upstream has 553 model repositories with Mode 22 / 21 definitions today, the shipped catalog 556 (the "880 repositories, 609 with signals" counted 71 make-level repositories as well); the candidate keeps the vehicles and brings the year filters, the debug years and the module types. Recorded-reply years (`tests/test_cases`) are not in the candidate; the proposal says how to fold them in.
1c. The evidence asset was derived from the repositories as downloaded on 2026-10-19. Regenerate it with the catalog refresh; a recorded year only ever gets added.
1d. **Use the module-tracking evidence in the Mileage analysis.** 47 module pairs (five or more recorded values each in one model year, fewer than one in ten of them agreeing) do not track each other (VW 7E0/7E1 and 714 on several models, Honda DA10/DA60 on three, Toyota Prius, Ford F-150 Lightning and others) and 5 match only after an extra mile/kilometre factor. A "sources differ" finding for such a pair may be a definition problem; the page could say so. Left as a proposal: it changes how findings are worded.
2. **Real readings**: one car per make with two or more modules (the matrix lists 302 vehicles) to see which definitions answer and whether the copies agree. The four captured cars did not return PID A6 at all. Until then every reading is 'public definition, not tested'.
3. **The BMW 22 DFE7 value** (module 0x19, bit 304, 32 bits, 343 recorded replies, 2,268 to 239,253 km) is an odometer copy. It could be a further module for the Mileage page after one BMW has confirmed it against the cluster; today it is only quarantined on the Service side.
3a. **Rows the page cannot use on purpose**: Ford `Transmission odometer` (declared in celsius), Toyota / Lexus / Scion `Total distance` (unit 'scalar'), 40-bit fields, and rows whose module address cannot be resolved safely (25 total-odometer rows). Each needs a vehicle to say what it is; do not guess a scale. The Ford Escape 2012 capture has `22 DD01` returning 243,087 in three bytes, unexplained.
4. **CAN fields that are candidates** (Rivian, further Hyundai, VW diagnostic frames, 2011 Ford layouts): a decoder that does not need a unit field, and one car each.
5. **DTC snapshots with a mileage inside** (`19 04`): no source says they exist; look at one manufacturer's record description before any code.
6. **Other modules that commercial testers read** (seat memory, electronic parking assistant are named by one report): no protocol known. Candidates for a capture from a trusted tester on a car that is known to be consistent.

## D. Licences and housekeeping

* `THIRD_PARTY_NOTICES.md` already credits OBDb; the research folder carries its own notice (README). If the folder is ever published, keep it under CC BY-SA 4.0 and keep the downloaded OBDb files out of it.
* `l77rodeo/gmlan` and `emdzej/ediabasx-docs-sgbd` have no licence: leads only.
* The ODbL / DbCL and GPL-3.0 sources stay quarantined.

## E. For the owner to decide

* Whether the first capture target is a Ford diesel (free tool, many listed models) or the Volt (already in hand, no tool known).
* Whether to refresh the catalog from upstream OBDb (item C1) before further mileage work: it changes the number of vehicles and the debug share, and it is the only way to get year filters.
  The decisions this needs are listed in section 8 of `catalog-refresh-proposal.md` (adopt the candidate, correct the licence strings of source 5, recorded replies in the catalog or in a separate file, the wording of the three states, the size).
