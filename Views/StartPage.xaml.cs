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
        UpdateLanguageButton();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _navigating = false;
        // Singleton service, transient page: subscribe only while visible.
        _localization.LanguageChanged -= OnLanguageChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        _localization.Apply(this);
        UpdateLanguageButton();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(UpdateLanguageButton);

    private void UpdateLanguageButton() =>
        LanguageButton.Text = _localization.CurrentBadge;

    private async void OnLanguageClicked(object? sender, EventArgs e)
    {
        try
        {
            var picked = await LanguagePickerPage.ShowAsync(_localization.Translate("ენა"));
            if (picked is not null && picked.Code != _localization.CurrentLanguageCode)
                _localization.SetLanguage(picked.DisplayName);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "StartPage.OnLanguageClicked");
        }
    }

    private async void OnObdTapped(object? sender, TappedEventArgs e)
    {
        try { await GoAsync("//HomePage"); }
        catch (Exception commandException) { CarScanner.Services.CommandErrors.Report(commandException, "StartPage.OnObdTapped"); }
    }

    private async void OnVinTapped(object? sender, TappedEventArgs e)
    {
        try { await GoAsync(nameof(VinLookupPage)); }
        catch (Exception commandException) { CarScanner.Services.CommandErrors.Report(commandException, "StartPage.OnVinTapped"); }
    }

    private async Task GoAsync(string route)
    {
        if (_navigating)
            return;

        _navigating = true;
        try
        {
            await CarScanner.Services.SafeNavigation.GoToAsync(route);
        }
        catch
        {
            _navigating = false;
        }
    }
}
