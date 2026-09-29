# Stable build setup

The project is intentionally mobile-first:
- Windows development machine: `net10.0-android`
- macOS: `net10.0-android;net10.0-ios;net10.0-maccatalyst`

The old Windows target and the per-user Visual Studio target file were removed so Visual Studio does not try to load/build the Windows target while you are trying to build Android.

The project no longer forces XAML SourceGen globally. .NET MAUI's default XAML pipeline is used, which avoids the previous `Binding with x:DataType from outer scope` build errors.

Before every clean build, `BUILD_ANDROID.bat` deletes `bin`/`obj`, clears NuGet caches, restores packages, and builds Android.
