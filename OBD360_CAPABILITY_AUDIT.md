# OBD360 - diagnostic capability audit (Phase 2)

Read from the code of this repository. "Real-vehicle verified" means a person tested it on a car; **nothing below was tested on a
physical vehicle or adapter in this work.** Unit tests use hand-written outcomes or the in-repo ELM327 simulator.

| Area | What the code really does | Status | Gap / decision |
|---|---|---|---|
| Module trouble-code scan (ABS, SRS, BCM, IPC, TCM, EPS, HVAC, hybrid, ...) | `ModuleDtcScanService`: read-only UDS 19 02 / KWP 18 / GMLAN A9 on the legislated 0x7E0-0x7E7 range plus address tables per make family (GM, Ford, Mazda, Hyundai/Kia, Toyota, VW group, Nissan, Subaru, BMW 0x6F1) and addresses from the offline catalog. Not asked while the car moves. Probes keep the reason for silence. | Implemented; address tables are unverified on cars here | Honda, Stellantis, Chrysler and Renault have no table - no documented addresses were available, none were invented |
| ABS code clearing | `ClearDetailedAsync` (UDS/KWP 0x14, GMLAN 0x04) only on explicit request; refusals (NRC) reported per module | Implemented | unchanged |
| ABS / chassis live data (wheel speeds, steering angle) | `ModuleLiveDataService` uses the public OBDb signal definitions (read-only UDS 22 / KWP 21), shows a value only after a positive, identifier-matching, in-range reply | Implemented for the models OBDb describes; unverified on cars here | no derived "wheel-speed fault" analysis was added (would be an interpretation without evidence) |
| **ABS / module reach vs "no faults"** | **New:** `CoreModuleStatusEvaluator` gives ABS, transmission, airbag, body, cluster, engine and hybrid battery one status each: reached with codes / reached with a complete empty list (the only "no faults") / incomplete / refused / no code list / foreign reply / request failed / silent / not asked (car moving, no verified address, not CAN). Shown as extra rows in the module list. | New, unit-tested (14 tests) | needs a real car to see the wording in use |
| Mileage | Standard PID A6 (odometer) and context PIDs 21/31/4D/4E kept apart from the odometer; read-only catalog UDS reads; passive CAN decoding with verified profiles (2 profiles: Toyota Prius 2017, Lexus CT200h 2018, from a public DBC); learned places; analyzer that treats zero/implausible values as "not used" | Implemented; coverage is narrow | `VerifiedMileageProfiles.json` is empty. Adding profiles needs a documented DBC/UDS source per model; none was added without one |
| Fuel | Direct fuel rate PID 5E, air-flow estimate (MAF) with fuel constants, diesel only with a measured lambda, fuel-cut inference labelled, trip engine with data-hole handling, hybrid split, EV energy readers | Implemented, simulator-tested | **New:** PID 9D (fuel mass flow, g/s) is used as a direct reading, converted with the density of the selected liquid fuel (never for unknown fuel / CNG); capability list calls it "calculated from reported data". 11 tests |
| Hybrid / EV battery | `HybridHealthService.*` + `HvBatteryCore`: Toyota hybrid, GM Volt/Bolt, Hyundai/Kia, Nissan Leaf and further EV decoders; values only from decoded replies | Implemented per brand; unverified on cars here | unchanged; no state of health is computed where the car does not report it |
| Capability / verification labels | `CapabilityDetectionService` tiers per connected car; manufacturer features need a verified address and definition | Implemented | unchanged |

## What was added in this work
1. PID 9D fuel-mass-rate source (resolver, mapper, compatibility list, live-data PID selection, text, language rows, tests).
2. Core-module status evaluator and its rows in the module scan result (model, scan outcome, report, view model, language rows, tests).

## Still needs a real vehicle and adapter
Everything above: address tables, ABS/chassis live data, 9D on a car that advertises it, the new module rows, hybrid decoders.
