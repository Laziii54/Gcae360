using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class PerformanceVideoPage : ContentPage
{
    private readonly PerformanceVideoViewModel _viewModel;
    private bool _isVisible;
#if ANDROID
    private Android.Content.PM.ScreenOrientation? _previousOrientation;
#endif

    public PerformanceVideoPage(PerformanceVideoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        CameraView.ControllerChanged += OnCameraControllerChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;

        try
        {
            LockPortrait(true);
            try { DeviceDisplay.Current.KeepScreenOn = true; } catch { }

            // Let the page finish opening first, then start the camera.
            await Task.Delay(150);
            await _viewModel.OnAppearingAsync();
            await TryAttachCameraAsync();
        }
        catch (Exception ex)
        {
            _viewModel.ReportCameraError(ex);
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;

        try
        {
            try { DeviceDisplay.Current.KeepScreenOn = false; } catch { }
            await _viewModel.OnDisappearingAsync();
        }
        catch
        {
            // Closing must never crash the app.
        }
        finally
        {
            LockPortrait(false);
        }
    }

    // The native camera view can be created before or after OnAppearing,
    // so the camera is started from whichever happens last.
    private async void OnCameraControllerChanged(object? sender, EventArgs e)
    {
        if (!_isVisible)
            return;

        await TryAttachCameraAsync();
    }

    private async Task TryAttachCameraAsync()
    {
        try
        {
            if (CameraView.Controller is { } controller)
                await _viewModel.AttachCameraAsync(controller);
        }
        catch (Exception ex)
        {
            _viewModel.ReportCameraError(ex);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.CloseCommand.Execute(null);
        return true;
    }

    // Recording is done in portrait so the saved video, the preview and the
    // burned-in data always have the same orientation.
    private void LockPortrait(bool locked)
    {
#if ANDROID
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity is null)
                return;

            if (locked)
            {
                _previousOrientation ??= activity.RequestedOrientation;
                activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Portrait;
            }
            else if (_previousOrientation is { } previous)
            {
                activity.RequestedOrientation = previous;
                _previousOrientation = null;
            }
        }
        catch
        {
        }
#endif
    }
}
