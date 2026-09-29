using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class BluetoothPage : ContentPage
{
    public BluetoothPage(BluetoothViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();

        if (BindingContext is BluetoothViewModel viewModel)
            await viewModel.OnPageAppearingAsync();
    }
}
