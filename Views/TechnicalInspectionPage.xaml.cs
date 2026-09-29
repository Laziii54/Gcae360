using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class TechnicalInspectionPage : ContentPage
{
    public TechnicalInspectionPage(TechnicalInspectionViewModel viewModel)
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
}
