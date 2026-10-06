using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class PrePurchaseAiPage : ContentPage
{
    public PrePurchaseAiPage(PrePurchaseAiViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        vm.Messages.CollectionChanged += async (_, _) =>
        {
            await Task.Delay(80);
            try { await ChatScroll.ScrollToAsync(0, ChatScroll.ContentSize.Height, true); }
            catch { }
        };
    }

    protected override async void OnAppearing()
    {
        try
        {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        if (BindingContext is PrePurchaseAiViewModel vm)
            await vm.StartAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "PrePurchaseAiPage.OnAppearing");
        }
    }
}
