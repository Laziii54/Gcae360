using CarScanner.Services.Localization;
using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Views;

/// <summary>
/// Language chooser shown as a bottom sheet in the same style as the rest of the app (flat cards, thin borders,
/// 12-pt corners, Primary accent). Tap a row to choose a language; tap the dark area, the close button or the
/// system back button to dismiss. Built in code (no XAML, no DI) so it can be dropped in as a single file.
/// </summary>
public sealed class LanguagePickerPage : ContentPage
{
    private readonly TaskCompletionSource<AppLanguage?> _result = new();
    private readonly Border _sheet;
    private bool _closing;

    private LanguagePickerPage(string title)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        var cardBg = Res("CardBg", dark ? Color.FromArgb("#1E293B") : Colors.White);
        var pageBg = Res("PageBg", dark ? Color.FromArgb("#0F172A") : Color.FromArgb("#F4F5F3"));
        var text = Res("TextColor", dark ? Colors.White : Color.FromArgb("#111827"));
        var subText = Res("SubTextColor", dark ? Color.FromArgb("#94A3B8") : Color.FromArgb("#5D636B"));
        var border = Res("BorderColor", dark ? Color.FromArgb("#334155") : Color.FromArgb("#DCDFE3"));
        var accent = Res("Primary", Color.FromArgb("#1D5BD6"));

        BackgroundColor = Color.FromArgb("#99000000");
        NavigationPage.SetHasNavigationBar(this, false);
        Shell.SetNavBarIsVisible(this, false);

        var current = LanguageCatalog.Current;

        // ---- language rows, all inside one list card (like the Home page cards) ----
        var rows = new VerticalStackLayout { Spacing = 0 };
        var languages = LanguageCatalog.All;

        for (var index = 0; index < languages.Count; index++)
        {
            var language = languages[index];
            var selected = language.Code == current.Code;

            var flag = new Border
            {
                WidthRequest = 40,
                HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 20 },
                BackgroundColor = pageBg,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = language.Flag,
                    FontSize = 22,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            var names = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
            names.Add(new Label
            {
                Text = language.DisplayName,
                TextColor = text,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation
            });

            if (!string.Equals(language.EnglishName, language.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(new Label
                {
                    Text = language.EnglishName,
                    TextColor = subText,
                    FontSize = 12,
                    LineBreakMode = LineBreakMode.TailTruncation
                });
            }

            View marker = selected
                ? new Border
                {
                    WidthRequest = 24,
                    HeightRequest = 24,
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 12 },
                    BackgroundColor = accent,
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = "✓",
                        TextColor = Colors.White,
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
                : new Border
                {
                    WidthRequest = 24,
                    HeightRequest = 24,
                    StrokeThickness = 1.5,
                    Stroke = border,
                    StrokeShape = new RoundRectangle { CornerRadius = 12 },
                    BackgroundColor = Colors.Transparent,
                    VerticalOptions = LayoutOptions.Center
                };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 12,
                Padding = new Thickness(14, 10),
                MinimumHeightRequest = 56,
                BackgroundColor = selected ? accent.WithAlpha(0.08f) : Colors.Transparent
            };

            row.Add(flag, 0);
            row.Add(names, 1);
            row.Add(marker, 2);

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await CloseAsync(language);
            row.GestureRecognizers.Add(tap);

            SemanticProperties.SetDescription(row, language.DisplayName);
            rows.Add(row);

            if (index < languages.Count - 1)
            {
                rows.Add(new BoxView
                {
                    HeightRequest = 1,
                    Color = border,
                    Margin = new Thickness(66, 0, 0, 0)
                });
            }
        }

        var listCard = new Border
        {
            BackgroundColor = cardBg,
            Stroke = border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = 0,
            Content = rows
        };

        // ---- header: grab handle, title, close button ----
        var handle = new BoxView
        {
            WidthRequest = 40,
            HeightRequest = 4,
            CornerRadius = 2,
            Color = border,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 10, 0, 4)
        };

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += async (_, _) => await CloseAsync(null);

        var closeButton = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            StrokeThickness = 1,
            Stroke = border,
            StrokeShape = new RoundRectangle { CornerRadius = 17 },
            BackgroundColor = cardBg,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "✕",
                TextColor = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        closeButton.GestureRecognizers.Add(closeTap);
        SemanticProperties.SetDescription(closeButton, "Close");

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(16, 4, 16, 10)
        };

        header.Add(new Label
        {
            Text = title,
            TextColor = text,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        }, 0);
        header.Add(closeButton, 1);

        var maxListHeight = Math.Max(
            280,
            DeviceDisplay.Current.MainDisplayInfo.Height / DeviceDisplay.Current.MainDisplayInfo.Density * 0.62);

        var scroll = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 0, 16, 20),
                Children = { listCard }
            },
            MaximumHeightRequest = maxListHeight
        };

        var sheetContent = new VerticalStackLayout { Spacing = 0 };
        sheetContent.Add(handle);
        sheetContent.Add(header);
        sheetContent.Add(scroll);

        _sheet = new Border
        {
            BackgroundColor = pageBg,
            Stroke = border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20, 20, 0, 0) },
            Padding = 0,
            MaximumWidthRequest = 560,
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Fill,
            Content = sheetContent
        };

        // Swallow taps on the sheet so only the dark area above it dismisses the picker.
        _sheet.GestureRecognizers.Add(new TapGestureRecognizer());

        var backdrop = new Grid { Children = { _sheet } };
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            // Slide the sheet up from the bottom edge.
            _sheet.TranslationY = 420;
            await _sheet.TranslateTo(0, 0, 220, Easing.CubicOut);
        }
        catch
        {
            _sheet.TranslationY = 0;
        }
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
