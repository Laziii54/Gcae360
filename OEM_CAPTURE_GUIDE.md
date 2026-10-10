# Capturing real-vehicle observations (read-only)

Goal: get evidence from a real car so that someone can later check it against documentation. A capture is raw
material. **It does not make any feature supported**, and nothing is marked verified until the capture has been
reviewed.

The 10 research targets and what is known about each are in `Resources/Raw/oem_capture_targets.json`
(Mercedes-Benz C-Class and E-Class, Nissan Altima and Rogue, Opel Astra, Skoda Octavia, Suzuki Swift, Tesla Model 3,
Renault Megane, Chevrolet Volt). Per-model gaps are summarised at the end of this file.

## What the app does and does not do

- The recording only listens to requests the app already sends (standard OBD-II reads and the read-only module scan).
  It sends nothing extra to the car and nothing to the internet.
- It keeps only read-only requests. VIN requests, VIN-like text in an answer, serial numbers, session/security/routine
  requests, writes and "clear codes" are dropped. Standard OBD-II answers are reduced to "this PID answered" and the
  supported-PID bitmap, never the measured values.
- A module's raw reply is kept, and it can contain a value the module reported at that moment (for example a
  temperature). The file has no VIN, name, account or location. Look at it before you share it.
- It is independent of the optional "share compatibility data" setting. That setting uploads to the server; the
  recording never does.

## Before you start

1. Use your own car, parked in a safe place. Do not record while driving.
2. Ignition on, engine off, unless the target says otherwise. For the Volt, make one recording with READY off and a
   second with the car in READY.
3. Use an adapter you have already used successfully with this app. A cheap adapter that drops frames gives
   incomplete evidence.
4. Write down, outside the app: model year, engine and fuel type, gearbox, market, and the protocol the app shows.

## Steps

1. Open **Settings → Record a car capture to a file**.
2. Choose the target (or "Other vehicle") and press **Start recording**.
3. Connect to the car and wait until the app shows the vehicle make and model. If it does not, the recording keeps
   nothing (it needs a make and model); **Save and share** will then say that nothing was recorded.
4. Run the **full scan**, then the **module scan**. Nothing else is needed.
5. For the Volt only: open the hybrid battery page once so its read-only battery requests are exchanged.
6. Return to Settings, press **Stop recording**, then **Save and share recording**. Send the file to whoever reviews it.

## What a useful capture shows

For each target, the evidence we need is listed under `missing` in `oem_capture_targets.json`. In general:

- which module addresses answered, and with what frames (the file lists per module how many requests got frames and
  how many got only a status such as NO DATA);
- the protocol and model year the app detected;
- for a model with no answer at all, that fact (an empty module list on a connected car is also evidence).

A reply is not proof of what a request means. Someone must compare it with documentation before a feature is marked
supported.

## Models that probably need other equipment

- **Tesla Model 3**: this app has no evidence that an ELM327 gets any answer from it. Public projects read it through
  its CAN buses with dedicated hardware. If the capture shows no answer to the standard 0100 request, treat the model
  as unsupported for this adapter.
- **Mercedes-Benz, Opel, Suzuki**: generic OBD-II may work, but module addressing has no documented basis here, so a
  capture can only show which addresses answered.

## Gaps per model (from the catalog snapshot)

| Model | Known | Missing |
|---|---|---|
| Mercedes C-Class | Standard OBD-II only; no C-Class generation data in the catalog | Generations, module addresses, services, gateway behaviour |
| Mercedes E-Class | Generation list; one 2010 E350 CAN signal file | Module addresses and services per generation |
| Nissan Altima | 4 catalog addresses (2019-24), 7 unverified probe addresses | Response IDs, services, DTC format |
| Nissan Rogue | Probe addresses shared with Altima | Any Rogue-specific data |
| Opel Astra | Generation list; scanned as Stellantis family | Which design/protocol applies per generation, module data |
| Skoda Octavia | 5 catalog addresses (Mk3), 11 probe addresses | Response IDs, services, other generations |
| Suzuki Swift | Generation list only | Everything beyond generic OBD-II |
| Tesla Model 3 | CAN signal files, 1 address | Whether any ELM327 path exists |
| Renault Megane | Nothing for Megane (Zoe/Twizy data only) | Everything model-specific |
| Chevrolet Volt | Most developed: battery reader, oil-life DID research, simulation tests | Real-car confirmation, Gen 2 differences, reset routine |
