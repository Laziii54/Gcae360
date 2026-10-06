namespace CarScanner.Controls;

/// <summary>
/// Battery power of the last minutes as thin bars around a zero line:
/// consumption above (orange), charging / regeneration below (green).
/// </summary>
public sealed class PowerHistoryView : GraphicsView
{
    public static readonly BindableProperty SamplesProperty = BindableProperty.Create(
        nameof(Samples), typeof(IReadOnlyList<double>), typeof(PowerHistoryView), null,
        propertyChanged: (b, _, _) => ((PowerHistoryView)b).Invalidate());

    public static readonly BindableProperty CapacityProperty = BindableProperty.Create(
        nameof(Capacity), typeof(int), typeof(PowerHistoryView), 150,
        propertyChanged: (b, _, _) => ((PowerHistoryView)b).Invalidate());

    public static readonly BindableProperty DischargeColorProperty = BindableProperty.Create(
        nameof(DischargeColor), typeof(Color), typeof(PowerHistoryView), Color.FromArgb("#D0701F"),
        propertyChanged: (b, _, _) => ((PowerHistoryView)b).Invalidate());

    public static readonly BindableProperty ChargeColorProperty = BindableProperty.Create(
        nameof(ChargeColor), typeof(Color), typeof(PowerHistoryView), Color.FromArgb("#1E8A4C"),
        propertyChanged: (b, _, _) => ((PowerHistoryView)b).Invalidate());

    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(
        nameof(LineColor), typeof(Color), typeof(PowerHistoryView), Color.FromArgb("#C9CDD2"),
        propertyChanged: (b, _, _) => ((PowerHistoryView)b).Invalidate());

    public PowerHistoryView()
    {
        BackgroundColor = Colors.Transparent;
        HeightRequest = 72;
        Drawable = new HistoryDrawable(this);
    }

    /// <summary>kW, oldest first; + = consumption, − = charging.</summary>
    public IReadOnlyList<double>? Samples
    {
        get => (IReadOnlyList<double>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>How many samples fill the full width.</summary>
    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public Color DischargeColor
    {
        get => (Color)GetValue(DischargeColorProperty);
        set => SetValue(DischargeColorProperty, value);
    }

    public Color ChargeColor
    {
        get => (Color)GetValue(ChargeColorProperty);
        set => SetValue(ChargeColorProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    private sealed class HistoryDrawable : IDrawable
    {
        private readonly PowerHistoryView _owner;

        public HistoryDrawable(PowerHistoryView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var mid = rect.Top + rect.Height / 2f;
            canvas.StrokeColor = _owner.LineColor;
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = new float[] { 3, 3 };
            canvas.DrawLine(rect.Left, mid, rect.Right, mid);
            canvas.StrokeDashPattern = null;

            var samples = _owner.Samples;
            if (samples is null || samples.Count == 0)
                return;

            var capacity = Math.Max(_owner.Capacity, samples.Count);
            var step = rect.Width / capacity;
            var barWidth = Math.Max(1f, step * 0.75f);
            var scale = Math.Max(10d, samples.Max(value => Math.Abs(value)));
            var half = rect.Height / 2f - 1f;
            var start = rect.Right - samples.Count * step;

            for (var i = 0; i < samples.Count; i++)
            {
                var value = samples[i];
                var length = (float)(Math.Abs(value) / scale * half);
                if (length < 0.5f)
                    continue;

                canvas.FillColor = value >= 0 ? _owner.DischargeColor : _owner.ChargeColor;
                var x = start + i * step;
                if (value >= 0)
                    canvas.FillRectangle(x, mid - length, barWidth, length);
                else
                    canvas.FillRectangle(x, mid, barWidth, length);
            }
        }
    }
}
