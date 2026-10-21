# OBD360 — Adapter comparison: how to run the car tests

**Status: no real-vehicle test has been run yet.** The diagnostic build has been built and checked on a PC only (see section 0).
This guide is what you do with it. Nothing in it asks you to change an adapter setting, clear a code, code a module or send a
special command — the app only does what its own screens already do, and the new logging only *watches*.

The test table and the log-reading key are in `OBD360_ADAPTER_EVIDENCE_MATRIX.md`. What the results will decide is in
`OBD360_ADAPTER_STAGE2_PROPOSAL.md`. A Georgian copy of this guide: `OBD360_ADAPTER_HARDWARE_TEST_GUIDE_KA.md`.

## 0. What you are installing

| | |
|---|---|
| File | `bin\DiagnosticApk-Stage2A\com.obd360.app-Signed.apk` (in the project folder) — the **Stage 2A** build. The earlier Stage 0/1 build is still in `bin\DiagnosticApk\` (different file, SHA-256 `9371715…`); use the Stage 2A one for the tests. |
| Size | 118,998,698 bytes (about 113 MB) |
| SHA-256 | `8caf5a446d5e540ddff647573edb96412a882b404e960d87749e734904e79d02` |
| Package / version | `com.obd360.app`, versionName 1.0.0, versionCode 62 |
| Build type | **Debug, signed with the standard Android *debug* key** (not your release key, not a store key) |
| Checked | package data, alignment and signature checked with the Android build tools (`apksigner verify`: valid, v2 + v3 signature, "Android Debug" certificate). Installed and launched on an **Android 16 x86_64 emulator** (no Bluetooth/Wi-Fi adapter, no car): it starts, the home screen shows, no crash. The Raw capture button was checked on the Stage 0/1 build in all nine languages (Stage 2A did not touch that page). **Not installed or run on a real phone.** |

**What the Stage 2A build changes** (it does not change any request sent to the car): a lost Bluetooth link now ends a request with the proper "link lost" error instead of an internal error; the Hybrid battery page says *why* a Volt check ended without a result (adapter refused a setting, battery module silent, another module answered, reply unreadable, too few cells, link lost) — otherwise it shows the same sentence as before; the Mileage log lines say what each request showed (`[no reply]`, `[negative response]`, `[adapter/transport]`, `[malformed reply]`, `[other ECU]`). The key to reading them is in `OBD360_ADAPTER_EVIDENCE_MATRIX.md`.

Because it is signed with the debug key:

- Android will **not install it over** a copy of OBD360 that came from Google Play or from your release key. Android shows
  "App not installed" / a signature conflict. To use this build you must **uninstall the other copy first — that deletes the
  app's data on the phone** (sign-in, saved settings, local history). If that matters, use a spare phone, or a second Android
  user profile, or back up what you need first.
- It cannot be uploaded to Google Play. It is only for these tests.
- When the tests are over, uninstall it and reinstall your normal build.

## 1. Install and open the APK

1. Copy the `.apk` to the phone (USB cable, Drive, or any file transfer you trust). Check the size (about 113 MB).
2. On the phone, open the file. Android asks to allow installs from that app (Files / Chrome / …) — allow it for this one
   install, then go back.
3. Or from a PC with the phone in USB-debugging mode:
   ```
   adb install -r "C:\GCar360\Georgian-Car-Scanner-Universal-MultiVehicle-FIXED\bin\DiagnosticApk-Stage2A\com.obd360.app-Signed.apk"
   ```
4. Open **OBD360**. On first start it shows a data-sharing question ("Let's improve OBD360 together!") with **I agree** /
   **Not now**. That is your own choice and is **not needed for these tests**; the Raw capture described below is a separate,
   local feature. The next screen has two cards — tap **OBD scanner** (not VIN Check). Allow the Bluetooth / nearby-devices
   permission when asked (needed for Bluetooth adapters).
5. Leave the phone's language on the language you normally use. The new "Raw capture" button was checked on the emulator in
   all nine app languages (Georgian, English, Turkish, German, Polish, Spanish, Portuguese, French, Italian) and also at a
   narrow screen with a large font: the text wraps onto two or three lines and nothing is cut off. The buttons **TEST** and
   **Copy connection log** are shown in English in every language (that is how the app already behaved; it is not part of
   this change).

## 2. Connect each adapter

Do this once per test, **after** step 3 below (Raw capture on). One adapter plugged in at a time, no second scan tool or
phone app talking to the car.

- **Bluetooth adapter** (BLE or classic): pair/choose it the usual way in the app's connect screen. You do **not** need to
  know whether it is BLE or classic — the connection log says it (`Connected over BLE` or `Connected over Classic SPP`).
- **Wi-Fi adapter**: join the adapter's own Wi-Fi network in the phone's Wi-Fi settings first (if Android says "no internet,
  stay connected?" choose to stay). Then connect in the app. The log says `Wi-Fi TCP`.
- Only use a transport the adapter really supports. Do not try Wi-Fi on a Bluetooth-only adapter.
- If an adapter supports **both** BLE and Wi-Fi, test both on the same car, same state, one after the other
  (test `X-BLE-WIFI` in the matrix). If you are not sure it supports both, skip it.

## 3. Turn Raw capture on (before connecting)

1. OBD scanner home screen → scroll down to the **Settings** section ("პარამეტრები") → tile **Adapter** ("ადაპტერი"). This opens
   *OBD ADAPTER CENTER*. It works before you are connected (the other tiles are greyed out until you connect). The page has
   three buttons: **TEST**, **Copy connection log**, and the small **Raw capture** button.
2. Tap the small button at the bottom of the buttons: **"Raw capture: OFF — tap to start (for support)"**.
3. It changes to **"Raw capture: ON, about 30 min left — tap to stop"**. It stops recording by itself after 30 minutes
   (the text on the button changes back to OFF). What was already recorded is **kept** until you close the app, so you can
   still copy it afterwards. Do not tap the button again while it is ON — that switches it OFF.
4. Go back and connect the adapter (section 2).

What Raw capture is: a bounded note of the commands the app sent and the text the adapter answered, with timings.
It is **off** unless you switch it on, it is kept **in memory only** (closing the app wipes it), and it is cleaned: VINs,
Bluetooth addresses and the replies to VIN/identification requests are never written. It is not sent anywhere; it leaves the
phone only when you tap Copy and paste it yourself.

**Size limit:** it keeps the newest 600 entries; when it is full the oldest are dropped. The first line of the copied text
says how many entries it holds. **If it says 600, the beginning was cut off** — write that in the record. The connection log
(the `Init …`, `Setup …`, `Connected over …` lines) is separate and still shows how the session started.

## 4. The tests (what to do, in order)

Run them in the order of the matrix: **control → feature → other adapter**, same car, same state, same day.

### 4a. Control test (every adapter, every car) — *Live data*
Connect → **Live data** ("ცოცხალი მონაცემები", in the Diagnostics section) → leave it running for about 30 seconds with standard values
(speed, RPM, coolant temperature) showing → go back. Record whether values appeared and kept updating.
This tells us whether the adapter works on that car at all, so a failure later is not a connection problem.

### 4b. Ford Escape — Original Mileage (*expensive adapter fails, cheap one works*)
Connect (the **Connect** row at the top of the home screen) → scroll to the **Technical** section → **Mileage Verification**
("გარბენის გადამოწმება") → let it run to the end (do not leave the screen) →
note the result headline **word for word** and whether any odometer value appeared. Do not type any number in.
Run it **once** per adapter, same ignition state. If it fails, you may repeat it **once more** to see whether it fails the
same way (write "ran 2 times, same result both times" or "different").

### 4c. Chevrolet Volt 2012 — Hybrid battery (*expensive adapter reads cells, cheap one fails*)
Connect → on the home screen tap the **Hybrid battery** card near the top ("ჰიბრიდის ბატარეა", subtitle "Cells, SOH, load
test"; it shows a PRO badge — it needs your PRO access like any paid feature). It is **not** the plain **Battery** tile in the
Technical section, which is a different (12 V) page. Tap **Check** ("შემოწმება") → wait until it finishes → note the screen text word for word, and whether
cell values appeared (and roughly how many). If the page has **Copy log**, you may use it too, but the Adapter Center copy
in step 5 is the one that matters.
Run it **once** per adapter, same state; at most one repeat.

### 4d. (Optional, once per adapter) *TEST* in Adapter Center
Settings section → **Adapter** → **TEST**. This is the app's existing adapter check (it asks the adapter about
itself — name, firmware, voltage, supported commands — and sends the standard "0100" supported-PIDs request). Run it
**after** the feature test, in the same app session, and copy the log (step 5) afterwards. It is not required for the
comparison; skip it if you prefer to keep the test short.

## 5. Export the connection log (after each test)

1. Settings section → **Adapter** (same page as before; the capture stays as it was).
2. Tap **Copy connection log**. A status message says the connection log **and the raw capture** were copied.
   (If it says only "Connection log copied", the capture held nothing — it was off or nothing was recorded; write that in
   the record.)
3. Paste it into a note/e-mail/chat to yourself and name it with the Test ID (for example `F-MIL-E`).
4. **Fully close the app** (swipe it away) before the next test. The log and the capture live only in memory, so the next
   test starts clean.

## 6. What to write down for every test

Copy the block from section 2 of `OBD360_ADAPTER_EVIDENCE_MATRIX.md` once per test. In short:

- Test ID, date/time, vehicle and model year, **vehicle state** (ignition ON engine OFF / READY / charging …).
- The adapter's name as the app shows it, the name/model printed on the adapter or its box, and — from the log — the
  `Init ATZ: banner …` line and the `Connected over …` line (that is the real transport).
- Phone model, Android version, app build (the diagnostic APK above).
- Raw capture on before connecting: yes/no.
- Feature, result **word for word**, repeatable (N of N), anything odd (adapter LED behaviour, Bluetooth dropping, the car
  clicking, warnings on the dash).
- Do **not** write the VIN, plate number or owner name anywhere. The log and capture do not contain them; keep it that way
  in screenshots too (crop them out).

## 7. Keep the conditions the same — this is what makes the comparison valid

Only compare two tests if all of these match; if one differs, note it and treat the pair as **not comparable**:

- same vehicle, same day, close in time (the second adapter within minutes, not days);
- same ignition state (engine off with ignition on, or READY, or charging — pick one and use it for both adapters);
- Volt: charging cable connected or not — the same for both adapters (write which);
- same phone, same Android Bluetooth/Wi-Fi state, same distance from the adapter, app fully closed between tests;
- only the adapter being tested is plugged in; nothing else connected to the OBD port;
- same app build for every test (this APK).

If a test is interrupted (call, notification, adapter fell out) — discard it and redo it; do not mix it into the pair.

## 8. What not to do

- Do not clear codes, run service resets, coding, adaptations, actuator or any "active test" function.
- Do not type or edit values (for example in "Enter manually").
- Do not retry a failing feature more than once more, and do not change adapter settings or flash anything.
- Do not send anything from this build to anyone except the pasted log text you choose to share.

## 9. What happens with the results

Once the logs exist they are read with the key in the evidence matrix. Several of the Stage 2 changes
(`OBD360_ADAPTER_STAGE2_PROPOSAL.md`) stay unbuilt until a real log from the adapter in question shows the behaviour they
would fix. Nothing in this guide has been run on a vehicle yet; do not read any statement here as a result.
