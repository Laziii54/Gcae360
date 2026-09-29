# Georgian Car Scanner — Release Readiness

## Included in this source release
- Generic OBD-II DTC reliability and clear verification
- Offline-first generic DTC catalog refresh/cache
- Structured DTC, sensor, readiness, ECU identifier and freeze-frame persistence
- Structured Mode 05/06 parsing without inventing OEM engineering units
- Unified PID definition layer for validated common PIDs
- Vehicle VIN year disambiguation and Chevrolet Volt profile inference
- HTML/JSON/CSV/TXT diagnostic reporting
- Privacy export/delete controls
- Theme/language/unit/polling settings
- OEM/GM/Volt capability and safety gating
- UDS/ISO-TP foundation and negative-response semantics
- Store verification architecture and webhook idempotency layer
- Automated parser/UDS/PID tests
- Scroll-safe diagnostics pages

## External validation still required
- Android store signing and device acceptance; iOS build/signing on a Mac
- Google Play / Apple subscription credentials and sandbox tests
- Public HTTPS backend deployment
- Real adapter matrix testing
- Controlled 2012 Chevrolet Volt OEM validation before enabling any OEM write/service routine

AI configuration and hardening are included in the current source. The latest
backend changes still need deployment verification. See PRODUCTION_CHECKLIST.md
for the remaining source decisions and external acceptance requirements.
