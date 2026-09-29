# Georgian Car Scanner — Full Generic OBD-II Upgrade

This build upgrades the supplied project toward a production-oriented **generic OBD-II** scanner.

## Implemented in this upgrade

- ELM327 initialization and automatic protocol detection
- BLE command transport with small-packet write splitting
- Serialized command pipeline with retries/timeouts in the existing ELM layer
- Standard Mode 01 supported-PID discovery across the standard bitmap blocks
- Expanded generic PID registry and decoder
- Live data polling with cancellation
- Live-data recording to JSON/CSV in memory
- Stored, pending and permanent DTC reading
- DTC clear + verification
- Freeze-frame reader
- Readiness/MIL/DTC-count reader with raw bytes retained
- Mode 05 / Mode 06 raw access
- Mode 09 VIN / CALID / CVN / ECU name access
- Standard PID A6 odometer attempt when the ECU advertises/answers it
- Full generic OBD scan orchestration
- Text report generation and OS Share integration
- Rolling raw command log for troubleshooting
- Clear separation between generic OBD-II and manufacturer-specific diagnostics

## Important engineering boundary

No software can honestly guarantee **100% coverage of every vehicle** using a generic ELM327 BLE adapter. SAE J1979/ISO 15031 defines the standardized emissions-related OBD data, while ABS/SRS/BCM/hybrid/ADAS and other modules often use manufacturer-specific addressing and diagnostic services. Mode 08 is deliberately disabled in generic read-only mode because actuator control is vehicle-specific and can be unsafe.

The architecture now leaves an extension point for OEM profiles without pretending generic OBD can read proprietary ECUs.

## Verification

The supplied environment used for this upgrade did not contain the `dotnet` CLI, so a real MAUI build could not be executed here. The source was updated conservatively and the archive excludes generated `.vs`, `bin`, and `obj` folders. You should open the solution in Visual Studio with the required .NET MAUI workload, restore NuGet packages, build Android, then test against a real adapter and vehicle.
