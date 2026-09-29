using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class HomePage : ContentPage
{
    private readonly LocalizationService _localization;

    public HomePage(HomeViewModel vm, LocalizationService localization)
    {
        InitializeComponent();
        BindingContext = vm;
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
        UpdateLanguageToggle();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
        UpdateLanguageToggle();

        if (BindingContext is HomeViewModel viewModel)
            await viewModel.RefreshConnectionSummaryAsync(forceVinRefresh: true);
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        // LocalizationService applies static XAML strings immediately after
        // raising this event. Queue the flag update after that pass so it is
        // never overwritten by page localization.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Yield();
            UpdateLanguageToggle();
        });
    }

    private async void OnPrePurchaseClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync(nameof(PrePurchasePage)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Pre-purchase navigation failed: {ex.Message}"); }
    }

    private void OnPrePurchaseTapped(object? sender, TappedEventArgs e) =>
        OnPrePurchaseClicked(sender, e);

    private void UpdateLanguageToggle() =>
        LanguageToggleButton.Text = _localization.IsEnglish ? "KA" : "EN";

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
