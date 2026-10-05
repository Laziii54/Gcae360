# iPhone / iOS build

This project keeps iOS and MacCatalyst targets when opened on macOS. .NET MAUI uses one shared project for Android and iOS.

## On Windows
Build Android with `BUILD_ANDROID.bat`. A Windows machine cannot perform the final Apple signing/install step for an iPhone app.

## On macOS
1. Install the matching .NET 10 SDK and MAUI workload.
2. Install Xcode and accept its license.
3. Open this project on the Mac.
4. Set the Apple Development Team for bundle ID `com.gcar360.app` and let Xcode create the provisioning profile.
5. Select an iOS simulator for UI-only work, or a connected iPhone for Bluetooth, camera, notifications and StoreKit testing.
6. Create these product IDs in App Store Connect before testing purchases:
   - Auto-renewable subscriptions: `com.georgiancarscanner.pro.monthly`, `com.georgiancarscanner.vin.monthly`, `com.georgiancarscanner.full.monthly`.
   - Consumables: `com.georgiancarscanner.vin.pack5`, `com.georgiancarscanner.vin.pack10`, `com.georgiancarscanner.vin.single`.
7. For App Store distribution, archive the `net10.0-ios` Release target and upload it to TestFlight first.

## Required real-device checks
- BLE OBD discovery, connect, reconnect and a full diagnostic scan.
- Wi-Fi OBD connection after accepting the Local Network permission.
- VIN camera OCR and torch on a windshield and door-frame VIN.
- 0–100 recording with and without microphone permission, followed by saving to Photos.
- Calendar notification one day before and on the due date.
- Purchase and restore in Apple Sandbox for every App Store Connect product.

Classic Bluetooth SPP ELM327 adapters are not supported by ordinary iPhone apps. Use a BLE or Wi-Fi OBD-II adapter.

The shared OBD/diagnostic code is kept platform-neutral; BLE permissions are declared in `Platforms/iOS/Info.plist` and Android permissions in `Platforms/Android/AndroidManifest.xml`.
