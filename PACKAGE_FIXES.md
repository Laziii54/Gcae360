# Build/package fixes

- Fixed `ProProductId` declaration order in backend Program.cs.
- Fixed nullable `JsonElement` handling in Apple purchase verification.
- Pinned AndroidX versions to the ranges required by Plugin.InAppBilling.
- Backend SQLite uses SQLitePCLRaw.bundle_e_sqlite3 3.0.3 to avoid the vulnerable 2.1.11 native SQLite package.
- Use `BUILD_CLEAN_WINDOWS.bat` before the first build to remove stale generated Android resources.

The Android `.9.png` AAPT errors in the supplied log were generated under `obj/Debug`; the clean script deliberately removes `obj` before restore/build.
