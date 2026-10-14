using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Controls;

/// <summary>
/// A monochrome outline icon from <see cref="IconCatalog"/>. Follows the theme text colour unless <see cref="Tint"/>
/// is set. Decorative by default: put the meaning in a text label or SemanticProperties.Description of the parent.
/// (MAUI's Path is sealed, so the icon wraps one.)
/// </summary>
public sealed class Icon : ContentView
{
    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind), typeof(string), typeof(Icon), null,
        propertyChanged: (b, _, n) => ((Icon)b).ApplyKind(n as string));

    public static readonly BindableProperty TintProperty = BindableProperty.Create(
        nameof(Tint), typeof(Color), typeof(Icon), null,
        propertyChanged: (b, _, _) => ((Icon)b).ApplyColors());

    public static readonly BindableProperty FilledProperty = BindableProperty.Create(
        nameof(Filled), typeof(bool), typeof(Icon), false,
        propertyChanged: (b, _, _) => ((Icon)b).ApplyColors());

    public static readonly BindableProperty StrokeThicknessProperty = BindableProperty.Create(
        nameof(StrokeThickness), typeof(double), typeof(Icon), 1.7,
        propertyChanged: (b, _, n) => ((Icon)b)._path.StrokeThickness = (double)n);

    private static readonly PathGeometryConverter GeometryConverter = new();

    private readonly Microsoft.Maui.Controls.Shapes.Path _path = new()
    {
        StrokeThickness = 1.7,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Aspect = Stretch.Uniform,
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill
    };

    public Icon()
    {
        WidthRequest = 20;
        HeightRequest = 20;
        VerticalOptions = LayoutOptions.Center;
        HorizontalOptions = LayoutOptions.Center;
        InputTransparent = true;
        _path.SetDynamicResource(Microsoft.Maui.Controls.Shapes.Path.StrokeProperty, "TextColor");
        Content = _path;
        AutomationProperties.SetIsInAccessibleTree(this, false);
    }

    /// <summary>Catalog name, for example "close" or "chevron-right".</summary>
    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Explicit colour; null keeps the theme text colour.</summary>
    public Color? Tint
    {
        get => (Color?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>Fills the shape with the icon colour (used for an on/off state such as a favourite star).</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private void ApplyKind(string? name)
    {
        var data = IconCatalog.PathData(name);
        _path.Data = data is null ? new PathGeometry() : (Geometry)GeometryConverter.ConvertFromInvariantString(data)!;
        _path.IsVisible = data is not null;
    }

    private void ApplyColors()
    {
        var color = Tint;
        if (color is null) _path.SetDynamicResource(Microsoft.Maui.Controls.Shapes.Path.StrokeProperty, "TextColor");
        else _path.Stroke = new SolidColorBrush(color);

        if (!Filled) _path.ClearValue(Microsoft.Maui.Controls.Shapes.Path.FillProperty);
        else if (color is null) _path.SetDynamicResource(Microsoft.Maui.Controls.Shapes.Path.FillProperty, "TextColor");
        else _path.Fill = new SolidColorBrush(color);
    }
}
