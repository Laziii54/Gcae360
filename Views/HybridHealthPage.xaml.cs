using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class HybridHealthPage : ContentPage
{
    public HybridHealthPage(HybridHealthViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnDisappearing()
    {
        if (BindingContext is HybridHealthViewModel viewModel)
            viewModel.CancelScan();
        base.OnDisappearing();
    }
}
