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

        // Without location the system suspends the app as soon as the screen turns off. Say so instead of recording
        // silently for a few seconds and then freezing. (An answer to the permission prompt is looked at again later.)
        if (LocationRefused())
            ReportProblem(TripKeepAliveProblem.LocationNotAllowed);
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

    static partial void PlatformRecheck(string title, string text)
    {
        if (LocationRefused())
        {
            ReportProblem(TripKeepAliveProblem.LocationNotAllowed);
            return;
        }

        // The permission may have been given since (the prompt, or Settings): clear the warning and make sure the
        // updates really run.
        if (Problem == TripKeepAliveProblem.LocationNotAllowed)
            ReportProblem(TripKeepAliveProblem.None);

        _location?.StartUpdatingLocation();
    }

    /// <summary>Denied, restricted, or location services switched off for the whole phone (iOS reports that as denied).</summary>
    private static bool LocationRefused() =>
        CLLocationManager.Status is CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted;
}
