# iOS `MissingCtor` around Shell TitleView — investigation

Status: **root cause NOT established.** One code-level defect of the same family was found and fixed; no reproduction was
possible here (no Mac, no iPhone, no crash log in the repository).

## What the report said

`ObjCRuntime.Runtime.MissingCtor` ×2, raised while MAUI's iOS Shell created a title view. This exception means: UIKit gave the
runtime a native object whose managed wrapper had already been collected, and the managed type has no constructor taking a single
`NativeHandle`, so the runtime could not build a new wrapper.

## What was inspected

| Area | Finding |
|---|---|
| TitleViews in the app | Two only: `AppShell.xaml` (Shell level, shared by every pushed page that has a visible navigation bar — the two root `ShellContent` pages hide it) and `Views/AccelerationPage.xaml` (page level, created together with the page). A test (`IosShellTitleViewTests`) now fails if a third one appears without a review. |
| Content of the title views | Labels, a `VerticalStackLayout`, one `Border` with a drawn `GraphicsView` icon. No custom native handler, no web view. Test-guarded. |
| Custom native handlers | One: `PerformanceCameraViewHandler` with `CameraPreviewView : UIView`. It is not inside a title view, but it **had no `NativeHandle` constructor** — the exact precondition of `MissingCtor`. **Fixed**: `CameraPreviewView()` and `CameraPreviewView(NativeHandle)` added. Source test: every `: UIView` class under `Platforms/iOS` must declare the constructor. |
| Other managed `NSObject` subclasses | `ProductRequestDelegate`, `PaymentObserver` (StoreKit) and the two AVFoundation delegates are private nested classes held by fields for their whole use; not changed, listed as candidates if a crash log names one of them. |
| Updating bound text | `ShellStatusViewModel` (the title view's binding context) hops to the main thread in all three event handlers. Its async refresh continues on the UI context. No off-thread update found. |
| Localization | `LocalizationService.ApplyCurrentPage` walks the Shell title view and the page title view and sets `Label.Text` on the main thread only. Setting text does not create or destroy native containers. |
| Theme change | Title view colours are `DynamicResource` bindings; no native view is replaced. |
| Navigation | All route calls go through `SafeNavigation`/`NavigationGate` (one transition at a time). This lowers the chance of overlapping transitions but is **not** proof that `MissingCtor` is gone. |
| Linker | iOS uses `MtouchLink=SdkOnly`: MAUI and app assemblies are not trimmed, so a trimmed-away constructor is unlikely. |

## Most likely explanation (hypothesis, unproven)

A known family of MAUI/Shell issues: a Shell-level `TitleView` instance is reused by the container that MAUI creates for each
pushed page; with quick push/pop the old container's native view can be returned to UIKit after its wrapper was collected. The app
cannot change MAUI's internal container class.

## Mitigations ranked by evidence (none applied beyond the first two)

1. Done: `CameraPreviewView` native constructor; navigation gate.
2. Done: tests that keep the candidate list closed.
3. If the next crash log names a `TitleView` container: replace the single Shell-level title view by per-page title content, or
   drop the vehicle-type subtitle from the bar and show it in the page (a visible change — needs approval).
4. If it names an app type: add the same constructor to that type.

## Exactly what is missing to finish this

* The **full crash report** for one occurrence: from App Store Connect → TestFlight/Crashes (or Xcode Organizer), or from the app's
  own crash upload (the admin panel keeps type, sanitised stack, version and platform) — the managed frames above `MissingCtor`
  and the "type of the managed object" line from the exception message.
* iOS version, device model, app build number, and what the person did in the last seconds (which page, how fast).
* A Release build on a physical iPhone and ten rapid push/pop cycles on every page that shows the navigation bar, with the Xcode
  console attached. Not possible from this Windows machine.
