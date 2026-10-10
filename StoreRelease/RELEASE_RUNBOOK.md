# GCar360 release runbook

Nothing here has been run against production. Steps marked **[APPROVAL]** change production, a store, or the shared GitHub repository, so run them only when you decide to.

Identifiers: package / bundle `com.gcar360.app` · version 1.0.0 · Android versionCode 55 (`ApplicationVersion`) · iOS build number comes from Codemagic `$BUILD_NUMBER`.

## 1. Review changes (local)
```bash
cd C:/GCar360/Georgian-Car-Scanner-Universal-MultiVehicle-FIXED
git status --short
git diff --stat
git status --short | grep -i coding     # must print nothing
```
The working tree has many uncommitted changes, including deleted Customs, Calendar and All Tools pages. Codemagic builds from Git, so only committed work reaches iOS.

## 2. Tests (local)
```bash
cd Tests/CarScanner.Tests && dotnet test -nologo
```
Last run: 969 passed, 0 failed, 0 skipped (software ELM327 simulator, not a real vehicle).

## 3. Android Release (local)
Plain build (unsigned for Play; it uses the debug key and cannot be uploaded):
```bash
dotnet build CarScanner.csproj -f net10.0-android -c Release
```
Play-uploadable, signed with your own keystore (the keystore never goes into the repository):
```powershell
cd StoreRelease
.\Publish-Android.ps1 -KeyStorePath "C:\path\to\your.keystore" -KeyAlias "youralias" -Password "<keystore password>"
```
The script takes the password as a plain string, so it ends up in your shell history; clear it afterwards.
Before upload: raise `ApplicationVersion` in `CarScanner.csproj` above the last version code you uploaded to Play (the project currently says 55).

## 4. Backend package (local)
```bash
cd Backend/CarScanner.Api
dotnet publish -c Release -o ../../StoreRelease/output/backend
```
Verified: the published package contains no `.db` file, so deploying it does not overwrite the production database. The tracked `Backend/CarScanner.Api/data/car-scanner-api.db` is a development file and is not shipped.

## 5. Is a backend deployment needed? Yes, if you want the opt-in data collection
The new `POST /api/ecu-observations`, `DELETE /api/ecu-observations/mine` and `/admin/ecu-observations.json` routes exist only in this code. Without deployment, the app's collection upload gets 404 and silently does nothing; ordinary diagnostics are unaffected. The QR report page also has a new design.
The new table (`EcuObservations`, `EcuObservationSources`) is created with `CREATE TABLE IF NOT EXISTS` at startup, so no migration step is needed and existing tables are untouched.

## 6. Deploy backend **[APPROVAL]**
Back up first (Azure portal → App Service → Advanced Tools/Kudu, or download `HOME/data/georgian-car-scanner`).
```bash
cd StoreRelease/output
powershell Compress-Archive -Path backend/* -DestinationPath backend.zip -Force
az webapp deploy --resource-group GeorgianCarScannerRG --name georgian-car-scanner-api-cyawhhdygecng6bt --src-path backend.zip --type zip
```
Keep the App Service at one instance (SQLite). Application settings are not changed by this command.

## 7. Verify backend
```bash
curl -i https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net/health
# Invalid request should be rejected, not accepted:
curl -i -X POST https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net/api/ecu-observations -H "Content-Type: application/json" -d "{}"
# expect 400 (404 would mean the new code is not live)
```
`/health/configuration` needs the `X-GCS-Diag-Token` header; it should report `readyForStoreRelease: true`.

## 8. Push source **[APPROVAL]**
Remote: `https://github.com/Laziii54/Gcae360.git`, branch `main`. Pushing does not publish an app: `codemagic.yaml` has no `triggering` section, so a Codemagic build starts only when you start it.
```bash
git add -A      # review `git status` first; make sure no secrets or .user files are staged
git commit -m "Fuel, mileage, service and data-collection updates"
git push origin main
```

## 9. iOS build **[APPROVAL]**
In Codemagic start the existing workflow `GCar360 iOS` (`maui-ios`) on `main`. It signs with the App Store Connect integration `codemagic-api`, builds an IPA with `$BUILD_NUMBER` as the build number, and publishes to App Store Connect/TestFlight. Test on TestFlight before any submission. The workflow's `publishing` step uploads automatically on success.

## 10. Store submission **[APPROVAL]**
Done by you in Play Console and App Store Connect: privacy/data-safety answers (`PRIVACY_AND_DATA_SAFETY.md` does not yet list the optional anonymous ECU diagnostic data; add it before submitting), justify `SCHEDULE_EXACT_ALARM` and `RECORD_AUDIO`, then submit.
