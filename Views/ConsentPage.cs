using CarScanner.Services.DataCollection;
using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Views;

/// <summary>
/// The first-run data-collection question: one centred card over a dimmed backdrop, two buttons. Built in code
/// (no XAML, no DI) in the same flat style as <see cref="LanguagePickerPage"/>. Colours come from the app's
/// theme resources and are bound with SetDynamicResource, so the card follows the light/dark theme.
/// The system back button counts as "Not now"; nothing here ever counts as consent except the Agree button.
/// </summary>
public sealed class ConsentPage : ContentPage
{
    private readonly TaskCompletionSource<ConsentChoice> _result = new();
    private bool _closing;

    private ConsentPage(ConsentText text)
    {
        BackgroundColor = Color.FromArgb("#99000000");
        NavigationPage.SetHasNavigationBar(this, false);
        Shell.SetNavBarIsVisible(this, false);

        var title = new Label
        {
            Text = text.Title,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.WordWrap
        };
        title.SetDynamicResource(Label.TextColorProperty, "TextColor");
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);

        var body = new Label
        {
            Text = text.Body,
            FontSize = 13.5,
            LineBreakMode = LineBreakMode.WordWrap,
            LineHeight = 1.15
        };
        body.SetDynamicResource(Label.TextColorProperty, "SubTextColor");

        var agree = new Button
        {
            Text = text.Agree,
            HeightRequest = 48,
            CornerRadius = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };
        agree.SetDynamicResource(VisualElement.BackgroundColorProperty, "Primary");
        agree.Clicked += (_, _) => Close(ConsentChoice.Agree);
        AutomationProperties.SetName(agree, text.Agree);

        var notNow = new Button
        {
            Text = text.NotNow,
            HeightRequest = 48,
            CornerRadius = 12,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 1.2
        };
        notNow.SetDynamicResource(Button.TextColorProperty, "TextColor");
        notNow.SetDynamicResource(Button.BorderColorProperty, "BorderColor");
        notNow.Clicked += (_, _) => Close(ConsentChoice.NotNow);
        AutomationProperties.SetName(notNow, text.NotNow);

        // Long texts (small phones, big fonts, German/French) scroll; the buttons stay visible below.
        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Padding = new Thickness(20, 20, 20, 16),
            MaximumWidthRequest = 440,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(18, 24),
            Content = new Grid
            {
                RowSpacing = 12,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    title,
                    new ScrollView { Content = body }.Row(1),
                    new VerticalStackLayout { Spacing = 8, Children = { agree, notNow } }.Row(2)
                }
            }
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        // A Border stroke is a Brush, so the thin outline takes the theme colour at the time the card is built.
        card.Stroke = Application.Current?.Resources.TryGetValue("BorderColor", out var border) == true && border is Color borderColor
            ? new SolidColorBrush(borderColor)
            : new SolidColorBrush(Colors.Gray);

        // Keep the card inside the screen: about 85 % of the height at most.
        var heightDp = DeviceDisplay.Current.MainDisplayInfo.Height / Math.Max(1, DeviceDisplay.Current.MainDisplayInfo.Density);
        if (heightDp > 0)
            card.MaximumHeightRequest = heightDp * 0.85;

        Content = card;
    }

    private void Close(ConsentChoice choice)
    {
        if (_closing)
            return;

        _closing = true;
        _result.TrySetResult(choice);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await Navigation.PopModalAsync(false); }
            catch { /* already gone */ }
        });
    }

    protected override bool OnBackButtonPressed()
    {
        Close(ConsentChoice.NotNow);
        return true;
    }

    /// <summary>Shows the question and returns the answer, or null when it could not be shown at all.</summary>
    public static async Task<ConsentChoice?> ShowAsync(ConsentText text)
    {
        var navigation = Shell.Current?.Navigation ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (navigation is null)
            return null;

        var page = new ConsentPage(text);
        await navigation.PushModalAsync(page, false);
        return await page._result.Task;
    }
}

internal static class GridRowExtensions
{
    public static T Row<T>(this T view, int row) where T : BindableObject
    {
        Grid.SetRow(view, row);
        return view;
    }
}
