namespace CarScanner.Services.Reports;

/// <summary>Short message at the bottom of the screen ("Added to the report").</summary>
public static partial class ReportToast
{
    public static void Show(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                PlatformShow(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast failed: {ex.Message}");
            }
        });
    }

    static partial void PlatformShow(string message);
}
