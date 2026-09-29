# Hyundai Sonata DN8 ABS/ESC read-only identification evidence

- Catalog scope: Hyundai Sonata (DN8), model years 2020-2023, ABS/ESC ECU only.
- Upstream project: comma.ai opendbc.
- Reviewed upstream commit: `52f5d03dde290dcaeb6cb96c35878fc24c7c7352`.
- Review date: 2026-09-17 UTC.
- Access level: read-only identification. This evidence does not authorize DTC clearing, routines, coding, security access, session changes, or actuator control.

## Verified facts

1. `opendbc/car/hyundai/values.py` identifies `HYUNDAI_SONATA` as Hyundai Sonata 2020-23.
2. The Hyundai firmware query uses UDS ReadDataByIdentifier request `22 F1 00` and expects a positive `62` response. The OBD-II-port query uses the default request configuration.
3. `opendbc/car/hyundai/fingerprints.py` assigns the Sonata ABS ECU to request CAN ID `0x7D1` and records multiple successful `F1 00` firmware responses beginning with the DN/ESC signature.
4. `opendbc/car/fw_query_definitions.py` sets the default diagnostic response offset to `0x8`; therefore request `0x7D1` maps to response `0x7D9`.
5. The Hyundai firmware response regex in `values.py` permits 19-56 data bytes after the echoed `F1 00`. This package decodes only the first 19 data bytes as HEX, which is within that reviewed lower bound and does not assume all bytes are printable ASCII.

## Exact sources

- https://github.com/commaai/opendbc/blob/52f5d03dde290dcaeb6cb96c35878fc24c7c7352/opendbc/car/hyundai/values.py
- https://github.com/commaai/opendbc/blob/52f5d03dde290dcaeb6cb96c35878fc24c7c7352/opendbc/car/hyundai/fingerprints.py
- https://github.com/commaai/opendbc/blob/52f5d03dde290dcaeb6cb96c35878fc24c7c7352/opendbc/car/fw_query_definitions.py
- https://github.com/commaai/opendbc/blob/52f5d03dde290dcaeb6cb96c35878fc24c7c7352/opendbc/car/uds.py

## Reviewed source hashes (SHA-256)

- `opendbc/car/hyundai/fingerprints.py`: `835D5024A5BC8F9D444A3AA294C70A88E714644119255C8AF108AB3D8CCEE10E`
- `opendbc/car/hyundai/values.py`: `52CE46AAFBA6EBC3260B06D39E6263E2E80BF5C15562B13EE68220097AA8228F`

## Deliberate exclusions

- No ABS DTC-read command is included yet. The reviewed source proves the ECU address and `22 F1 00` identification exchange, but not a vehicle-specific successful `19 02` ABS transaction.
- Sonata Hybrid is not included in this definition because its fingerprint block does not provide the same reviewed ABS entry.
- No other Hyundai model inherits this address merely because some models use the same CAN ID.
