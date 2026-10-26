using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class ProPage : ContentPage
{
    public ProPage(ProViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);

        // Sign in / account button text now comes from the view model (AccountButtonText,
        // bound in XAML) so it is localized the same way as every other PRO page string.
        if (BindingContext is ProViewModel vm)
            vm.RefreshAccountButtonText();
    }

    private async void OnAccountClicked(object? sender, EventArgs e)
    {
        try { await CarScanner.Services.SafeNavigation.GoToAsync(nameof(AccountPage)); }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ProPage.OnAccountClicked");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            ApplyUiLocalization();

            // Monthly automatic payment from the website (status + cancel).
            if (BindingContext is ProViewModel vm)
            {
                vm.Attach();
                _ = vm.LoadWebSubscriptionAsync();
            }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ProPage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Release the singleton PRO/language events (no page leak).
        (BindingContext as ProViewModel)?.Detach();
    }
}
