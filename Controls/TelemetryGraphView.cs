using CarScanner.Models;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;
using System.Collections;
using System.Collections.Specialized;

namespace CarScanner.Controls;

public sealed class TelemetryGraphView : GraphicsView
{
    public static readonly BindableProperty SamplesProperty = BindableProperty.Create(nameof(Samples), typeof(IEnumerable), typeof(TelemetryGraphView), null, propertyChanged: OnSamplesChanged);
    public static readonly BindableProperty PidProperty = BindableProperty.Create(nameof(Pid), typeof(int), typeof(TelemetryGraphView), 0x0C, propertyChanged: OnDataChanged);
    public static readonly BindableProperty PidsProperty = BindableProperty.Create(nameof(Pids), typeof(IEnumerable<int>), typeof(TelemetryGraphView), null, propertyChanged: OnDataChanged);
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(TelemetryGraphView), "Telemetry", propertyChanged: OnDataChanged);

    public IEnumerable? Samples { get => (IEnumerable?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public int Pid { get => (int)GetValue(PidProperty); set => SetValue(PidProperty, value); }
    public IEnumerable<int>? Pids { get => (IEnumerable<int>?)GetValue(PidsProperty); set => SetValue(PidsProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public TelemetryGraphView() { Drawable = new TelemetryDrawable(this); HeightRequest = 190; }
    private INotifyCollectionChanged? _sampleCollection;

    private static void OnSamplesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (TelemetryGraphView)bindable;
        if (view._sampleCollection is not null)
            view._sampleCollection.CollectionChanged -= view.OnSamplesCollectionChanged;
        view._sampleCollection = newValue as INotifyCollectionChanged;
        if (view._sampleCollection is not null)
            view._sampleCollection.CollectionChanged += view.OnSamplesCollectionChanged;
        view.Invalidate();
    }

    private void OnSamplesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();
    private static void OnDataChanged(BindableObject bindable, object oldValue, object newValue) => ((TelemetryGraphView)bindable).Invalidate();

    private sealed class TelemetryDrawable : IDrawable
    {
        private readonly TelemetryGraphView _owner;
        public TelemetryDrawable(TelemetryGraphView owner) => _owner = owner;
        public void Draw(ICanvas canvas, RectF rect)
        {
            canvas.FillColor = Color.FromArgb("#121A2A"); canvas.FillRoundedRectangle(rect, 14);
            var selected = _owner.Pids?.Distinct().Take(3).ToArray() ?? [_owner.Pid];
            var allSamples = _owner.Samples?.Cast<object>().OfType<ObdTelemetrySample>() ?? Enumerable.Empty<ObdTelemetrySample>();
            var series = selected.Select(pid => allSamples.Where(x => x.Pid == pid).OrderBy(x => x.Timestamp).TakeLast(120).ToList()).ToList();
            var points = series.SelectMany(item => item).ToList();
            canvas.FontColor = Colors.LightGray; canvas.FontSize = 13; canvas.DrawString(_owner.Title, 14, 10, rect.Width-28, 20, HorizontalAlignment.Left, VerticalAlignment.Center);
            if(points.Count<2){canvas.FontColor=Colors.Gray;canvas.DrawString("Live მონაცემებს ველოდები…",14,rect.Height/2-10,rect.Width-28,20,HorizontalAlignment.Left,VerticalAlignment.Center);return;}
            var left=14f;var top=38f;var right=rect.Width-14f;var bottom=rect.Height-18f;var min=points.Min(x=>x.Value);var max=points.Max(x=>x.Value);if(Math.Abs(max-min)<0.001){max=min+1;}
            canvas.StrokeColor=Color.FromArgb("#26334A");canvas.StrokeSize=1;canvas.DrawLine(left,bottom,right,bottom);canvas.DrawLine(left,top,left,bottom);
            var colors = new[] { "#38BDF8", "#4ADE80", "#F59E0B" };
            for (var seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
            {
                var line = series[seriesIndex];
                if (line.Count < 2) continue;
                var lineMin = line.Min(point => point.Value);
                var lineMax = line.Max(point => point.Value);
                if (Math.Abs(lineMax - lineMin) < 0.001) lineMax = lineMin + 1;
                canvas.StrokeColor = Color.FromArgb(colors[seriesIndex % colors.Length]);
                canvas.StrokeSize = 2;
                PointF? previous = null;
                for (var i = 0; i < line.Count; i++)
                {
                    var x = left + (right - left) * i / (line.Count - 1);
                    var y = bottom - (float)((line[i].Value - lineMin) / (lineMax - lineMin)) * (bottom - top);
                    var current = new PointF(x, y);
                    if (previous.HasValue) canvas.DrawLine(previous.Value, current);
                    previous = current;
                }
            }
            canvas.FontColor=Colors.Gray;canvas.FontSize=10;canvas.DrawString($"{min:0.##}",left,bottom-2,50,14,HorizontalAlignment.Left,VerticalAlignment.Center);canvas.DrawString($"{max:0.##}",left,top-2,60,14,HorizontalAlignment.Left,VerticalAlignment.Center);
        }
    }
}
