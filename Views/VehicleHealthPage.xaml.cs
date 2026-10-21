using CarScanner.Services.Localization;
using CarScanner.ViewModels;
namespace CarScanner.Views;
public partial class VehicleHealthPage : ContentPage
{
    public VehicleHealthPage(VehicleHealthViewModel viewModel) { InitializeComponent(); BindingContext = viewModel; }

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
