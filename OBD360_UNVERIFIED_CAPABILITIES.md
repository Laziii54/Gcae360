# OBD360 — Unverified / quarantined / hardware-dependent capabilities register

Per `OBD360_SERVICE_MILEAGE_RESEARCH_BRIEF_2026-10-21.md`, section 10 and acceptance criterion 14. Everything below requires
either a physical vehicle, an owner decision, or further paywalled/professional source access that this session did not have.
Nothing on this list is offered to a user as a working capability today; confirming that is itself one of this register's
purposes.

## 1. Service — awaiting physical validation

| Item | Current status | What a physical validation would need to show |
|---|---|---|
| Chevrolet Volt 2012/2013 GMLAN oil-life reset | `oem_operations.json` candidate `vehicle.chevrolet.volt.2012-2013.oil-life-reset`: `BLOCKED_UNTIL_EXACT_WIRE_CAPTURE`. The read path (`22 119F`, `4DC0`) is implemented; the write request is not. `SERVICE_RESET_RESEARCH.md` (2026-09) claims this was "done" on a real car, but no capture file exists and the 2026-09-28 audit closed the write for exactly that reason — this session did not find or restore any such file. | The GDS2/Tech2 reset request, session/security-access sequence, and a read-back on a VIN-verified 2012-2015 Volt, captured and hashed per `research/next-steps.md` item A.2. |
| OBDb `119F` byte decoding (Volt oil life) | OBDb decodes it as a raw percentage; the app as `A×100/255`. Both are flagged debug; nothing is recorded. | One read on the Volt next to the dashboard. |
| GM "set to a chosen percentage" (bulletin 12312A) | GM's own bulletin says its tool can set a chosen oil-life value on 2010-2012 2.4L cars. No request is published; `AllowsPercentTargets` is `false` for every entry in `ReviewedGmOilLifeResets` (which is itself empty). | A capture showing the GM tool's actual write, on a 2.4L car in that year range, with a chosen value other than 100%. |
| Ford/Mazda diesel PCM reset (FORScan-documented) | FORScan's changelog names the PCM procedures for several Ford/Mazda diesels; no byte-level request is public. | A passive capture of FORScan performing the reset on a named vehicle (Ranger/Everest 2.2/3.2/2.0 TDCi, EcoSport EU 1.5 TDCi, Edge 2.0 TDCi, Ka 1.5 TDCi, or a listed Mazda). |
| BMW CBS reset (`CBS_RESET` routine) | The EDIABAS description documents the record structure and the DIDs around it (`4050`, `404F`, `3701`, routine `F0ED`) but not what `CBS_RESET` itself writes. No licence on the documentation repository (lead only). | The exact write identifier/payload from a licensed source, or a capture. |
| VAG cluster service-interval reset (UDS clusters) | Ross-Tech names the items (ESI, "FIX" entries); no raw bytes, and the identifiers are cluster-specific (depend on the cluster's data set). | A capture naming the exact cluster part number. |
| Stellantis IPC/PCM reset routines | STAR case S2309000009 names the routines (IPC "Reset Service Information", PCM "Reset oil change indicator") without publishing the request. | A capture on a Fiat 500X or Jeep Renegade, per `research/next-steps.md` item A.4. |
| Honda/Acura `22 7060` "Maintenance, &lt;item&gt;" values | Recorded for Accord/Civic/Clarity (0–9,999); no unit documented. | One read on a named car to learn what the number means before showing it as more than a raw figure. |
| Mazda diesel "learned oil value" | This is oil **dilution**, not oil life; FORScan shows it in grams. Currently left unread/unshown on the Service page pending a decision on whether to show it under its own kind. | An owner/product decision, not a vehicle — tracked here as unresolved. |

## 2. Service — quarantined (do not restore without new evidence)

| Item | Why quarantined | What would lift it |
|---|---|---|
| BMW/MINI/Toyota `22 DFE7` ("Distance since last oil change") | `Services/OemDiagnostics/KnownMislabelledDefinitions.cs`: 343 recorded replies are odometer-sized (49–74% within 50 km / 0.2% of a same-year odometer value, model by model); this is almost certainly the odometer, not a service interval. Excluded from both the Service page's exact-model and brand-wide candidate paths. | A real BMW/MINI/Toyota confirming what this value actually is, against both the dashboard mileage and the dashboard's own "distance since oil change" figure if the car shows one. |
| Generic `22→2E` write candidates (GM `4DC0`, Ford `054B`, Mazda `0519`) and the inferred GMLAN `$3B` write-back | Disabled in the 2026-09-28 audit: no verified exact request for any of them. | Per-vehicle capture, same bar as section 1. |
| `arduino-psa-diag`, `l77rodeo/gmlan`, `emdzej/ediabasx-docs-sgbd` as sources | GPL-3.0 / no licence / no licence respectively — leads only, never a basis for a shipped identifier. | A separately-licensed or primary source for the same fact. |

## 3. Original Mileage — awaiting physical validation

| Item | Current status | What a physical validation would need to show |
|---|---|---|
| BMW `22 DFE7` as a *Mileage*-page candidate | Odometer-sized by the evidence above; never an active Mileage-page request. Could become a further module for the Mileage page after confirmation. | A real BMW confirming the value against the cluster's own odometer reading. |
| 302 vehicles with ≥2 odometer modules | Public definitions only; no capture has compared the modules of any of them. The four cars this project did capture did not return PID A6 at all. | One car per make with 2+ modules, per `research/next-steps.md` item C.2. |
| 47 module pairs that disagree ≥90% of the time in OBDb's own recorded tests (VW 7E0/7E1/714, Honda DA10/DA60, Toyota Prius, Ford F-150 Lightning, others) | Flagged as a possible definition problem, not necessarily a real mileage conflict. | Either a vehicle, or an explicit decision on how "sources differ" should be worded when this pattern is detected. |
| Rows the Mileage page deliberately does not use (85 "total odometer" rows: 25 unresolved module address, 25 unit "scalar", 22 forty-bit fields, 13 unit "celsius"; Ford `Transmission odometer` declared in °C; Toyota/Lexus/Scion `Total distance` unit "scalar"; Ford Escape 2012 `22 DD01` = 243,087 in 3 bytes, unexplained) | Deliberately excluded; a scale/unit is never guessed. | One vehicle per case. |
| CAN-broadcast candidates (Rivian, further Hyundai, VW diagnostic frames, 2011 Ford layouts) | Identified in `mileage-source-catalog.json`; no decoder built, no unit field. | A decoder plus one car each. |
| DTC-snapshot mileage (`19 04`) | No source says this exists for any manufacturer. | A manufacturer record description before any code is written. |

## 4. Manufacturer families with no catalog entry at all (Service)

Hyundai, Kia, Genesis, Nissan, Infiniti, Mitsubishi, Suzuki had no Service-catalog entry and, before this session, no dedicated
search beyond the project's existing owner's-manual guides (which don't cover them either — the catalog's existing Hyundai entry
is a TPMS reset, a different system). This session searched directly for owner's-manual-level, driver-performed dashboard/menu
reset procedures for these families. One addition was made (Kia EV6 2023 — see `OBD360_FINAL_SERVICE_RESEARCH.md` §5 for the
full citation); it meets the same evidentiary bar as the other 165 guides already in `service_procedures.json`, so it was added
directly rather than held as a proposal, consistent with how those 165 were added.

| Family | Outcome |
|---|---|
| Kia (EV6, model year 2023 only) | **Added.** Official Kia owner's-manual web help names the exact menu path (Setup → Vehicle → Cluster → Service Interval → Reset). Not extended to other EV6 years (the 2024 manual page no longer carries this path) or other Kia models (a Kia TSB shows at least some other models need the dealer's KDS tool instead). |
| Hyundai | **Not added — no official source found.** Every result for Tucson/Santa Fe/Palisade/Sonata was a third-party blog or an SEO-farm mirror; no `hyundai.com`/`hyundaiusa.com` page with the actual steps turned up. |
| Nissan | **Not added — official source confirms the reminder but not the steps.** `nissan.ie`/`nissan.com.mt`'s own Juke manual says the oil indicator must be reset manually and does not reset itself, but the linked reset-procedure sub-page could not be retrieved through this session's tools. Third-party guides disagree with each other on the exact menu path. |
| Mitsubishi | **Not added — no official source found.** Outlander searched specifically; only third-party sites and apparent mirror/spam PDF hosts came back. |
| Genesis, Infiniti, Suzuki | **Not researched this session** — a genuine time-budget gap, not a negative finding. Still open for a future session. |

## 5. Not applicable to this run

* The 0–100 km/h acceleration/leaderboard system: out of scope, not investigated.
* Mode 05/06 diagnostics: not touched, per standing project rule.
