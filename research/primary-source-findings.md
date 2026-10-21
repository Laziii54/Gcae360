# Primary-source findings: service operations and oil life (second round, 2026-10-19)

This round went to the documents themselves instead of search summaries. What was actually read, in full or in part, is listed with each finding; what could not be
read is said. **Nothing here was run on a vehicle**, and no request bytes for any reset were found in any source the licences allow us to use. The result is therefore not a
new executable operation. It is a precise map of who executes each reset, which tool function does it, how the outcome can be checked, and what exactly is still missing.

## 1. What changed compared with the first round

| First round | Now |
|---|---|
| "GM scan tools can restore the oil-life percentage" (search summary of a bulletin) | The bulletin was read (GM 12312A, August 2013): it names the vehicles and the engine, says the engine control module is reprogrammed and the oil-life value restored **to the percentage recorded before** with a scan tool. A chosen percentage is therefore a documented capability of GM's own tool on those engines. |
| "FORScan lists oil resets for some diesels" | The whole change history was read (earlier), and four forum threads: none posts a request. One adds that on a Mazda diesel the reset clears the **learned oil dilution value**, a different thing from a percentage of oil life. |
| "BMW CBS reset exists" | BMW's own bulletins show that on F-series cars the CBS data lives in the instrument cluster, and the EDIABAS description of the DME gives the structure of the `CBS_RESET` job (see 3.5). |
| Ford and GM reset requests "not public" | opendbc (MIT) shows the Ford mechanism on the bus: the cluster message 0x430 carries a reset-request **bit**, and the PCM answers with oil-service message states and the percentage. GM's equivalent is a broadcast, 0x1F1. Both are broadcasts, not diagnostic requests, and both are unsafe to imitate (3.1, 3.2). |
| No data on which vehicles answer an oil-life read | OBDb's recorded replies per model year: 38 vehicles with a recorded oil-life reply, 2 with a recorded oil-degradation reply (`obdb-evidence-report.md`, `oil-life-capability-matrix.json`). |

## 2. Sources read this round

| Source | Read | Licence / use |
|---|---|---|
| GM bulletin 12312A, August 2013 (NHTSA files MC-10114191-9999 and MC-10246678-9999, the same text) | Full text | GM copyright; facts only |
| GM bulletin 18-NA-125 (May 2024) and PIP5282A (September 2016), oil-life monitor calculation | Full text | as above |
| GM PIP6076 (June 2025), Corvette transmission fluid life | Full text | as above |
| BMW SI B62 06 13 (February and May 2014), CBS due date reset | Full text | BMW copyright |
| Kia TSB ELE209 (Rev 3, 2022) and ENG241 (2022) | Full text | Kia copyright |
| Stellantis STAR case S2309000009 (October 2023) | Full text (two pages) | Stellantis copyright |
| Mazda owner's manual, SKYACTIV-D 2.2 engine oil data resetting (official web page) | Full page | Mazda copyright |
| EDIABAS description of the diesel DME `d72n47a0` (emdzej/ediabasx-docs-sgbd) | The job index and the `CBS_RESET` job; 300,000 of 618,000 characters | no licence: a lead, nothing used |
| Ross-Tech wiki: VW Golf VII cluster page, Audi service interval reminders | Full pages | proprietary text; facts only |
| opendbc (MIT), Ford and BMW DBC files | Searched and read in the shell | MIT |
| `ludwig-v/arduino-psa-diag` (GPL-3.0) | Cloned and searched | not used |
| `l77rodeo/gmlan` README (no licence) | Full README | a lead, nothing used |
| FORScan forum threads t=19160 and t=21428 | Read; t=18282 and t=17187 returned HTTP 403 | forum content; facts only |
| Car Scanner coding page | Read | vendor page; function names only |
| OBDb `.schemas` (CC BY-SA 4.0): the schema, `signals.py` (year filter), `loader.py` (year-range files), `signals_testing.py` (test layout) | Full files | CC BY-SA 4.0 |
| OBDb vehicle repositories: all 860 downloaded; signal sets, `generations.yaml`, and every `tests/test_cases/<year>/` | Parsed by `research/tools/evidence-obdb.js` | CC BY-SA 4.0; derived tables only |

Not read: the standards (SAE J1979, ISO 14229-1), BMW ISTA and GM GDS2/SPS documentation, Ford FDRS/IDS, Toyota Techstream, Honda i-HDS, Hyundai GDS and KDS, VAG ODIS, Stellantis wiTECH.
They are the tools in which the requests live, and their data is proprietary. `vis4vag.com` shows the VAG cluster operations by name ("Reset of service interval QI", "Reset of variable oil change interval QI6") behind a paywall with the values masked.

## 3. Findings per brand

### 3.1 GM (Chevrolet, GMC, Buick, Cadillac, Holden)

* **Who executes it:** the engine control module. Bulletin 12312A has dealers reprogram the ECM (which loses the oil-life value), clear the codes, and then "use a scan tool to reset the Engine Oil Life Remaining back to the original percentage recorded before".
* **Vehicles named:** 2010-2011 Buick LaCrosse, 2011-2012 Buick Regal, 2010-2012 Chevrolet Equinox and GMC Terrain, all with the 2.4L engine (RPO LAF, LEA, LUK).
* **What this proves, and what it does not:** GM's own tool can set the oil life to a chosen percentage on these engines. It does not say how, or that other GM engines accept a chosen value. No request is published.
* **Behaviour of the monitor (18-NA-125, PIP5282A):** three pathways: engine revolutions (with temperature multipliers), mileage since the reset (capped at 7,500 miles from model year 2013, except the Volt), time (0% after one year; the Volt uses two years and no mileage pathway). It starts after the first 50 miles and restarts at each reset.
* **Other reset items exist:** the Corvette bulletin PIP6076 is about the transmission fluid life, with its own reset.
* **On the bus:** `l77rodeo/gmlan` documents, on an E38 PCM and T43 TCM car, a high-speed message 0x1F1 sent by the gateway that lists "Engine oil life reset request" and also the remote-start request and the immobilizer pre-release password (0x1F3: "Transmission oil life reset request"). It gives no bit layout. **Sending 0x1F1 would send those other fields too; the app never does.**
* **Reads with recorded replies (OBDb):** `22 4DC0` at module 0x11 (29-bit, header DA11) for Cadillac Escalade 2022, Chevrolet Corvette 2024-2025, Silverado 1500 2022-2026, Silverado 2500 2025, Tahoe 2022 and 2025, GMC Canyon 2023 and 2025, Sierra 2500HD 2024, Terrain 2026, Yukon 2024-2025; values 0 to 99.2 percent (the byte scaled by 100/255). Nothing is recorded for the older `7E0` requests (`4DC0`, `119F`), which OBDb still flags debug, including the Volt.
* **A discrepancy to settle on a car:** OBDb decodes the Volt's `22 119F` as a raw percentage (`max 255`, no scale); the app decodes it as `A*100/255`. Both are flagged debug and no reply is recorded.
* **Verification path:** read the oil life again (`22 4DC0`) and compare; the app's `ServiceResetVerificationService` is built for exactly that.
* **Missing:** the scan-tool request itself (service, identifier, session, security), and whether the module accepts values other than 100%. **Next lead:** a ToolSession recording (the app's reset recorder, Y-splitter) of GM's tool on one of the named 2.4L cars, or of GDS2 on a recent truck for which a read is recorded.

### 3.2 Ford and Lincoln

* **Mechanism on the bus (opendbc, MIT):** the cluster message 0x430 (`Cluster_Info1_FD1`, sent by the gateway) carries `EngOilLife_B_RqReset` (bit 43, i.e. byte 5 bit 3); the PCM, the diesel ECM and the hybrid PCM receive it. In the 2011 database the same bit is bit 18 (byte 2 bit 2). The same message 0x430 carries `OdometerMasterValue`, so **imitating the reset means writing a whole cluster frame with the odometer in it; the app never does.**
* **The answer:** 0x179 `EngineData_7` (0x159 in 2011) broadcasts `EngOilLife_Pc_Actl` (7 bits, percent) on petrol variants; 0x17D `EngineData_17` on diesel variants broadcasts `EngOilLife_T_Actl` (days), `DistToNextOilChange` and `EngOilSrvcMsgTxt_D_Rq`, whose states include `ChangeOilSoon`, `ChangeOilNow`, `ResetInProgress` and `ResetComplete`. That is an ECU-side confirmation that a reset was taken.
* **Diagnostic side:** FORScan's change history lists the PCM service procedure "Reset oil change indicator" for named diesel models and "Reset Engine Oil Deterioration Learned Value" for Mazda diesels and the F-Series 6.7 Powerstroke. Ford's own bulletins (TSB 22-2484, SSM 49728) treat the reminder reset as a final owner's-manual step after a PCM update.
* **Reads with recorded replies:** `22 054B` at 7E0 for 33 Ford and Lincoln vehicles (F-150 2014-2026, Escape and Kuga 2014-2025, Expedition 2018-2025, Focus, Fusion, Mustang 2016, 2019, 2024-2026, Transit, Bronco, Maverick and others); values 0 to 100. For the Mustang OBDb flags the definition debug for 2017-2018 and 2020-2023, so the green "real ECU read" badge was too strong for those years (now corrected).
* **Missing:** the UDS routine or write that FORScan's PCM procedure sends, and the module's answer. **Next lead:** a ToolSession recording of FORScan resetting a listed diesel (Ranger, Everest, EcoSport EU 1.5 TDCi, Edge 2.0 TDCi). FORScan's terms of use need checking first.

### 3.3 Stellantis (Jeep, Dodge, Ram, Chrysler, Fiat, Alfa Romeo, Peugeot, Citroen)

* **Who executes it:** the case S2309000009 (October 2023, "Oil Change Monitor Reset") has the technician run two routines of the dealer tool, both under "Miscellaneous Functions": in the instrument panel cluster "Reset Service Information", and in the PCM "Reset oil change indicator".
* **Value:** OBDb publishes `22 3813` at module DA10 as "Engine oil degradation": 0 is new oil and the value grows. Replies recorded for the Fiat 500X (2019, 2021: 65 to 100) and the Jeep Renegade (2018-2019: 42 to 87). It is **not** oil life remaining.
* **Nothing else public:** `arduino-psa-diag` (GPL-3.0) documents session and unlock services for PSA coding zones and has no service reset.
* **Missing:** the routine identifiers. **Next lead:** wiTECH session recorded on the 500X or Renegade, where a read-back (`22 3813` going back to about 0) is already defined.

### 3.4 Mazda

* Official owner's manual, SKYACTIV-D 2.2: "engine oil data resetting" initializes the recorded oil value after an oil change; otherwise the wrench indicator may stay on or come on early. Driver method: ignition off, then on while the instrument-panel illumination knob is held for about 5 seconds until the master warning light flashes, then wait for it to flash for several seconds.
* On a Mazda diesel the learned value is the oil dilution (fuel in the oil), which a forum poster says FORScan can read in grams and reset. That is not an oil-life percentage.
* A 2011/2012 European bulletin names M-MDS "Powertrain, Service Functions, PCM, reset the oil change indicator" (seen only through a search summary).
* Reads with recorded replies: `22 0519` "Distance since oil changed" for Mazda 3, 6 and CX-5 (948 to 19,888 km).

### 3.5 BMW and MINI

* BMW's bulletins SI B62 06 13 (2014) show that on F01-F30 cars the CBS due dates for "Oil Service" and "Check Vehicle" are held in the **instrument cluster**, that a cluster "test function #25 Software reset" repairs a software fault in which the due date falls back to 01/2000 (it is a repair, not a service reset), and that ISTA has a test plan for "resetting/adjusting the Condition Based Service data".
* The EDIABAS description of a diesel DME (CBS version 5) lists the jobs: `CBS_DATEN_LESEN` (UDS `22`), `CBS_RESET` (UDS `2E`), `CBS_RESET_DATEN_LESEN` (`22 4050`), `STEUERN_CBS_DATEN_SICHERN` (`31 01 F0ED`), `STATUS_CBS_DATEN_SICHERN` (`31 03 F0ED`), `STATUS_CBSMOTOROELHIST` (`22 404F`), `STATUS_CODIERUNG_OEL` (`22 3701`). `CBS_RESET` takes the CBS item (for example `Oel`, `Br_v`, `Br_h`), an availability percentage (0 to 100; 150 for brake fluid), the number of services, a target month and year, a remaining distance with its unit, and forecast intervals. A sample argument string is `br_v,100,1,0,0,0,1,0,0`. **The identifier that `CBS_RESET` writes is not shown**, so the request cannot be built, and the page is derived from BMW's proprietary description file without a licence.
* OBDb's BMW "Distance since last oil change" (`22 DFE7`, module 0x19, bit 304) is not that: see section 5.

### 3.6 VAG (Volkswagen, Audi, Seat, Skoda, Cupra)

* Ross-Tech documents, in the instrument cluster (module 17, Adaptation), these items by name: "ESI: Resetting ESI" (oil service), "FIX: Distance covered since last mileage-dependent inspection" and "FIX: Time since last time-dependent inspection" (set to 0), and "SID/SIA: maximum value of distance/time to service" (the interval itself, in km). Older cars use numbered adaptation channels (02, or 05 to 08). No identifiers or bytes appear. US bulletins describe the manual cluster resets and warn that a manual oil-service reset on a flexible-service car switches the display to a fixed interval, and that resetting the interval between services makes the display wrong.
* OBDb has oil-level signals for the group and no service-interval value, so there is nothing to read back.

### 3.7 Kia, Hyundai, Genesis

* Kia TSB ELE209 (Rev 3, 2022): for 2019MY and later, the dealer tool KDS, "Cluster Module", "S/W Management", "Service Reminder Setting", change the service type from workshop to customer and enter the mileage and time. TSB ENG241 (2022): on 2023MY and later cars with the oil life management system the owner resets from the head unit ("Vehicle Settings", "Cluster", "Oil Change Reminder", "Reset") and the remaining oil life is shown there.
* Nothing is published about the wire. No oil-life or service signal exists in OBDb for these makes.

### 3.8 Honda and Acura, Toyota and Lexus, Mercedes-Benz

* Honda/Acura: older HDS path "Body Electrical", "Adjustment", "Maintenance Minder", "Reset", then the sub-item; newer cars with i-HDS or from the multi-information display depending on the item (bulletin 23-073). OBDb `22 7060` at module DA60 returns nine maintenance items with a recorded reply for the Accord, Civic and Clarity; the unit is not stated and values run 0 to 9,999.
* Toyota/Lexus: the Maintenance Required reminder is cleared from the multi-information display settings (a 2023 Lexus bulletin); Techstream appears in the bulletins for other jobs. OBDb `21 41` at module 7C0 ("Distance since oil change") has recorded replies for 16 Toyota and Lexus models, 0 to 28,968 km.
* Mercedes-Benz: the official Sprinter maintenance booklets say the dealer resets the ASSYST display and warn of engine damage if it is reset without the service. OBDb `22 6504` at 7E5 ("Distance to next service") has recorded replies for the EQB and EQV, 8,000 to 40,000 km.

## 4. What the evidence supports, per question of the mission

| Question | Answer |
|---|---|
| A reset that the app can execute and verify, anywhere | **None.** No source publishes a request that may be used, and the project's own promotion checklist (exact request, exact reply, read-back, real-vehicle validation) cannot be met without a vehicle. |
| Reset to 100% by a guided procedure | 165 owner-level procedures are shipped: the 164 of the first round plus, added from this round's documents, the Mazda CX-5 2017-2018 SKYACTIV-D 2.2 owner method (`mazda.cx5.2017-2018.skyactiv-d-oil-data-reset`, from the official owner's manual). Nothing else in this round's documents was an owner-level procedure the shipped guides did not already cover. |
| A chosen percentage (50%) | **Documented only for GM's own tool on the 2.4L engines above.** For the app: not available, and not offered. |
| Reminder-only reset | Documented per brand above; the module that holds it differs: cluster (BMW F-series, VAG, Kia, Honda), PCM (Ford, Mazda), both (Stellantis, GM's ECM plus cluster). |
| Whether the adapter can execute it | There is nothing to execute. For reads see `oil-life-capability-matrix.json`: which addressing form each request needs. |

## 5. Definitions that do not say what they hold

OBDb's BMW "Distance since last oil change" (`22 DFE7` at 6F1/19, bit 304, 32 bits) holds the **odometer**. The 343 replies recorded for it (BMW 3, 5, 7 Series, X3, X5, model years 2017-2024) run from 2,268 to 239,253 km; for the 3 Series 49 of 72 recorded values lie within 50 km (or 0.2 %) of an odometer value recorded for the same model year, 74 of 134 for the 5 Series, 30 of 52 for the 7 Series, 51 of 83 for the X5. In one recorded 3 Series reply the value is 97,546 km next to an odometer of 97,545 km. The shared definition also covers 19 BMW models, 4 MINI models and the Toyota GR Supra. The Service page would have shown it as a distance since the last oil change; it no longer does (`KnownMislabelledDefinitions`). Whether it is a mileage copy that the Mileage page could use as a further module needs a vehicle to say.

## 6. Negative findings of this round

* GitHub repository search for oil, service and CBS resets (ten queries): only unrelated repositories and two motorcycle service-reset tools; no car implementation.
* Web searches for byte-level resets (GM, BMW, Mercedes, VAG, Ford, Hyundai/Kia, Toyota): procedures and tool names only.
* FORScan threads: no raw data posted.
* `arduino-psa-diag`: no service reset.
* NHTSA bulletins for Ford, Toyota, Honda, VAG: they point to the owner's manual or the multi-information display, not to a tool request.

## 7. Next concrete leads, in the order that can produce a verified operation

1. **Ford diesel with FORScan** (Ranger, Everest, EcoSport EU, Edge): ToolSession recording through a Y-splitter. A read-back already exists (`22 054B`, recorded for Ford). Check FORScan's terms first.
2. **GM 2.4L (Equinox, Terrain, LaCrosse, Regal 2010-2012)** with GM's tool: records the scan-tool request that sets a chosen percentage; the oil-life read for these older cars is not yet recorded anywhere, so a first read capture is also needed.
3. **Fiat 500X or Jeep Renegade with wiTECH**: the routine plus the `22 3813` read-back.
4. **BMW F-series with ISTA**: the CBS write identifier and record encoding (the description names the job but not the identifier).
5. **VAG**: one cluster, with the part number, resetting "ESI" and the two "FIX" items.
6. **Production recordings:** the server keeps uploaded recordings (`/admin/reset-recordings`). Whether any exist was not checked: it needs the production admin login.
