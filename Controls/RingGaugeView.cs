namespace CarScanner.Controls;

/// <summary>Flat ring gauge (0..1). Track + colored arc starting at 12 o'clock, clockwise.</summary>
public sealed class RingGaugeView : GraphicsView
{
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(RingGaugeView), 0d,
        propertyChanged: (b, _, _) => ((RingGaugeView)b).Invalidate());

    public static readonly BindableProperty RingColorProperty = BindableProperty.Create(
        nameof(RingColor), typeof(Color), typeof(RingGaugeView), Color.FromArgb("#1F8A4C"),
        propertyChanged: (b, _, _) => ((RingGaugeView)b).Invalidate());

    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(RingGaugeView), Color.FromArgb("#ECEEF0"),
        propertyChanged: (b, _, _) => ((RingGaugeView)b).Invalidate());

    public static readonly BindableProperty ThicknessProperty = BindableProperty.Create(
        nameof(Thickness), typeof(float), typeof(RingGaugeView), 14f,
        propertyChanged: (b, _, _) => ((RingGaugeView)b).Invalidate());

    public RingGaugeView()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new RingDrawable(this);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public float Thickness
    {
        get => (float)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    private sealed class RingDrawable : IDrawable
    {
        private readonly RingGaugeView _owner;

        public RingDrawable(RingGaugeView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var stroke = Math.Max(2f, _owner.Thickness);
            var size = Math.Min(rect.Width, rect.Height) - stroke;
            if (size <= 0)
                return;

            var x = rect.X + (rect.Width - size) / 2f;
            var y = rect.Y + (rect.Height - size) / 2f;
            var p = double.IsNaN(_owner.Progress) ? 0 : Math.Clamp(_owner.Progress, 0, 1);

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Round;

            canvas.StrokeColor = _owner.TrackColor ?? Colors.LightGray;
            canvas.DrawEllipse(x, y, size, size);

            if (p <= 0)
                return;

            canvas.StrokeColor = _owner.RingColor ?? Colors.Green;
            if (p >= 0.999)
            {
                canvas.DrawEllipse(x, y, size, size);
                return;
            }

            // MAUI angles: 0 = 3 o'clock, counter-clockwise positive. Start at 12 o'clock, go clockwise.
            var end = 90f - (float)(360 * p);
            canvas.DrawArc(x, y, size, size, 90f, end, true, false);
        }
    }
}
