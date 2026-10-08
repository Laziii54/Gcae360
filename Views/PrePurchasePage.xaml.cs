using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class PrePurchasePage : ContentPage
{
    public PrePurchasePage(PrePurchaseViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        if (BindingContext is PrePurchaseViewModel vm)
        {
            vm.Attach();
            try { await vm.RefreshHeaderAsync(); }
            catch { }
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "PrePurchasePage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Leaving the page in the middle of the check stops it (the adapter is released).
        if (BindingContext is PrePurchaseViewModel { IsRunning: true } vm)
            vm.StopCommand.Execute(null);
        (BindingContext as PrePurchaseViewModel)?.Detach();
    }
}
