# Physical-Device Recording Checklist

A step-by-step script for recording the video Apple requested, on a real iPhone. This was not
recorded as part of this audit — no device or recording capability is available in this
environment. Follow this script yourself.

## Before you start

- [ ] Use a real iPhone (not the Simulator — Apple asked for a physical-device recording).
- [ ] Install the **current** build from TestFlight or a direct install, not an old one — confirm
      the version/build number on screen matches what you intend to resubmit.
- [ ] Have a real, compatible OBD2 adapter (Bluetooth LE or Wi-Fi) and a vehicle available and
      ready (ignition on) for the scanning portion.
- [ ] Have your App Store sandbox tester Apple ID signed in on the device (Settings → App Store →
      Sandbox Account) so the purchase step doesn't charge real money.
- [ ] Decide whether you'll create a dedicated review/demo account, or sign in with a personal
      one. **Do not show a real payment card, a real email inbox with personal mail, or anyone
      else's personal data on screen.**
- [ ] Use the system screen-recording feature (Control Center → Screen Recording) so the output is
      a real device capture.

## Recording script

1. **Launch** — start from the Home Screen, tap the OBD360 icon, record the full launch to the
   first screen (`StartPage`: "OBD scanner" / "VIN Check" cards).
2. **VIN Check (no hardware needed)** — tap "VIN Check," enter a real or test VIN, show the lookup
   completing and a result screen. This demonstrates a fully functional feature with no adapter
   required.
3. **OBD scanner — connect** — go back, tap "OBD scanner," open the Bluetooth/adapter screen, pair
   with or connect to your OBD2 adapter. If you want to also show the "not connected" empty state
   first (recommended — it's informative, not broken), briefly show the screen before connecting.
4. **Diagnostic scan** — with the adapter connected and the vehicle's ignition on, run a trouble-code
   scan. Show the results screen (whether it finds codes or shows "no codes found" — either is
   fine, it just needs to demonstrate the real flow).
5. **Live data** — open Live Data or Sensors, show live values updating from the vehicle.
6. **Module diagnostics** — open the module/ECU screen and show the modules the vehicle actually
   answers (ABS, SRS, transmission, etc. where supported). This is the part App Review said it
   could not evaluate, so give it a few seconds of unhurried screen time.
7. **(Optional but recommended) AI explanation** — if you have an active entitlement or sandbox
   purchase ready, open the AI diagnostic explanation for a result.
8. **PRO / purchase flow — deliberately signed out.** Do this step **before** signing in to any
   OBD360 account, because the point is that a purchase needs no account:
   - Stay signed out, open the PRO screen from the tab/menu (or by opening a PRO-gated feature and
     tapping through the upgrade prompt). There is no login prompt and no registration step.
   - Show the three monthly plans (VIN PRO / OBD PRO / Full PRO) and the price on each card. There
     is **no Monthly/Yearly toggle and no annual card** in this build, so nothing to switch.
   - Tap the purchase button and complete Apple's own purchase sheet with your sandbox account.
   - Show the app reflecting the new PRO status afterwards (e.g. a PRO-gated feature now opening).
   - Kill and relaunch the app while still signed out, to show PRO survives a restart with no
     account.
9. **Restore Purchases — still signed out.** Delete and reinstall the app (or use a second device
   with the same sandbox Apple Account), stay signed out, and tap "Restore Purchases". Show it
   reactivating PRO. It must not ask for an OBD360 account.
10. **Account (optional)** — go to the Account page, show signing in (email/password, or
    Google/Apple — your choice), *without exposing the password on screen* (type into a masked
    field, don't narrate it). Then show that PRO is still active, and that signing out again keeps
    the App Store subscription active.
11. **Legal links** — from the PRO page, tap through to Privacy Policy and Terms of Use, and
    Settings → Manage Subscription, and show they load real content.
12. **Account deletion (optional but strong evidence for 5.1.1(v))** — on a throwaway test account
    only, show the Account page's "Delete account" flow through its confirmation dialogs. **Do
    not do this on an account you want to keep**, and don't do it on your sandbox purchase account
    until after steps 8–10 are captured.

## What this does NOT require

- You do not need to demonstrate every diagnostic module or every vehicle-specific feature — one
  real scan and one live-data view is enough to show the core flow works.
- You do not need a specific make/model of car; any vehicle the adapter supports is fine.

## Privacy / content checklist before sharing the recording

- [ ] No real payment card number visible (the sandbox purchase sheet shows Apple's sandbox
      pricing/flow, not a real card — that's expected and fine).
- [ ] No personal email inbox, contacts, or unrelated personal data visible.
- [ ] No real API keys, internal URLs, or debug/developer menus visible.
- [ ] If you created a dedicated review account, note its credentials in `app-review-notes.md`
      (not in this file) before resubmitting — don't leave real personal credentials in either
      document.

## Do not fabricate results

Show real diagnostic output from your actual adapter and vehicle. If no trouble codes are present,
recording a "no codes found" result is completely fine and expected — do not stage or fake a
fault code for the video.
