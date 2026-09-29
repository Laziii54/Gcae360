namespace CarScanner.Views;

/// <summary>
/// Opening screen: the animated GCar360 logo in the middle of the screen.
/// Plays one loop of the GIF, then fades out and the app opens.
/// </summary>
public partial class LoadingPage : ContentPage
{
    // One loop of Resources/Images/gcar_loading.gif is 3.36 s.
    private const int AnimationMilliseconds = 3400;

    public LoadingPage()
    {
        InitializeComponent();
    }

    public async Task PlayAsync()
    {
        try
        {
            await Task.Delay(AnimationMilliseconds);
            await Logo.FadeToAsync(0, 250, Easing.CubicIn);
        }
        catch
        {
        }
    }
}
