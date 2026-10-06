using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class GraphStudioPage : ContentPage
{
    private readonly GraphStudioViewModel _viewModel;

    public GraphStudioPage(GraphStudioViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        SizeChanged += (_, _) =>
        {
            // Phone turned sideways: graph on the whole screen.
            if (Width > 0 && Height > 0)
                _viewModel.SetLandscape(Width > Height);
        };
    }

    protected override async void OnAppearing()
    {
        try
        {
            base.OnAppearing();
            Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
            await _viewModel.OnAppearingAsync();
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "GraphStudioPage.OnAppearing");
        }
    }

    protected override async void OnDisappearing()
    {
        try
        {
            base.OnDisappearing();
            await _viewModel.OnDisappearingAsync();
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "GraphStudioPage.OnDisappearing");
        }
    }
}
