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

        // Sign in / account button (the plan belongs to the account).
        var account = Application.Current?.Handler?.MauiContext?.Services.GetService<AccountService>();
        var english = localization?.IsEnglish == true;
        AccountButton.Text = account?.IsSignedIn == true
            ? (english ? "Account: " : "ანგარიში: ") + account.Email
            : (english ? "Sign in / create account" : "შესვლა / ანგარიშის შექმნა");
    }

    private async void OnAccountClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync(nameof(AccountPage)); } catch { }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();

        // Monthly automatic payment from the website (status + cancel).
        if (BindingContext is ProViewModel vm)
            _ = vm.LoadWebSubscriptionAsync();
    }
}
