namespace CarScanner.Controls;

/// <summary>
/// Battery power bar: regeneration grows left of the zero mark (green),
/// consumption grows right of it (blue).
/// </summary>
public sealed class PowerBarView : GraphicsView
{
    private const float ZeroAt = 0.3f;

    public static readonly BindableProperty PowerKwProperty = BindableProperty.Create(
        nameof(PowerKw), typeof(double), typeof(PowerBarView), 0d,
        propertyChanged: (b, _, _) => ((PowerBarView)b).Invalidate());

    public static readonly BindableProperty MaxConsumptionKwProperty = BindableProperty.Create(
        nameof(MaxConsumptionKw), typeof(double), typeof(PowerBarView), 150d,
        propertyChanged: (b, _, _) => ((PowerBarView)b).Invalidate());

    public static readonly BindableProperty MaxRegenerationKwProperty = BindableProperty.Create(
        nameof(MaxRegenerationKw), typeof(double), typeof(PowerBarView), 60d,
        propertyChanged: (b, _, _) => ((PowerBarView)b).Invalidate());

    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(PowerBarView), Color.FromArgb("#ECEEF0"),
        propertyChanged: (b, _, _) => ((PowerBarView)b).Invalidate());

    public static readonly BindableProperty MarkColorProperty = BindableProperty.Create(
        nameof(MarkColor), typeof(Color), typeof(PowerBarView), Color.FromArgb("#8A9099"),
        propertyChanged: (b, _, _) => ((PowerBarView)b).Invalidate());

    public PowerBarView()
    {
        BackgroundColor = Colors.Transparent;
        HeightRequest = 10;
        Drawable = new BarDrawable(this);
    }

    public double PowerKw
    {
        get => (double)GetValue(PowerKwProperty);
        set => SetValue(PowerKwProperty, value);
    }

    public double MaxConsumptionKw
    {
        get => (double)GetValue(MaxConsumptionKwProperty);
        set => SetValue(MaxConsumptionKwProperty, value);
    }

    public double MaxRegenerationKw
    {
        get => (double)GetValue(MaxRegenerationKwProperty);
        set => SetValue(MaxRegenerationKwProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public Color MarkColor
    {
        get => (Color)GetValue(MarkColorProperty);
        set => SetValue(MarkColorProperty, value);
    }

    private sealed class BarDrawable : IDrawable
    {
        private readonly PowerBarView _owner;

        public BarDrawable(PowerBarView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var radius = rect.Height / 2f;
            canvas.FillColor = _owner.TrackColor ?? Colors.LightGray;
            canvas.FillRoundedRectangle(rect, radius);

            var zeroX = rect.X + rect.Width * ZeroAt;
            var power = double.IsFinite(_owner.PowerKw) ? _owner.PowerKw : 0;
            if (power > 0.05)
            {
                var share = (float)Math.Clamp(power / Math.Max(1, _owner.MaxConsumptionKw), 0, 1);
                var width = (rect.Width * (1 - ZeroAt)) * share;
                canvas.FillColor = Color.FromArgb("#2563EB");
                canvas.FillRoundedRectangle(zeroX, rect.Y, Math.Max(width, rect.Height), rect.Height, radius);
            }
            else if (power < -0.05)
            {
                var share = (float)Math.Clamp(-power / Math.Max(1, _owner.MaxRegenerationKw), 0, 1);
                var width = (rect.Width * ZeroAt) * share;
                width = Math.Max(width, rect.Height);
                canvas.FillColor = Color.FromArgb("#1F8A4C");
                canvas.FillRoundedRectangle(zeroX - width, rect.Y, width, rect.Height, radius);
            }

            canvas.FillColor = _owner.MarkColor ?? Colors.Gray;
            canvas.FillRectangle(zeroX - 1, rect.Y, 2, rect.Height);
        }
    }
}
