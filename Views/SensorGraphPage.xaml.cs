using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class SensorGraphPage : ContentPage
{
    public SensorGraphPage(SensorGraphViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        (BindingContext as SensorGraphViewModel)?.OnDisappearing();
    }
}
