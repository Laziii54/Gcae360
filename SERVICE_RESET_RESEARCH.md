# Service / Oil Reset — how other scanner apps do it (research, 2026-09)

Goal for OBD360: a Service page that works on every car the same way it works on
the Chevrolet Volt 2013 (oil %, reset 100% / 50%), plus per-item reminders
(e.g. Mercedes "Service in X days", BMW CBS items) where the car has them.

## Key facts

- Oil / service reset is **not part of the OBD-II standard** (SAE J1979). Every
  manufacturer uses its own command; generic readers cannot do it.
- **No app tries "all codes" blindly.** Every app first identifies the car
  (profile or VIN) and sends only that car's verified command.
- No open-source database of service-reset commands for all brands was found;
  the big apps keep theirs closed and paid.

## 14 apps compared

| # | App | Brands for service reset | How the car is identified | Model |
|---|-----|--------------------------|---------------------------|-------|
| 1 | Car Scanner ELM OBD2 | GM (Opel, Chevrolet, Vauxhall, Holden) service reminder; Hyundai/Kia service; Nissan & Mitsubishi CVT oil degradation; Toyota/Renault/VW mostly coding | User picks connection profile (make/model/year) | Per-platform, some features "experimental" |
| 2 | Carly | BMW, Mercedes (2005+), VW group, others basic | Brand + model + build year, then reads the ECUs | Per brand; features differ by model/year |
| 3 | Carista | 32+ brands, "service/oil reset, TPMS, EPB, battery registration" | Brand + model + generation (chassis code) | Per model/generation; unsupported cars added "within months" |
| 4 | OBDeleven | VW / Audi / Skoda / SEAT / Cupra (VAG) | Reads the car, offers per-model One-Click Apps | One-Click App per model and year |
| 5 | BimmerLink | BMW, MINI, Toyota Supra 2019+ | BMW-only app | "Reset the service display" (CBS items separately) |
| 6 | BimmerCode | BMW / MINI (coding) | BMW-only | Coding, not maintenance reset |
| 7 | BlueDriver (Pro) | "Oil light reset" = **shows the dashboard button procedure** for the car | Year + make + model | Instructions, not an OBD command |
| 8 | FIXD | No reset; mileage-based maintenance reminders | Make/model/year | Tracking only |
| 9 | Autel MaxiAP AP200 | Oil reset among "19 hot functions" | AutoVIN | **One brand software free, others bought per brand** |
| 10 | Launch ThinkDiag / X431 | 15+ reset functions | VIN / brand menu | Per-brand software licence |
| 11 | Topdon TopScan | Service resets incl. oil | VIN / brand menu | Per-brand software |
| 12 | Innova RepairSolutions2 | Maintenance light reset | Vehicle selection in app | Tool + app, per vehicle coverage |
| 13 | iCarsoft MB | Mercedes / Sprinter / Smart oil service (ASSYST) | Brand-dedicated device | One device per brand family |
| 14 | Torque Pro / OBD Fusion | No service reset (live data, codes, PID packs) | — | Reading only |

## Conclusions for OBD360

1. Identify the car **automatically by VIN** (already works in OBD360).
2. Keep a **per-brand command list**; send only the command verified for that
   brand/model. Never try unknown commands (a command that resets oil on one
   brand can change a different setting on another).
3. Show **each reminder separately** (oil service, Service A/B, brake fluid,
   inspection …) with its own reset button; show nothing the car does not have.
4. Where the value can be read but the reset command is not verified yet,
   show the value without a reset button.
5. Start with brands that are known to allow reset over OBD: **GM** (done on
   Volt 2013), then Hyundai/Kia, then VAG, BMW; Mercedes is the most closed.

## Verification audit — 2026-09-28

- A readable service value is not proof that the same DID is writable. The
  former generic `0x22 DID` → `0x2E DID` path for GM `0x4DC0`, Ford `0x054B`
  and Mazda `0x0519` has therefore been disabled.
- A learned GMLAN `$1A` oil-life identifier is also read-only evidence. The
  inferred `$3B` write-back path has been disabled until an exact make/model/
  year/ECU request and response are verified on real hardware.
- Public manufacturer and specialist documentation confirms that reset
  functions exist, but does not provide one universal raw payload. VAG SRI,
  BMW CBS and Mercedes ASSYST vary by cluster/ECU dataset and generation.
- New automatic resets may be enabled only through `oem_operations.json`,
  after the existing promotion checklist is complete. Guided dashboard resets
  remain available while no exact executable profile is approved.

## Sources

- Car Scanner coding & service features: https://www.carscanner.info/coding/
- Carly supported features: https://support.mycarly.com/hc/en-us/articles/360011226299-What-features-does-Carly-support-for-my-car
- Carista supported cars: https://carista.com/en-us/apps/supported-cars
- OBDeleven service reset: https://obdeleven.com/service-reset
- BimmerLink: https://bimmerlink.app/
- BlueDriver Pro compatibility: https://us.bluedriver.com/pages/bluedriver-pro-compatibility
- FIXD features: https://www.fixdapp.com/features/
- Autel MaxiAP AP200: https://www.autelonline.com/products/maxiap-ap200
- Launch ThinkDiag: https://www.launch-tool.com/product/thinkcar-thinkdiag-all-car-brands-license/
- Topdon TopScan: https://www.topdon.us/products/topscan
- Innova RepairSolutions2 maintenance reset: https://help.repairsolutions.com/article/2329-innova-1000-how-to-perform-a-maintenance-light-reset-oil-reset
- iCarsoft MB: https://www.icarsoft-us.com/products/mb-v1-0
- Oil reset not standardized in OBD-II: https://iamcarhacker.com/obd2-scanner/service-functions/oil-service-reset-with-obd2-scanner-guide/
