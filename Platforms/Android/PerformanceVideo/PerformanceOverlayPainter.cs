using System.Globalization;
using ABitmap = Android.Graphics.Bitmap;
using ACanvas = Android.Graphics.Canvas;
using APaint = Android.Graphics.Paint;
using APaintFlags = Android.Graphics.PaintFlags;
using ARectF = Android.Graphics.RectF;
using ATypeface = Android.Graphics.Typeface;
using ATypefaceStyle = Android.Graphics.TypefaceStyle;
using CarScanner.Services.PerformanceVideo;
using AColor = Android.Graphics.Color;

namespace CarScanner.Droid.PerformanceVideo;

/// <summary>
/// Draws the Performance Video overlay (live speed + 0–20/50/80/100 bar)
/// into a transparent bitmap, with the same layout and theme colors as the
/// camera preview.
/// </summary>
internal sealed class PerformanceOverlayPainter : IDisposable
{
    private readonly PerformanceOverlayStyle _style;
    private readonly string _speedUnit;
    private readonly string _secondsUnit;
    private readonly APaint _panel;
    private readonly APaint _text;
    private readonly APaint _line;
    private readonly ATypeface _bold = ATypeface.Create(ATypeface.Default, ATypefaceStyle.Bold)!;
    private readonly ATypeface _regular = ATypeface.Default!;

    public PerformanceOverlayPainter(PerformanceOverlayStyle style, string speedUnit, string secondsUnit)
    {
        _style = style;
        _speedUnit = speedUnit;
        _secondsUnit = secondsUnit;
        _panel = new APaint(APaintFlags.AntiAlias) { Color = AColor.ParseColor(style.PanelColor) };
        _text = new APaint(APaintFlags.AntiAlias | APaintFlags.SubpixelText);
        _line = new APaint(APaintFlags.AntiAlias) { Color = AColor.ParseColor(style.BorderColor) };
    }

    public static string KeyOf(PerformanceOverlayFrame state) =>
        (state.Speed?.ToString(CultureInfo.InvariantCulture) ?? "-") + "|" +
        string.Join(",", state.Splits.Select(value => value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"));

    public void Draw(ABitmap bitmap, PerformanceOverlayFrame state)
    {
        bitmap.EraseColor(0);
        using var canvas = new ACanvas(bitmap);

        float w = bitmap.Width;
        float h = bitmap.Height;
        var unit = Math.Min(w, h);
        var margin = unit * 0.045f;

        // ---- Split bar: 0–20 | 0–50 | 0–80 | 0–100 ----
        var barHeight = unit * 0.14f;
        var barBottom = h - unit * 0.045f;
        var barTop = barBottom - barHeight;
        var bar = new ARectF(margin, barTop, w - margin, barBottom);
        canvas.DrawRoundRect(bar, unit * 0.035f, unit * 0.035f, _panel);

        var targets = PerformanceSplitTracker.Targets;
        var columnWidth = bar.Width() / targets.Length;
        _line.StrokeWidth = Math.Max(1f, unit * 0.002f);
        _text.TextAlign = APaint.Align.Center;

        for (var i = 0; i < targets.Length; i++)
        {
            var left = bar.Left + columnWidth * i;
            var centerX = left + columnWidth / 2f;

            if (i > 0)
                canvas.DrawLine(left, barTop + barHeight * 0.22f, left, barBottom - barHeight * 0.22f, _line);

            _text.SetTypeface(_regular);
            _text.TextSize = unit * 0.03f;
            _text.Color = AColor.ParseColor(_style.SubTextColor);
            canvas.DrawText($"0–{targets[i]}", centerX, barTop + barHeight * 0.4f, _text);

            var value = i < state.Splits.Length ? state.Splits[i] : null;
            _text.SetTypeface(_bold);
            _text.TextSize = unit * 0.048f;
            _text.Color = AColor.ParseColor(_style.AccentColor);
            canvas.DrawText(
                value is { } seconds ? $"{seconds.ToString("0.0", CultureInfo.InvariantCulture)} {_secondsUnit}" : "—",
                centerX,
                barTop + barHeight * 0.8f,
                _text);
        }

        // ---- Live speed: bottom-left, not in the very corner ----
        var number = state.Speed?.ToString(CultureInfo.InvariantCulture) ?? "—";
        var numberSize = unit * 0.17f;
        var unitSize = unit * 0.045f;
        var padding = unit * 0.035f;

        _text.TextAlign = APaint.Align.Left;
        _text.SetTypeface(_bold);
        _text.TextSize = numberSize;
        var numberWidth = _text.MeasureText(number);
        var numberHeight = numberSize * 0.74f;
        _text.TextSize = unitSize;
        var unitWidth = _text.MeasureText(_speedUnit);

        var panelLeft = margin + unit * 0.025f;
        var panelBottom = barTop - unit * 0.035f;
        var panelTop = panelBottom - numberHeight - padding * 2f;
        var panelRight = panelLeft + padding + numberWidth + unit * 0.018f + unitWidth + padding;
        canvas.DrawRoundRect(new ARectF(panelLeft, panelTop, panelRight, panelBottom), unit * 0.04f, unit * 0.04f, _panel);

        var baseline = panelBottom - padding;
        _text.TextSize = numberSize;
        _text.Color = AColor.ParseColor(_style.TextColor);
        canvas.DrawText(number, panelLeft + padding, baseline, _text);

        _text.TextSize = unitSize;
        _text.Color = AColor.ParseColor(_style.SubTextColor);
        canvas.DrawText(_speedUnit, panelLeft + padding + numberWidth + unit * 0.018f, baseline - unit * 0.01f, _text);
    }

    public void Dispose()
    {
        _panel.Dispose();
        _text.Dispose();
        _line.Dispose();
    }
}
