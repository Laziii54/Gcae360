# Georgian Car Scanner – Final Hardening

The current source includes generic OBD reliability, offline-first DTC catalog synchronization, structured Mode 05/06 data, structured scan history, reports, vehicle identification improvements, privacy controls, settings, UDS/OEM capability gates, backend AI hardening, store verification and webhook idempotency, and test coverage.

## External production steps
- Deploy the latest backend changes to the configured HTTPS service and verify its configuration health endpoint.
- Set Google Play/App Store product IDs and store credentials on the backend.
- Set `GCS_STORE_WEBHOOK_SECRET` and configure provider notifications.
- Validate store purchase, restore, renewal, cancellation and refund flows in sandbox/test tracks.
- Test OBD adapters and real vehicles, especially Chevrolet Volt, before enabling any OEM service/write routine.

## Generic DTC data
The app uses the CC0 OBDex generic catalog as a refreshable offline-first cache: https://foerbsnavi.github.io/obdex/generic.min.json
Manufacturer-specific definitions are not fabricated.

## Safety
UDS security access, coding, programming, immobilizer/key operations, memory writes, critical brake/airbag routines and high-voltage safety-state changes remain disabled until a verified OEM capability and controlled validation are available.
