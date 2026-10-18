using CarScanner.Converters;
using CarScanner.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Controls;

/// <summary>
/// A modal bottom sheet with the live graph of one value (current, maximum, minimum). A page puts one of these on top
/// of its content (a Grid with the page body and this control) and calls <see cref="Show"/> with the row the user
/// tapped. It reuses <see cref="SensorGraphViewModel"/>, the same view model as the full "Graphic Datastream" page, so
/// the graph is fed by the same property-changed notifications and never starts its own ECU polling.
/// </summary>
public sealed class SensorGraphSheet : ContentView
{
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(220);

    private readonly SensorGraphViewModel _viewModel = new();
    private readonly Grid _root = new() { IsVisible = false };
    private readonly Border _sheet;
    private bool _closing;

    /// <summary>Raised after the sheet is closed (the page can stop whatever fed the graph).</summary>
    public event EventHandler? Closed;

    public bool IsOpen { get; private set; }

    public SensorGraphSheet()
    {
        BindingContext = _viewModel;
        // Closed: the control is invisible to touches, so the page underneath keeps working.
        InputTransparent = true;
        CascadeInputTransparent = false;

        var backdrop = new BoxView { Color = Color.FromRgba(0, 0, 0, 0.45) };
        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += (_, _) => Close();
        backdrop.GestureRecognizers.Add(dismiss);

        var grab = new BoxView
        {
            HeightRequest = 4,
            WidthRequest = 40,
            CornerRadius = 2,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        grab.SetDynamicResource(BoxView.ColorProperty, "BorderColor");

        var title = new Label
        {
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "TextColor");
        title.SetBinding(Label.TextProperty, nameof(SensorGraphViewModel.SubtitleText));

        var close = new IconButton { Kind = "close", Plain = true, IconSize = 16, WidthRequest = 40, HeightRequest = 40 };
        close.SetDynamicResource(IconButton.TintProperty, "TextColor");
        SemanticProperties.SetDescription(close, "Close");
        close.Clicked += (_, _) => Close();

        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        header.Add(title);
        header.Add(close, 1, 0);

        var chart = new MultiChartView { HeightRequest = 240, ShowTooltip = true };
        chart.SetBinding(MultiChartView.SeriesProperty, nameof(SensorGraphViewModel.Series));
        chart.SetBinding(MultiChartView.WindowSecondsProperty, nameof(SensorGraphViewModel.WindowSeconds));
        chart.SetBinding(MultiChartView.EndTimeProperty, nameof(SensorGraphViewModel.EndTime));
        chart.SetDynamicResource(MultiChartView.GridColorProperty, "BorderColor");
        chart.SetDynamicResource(MultiChartView.LabelColorProperty, "SubTextColor");
        chart.SetDynamicResource(MultiChartView.CursorColorProperty, "TextColor");

        var noData = new Label
        {
            Text = "ECU-დან მონაცემი ჯერ არ მოსულა — სურათი გამოჩნდება პირველივე პასუხზე.",
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        noData.SetDynamicResource(Label.TextColorProperty, "SubTextColor");
        noData.SetBinding(IsVisibleProperty, new Binding(nameof(SensorGraphViewModel.HasAnyData), converter: new InverseBoolConverter()));

        var chartCard = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(10, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout { Spacing = 0, Children = { chart, noData } }
        };
        chartCard.SetDynamicResource(Border.BackgroundColorProperty, "CardBg");
        chartCard.SetDynamicResource(Border.StrokeProperty, "BorderColor");

        var stats = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            Margin = new Thickness(0, 14, 0, 0)
        };
        stats.Add(Stat(nameof(SensorGraphViewModel.CurrentText), "მიმდინარე"), 0, 0);
        stats.Add(Stat(nameof(SensorGraphViewModel.MaximumText), "მაქსიმუმი"), 1, 0);
        stats.Add(Stat(nameof(SensorGraphViewModel.MinimumText), "მინიმუმი"), 2, 0);

        _sheet = new Border
        {
            VerticalOptions = LayoutOptions.End,
            StrokeThickness = 0,
            Padding = new Thickness(16, 10, 16, 22),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(24, 24, 0, 0) },
            Content = new VerticalStackLayout { Spacing = 10, Children = { grab, header, chartCard, stats } }
        };
        _sheet.SetDynamicResource(Border.BackgroundColorProperty, "PageBg");
        // Swallow taps on the sheet itself so they do not reach the backdrop behind it.
        _sheet.GestureRecognizers.Add(new TapGestureRecognizer());

        _root.Add(backdrop);
        _root.Add(_sheet);
        Content = _root;
    }

    private static VerticalStackLayout Stat(string valueProperty, string caption)
    {
        var value = new Label { FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        value.SetDynamicResource(Label.TextColorProperty, "TextColor");
        value.SetBinding(Label.TextProperty, valueProperty);

        var label = new Label { Text = caption, FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
        label.SetDynamicResource(Label.TextColorProperty, "SubTextColor");

        return new VerticalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.Center, Children = { value, label } };
    }

    /// <summary>Opens the sheet on <paramref name="item"/> (a LivePidItem, ModuleLiveSignal, EcuPidReading or LiveSensorSource).</summary>
    public async void Show(object item)
    {
        try
        {
            if (IsOpen || _closing)
                return;

            IsOpen = true;
            InputTransparent = false;
            _viewModel.ObservedItem = item;
            _sheet.TranslationY = 600;
            _root.IsVisible = true;
            await _sheet.TranslateToAsync(0, 0, (uint)SlideTime.TotalMilliseconds, Easing.CubicOut);
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "SensorGraphSheet.Show");
        }
    }

    /// <summary>Closes the sheet and stops listening to the value. Safe to call when it is already closed.</summary>
    public async void Close()
    {
        try
        {
            if (!IsOpen || _closing)
                return;

            _closing = true;
            _viewModel.ObservedItem = null;
            await _sheet.TranslateToAsync(0, 600, (uint)SlideTime.TotalMilliseconds, Easing.CubicIn);
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "SensorGraphSheet.Close");
        }
        finally
        {
            _root.IsVisible = false;
            InputTransparent = true;
            IsOpen = false;
            _closing = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
