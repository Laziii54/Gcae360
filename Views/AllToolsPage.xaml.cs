using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

/// <summary>
/// "All tools": the less used rows moved off the home page. It shares the home page's
/// HomeViewModel (singleton), so every row uses exactly the same command as before.
/// </summary>
public partial class AllToolsPage : ContentPage
{
    public AllToolsPage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
            localization?.Apply(this);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AllToolsPage.OnAppearing");
        }
    }

    private async void OnCustomsTapped(object? sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync(nameof(CustomsCalculatorPage)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Customs navigation failed: {ex.Message}"); }
    }
}
