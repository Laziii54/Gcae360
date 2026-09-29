# Georgian Car Scanner — current status

This source package is the hardened daily-use baseline.

The app is no longer a demo-only OBD shell: it has reliable generic OBD-II error semantics, scan sessions, adapter diagnostics, report export, privacy controls, backend-only AI and PRO gating, in-app subscription client integration, server-side entitlement persistence/verification hooks, and a GM/Volt OEM capability framework with strict safety gates.

The bundled Georgian generic DTC catalog now contains 9,530 codes and 67,074 validated translated fields. A regression test parses all seven YAML files and locks the catalog counts while rejecting empty Georgian values, foreign writing systems, and known mixed-language translation artifacts.

Do not interpret the OEM layer as permission to invent or execute unknown proprietary commands. A vehicle/module is shown as supported only when a profile explicitly exposes that capability. High-risk write/security/programming operations remain disabled by default.

Before store release, deploy the backend and configure the store/provider credentials listed in `PRODUCTION_CHECKLIST.md` and `SERVER_ENVIRONMENT.example`.

The current Android source completed a clean Release build with zero errors and
zero warnings on 2026-09-10. The backend Release build also completed cleanly,
and all 108 automated regression tests passed. Store signing, real adapters,
real vehicles and store sandbox flows remain acceptance tests rather than
source-build checks.
