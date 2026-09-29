# Georgian Car Scanner — 100% Readiness Matrix

## Implemented in source
- Reliable Generic OBD-II error semantics
- Clear DTC verification
- Structured scan sessions
- DTC/PID catalog cache/refresh architecture
- Unified PID definition layer
- Mode 05/06 structured parsing
- OEM/GM/Volt capability framework
- ISO-TP multi-frame request engine with explicit verified CAN addressing
- Safety-gated service/coding framework
- SQLite entitlement backend
- Idempotent verified webhook flow
- Privacy export/delete
- HTML/JSON/CSV/TXT/PDF report generation
- Expanded Georgian/English diagnostic UI vocabulary
- Automated parser tests

## External validation required
- Real 2012 Chevrolet Volt ECU addressing and ISO-TP capture validation
- Real HPCM/EBCM/SDM/BCM/HVAC/charger read-only sessions
- Google Play/Apple sandbox purchase tests and production credentials
- Real BLE adapter matrix
- Android/iOS release signing and store upload

No unverified OEM write/security/flash command is enabled by default.
