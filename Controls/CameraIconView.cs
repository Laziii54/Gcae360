namespace CarScanner.Controls;

/// <summary>Small theme-aware camera icon (drawn, so it follows Light/Dark colors).</summary>
public sealed class CameraIconView : GraphicsView
{
    public static readonly BindableProperty IconColorProperty = BindableProperty.Create(
        nameof(IconColor),
        typeof(Color),
        typeof(CameraIconView),
        Colors.White,
        propertyChanged: (bindable, _, _) => ((CameraIconView)bindable).Invalidate());

    public CameraIconView()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new CameraGlyph(this);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    private sealed class CameraGlyph : IDrawable
    {
        private readonly CameraIconView _owner;

        public CameraGlyph(CameraIconView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var size = Math.Min(rect.Width, rect.Height);
            var x = rect.X + (rect.Width - size) / 2f;
            var y = rect.Y + (rect.Height - size) / 2f;
            var stroke = Math.Max(1.6f, size * 0.085f);

            canvas.StrokeColor = _owner.IconColor;
            canvas.FillColor = _owner.IconColor;
            canvas.StrokeSize = stroke;
            canvas.StrokeLineJoin = LineJoin.Round;

            // Body
            var bodyTop = y + size * 0.3f;
            canvas.DrawRoundedRectangle(x + size * 0.08f, bodyTop, size * 0.84f, size * 0.56f, size * 0.12f);

            // Top bump
            var bump = new PathF();
            bump.MoveTo(x + size * 0.33f, bodyTop);
            bump.LineTo(x + size * 0.4f, y + size * 0.18f);
            bump.LineTo(x + size * 0.6f, y + size * 0.18f);
            bump.LineTo(x + size * 0.67f, bodyTop);
            canvas.DrawPath(bump);

            // Lens
            canvas.DrawCircle(x + size * 0.5f, bodyTop + size * 0.28f, size * 0.15f);

            // Flash dot
            canvas.FillCircle(x + size * 0.77f, bodyTop + size * 0.12f, size * 0.035f);
        }
    }
}
