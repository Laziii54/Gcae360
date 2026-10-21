# OBD360 — Adapter compatibility: evidence matrix and how the logs will be read

**No real-vehicle evidence has been collected yet.** This file is the plan for collecting it and the key for reading it. Every
cell below starts empty. Nothing here claims a result.

Companion files: `OBD360_ADAPTER_HARDWARE_TEST_GUIDE.md` (what the owner does, step by step) and
`OBD360_ADAPTER_STAGE2_PROPOSAL.md` (what the evidence decides).

## 1. The tests

Use only a transport the adapter really supports. A Bluetooth-only adapter has no Wi-Fi row; an adapter that offers both gets
both. Keep the vehicle state the same for every adapter on the same car (see the guide).

| ID | Vehicle | Adapter | Transport | Feature | Purpose | Done |
|---|---|---|---|---|---|---|
| **V-CTL-E** | Chevrolet Volt 2012 | expensive | what it supports (state it) | Live Data, 30 s, standard values only | Control: does this adapter work at all on this car? | ☐ |
| **V-CTL-C** | Chevrolet Volt 2012 | cheap | the same transport as V-CTL-E | Live Data, 30 s | Control | ☐ |
| **V-BAT-E** | Chevrolet Volt 2012 | expensive | same | Hybrid battery check | The feature that works | ☐ |
| **V-BAT-C** | Chevrolet Volt 2012 | cheap | same | Hybrid battery check | The feature that fails | ☐ |
| **F-CTL-E** | Ford Escape | expensive | what it supports (state it) | Live Data, 30 s | Control | ☐ |
| **F-CTL-C** | Ford Escape | cheap | the same transport | Live Data, 30 s | Control | ☐ |
| **F-MIL-E** | Ford Escape | expensive | same | Mileage Verification | The feature that fails | ☐ |
| **F-MIL-C** | Ford Escape | cheap | same | Mileage Verification | The feature that works | ☐ |
| **X-BLE-WIFI** | either car | one physical adapter that supports **both** BLE and Wi-Fi | BLE, then Wi-Fi | the car's failing feature | Is it the radio link or the adapter? (only if such an adapter exists) | ☐ |

Order inside a car: control first, then the feature, then the other adapter — with the car left in the same state and the
same day. One test per app start (fully close the app between tests; the connection log lives only in memory).

## 2. What to record for every test (copy this block once per test)

```
Test ID:                  (from the table)
Date / time:
Vehicle + model year:
Vehicle state:            (e.g. ignition ON, engine OFF  /  READY)
Adapter name as shown by the app:
Adapter's own name/model on the box (if different):
Adapter identification in the log:   (the "Init ATZ: banner …" line)
Transport:                BLE / Bluetooth Classic / Wi-Fi
Phone model + Android version:
App version:              (diagnostic build)
Raw capture was ON before connecting:   yes / no
Feature tested:
Did it succeed?           yes / no / partly      What does the screen say, word for word?
Is it repeatable?         ran N times, same result N times
Connection log pasted:    yes (attach)
Notes (anything odd: the adapter blinked, the phone dropped Bluetooth, …):
```

Keep VINs, plates and names out of notes and screenshots. The log and the capture do not contain the VIN.

## 3. How the logs will be read

Every line below is written by the Stage 0 logging; the right-hand column is what the line suggests, **not** a conclusion.
Several lines together decide; one line alone does not.

| What the log shows | What it suggests | Which Stage 2 item it feeds |
|---|---|---|
| `Init ATZ: banner "ELM327 v…"` | The adapter's own identification (and over which link: `BLE`, `Classic SPP`, `Wi-Fi TCP`). | — |
| `Init <command>: NOT accepted (reply "?", AdapterRejected [adapter])` | The adapter's firmware does not know that command. | A (if the command is `ATS1`/`ATS0`) |
| `Setup: required command ATS0 NOT accepted …` followed by `Module session over …: 0 send(s)` | The Volt-style battery read cannot even start on this adapter. | **A** |
| `Read <DID>: tx 7E1, reply id not given, standard pair 7E9; frames from 7E8x1; ForeignReply [ecu]` | A *different* module answered. Consistent with an adapter that did not apply the requested address. | **C**, D |
| `Module session over …: N send(s); … frames from 7E8 xN; N replies came only from other modules` | Same, for a battery-module session. | E, C |
| `Read <DID>: …; NoData [ecu]` with `filter: ATCRA` | Silence. Cannot tell "module absent/asleep" from "address ignored behind a hardware filter". | (the explicit header check, if ever approved) |
| `Init <command>: accepted (reply "<command> OK", Echoed [adapter])` | The adapter repeats commands although echo was switched off. | E, B |
| `Timeout: <cmd> (streak n); no reply within X s, N attempt(s), M ms spent` | Slow or silent adapter/car; transport vs ECU is decided by the neighbouring lines. | F |
| `Wi-Fi: no reply to <cmd> …; the resync was answered (adapter alive, just slow)` | A slow adapter, not a dead link. | F |
| `BLE link event: … (was connected: True)` | The BLE stack reported a drop; the retries after it are normal. | F-1 |
| `Adapter test: can.set-header = …` / `can.receive-filter-verified = …` | What the adapter's explicit self-test found. | A, C |
| Raw capture line `… try 1/1 … -> Data reply "7E8 …"` | The exact text the adapter returned, with timing. Replies to VIN/identification requests are never stored. | any |
| Capture line `… try 1/3 … [transport] … (IOException)` | The BLE link was lost during that request (Stage 2A: this is now an `IOException`; older builds showed `NullReferenceException`). The retries that follow show `Fault [transport] … (InvalidOperationException)`. | F-2 |
| Mileage log `…: Negative — [no reply] NO DATA: …` | Silence at the asked address. Does not prove the module lacks the function. | C |
| Mileage log `…: Negative — [negative response] ECU refused: … (NRC 0x31)` | The module answered and said no. A different finding from silence, although the decoder files both under "Negative". | C |
| Mileage log `… — [adapter/transport] …` | The adapter refused a setup command, answered `?`, reported a bus error, or the link was down when the request failed. A limit of the adapter or link, not of the car. | A, F |
| Mileage log `… — [malformed reply] …` | Something arrived that is not the answer to this request, arrived incomplete, or cannot be decoded (too short, bad scaling). | B |
| Mileage log `… — [other ECU] only frames from 7E8 arrived, none from the asked module` | A reply id was known and a different module answered; the value is not used. | C |
| Mileage log `…: 243087 km — [other ECU] value decoded; the reply came from ECU 0x7E8, not from the addressed module …` | No reply id was known; the value was decoded and **named** as coming from another ECU. It is not verified. | **C** |
| Mileage log `… — [unverified] value decoded; the signal definition is marked as under test …` | A value from a definition that has not been verified on a car. | — |
| Hybrid battery page: "This adapter does not accept a setting the battery check needs (ATS0)…" | The adapter refused a setup command of the battery session. Same evidence as `Setup: required command ATS0 NOT accepted` in the connection log. | **A** |
| Hybrid battery page: "The battery module did not respond to the requests …" | The first three cell requests all ended in silence or timeouts, with nothing else seen. One possible cause is that the car is not switched on; the app does not claim it. | A, C |
| Hybrid battery page: "The adapter returned a reply from another module …" | Frames came back only from another module (matches `ForeignReply [ecu]` / `N replies came only from other modules` in the log). | **C** |
| Hybrid battery page: "A reply came back … but it could not be read …" | Text arrived that the pack parser could not read (echo, damaged frames, or a value that is not a cell voltage). | B |
| Hybrid battery page: "Only N of the battery's 96 cells returned a valid value …" | The module answered, but fewer than 80 cells gave a valid value. The log lists `GM BECM 7E7 …: N/96`. | — |
| Hybrid battery page: "The connection to the adapter was lost during the check …" | The link was down when the read ended, or a request failed because it dropped. | F |
| Hybrid battery page: "The battery did not answer. Turn the car on (READY) and try again." | **Nothing was decided** (a refusal, a bus error, a mixture, or no request was made). This is the sentence the page always had. | — |

**Reading the pair of adapters on one car:** if the control passes on both and only the feature differs, the adapters differ in
how they handle *that kind of request* (addressing, setup commands, framing). If the control fails on the cheap one, the problem
is general (connection, power, firmware), not this feature. If BLE and Wi-Fi on the *same* adapter differ, the transport is
involved.

## 4. What will **not** be asked of the owner

No clearing of codes, no coding, no service reset, no experimental command, no change of any adapter setting, no driving test.
Everything above is read-only and uses only what the app already does on its own screens.
