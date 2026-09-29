using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class LiveDataPage : ContentPage
{
    public LiveDataPage(LiveDataViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
        if (BindingContext is LiveDataViewModel vm) await vm.StartLiveDataCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is LiveDataViewModel vm) vm.StopLiveDataCommand.Execute(null);
    }

    private async void OnManufacturerDataTapped(object? sender, TappedEventArgs e)
    {
        var pro = Application.Current?.Handler?.MauiContext?.Services.GetService<CarScanner.Services.ProService>();
        if (pro is null)
            return;
        if (await CarScanner.Services.ProGate.EnsureAsync(pro, "მწარმოებლის მონაცემები", "Manufacturer data"))
            await Shell.Current.GoToAsync(nameof(ModuleLiveDataPage));
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
