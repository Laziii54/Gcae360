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

    /// <summary>Back to the start screen (choose OBD2 diagnostics or VIN check).</summary>
    private async void OnBackToStartTapped(object? sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync("//StartPage"); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Start navigation failed: {ex.Message}"); }
    }

    /// <summary>Copies the VIN; shows "copied" in green for 2 seconds.</summary>
    private async void OnCopyVinTapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is not HomeViewModel { CopyableVin: { } vin })
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(vin);

            var green = Color.FromArgb("#1F8A4C");
            CopyVinLabel.Text = _localization.IsEnglish ? "copied" : "დაკოპირდა";
            CopyVinLabel.TextColor = green;
            CopyVinIcon.Data = (Microsoft.Maui.Controls.Shapes.Geometry)new Microsoft.Maui.Controls.Shapes.PathGeometryConverter()
                .ConvertFromInvariantString("M5,12 L10,17 L19,7")!;
            CopyVinIcon.Stroke = new SolidColorBrush(green);
            CopyVinButton.Stroke = new SolidColorBrush(green);

            await Task.Delay(2000);
        }
        catch
        {
            // Clipboard can be unavailable; nothing else to do.
        }
        finally
        {
            CopyVinLabel.Text = "copy";
            CopyVinLabel.SetDynamicResource(Label.TextColorProperty, "Primary");
            CopyVinIcon.Data = (Microsoft.Maui.Controls.Shapes.Geometry)new Microsoft.Maui.Controls.Shapes.PathGeometryConverter()
                .ConvertFromInvariantString("M9,9 L20,9 L20,20 L9,20 Z M5,15 L5,4 L16,4")!;
            CopyVinIcon.SetDynamicResource(Microsoft.Maui.Controls.Shapes.Shape.StrokeProperty, "Primary");
            CopyVinButton.SetDynamicResource(Border.StrokeProperty, "BorderColor");
        }
    }

    private async void OnCustomsTapped(object? sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync(nameof(CustomsCalculatorPage)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Customs navigation failed: {ex.Message}"); }
    }

    /// <summary>Shows / hides the rarely used technical tools.</summary>
    private void OnTechnicalTapped(object? sender, TappedEventArgs e)
    {
        TechnicalRows.IsVisible = !TechnicalRows.IsVisible;
        TechnicalChevron.Rotation = TechnicalRows.IsVisible ? 90 : 0;
    }

    private async void OnAccountTapped(object? sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync(nameof(AccountPage)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Account navigation failed: {ex.Message}"); }
    }

    private void UpdateLanguageToggle() =>
        LanguageToggleButton.Text = _localization.IsEnglish ? "KA" : "EN";

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
