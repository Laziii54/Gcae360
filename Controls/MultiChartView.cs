using System.Globalization;
using System.Windows.Input;

namespace CarScanner.Controls;

/// <summary>One sample of a graph line.</summary>
public readonly record struct ChartPoint(DateTime Time, double Value);

/// <summary>A vertical event line (user mark, warning …).</summary>
public sealed record ChartMarker(DateTime Time, string Label, Color Color);

/// <summary>A graph line ready to draw (points already limited to the visible window).</summary>
public sealed class ChartSeries
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public Color Color { get; init; } = Colors.Blue;
    public IReadOnlyList<ChartPoint> Points { get; init; } = Array.Empty<ChartPoint>();
    public double? Threshold { get; init; }

    /// <summary>Value at a moment (linear between samples), or null outside the data.</summary>
    public double? ValueAt(DateTime time)
    {
        var points = Points;
        if (points.Count == 0 || time < points[0].Time.AddSeconds(-1) || time > points[^1].Time.AddSeconds(1))
            return null;

        var lo = 0;
        var hi = points.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (points[mid].Time <= time) lo = mid; else hi = mid;
        }

        var a = points[lo];
        var b = points[hi];
        if (time <= a.Time || lo == hi) return a.Value;
        if (time >= b.Time) return b.Value;
        var f = (time - a.Time).TotalMilliseconds / Math.Max(1, (b.Time - a.Time).TotalMilliseconds);
        return a.Value + (b.Value - a.Value) * f;
    }
}

public enum ChartScaleMode
{
    /// <summary>Every line uses its own min/max (lines of different units fit together).</summary>
    Own,
    /// <summary>Fixed 0–100 for every line (percent values).</summary>
    Percent,
    /// <summary>One common min/max for all lines.</summary>
    Shared
}

/// <summary>
/// Time graph with one or more lines, a shared cursor (drag a finger), limit
/// lines and event markers. Used by Graph Studio for single windows, the
/// comparison graph and the small grid windows.
/// </summary>
public sealed class MultiChartView : GraphicsView
{
    public static readonly BindableProperty SeriesProperty = BindableProperty.Create(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(MultiChartView), null, propertyChanged: Redraw);
    public static readonly BindableProperty WindowSecondsProperty = BindableProperty.Create(
        nameof(WindowSeconds), typeof(double), typeof(MultiChartView), 30d, propertyChanged: Redraw);
    public static readonly BindableProperty EndTimeProperty = BindableProperty.Create(
        nameof(EndTime), typeof(DateTime), typeof(MultiChartView), DateTime.MinValue, propertyChanged: Redraw);
    public static readonly BindableProperty CursorTimeProperty = BindableProperty.Create(
        nameof(CursorTime), typeof(DateTime?), typeof(MultiChartView), null, BindingMode.TwoWay, propertyChanged: Redraw);
    public static readonly BindableProperty MarkersProperty = BindableProperty.Create(
        nameof(Markers), typeof(IReadOnlyList<ChartMarker>), typeof(MultiChartView), null, propertyChanged: Redraw);
    public static readonly BindableProperty ScaleModeProperty = BindableProperty.Create(
        nameof(ScaleMode), typeof(ChartScaleMode), typeof(MultiChartView), ChartScaleMode.Own, propertyChanged: Redraw);
    public static readonly BindableProperty CompactProperty = BindableProperty.Create(
        nameof(Compact), typeof(bool), typeof(MultiChartView), false, propertyChanged: Redraw);
    public static readonly BindableProperty ShowTooltipProperty = BindableProperty.Create(
        nameof(ShowTooltip), typeof(bool), typeof(MultiChartView), false, propertyChanged: Redraw);
    public static readonly BindableProperty GridColorProperty = BindableProperty.Create(
        nameof(GridColor), typeof(Color), typeof(MultiChartView), Color.FromArgb("#EDEFF1"), propertyChanged: Redraw);
    public static readonly BindableProperty LabelColorProperty = BindableProperty.Create(
        nameof(LabelColor), typeof(Color), typeof(MultiChartView), Color.FromArgb("#61676F"), propertyChanged: Redraw);
    public static readonly BindableProperty CursorColorProperty = BindableProperty.Create(
        nameof(CursorColor), typeof(Color), typeof(MultiChartView), Color.FromArgb("#16181B"), propertyChanged: Redraw);
    public static readonly BindableProperty KeepCursorProperty = BindableProperty.Create(
        nameof(KeepCursor), typeof(bool), typeof(MultiChartView), false);
    public static readonly BindableProperty TapCommandProperty = BindableProperty.Create(
        nameof(TapCommand), typeof(ICommand), typeof(MultiChartView));
    public static readonly BindableProperty TapCommandParameterProperty = BindableProperty.Create(
        nameof(TapCommandParameter), typeof(object), typeof(MultiChartView));

    private PointF _touchStart;
    private bool _moved;

    public MultiChartView()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new ChartDrawable(this);
        StartInteraction += OnStartInteraction;
        DragInteraction += OnDragInteraction;
        EndInteraction += OnEndInteraction;
        CancelInteraction += (_, _) => ReleaseCursor();
    }

    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public double WindowSeconds { get => (double)GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
    /// <summary>Right edge of the graph (DateTime.MinValue = latest sample).</summary>
    public DateTime EndTime { get => (DateTime)GetValue(EndTimeProperty); set => SetValue(EndTimeProperty, value); }
    public DateTime? CursorTime { get => (DateTime?)GetValue(CursorTimeProperty); set => SetValue(CursorTimeProperty, value); }
    public IReadOnlyList<ChartMarker>? Markers { get => (IReadOnlyList<ChartMarker>?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    public ChartScaleMode ScaleMode { get => (ChartScaleMode)GetValue(ScaleModeProperty); set => SetValue(ScaleModeProperty, value); }
    /// <summary>Small grid window: thin line, no labels.</summary>
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public bool ShowTooltip { get => (bool)GetValue(ShowTooltipProperty); set => SetValue(ShowTooltipProperty, value); }
    public Color GridColor { get => (Color)GetValue(GridColorProperty); set => SetValue(GridColorProperty, value); }
    public Color LabelColor { get => (Color)GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }
    public Color CursorColor { get => (Color)GetValue(CursorColorProperty); set => SetValue(CursorColorProperty, value); }
    /// <summary>Keep the cursor after the finger is lifted (paused graphs).</summary>
    public bool KeepCursor { get => (bool)GetValue(KeepCursorProperty); set => SetValue(KeepCursorProperty, value); }
    public ICommand? TapCommand { get => (ICommand?)GetValue(TapCommandProperty); set => SetValue(TapCommandProperty, value); }
    public object? TapCommandParameter { get => GetValue(TapCommandParameterProperty); set => SetValue(TapCommandParameterProperty, value); }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((MultiChartView)bindable).Invalidate();

    internal DateTime ResolveEnd()
    {
        if (EndTime != DateTime.MinValue)
            return EndTime;

        var latest = DateTime.MinValue;
        foreach (var series in Series ?? Array.Empty<ChartSeries>())
            if (series.Points.Count > 0 && series.Points[^1].Time > latest)
                latest = series.Points[^1].Time;
        return latest == DateTime.MinValue ? DateTime.Now : latest;
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        _touchStart = e.Touches[0];
        _moved = false;
        if (!Compact)
            MoveCursor(e.Touches[0].X);
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        var point = e.Touches[0];
        if (Math.Abs(point.X - _touchStart.X) > 6 || Math.Abs(point.Y - _touchStart.Y) > 6)
            _moved = true;
        if (!Compact)
            MoveCursor(point.X);
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        if (!_moved && TapCommand is { } command && command.CanExecute(TapCommandParameter))
            command.Execute(TapCommandParameter);
        ReleaseCursor();
    }

    private void ReleaseCursor()
    {
        if (!KeepCursor)
            CursorTime = null;
    }

    private void MoveCursor(float x)
    {
        var width = (float)Width;
        if (width <= 0) return;
        var plotLeft = Compact ? 0f : 4f;
        var fraction = Math.Clamp((x - plotLeft) / Math.Max(1f, width - plotLeft * 2), 0f, 1f);
        var end = ResolveEnd();
        CursorTime = end.AddSeconds(-WindowSeconds * (1 - fraction));
    }

    private sealed class ChartDrawable : IDrawable
    {
        private readonly MultiChartView _owner;

        public ChartDrawable(MultiChartView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            if (rect.Width <= 2 || rect.Height <= 2)
                return;

            var compact = _owner.Compact;
            var plot = compact ? new RectF(rect.X, rect.Y + 2, rect.Width, rect.Height - 4)
                               : new RectF(rect.X + 4, rect.Y + 14, rect.Width - 8, rect.Height - 28);
            var end = _owner.ResolveEnd();
            var window = Math.Max(1, _owner.WindowSeconds);
            var start = end.AddSeconds(-window);
            var series = _owner.Series ?? Array.Empty<ChartSeries>();

            // grid
            if (!compact)
            {
                canvas.StrokeColor = _owner.GridColor;
                canvas.StrokeSize = 1;
                for (var i = 1; i < 4; i++)
                {
                    var y = plot.Top + plot.Height * i / 4f;
                    canvas.DrawLine(plot.Left, y, plot.Right, y);
                }
            }

            // common scale
            double sharedMin = double.MaxValue, sharedMax = double.MinValue;
            foreach (var s in series)
                foreach (var p in s.Points)
                {
                    if (p.Time < start) continue;
                    sharedMin = Math.Min(sharedMin, p.Value);
                    sharedMax = Math.Max(sharedMax, p.Value);
                }

            float X(DateTime t) => plot.Left + (float)((t - start).TotalSeconds / window) * plot.Width;

            var first = true;
            foreach (var s in series)
            {
                var (min, max) = Range(s, start, _owner.ScaleMode, sharedMin, sharedMax);
                float Y(double v) => plot.Bottom - (float)((v - min) / (max - min)) * plot.Height;

                // limit line
                if (s.Threshold is { } limit && limit >= min && limit <= max)
                {
                    canvas.StrokeColor = s.Color.WithAlpha(0.8f);
                    canvas.StrokeSize = 1;
                    canvas.StrokeDashPattern = new float[] { 4, 3 };
                    canvas.DrawLine(plot.Left, Y(limit), plot.Right, Y(limit));
                    canvas.StrokeDashPattern = null;
                    if (!compact)
                    {
                        canvas.FontColor = s.Color;
                        canvas.FontSize = 9;
                        canvas.DrawString(limit.ToString("0.#", CultureInfo.InvariantCulture), plot.Right - 60, Y(limit) - 12, 58, 11, HorizontalAlignment.Right, VerticalAlignment.Center);
                    }
                }

                // line
                var path = new PathF();
                var started = false;
                foreach (var p in s.Points)
                {
                    if (p.Time < start.AddSeconds(-1) || p.Time > end.AddSeconds(1)) continue;
                    var x = Math.Clamp(X(p.Time), plot.Left, plot.Right);
                    var y = Math.Clamp(Y(p.Value), plot.Top, plot.Bottom);
                    if (!started) { path.MoveTo(x, y); started = true; }
                    else path.LineTo(x, y);
                }

                if (started)
                {
                    canvas.StrokeColor = s.Color;
                    canvas.StrokeSize = compact ? 1.6f : 2.2f;
                    canvas.StrokeLineJoin = LineJoin.Round;
                    canvas.DrawPath(path);
                }

                // axis labels of the first line
                if (!compact && first && _owner.ScaleMode != ChartScaleMode.Shared)
                {
                    canvas.FontColor = s.Color;
                    canvas.FontSize = 9;
                    canvas.DrawString(Format(max), plot.Left, rect.Top, 80, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                    canvas.DrawString(Format(min), plot.Left, plot.Bottom + 2, 80, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                }
                else if (!compact && first)
                {
                    canvas.FontColor = _owner.LabelColor;
                    canvas.FontSize = 9;
                    canvas.DrawString(Format(max), plot.Left, rect.Top, 80, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                    canvas.DrawString(Format(min), plot.Left, plot.Bottom + 2, 80, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                }

                first = false;
            }

            // markers
            foreach (var marker in _owner.Markers ?? Array.Empty<ChartMarker>())
            {
                if (marker.Time < start || marker.Time > end) continue;
                var x = X(marker.Time);
                canvas.StrokeColor = marker.Color;
                canvas.StrokeSize = 1;
                canvas.StrokeDashPattern = new float[] { 4, 3 };
                canvas.DrawLine(x, plot.Top, x, plot.Bottom);
                canvas.StrokeDashPattern = null;
                if (!compact)
                {
                    canvas.FontColor = marker.Color;
                    canvas.FontSize = 10;
                    canvas.DrawString(marker.Label, x + 3, plot.Top, 120, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                }
            }

            // time span label
            if (!compact)
            {
                canvas.FontColor = _owner.LabelColor;
                canvas.FontSize = 9;
                canvas.DrawString(end.ToString("HH:mm:ss", CultureInfo.InvariantCulture), plot.Right - 70, plot.Bottom + 2, 70, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
            }

            // cursor
            if (_owner.CursorTime is { } cursor && cursor >= start && cursor <= end)
            {
                var x = X(cursor);
                canvas.StrokeColor = _owner.CursorColor;
                canvas.StrokeSize = 1;
                canvas.DrawLine(x, plot.Top, x, plot.Bottom);

                var lines = new List<(string Text, Color Color)>();
                foreach (var s in series)
                {
                    if (s.ValueAt(cursor) is not { } value) continue;
                    var (min, max) = Range(s, start, _owner.ScaleMode, sharedMin, sharedMax);
                    var y = plot.Bottom - (float)((value - min) / (max - min)) * plot.Height;
                    canvas.FillColor = s.Color;
                    canvas.FillCircle(x, Math.Clamp(y, plot.Top, plot.Bottom), compact ? 2.5f : 3.5f);
                    lines.Add(($"{Format(value)} {s.Unit}".Trim(), s.Color));
                }

                if (_owner.ShowTooltip && lines.Count > 0)
                {
                    var boxWidth = 104f;
                    var boxHeight = 16f + lines.Count * 12f;
                    var bx = x + 8 + boxWidth > plot.Right ? x - 8 - boxWidth : x + 8;
                    canvas.FillColor = Color.FromArgb("#E616181B");
                    canvas.FillRoundedRectangle(bx, plot.Top + 2, boxWidth, boxHeight, 6);
                    canvas.FontSize = 9.5f;
                    canvas.FontColor = Colors.White;
                    canvas.DrawString(cursor.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture), bx + 6, plot.Top + 4, boxWidth - 8, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                    for (var i = 0; i < lines.Count; i++)
                    {
                        canvas.FontColor = Lighten(lines[i].Color);
                        canvas.DrawString(lines[i].Text, bx + 6, plot.Top + 16 + i * 12, boxWidth - 8, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                    }
                }
            }
        }

        private static (double Min, double Max) Range(ChartSeries s, DateTime start, ChartScaleMode mode, double sharedMin, double sharedMax)
        {
            double min, max;
            switch (mode)
            {
                case ChartScaleMode.Percent:
                    min = 0; max = 100;
                    break;
                case ChartScaleMode.Shared when sharedMin <= sharedMax:
                    min = sharedMin; max = sharedMax;
                    break;
                default:
                    min = double.MaxValue; max = double.MinValue;
                    foreach (var p in s.Points)
                    {
                        if (p.Time < start) continue;
                        min = Math.Min(min, p.Value);
                        max = Math.Max(max, p.Value);
                    }
                    if (min > max) { min = 0; max = 1; }
                    if (s.Threshold is { } t) { min = Math.Min(min, t); max = Math.Max(max, t); }
                    break;
            }

            if (max - min < 1e-6) { max += 1; min -= 1; }
            var pad = (max - min) * 0.06;
            return (min - pad, max + pad);
        }

        private static string Format(double value) =>
            Math.Abs(value) >= 100 ? value.ToString("0", CultureInfo.InvariantCulture)
            : Math.Abs(value) >= 10 ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

        private static Color Lighten(Color color) =>
            new(Math.Min(1f, color.Red + 0.35f), Math.Min(1f, color.Green + 0.35f), Math.Min(1f, color.Blue + 0.35f));
    }
}

/// <summary>X–Y graph: one value against another (e.g. MAF over RPM). Older points fade.</summary>
public sealed class ScatterChartView : GraphicsView
{
    public static readonly BindableProperty PointsProperty = BindableProperty.Create(
        nameof(Points), typeof(IReadOnlyList<Point>), typeof(ScatterChartView), null,
        propertyChanged: (b, _, _) => ((ScatterChartView)b).Invalidate());
    public static readonly BindableProperty PointColorProperty = BindableProperty.Create(
        nameof(PointColor), typeof(Color), typeof(ScatterChartView), Color.FromArgb("#1D5BD6"),
        propertyChanged: (b, _, _) => ((ScatterChartView)b).Invalidate());
    public static readonly BindableProperty GridColorProperty = BindableProperty.Create(
        nameof(GridColor), typeof(Color), typeof(ScatterChartView), Color.FromArgb("#EDEFF1"),
        propertyChanged: (b, _, _) => ((ScatterChartView)b).Invalidate());
    public static readonly BindableProperty LabelColorProperty = BindableProperty.Create(
        nameof(LabelColor), typeof(Color), typeof(ScatterChartView), Color.FromArgb("#61676F"),
        propertyChanged: (b, _, _) => ((ScatterChartView)b).Invalidate());

    public ScatterChartView()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new ScatterDrawable(this);
    }

    public IReadOnlyList<Point>? Points { get => (IReadOnlyList<Point>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Color PointColor { get => (Color)GetValue(PointColorProperty); set => SetValue(PointColorProperty, value); }
    public Color GridColor { get => (Color)GetValue(GridColorProperty); set => SetValue(GridColorProperty, value); }
    public Color LabelColor { get => (Color)GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }

    private sealed class ScatterDrawable : IDrawable
    {
        private readonly ScatterChartView _owner;

        public ScatterDrawable(ScatterChartView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var plot = new RectF(rect.X + 30, rect.Y + 6, rect.Width - 36, rect.Height - 24);
            if (plot.Width <= 2 || plot.Height <= 2) return;

            canvas.StrokeColor = _owner.GridColor;
            canvas.StrokeSize = 1;
            for (var i = 0; i <= 4; i++)
            {
                canvas.DrawLine(plot.Left, plot.Top + plot.Height * i / 4f, plot.Right, plot.Top + plot.Height * i / 4f);
                canvas.DrawLine(plot.Left + plot.Width * i / 4f, plot.Top, plot.Left + plot.Width * i / 4f, plot.Bottom);
            }

            var points = _owner.Points;
            if (points is null || points.Count == 0) return;

            var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
            if (maxX - minX < 1e-6) { maxX += 1; minX -= 1; }
            if (maxY - minY < 1e-6) { maxY += 1; minY -= 1; }

            canvas.FontColor = _owner.LabelColor;
            canvas.FontSize = 9;
            canvas.DrawString(maxY.ToString("0.#", CultureInfo.InvariantCulture), rect.X, plot.Top - 2, 28, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
            canvas.DrawString(minY.ToString("0.#", CultureInfo.InvariantCulture), rect.X, plot.Bottom - 10, 28, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
            canvas.DrawString(minX.ToString("0.#", CultureInfo.InvariantCulture), plot.Left, plot.Bottom + 4, 60, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.DrawString(maxX.ToString("0.#", CultureInfo.InvariantCulture), plot.Right - 60, plot.Bottom + 4, 60, 12, HorizontalAlignment.Right, VerticalAlignment.Top);

            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var x = plot.Left + (float)((p.X - minX) / (maxX - minX)) * plot.Width;
                var y = plot.Bottom - (float)((p.Y - minY) / (maxY - minY)) * plot.Height;
                var age = (float)i / points.Count;
                canvas.FillColor = _owner.PointColor.WithAlpha(0.15f + 0.75f * age);
                canvas.FillCircle(x, y, i == points.Count - 1 ? 5f : 2.6f);
            }
        }
    }
}
