using CarScanner.Services.Localization;
using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Views;

/// <summary>
/// Small modal "card" that lists every supported language. Tap a row to choose it; tap outside the card or the
/// system back button to dismiss. Built in code (no XAML, no DI) so it can be dropped in as a single file.
/// </summary>
public sealed class LanguagePickerPage : ContentPage
{
    private readonly TaskCompletionSource<AppLanguage?> _result = new();
    private bool _closing;

    private LanguagePickerPage(string title)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        var cardBg = Res("CardBg", dark ? Color.FromArgb("#1E293B") : Colors.White);
        var text = Res("TextColor", dark ? Colors.White : Color.FromArgb("#111827"));
        var subText = Res("SubTextColor", dark ? Color.FromArgb("#94A3B8") : Color.FromArgb("#475569"));
        var border = Res("BorderColor", dark ? Color.FromArgb("#334155") : Color.FromArgb("#CBD5E1"));
        var accent = Res("Primary", Color.FromArgb("#2563EB"));

        BackgroundColor = Color.FromArgb("#99000000");
        NavigationPage.SetHasNavigationBar(this, false);
        Shell.SetNavBarIsVisible(this, false);

        var current = LanguageCatalog.Current;

        var list = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(16, 4, 16, 16) };

        foreach (var language in LanguageCatalog.All)
        {
            var selected = language.Code == current.Code;

            var check = new Label
            {
                Text = selected ? "✓" : string.Empty,
                TextColor = accent,
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 24,
                HorizontalTextAlignment = TextAlignment.Center
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 14,
                Padding = new Thickness(14, 12)
            };

            row.Add(new Label { Text = language.Flag, FontSize = 26, VerticalOptions = LayoutOptions.Center }, 0);
            row.Add(new Label
            {
                Text = language.DisplayName,
                TextColor = text,
                FontSize = 17,
                FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
                VerticalOptions = LayoutOptions.Center
            }, 1);
            row.Add(check, 2);

            var item = new Border
            {
                Content = row,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                StrokeThickness = selected ? 2 : 1,
                Stroke = selected ? accent : border,
                BackgroundColor = selected ? accent.WithAlpha(0.12f) : Colors.Transparent
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await CloseAsync(language);
            item.GestureRecognizers.Add(tap);

            list.Add(item);
        }

        var card = new Border
        {
            BackgroundColor = cardBg,
            Stroke = border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Margin = new Thickness(24, 40),
            MaximumWidthRequest = 420,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star)
                },
                Children =
                {
                    new Label
                    {
                        Text = "🌐  " + title,
                        TextColor = text,
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        Padding = new Thickness(22, 20, 22, 12)
                    }
                }
            }
        };

        var scroll = new ScrollView { Content = list };
        ((Grid)card.Content).Add(scroll, 0, 1);

        // Swallow taps on the card so only the dark area outside it dismisses the picker.
        card.GestureRecognizers.Add(new TapGestureRecognizer());

        var backdrop = new Grid { Children = { card } };
        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += async (_, _) => await CloseAsync(null);
        backdrop.GestureRecognizers.Add(dismiss);

        Content = backdrop;
    }

    /// <summary>Shows the picker and returns the chosen language, or null when it was dismissed.</summary>
    public static async Task<AppLanguage?> ShowAsync(string title)
    {
        var navigation = Shell.Current?.Navigation ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (navigation is null)
            return null;

        var page = new LanguagePickerPage(title);
        await navigation.PushModalAsync(page, false);
        return await page._result.Task;
    }

    private async Task CloseAsync(AppLanguage? language)
    {
        if (_closing)
            return;

        _closing = true;
        _result.TrySetResult(language);

        try
        {
            await Navigation.PopModalAsync(false);
        }
        catch
        {
            // Already dismissed.
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _result.TrySetResult(null);
    }

    private static Color Res(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            return color;

        return fallback;
    }
}
