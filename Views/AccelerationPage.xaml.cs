using CarScanner.Services.Localization;
using CarScanner.Services.PerformanceVideo;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class AccelerationPage : ContentPage
{
    public AccelerationPage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        try
        {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        PageTitleView.BindingContext = Shell.Current?.BindingContext;
        SemanticProperties.SetDescription(CameraButton, PerformanceVideoText.Get(PerformanceVideoText.OpenCamera));
        if (BindingContext is HomeViewModel viewModel)
            await viewModel.StartAccelerationPreviewAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationPage.OnAppearing");
        }
    }

    private async void OnCameraTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var exporter = Application.Current?.Handler?.MauiContext?.Services.GetService<IPerformanceVideoExporter>();
            if (exporter is null || !exporter.IsSupported)
            {
                await DisplayAlertAsync(
                    PerformanceVideoText.Get(PerformanceVideoText.Title),
                    PerformanceVideoText.Get(PerformanceVideoText.NotSupported),
                    PerformanceVideoText.Get(PerformanceVideoText.Close));
                return;
            }

            await Shell.Current.GoToAsync(nameof(PerformanceVideoPage));
        }
        catch
        {
            // Never close the app because of the camera shortcut.
        }
    }

    /// <summary>Takes a picture of the result card and opens the share sheet.</summary>
    private async void OnShareResultClicked(object? sender, EventArgs e)
    {
        try
        {
            var shot = await ResultCard.CaptureAsync();
            if (shot is null)
                return;

            var path = Path.Combine(FileSystem.CacheDirectory, $"gcar360-0-100-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await using (var source = await shot.OpenReadAsync(ScreenshotFormat.Png))
            await using (var target = File.Create(path))
                await source.CopyToAsync(target);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "GCar360 0–100",
                File = new ShareFile(path)
            });
        }
        catch
        {
            // Sharing is optional; never close the page because of it.
        }
    }

    protected override async void OnDisappearing()
    {
        try
        {
        base.OnDisappearing();
        if (BindingContext is HomeViewModel viewModel)
            await viewModel.StopAccelerationPreviewAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationPage.OnDisappearing");
        }
    }
}
