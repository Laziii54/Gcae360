using Android.App;
using Android.Content.PM;
using Android.OS;

namespace CarScanner
{
    // The launcher entries are the two activity-aliases in AndroidManifest.xml
    // (light and dark app icon). AppIconSwitcher enables one of them.
    [Activity(Name = "carscanner.MainActivity", Exported = true, Theme = "@style/Maui.SplashTheme", MainLauncher = false, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
    }
}
