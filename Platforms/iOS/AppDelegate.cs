using CarScanner.Platforms.iOS.Billing;
using Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace CarScanner
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp()
        {
            var app = MauiProgram.CreateMauiApp();

            // Register the StoreKit observer at launch so pending and restored
            // transactions are delivered before the PRO page is opened.
            _ = app.Services
                .GetRequiredService<iOSBillingService>();

            return app;
        }
    }
}
