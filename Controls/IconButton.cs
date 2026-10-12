using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Controls;

/// <summary>
/// A square-ish outlined button that shows one catalog icon instead of a glyph or emoji. Set
/// SemanticProperties.Description so screen readers get the action name (the icon itself is decorative).
/// </summary>
public sealed class IconButton : Border
{
    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind), typeof(string), typeof(IconButton), null,
        propertyChanged: (b, _, n) => ((IconButton)b)._icon.Kind = n as string);

    public static readonly BindableProperty TintProperty = BindableProperty.Create(
        nameof(Tint), typeof(Color), typeof(IconButton), null,
        propertyChanged: (b, _, n) => ((IconButton)b)._icon.Tint = n as Color);

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(IconButton), null,
        propertyChanged: (b, _, n) => ((IconButton)b)._tap.Command = n as ICommand);

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(IconButton), null,
        propertyChanged: (b, _, n) => ((IconButton)b)._tap.CommandParameter = n);

    public static readonly BindableProperty FilledProperty = BindableProperty.Create(
        nameof(Filled), typeof(bool), typeof(IconButton), false,
        propertyChanged: (b, _, n) => ((IconButton)b)._icon.Filled = n is true);

    public static readonly BindableProperty PlainProperty = BindableProperty.Create(
        nameof(Plain), typeof(bool), typeof(IconButton), false,
        propertyChanged: (b, _, n) => ((IconButton)b).ApplyPlain(n is true));

    private readonly Icon _icon = new();
    private readonly TapGestureRecognizer _tap = new();

    /// <summary>Raised on tap, for pages that handle the action in code-behind instead of a command.</summary>
    public event EventHandler? Clicked;

    public IconButton()
    {
        StrokeShape = new RoundRectangle { CornerRadius = 10 };
        StrokeThickness = 1;
        SetDynamicResource(BackgroundColorProperty, "CardBg");
        SetDynamicResource(StrokeProperty, "BorderColor");
        WidthRequest = 38;
        HeightRequest = 36;
        Padding = 0;
        Content = _icon;
        _tap.Tapped += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(_tap);
        SemanticProperties.SetHint(this, string.Empty);
    }

    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Color? Tint
    {
        get => (Color?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    /// <summary>No card background and no outline: a bare icon that still has a full tap target.</summary>
    public bool Plain
    {
        get => (bool)GetValue(PlainProperty);
        set => SetValue(PlainProperty, value);
    }

    private void ApplyPlain(bool plain)
    {
        StrokeThickness = plain ? 0 : 1;
        if (plain) BackgroundColor = Colors.Transparent;
        else SetDynamicResource(BackgroundColorProperty, "CardBg");
    }

    /// <summary>Icon size in device-independent pixels (default 20).</summary>
    public double IconSize
    {
        get => _icon.WidthRequest;
        set { _icon.WidthRequest = value; _icon.HeightRequest = value; }
    }
}
