using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    private async void OnAccountClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync(nameof(AccountPage)); } catch { }
    }

    private void OnAccountTapped(object? sender, TappedEventArgs e) =>
        OnAccountClicked(sender, e);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
    }
}