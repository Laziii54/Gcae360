# Georgian Car Scanner — latest update

## Included
- Android + iPhone target frameworks remain enabled.
- Global PRO Active badge after entitlement activation.
- Bluetooth moved to the top of the dashboard and scan shortened to 7 seconds.
- VIN + human-readable protocol explanation.
- Responsive dark UI with button hover/press states.
- Live Data hardened for generic OBD-II vehicles (gasoline/diesel/hybrid where the ECU exposes the PID).
- Sensors now flag selected obviously out-of-range values in red.
- ECU identifiers and emission tests use simple Georgian explanations.
- Safe generic Service operations: initialize, read DTC, clear DTC. Manufacturer-specific coding writes are intentionally not exposed without a validated vehicle implementation.
- Mileage verification tries standard OBD PID A6 but clearly distinguishes ECU-reported data from authoritative historical mileage.
- Fuel consumption estimates now account for common fuel types and reject implausible results.
- AI Diagnostic is PRO-only, performs a real OBD scan first, then supports contextual chat.
- Settings include theme, language preference and notifications preference.

## Important technical limitation
No generic OBD-II application can truthfully guarantee access to every ECU on every vehicle. ABS/SRS/BCM/HV battery and manufacturer-specific coding often require OEM-specific diagnostics. The app must not fabricate unavailable values or send unsafe write commands.


## 0.5 daily-use hardening
- Diagnostic/ECU/vehicle module result lists use virtualized CollectionView scrolling.
- Dark/Light/System theme is reapplied on navigation and OS theme changes.
- AI/Fuel/Mileage remain PRO-gated.
- AI uses backend-only Gemini configuration; the mobile app contains no provider key.
- Backend defaults to Gemini 3.6 Flash and has request validation + basic IP rate limiting.
- Full OBD scans surface a low-voltage warning when battery/adapter voltage is below the safe threshold for service operations.


# Universal vehicle scope

This release is brand-agnostic. Generic OBD-II is the universal baseline for OBD-II compliant vehicles. OEM profiles now cover major brand families (BMW, Mercedes-Benz, Subaru, Volvo, Toyota/Lexus, Honda/Acura, Nissan/Infiniti, Mazda, Ford/Lincoln, GM brands, Volkswagen Group brands, Hyundai/Kia/Genesis, Renault/Dacia, Peugeot/Citroën, Fiat/Alfa Romeo, Jeep/Dodge/Chrysler, Jaguar/Land Rover, Mitsubishi, Suzuki, Isuzu, Saab, plus a universal fallback).

OEM read/clear/data/routine/write operations remain capability-gated and require verified vehicle-specific CAN/ISO-TP addressing and service definitions. The scanner never guesses OEM addresses.
