using CarScanner.Services.Localization;
using CarScanner.VinCheck.Views;

namespace CarScanner.Views;

/// <summary>First screen: choose OBD scanner or VIN Check.</summary>
public partial class StartPage : ContentPage
{
    private readonly LocalizationService _localization;
    private bool _navigating;

    public StartPage(LocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateLanguageButton);
        UpdateLanguageButton();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _navigating = false;
        _localization.Apply(this);
        UpdateLanguageButton();
    }

    private void UpdateLanguageButton() =>
        LanguageButton.Text = _localization.IsEnglish ? "KA" : "EN";

    private void OnLanguageClicked(object? sender, EventArgs e) =>
        _localization.SetLanguage(_localization.IsEnglish ? "ქართული" : "English");

    private async void OnObdTapped(object? sender, TappedEventArgs e) => await GoAsync("//HomePage");

    private async void OnVinTapped(object? sender, TappedEventArgs e) => await GoAsync(nameof(VinLookupPage));

    private async Task GoAsync(string route)
    {
        if (_navigating)
            return;

        _navigating = true;
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch
        {
            _navigating = false;
        }
    }
}
