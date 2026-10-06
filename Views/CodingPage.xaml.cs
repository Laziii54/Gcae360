using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class CodingPage : ContentPage
{
    public CodingPage(CodingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
            if (BindingContext is CodingViewModel vm)
                await vm.OnAppearingAsync();
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "CodingPage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        (BindingContext as CodingViewModel)?.OnDisappearing();
    }
}
