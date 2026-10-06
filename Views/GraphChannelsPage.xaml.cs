using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class GraphChannelsPage : ContentPage
{
    public GraphChannelsPage(GraphStudioViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
    }
}
