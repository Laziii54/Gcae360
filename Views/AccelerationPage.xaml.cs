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
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        PageTitleView.BindingContext = Shell.Current?.BindingContext;
        SemanticProperties.SetDescription(CameraButton, PerformanceVideoText.Get(PerformanceVideoText.OpenCamera));
        if (BindingContext is HomeViewModel viewModel)
            await viewModel.StartAccelerationPreviewAsync();
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

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is HomeViewModel viewModel)
            await viewModel.StopAccelerationPreviewAsync();
    }
}
