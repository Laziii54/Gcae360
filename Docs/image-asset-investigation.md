# Android launcher icon — investigation and fix

## The original problem

The OBD360 launcher icon was reported to look incorrectly scaled, compressed, or
misaligned on a particular Samsung Android phone, while looking correct on other
devices. **No screenshot of the issue was available to this investigation** — the
findings below come entirely from inspecting the repository's source files and the
actual pixel content of the shipped icon images, not from comparing against the
reported photo. Where a screenshot would be needed to confirm a detail, that is
called out explicitly.

## Relevant source files

- `Platforms/Android/AndroidManifest.xml` — declares `android:icon="@mipmap/ic_gcar_light"`
  and `android:roundIcon="@mipmap/ic_gcar_light_round"` on the application, plus two
  `activity-alias` entries (`.LauncherLight` / `.LauncherDark`) with the same pattern for
  `ic_gcar_dark*`.
- `Platforms/Android/Resources/mipmap-anydpi-v26/ic_gcar_{light,dark}{,_round}.xml` — the
  Android Adaptive Icon definitions (API 26+), each pairing a `background` drawable with a
  `foreground` drawable.
- `Platforms/Android/Resources/mipmap-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/` — the per-density
  PNGs: `ic_gcar_{light,dark}.png` (legacy pre-API-26 launcher icon), `ic_gcar_{light,dark}_round.png`
  (legacy round variant) and `ic_gcar_{light,dark}_foreground.png` (the adaptive-icon foreground
  layer).
- `Platforms/Android/Resources/values/colors.xml` — held the adaptive-icon background colors.
- `Resources/AppIcon/appicon.png` — the 1024×1024 source icon used only for the iOS asset
  catalog (`CarScanner.csproj`'s `<MauiIcon>` is `Condition="...TargetPlatformIdentifier...==ios"`,
  so it never touches Android).
- `Resources/Splash/obd360_splash.png` — the existing transparent-background version of the
  same mark, used by `<MauiSplashScreen>`.
- `Services/AppIconSwitcher.cs` — switches the enabled `activity-alias` (light/dark) to match
  the in-app theme preference.

## Measurements taken (pixel-level, via `System.Drawing`)

| File | Canvas | Opaque content bounding box | Content fill | Margin |
|---|---|---|---|---|
| `mipmap-xxxhdpi/ic_gcar_light_foreground.png` (before) | 432×432 | x=[0,431] y=[0,431] | **100%** (opaque edge to edge) | **0%** on every side |
| `mipmap-xxxhdpi/ic_gcar_light.png` (before, legacy) | 192×192 | x=[0,191] y=[0,191] | **100%** | **0%** |
| `mipmap-xxxhdpi/ic_gcar_light_round.png` (before) | 192×192 | — | 78.4% opaque | matches π/4, i.e. a circle cookie-cut from the same edge-to-edge square |
| `Resources/AppIcon/appicon.png` | 1024×1024 | x=[0,1023] y=[0,1023] | 100% | 0% (correct for iOS) |
| `Resources/Splash/obd360_splash.png` | 1024×1024 | x=[215,808] y=[319,706] | 58% × 38% | 21% L/R, ~31% T/B |

`ic_gcar_light_*` and `ic_gcar_dark_*` were also found to be byte-identical (same MD5) at
every density — the light/dark switching infrastructure in `AppIconSwitcher` is wired up
correctly, but currently switches between two visually identical images.

## Root cause (confirmed, not a hypothesis)

Android's Adaptive Icon system requires the **foreground** layer to leave a transparent
safe margin (real content should stay within roughly the inner 66% of the 108dp×108dp
canvas) so that whichever mask shape a launcher applies — circle, squircle, rounded
square, teardrop — only ever crops into empty margin, never into the artwork. Samsung's
One UI launcher is well known for defaulting to (and letting the user choose between)
several mask shapes that differ from the plain circle other launchers use.

This project's `ic_gcar_*_foreground.png` files were **100% opaque, edge to edge, with
no margin at all** — confirmed by the bounding-box measurement above and visually: the
source artwork (a check-engine-light glyph crossed by a diagonal "scan" streak with small
sparkle accents) extends almost into two corners. The `appicon.png` master used to
generate these is itself correctly full-bleed — that's the right format for iOS, which
applies its own corner rounding and explicitly wants no transparency — but the same
full-bleed export was reused for the Android adaptive-icon foreground, which has the
opposite requirement. The 78.4%-opaque `_round` icon confirms this: it was produced by
circle-cropping that same full-bleed square with no added margin first.

Because the artwork reaches the very edges, any mask shape crops directly into it. A
circle mask (≈78.5% of the square's area kept) and Samsung's own mask shape remove
different areas of the square, so the amount and position of clipping — especially of
the diagonal streak's corner-reaching tips — genuinely differs by launcher/device. That
is a sufficient, well-documented explanation for "looks fine on some devices, off on
this Samsung phone" without needing to assume anything Samsung-specific is broken.

**What this does not explain, and could not be ruled out without the screenshot:** a
stale/cached launcher icon, or a device-specific "icon shape" setting change after
install, could independently produce a similar-looking symptom. Neither can be confirmed
or excluded from source inspection alone.

Ruled out:
- **Duplicate/conflicting generated resources** — `<MauiIcon>` is Android-excluded in the
  `.csproj`, and the build's `obj/.../resizetizer` output contains no Android icon files,
  confirming the hand-maintained mipmap PNGs are the only source and ship unmodified.
- **Android version/manifest misconfiguration** — the manifest and the `mipmap-anydpi-v26`
  XML are structured correctly per the Adaptive Icon spec; the bug is purely in the
  foreground PNGs' own content composition.

## Fix applied

1. **Foreground layers regenerated** (`ic_gcar_{light,dark}_foreground.png`, 5 densities):
   rebuilt from `Resources/Splash/obd360_splash.png` — the existing, already-isolated
   transparent-background mark — cropped to its visible content and re-centered on a
   transparent canvas so the artwork's largest dimension is 60% of the canvas (new
   measured margins: 20% left/right, ~31% top/bottom on every density, comfortably inside
   the safe zone). Same artwork, same colors; only the margin changed.
2. **Legacy + round icons regenerated** (`ic_gcar_{light,dark}.png` and `_round.png`, 5
   densities): same safe-margin composition, flattened onto the real navy gradient (see
   below) since legacy pre-API-26 icons must be fully pre-composed.
3. **Adaptive-icon background**: previously a flat `#000000` (`values/colors.xml`,
   `ic_gcar_{light,dark}_background`). Since the foreground is no longer full-bleed, this
   color would now show through the new margin, so it needed to be correct. Replaced with
   a new drawable, `Platforms/Android/Resources/drawable/ic_gcar_background.xml`, a
   diagonal gradient using the exact corner colors sampled from the real shipped
   `appicon.png` (`#023884` top-left → `#002050` bottom-right, `android:angle="315"`) —
   not an invented color. All four `mipmap-anydpi-v26/*.xml` files now reference
   `@drawable/ic_gcar_background`; the now-unused flat-black color entries were removed
   from `colors.xml`.
4. Light and dark variants were regenerated identically (matching the pre-existing,
   unchanged behavior of `AppIconSwitcher`) — this fix did not introduce or remove any
   visual difference between them.

Nothing about the logo's artwork, colors, or composition was redesigned — this is purely
a safe-margin/composition correction using the project's own existing assets and colors.

## Files changed

- `Platforms/Android/Resources/mipmap-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/ic_gcar_{light,dark}.png`
- `Platforms/Android/Resources/mipmap-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/ic_gcar_{light,dark}_round.png`
- `Platforms/Android/Resources/mipmap-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/ic_gcar_{light,dark}_foreground.png`
- `Platforms/Android/Resources/mipmap-anydpi-v26/ic_gcar_{light,dark}{,_round}.xml` (background reference)
- `Platforms/Android/Resources/drawable/ic_gcar_background.xml` (new)
- `Platforms/Android/Resources/values/colors.xml` (removed the now-unused flat-black entries)
- `Tests/CarScanner.Tests/BrandingTests.cs` (updated the test that pinned the old flat-black background)

Not touched: `Resources/AppIcon/appicon.png`, `CarScanner.csproj`'s `<MauiIcon>`, any iOS
configuration, `AndroidManifest.xml`, `Services/AppIconSwitcher.cs`, and every other page
or feature.

## Verification results

- `dotnet build CarScanner.csproj -f net10.0-android -c Debug` — **succeeded, 0 errors**
  (297 warnings, all pre-existing `aapt2`/`CS8604` resource-merge notices unrelated to this
  change). The build log confirms `drawable/ic_gcar_background` and all 30 regenerated
  mipmap files were compiled and merged.
- Re-measured the new foreground PNGs programmatically: content now fills 60% × 39% of the
  canvas with ≥20% margin on every side, at every density — inside Android's recommended
  safe zone.
- `dotnet test Tests/CarScanner.Tests` — this change broke one existing test,
  `BrandingTests.TheAdaptiveIconBackground_IsThePureBlackOfTheLogoArt`, which pinned the old
  flat `#000000` background. That premise was itself wrong (the logo's real background is a
  navy gradient, not black), so the test was updated to `TheAdaptiveIconBackground_IsTheRealGradientOfTheLogoArt`,
  asserting the new gradient drawable and its wiring into all four adaptive-icon XML files.
  Full suite: 2773/2774 passing, the one remaining failure (`ObdbYearEvidenceTests`) predates
  this change and is unrelated.
- iOS: not built or tested (no Mac available in this environment); `appicon.png` and the
  iOS `<MauiIcon>` entry are unmodified, so no iOS regression is expected, but this was not
  verified by an actual iOS build.

## Remaining uncertainty

- **No physical Samsung device or emulator was used.** The fix addresses a confirmed,
  measurable defect (zero safe-zone margin on the adaptive-icon foreground) that is a
  well-documented cause of exactly the reported symptom class, but whether it is the
  *entire* explanation for this specific phone's appearance cannot be confirmed without
  either the original screenshot or a fresh install on that device.
- If the issue persists after installing a build with this fix, the next things to check
  on-device are: whether the launcher is showing a stale cached icon (a device/launcher
  restart, or uninstall+reinstall, clears this), and what icon-shape setting that phone's
  launcher is using (Samsung: Settings → Home screen → Icon frame shape, or similar,
  depending on One UI version).
- Light and dark launcher icons remain visually identical; nothing in this task asked for
  them to be differentiated, so that was left as-is.
