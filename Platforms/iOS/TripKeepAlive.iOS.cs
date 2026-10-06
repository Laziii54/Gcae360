using CoreLocation;
using UIKit;

namespace CarScanner.Services.Background;

public static partial class TripKeepAlive
{
    private static CLLocationManager? _location;

    static partial void PlatformStart(string title, string text)
    {
        // iOS suspends an app in the background; Bluetooth only wakes it for a
        // moment when data arrives, so the polling loop would stop. Background
        // location updates (low accuracy, with the blue status-bar indicator)
        // keep the trip recording until the user presses Stop.
        _location ??= new CLLocationManager
        {
            DesiredAccuracy = CLLocation.AccuracyThreeKilometers,
            DistanceFilter = CLLocationDistance.FilterNone,
            PausesLocationUpdatesAutomatically = false,
            ActivityType = CLActivityType.AutomotiveNavigation
        };

        if (CLLocationManager.Status == CLAuthorizationStatus.NotDetermined)
            _location.RequestWhenInUseAuthorization();

        _location.AllowsBackgroundLocationUpdates = true;
        if (UIDevice.CurrentDevice.CheckSystemVersion(11, 0))
            _location.ShowsBackgroundLocationIndicator = true;

        _location.StartUpdatingLocation();
    }

    static partial void PlatformUpdate(string text)
    {
        // iOS shows the blue location indicator; there is no ongoing notification.
    }

    static partial void PlatformStop()
    {
        if (_location is null)
            return;

        _location.StopUpdatingLocation();
        _location.AllowsBackgroundLocationUpdates = false;
    }
}
