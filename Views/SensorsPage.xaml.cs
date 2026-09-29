using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class SensorsPage : ContentPage
{
    public SensorsPage(SensorsViewModel vm){InitializeComponent();BindingContext=vm;}
    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
