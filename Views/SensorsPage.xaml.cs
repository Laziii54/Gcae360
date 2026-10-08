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
        (BindingContext as SensorsViewModel)?.Attach();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        (BindingContext as SensorsViewModel)?.Detach();
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
