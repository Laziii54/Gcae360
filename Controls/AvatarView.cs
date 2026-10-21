using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Controls;

/// <summary>
/// A round profile picture. When the person has a (checked) picture it is shown; otherwise a car icon on a colour that
/// comes from the name, so the same driver always gets the same colour and the ranking looks varied without any letters.
/// </summary>
public sealed class AvatarView : ContentView
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb("#1D5BD6"), Color.FromArgb("#0E8A7D"), Color.FromArgb("#B3361F"), Color.FromArgb("#7A4FD1"),
        Color.FromArgb("#C77A12"), Color.FromArgb("#2B8A3E"), Color.FromArgb("#C2255C"), Color.FromArgb("#3B5BDB")
    };

    private const string CarPath =
        "M4,15 L5.5,10 C5.8,9 6.6,8.5 7.6,8.5 L16.4,8.5 C17.4,8.5 18.2,9 18.5,10 L20,15 M4,15 L4,18 M20,15 L20,18 M3,15 L21,15 M7.5,12.2 L7.6,12.2 M16.4,12.2 L16.5,12.2";

    public static readonly BindableProperty NameProperty = BindableProperty.Create(
        nameof(Name), typeof(string), typeof(AvatarView), string.Empty, propertyChanged: (b, _, _) => ((AvatarView)b).Apply());

    /// <summary>Absolute URL of the picture; empty = default car icon.</summary>
    public static readonly BindableProperty AvatarUrlProperty = BindableProperty.Create(
        nameof(AvatarUrl), typeof(string), typeof(AvatarView), string.Empty, propertyChanged: (b, _, _) => ((AvatarView)b).Apply());

    /// <summary>A picture file on this phone (the person's own picture, kept locally); it wins over <see cref="AvatarUrl"/> while the file exists.</summary>
    public static readonly BindableProperty LocalFilePathProperty = BindableProperty.Create(
        nameof(LocalFilePath), typeof(string), typeof(AvatarView), string.Empty, propertyChanged: (b, _, _) => ((AvatarView)b).Apply());

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(AvatarView), 38d, propertyChanged: (b, _, _) => ((AvatarView)b).Apply());

    private readonly Border _frame = new() { StrokeThickness = 0, Padding = 0 };
    private readonly Image _image = new() { Aspect = Aspect.AspectFill, IsVisible = false };
    private readonly Microsoft.Maui.Controls.Shapes.Path _car;

    public AvatarView()
    {
        _car = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry?)new PathGeometryConverter().ConvertFromInvariantString(CarPath),
            Stroke = Colors.White,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.Uniform,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var layers = new Grid();
        layers.Add(_car);
        layers.Add(_image);
        _frame.Content = layers;
        Content = _frame;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        Apply();
    }

    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public string AvatarUrl { get => (string)GetValue(AvatarUrlProperty); set => SetValue(AvatarUrlProperty, value); }
    public string LocalFilePath { get => (string)GetValue(LocalFilePathProperty); set => SetValue(LocalFilePathProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>The colour of a name (stable between runs and devices).</summary>
    public static Color ColorFor(string? name)
    {
        // FNV-1a over the UTF-16 code units: the same name always lands on the same colour.
        var hash = 2166136261u;
        foreach (var c in name ?? string.Empty)
            hash = (hash ^ c) * 16777619u;
        return Palette[hash % (uint)Palette.Length];
    }

    private void Apply()
    {
        var size = Size <= 0 ? 38 : Size;
        WidthRequest = HeightRequest = size;
        _frame.WidthRequest = _frame.HeightRequest = size;
        _frame.StrokeShape = new RoundRectangle { CornerRadius = size / 2 };
        _frame.BackgroundColor = ColorFor(Name);
        _car.WidthRequest = _car.HeightRequest = size * 0.6;

        var hasLocal = !string.IsNullOrWhiteSpace(LocalFilePath) && File.Exists(LocalFilePath);
        var hasPicture = hasLocal || (Uri.TryCreate(AvatarUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
        _image.Source = hasLocal
            ? ImageSource.FromFile(LocalFilePath)
            : hasPicture && Uri.TryCreate(AvatarUrl, UriKind.Absolute, out var remote) ? ImageSource.FromUri(remote) : null;
        _image.IsVisible = hasPicture;
        SemanticProperties.SetDescription(this, string.IsNullOrWhiteSpace(Name) ? "Profile photo" : Name);
    }
}
