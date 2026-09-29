using CarScanner.Services.Localization;
using CarScanner.Models;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class VehicleTypeSelectionPage : ContentPage
{
    public VehicleTypeSelectionPage(VehicleTypeSelectionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
    }
}
