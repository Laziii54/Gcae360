# Georgian Car Scanner — Multi-Vehicle Diagnostics

This release extends the existing application with a vehicle-type context and a Motorcycle / Scooter / Moped diagnostics subsystem.

## Vehicle selector

The Shell title bar contains a vehicle icon. Tapping it opens the vehicle selector:

- Car
- Motorcycle
- Scooter
- Moped
- Other

The selected vehicle kind is persisted locally and drives the Home page UX.

## Two-wheeler subsystem

The two-wheeler page contains:

- vehicle type
- manufacturer
- model
- year
- engine
- displacement
- fuel type
- ECU
- protocol
- connection type
- capability status
- DTC scan / counts
- Live Data
- Clear Codes
- ECU / ABS / service placeholders with strict unsupported gating
- diagnostic session history

The manufacturer catalog includes the manufacturers requested in the multi-vehicle specification.

## No fake diagnostics

No motorcycle/scoter/moped response is synthesized. A protocol is considered executable only when the generic OBD-II baseline is actually initialized or a verified manufacturer profile is later added.

Unknown / unverified OEM functionality is presented as:

> მხარდაჭერა არ არის / პროტოკოლი ვერიფიცირებული არ არის

## Transport architecture

The subsystem introduces `IVehicleDiagnosticTransport` and `IDiagnosticProtocol` so future verified BLE, Classic Bluetooth, USB, CAN, K-Line and manufacturer-specific transports can be added without moving protocol logic into pages or ViewModels.
