namespace CarScanner.Views;

/// <summary>
/// Opening screen: the OBD360 logo in the middle of the screen (same navy as the splash screen).
/// The first launch shows it a little longer; later cold starts show it briefly (under one second)
/// so the app opens quickly.
/// </summary>
public partial class LoadingPage : ContentPage
{
    // First launch: the logo stays about as long as the old animated intro did.
    private const int FirstLaunchAnimationMilliseconds = 3400;
    private const int RepeatLaunchMilliseconds = 800;
    private const int FirstLaunchFadeMilliseconds = 250;
    private const int RepeatLaunchFadeMilliseconds = 150;
    private const string IntroShownKey = "loading_intro_shown_v1";

    public LoadingPage()
    {
        InitializeComponent();
    }

    public async Task PlayAsync()
    {
        try
        {
            var firstLaunch = true;
            try
            {
                firstLaunch = !Preferences.Default.Get(IntroShownKey, false);
                if (firstLaunch)
                    Preferences.Default.Set(IntroShownKey, true);
            }
            catch
            {
                firstLaunch = false;
            }

            await Task.Delay(firstLaunch ? FirstLaunchAnimationMilliseconds : RepeatLaunchMilliseconds);
            await Logo.FadeToAsync(0, firstLaunch ? (uint)FirstLaunchFadeMilliseconds : (uint)RepeatLaunchFadeMilliseconds, Easing.CubicIn);
        }
        catch
        {
        }
    }
}
