# Bluetooth OBD2 Fix

This version fixes the adapter discovery path on Android by supporting both:

- BLE OBD2 adapters through Plugin.BLE (Android + iPhone/iPad)
- Classic Bluetooth/SPP ELM327 adapters on Android

Many inexpensive ELM327 adapters are Classic Bluetooth and were previously invisible because the app forced BLE transport. The new Bluetooth service scans BLE and Android Classic Bluetooth, shows paired OBD adapters immediately, and connects Classic adapters through the standard SPP UUID.

## Important

- iPhone/iPad does **not** support generic Classic Bluetooth SPP in this app. For iOS, use a BLE-capable OBD adapter.
- Android may request the **Nearby devices / Bluetooth** permission on first scan.
- Pair a Classic Bluetooth adapter in Android Settings first if the adapter requires pairing.
- The existing ELM327 initialization and OBD command layer remains unchanged.

## Android permission fix
The Bluetooth service now explicitly checks and requests the Android Bluetooth runtime permission before scanning. On Android 11 and older it also requests LocationWhenInUse because BLE discovery requires it there. Android 12+ uses BLUETOOTH_SCAN/BLUETOOTH_CONNECT. Legacy Bluetooth and location manifest permissions are limited to API 30 and below.
