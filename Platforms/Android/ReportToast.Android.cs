namespace CarScanner.Services.Reports;

public static partial class ReportToast
{
    static partial void PlatformShow(string message) =>
        Android.Widget.Toast.MakeText(Android.App.Application.Context, message, Android.Widget.ToastLength.Short)?.Show();
}
