using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class FreezeFramePage : ContentPage
{
    public FreezeFramePage(FreezeFrameViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        ApplyUiLocalization();

        if (BindingContext is FreezeFrameViewModel vm)
        {
            vm.Attach();
            await vm.OnAppearingAsync();
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FreezeFramePage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (BindingContext is FreezeFrameViewModel vm)
        {
            vm.OnDisappearing();
            vm.Detach();
        }
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
