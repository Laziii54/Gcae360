using System.ComponentModel;
using CarScanner.Controls;
using CarScanner.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CarScanner.ViewModels;

/// <summary>
/// Tapping any live value on the Live Data or Module Live Data screens opens this: one sensor, a
/// live-updating chart, and its current/maximum/minimum over the visible window.
/// </summary>
/// <remarks>
/// Takes the exact row object the user tapped (a <see cref="LivePidItem"/> or a
/// <see cref="ModuleLiveSignal"/>) rather than re-subscribing to whichever service feeds it. Both are
/// already <see cref="INotifyPropertyChanged"/> and already updated in place by their own page's
/// existing polling loop, so this never starts its own polling and never changes what gets read —
/// it only listens for the same property-changed notifications the row's own label already redraws
/// on, and plots the numeric value each one carries.
/// </remarks>
[QueryProperty(nameof(ObservedItem), "item")]
public partial class SensorGraphViewModel : BaseViewModel
{
    // Long enough to see a trend, short enough that an old reading has scrolled off by the time it
    // stops being relevant — matches the window GraphStudio's own single-channel view uses.
    private const double VisibleWindowSeconds = 120;
    private const int MaxPoints = 1500;

    // A steady sensor raises no property change, so without this its line would stop and never reach the right edge.
    private static readonly TimeSpan HoldInterval = TimeSpan.FromSeconds(1);
    private IDispatcherTimer? _holdTimer;
    private DateTime _lastPointAt = DateTime.MinValue;

    private readonly List<ChartPoint> _points = new();
    private INotifyPropertyChanged? _observed;
    private string _name = "";
    private string _unit = "";

    [ObservableProperty] private object? observedItem;
    [ObservableProperty] private string subtitleText = "";
    [ObservableProperty] private IReadOnlyList<ChartSeries> series = Array.Empty<ChartSeries>();
    [ObservableProperty] private DateTime endTime = DateTime.Now;
    [ObservableProperty] private string currentText = "—";
    [ObservableProperty] private string maximumText = "—";
    [ObservableProperty] private string minimumText = "—";
    [ObservableProperty] private bool hasAnyData;

    public double WindowSeconds => VisibleWindowSeconds;

    public SensorGraphViewModel() => Title = CarScanner.Services.OBD.ObdLang.L("გრაფიკი — Live Data", "Graphic Datastream");

    partial void OnObservedItemChanged(object? value)
    {
        Detach();
        _points.Clear();
        HasAnyData = false;
        Series = Array.Empty<ChartSeries>();
        CurrentText = MaximumText = MinimumText = "—";

        if (value is null)
            return;

        (_name, _unit) = NameAndUnit(value);
        var (name, unit) = (_name, _unit);
        SubtitleText = string.IsNullOrWhiteSpace(unit)
            ? name
            : CarScanner.Services.OBD.ObdLang.L($"{name} (ერთეული: {unit})", $"{name} (Unit: {unit})");

        TryAddPointFrom(value, name, unit);

        if (value is INotifyPropertyChanged observable)
        {
            _observed = observable;
            _observed.PropertyChanged += OnObservedPropertyChanged;
            StartHoldTimer();
        }
    }

    public void OnDisappearing() => Detach();

    private void StartHoldTimer()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _holdTimer ??= dispatcher.CreateTimer();
        _holdTimer.Interval = HoldInterval;
        _holdTimer.Tick -= OnHoldTick;
        _holdTimer.Tick += OnHoldTick;
        _holdTimer.Start();
    }

    // Re-reads the value the row currently shows (no ECU request) so a steady sensor draws a flat line.
    private void OnHoldTick(object? sender, EventArgs e)
    {
        if (_observed is { } item && HasAnyData && DateTime.Now - _lastPointAt >= HoldInterval)
            TryAddPointFrom(item, _name, _unit);
    }

    private void Detach()
    {
        _holdTimer?.Stop();
        if (_observed is not null)
            _observed.PropertyChanged -= OnObservedPropertyChanged;
        _observed = null;
    }

    private void OnObservedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is null)
            return;

        // Both LivePidItem (CurrentValue) and ModuleLiveSignal (ValueText) fire this property when a
        // fresh reading lands; anything else (favorite, statistics text, …) is ignored here.
        if (e.PropertyName is nameof(LivePidItem.CurrentValue) or nameof(ModuleLiveSignal.ValueText) or nameof(LiveSensorSource.Value))
        {
            var (name, unit) = NameAndUnit(sender);
            TryAddPointFrom(sender, name, unit);
        }
    }

    private static (string Name, string Unit) NameAndUnit(object item) => item switch
    {
        LivePidItem pid => (pid.DisplayName, pid.Unit),
        ModuleLiveSignal signal => (signal.Name, signal.Unit),
        LiveSensorSource source => (source.Name, source.Unit),
        EcuPidReading reading => (reading.Name, reading.Unit),
        _ => ("", "")
    };

    private void TryAddPointFrom(object item, string name, string unit)
    {
        double? value = item switch
        {
            LivePidItem pid when pid.SampleCount > 0 => pid.CurrentValue,
            ModuleLiveSignal signal when double.TryParse(
                signal.ValueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            LiveSensorSource source => source.Value,
            // ECU Explorer rows: the page already refreshes CurrentValue in place; a "—" (no answer yet) is skipped.
            EcuPidReading reading when double.TryParse(
                reading.CurrentValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedReading) => parsedReading,
            _ => null
        };

        if (value is { } number)
            AddPoint(number, name, unit);
    }

    private void AddPoint(double value, string name, string unit)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var now = DateTime.Now;
            _lastPointAt = now;
            _points.Add(new ChartPoint(now, value));

            var cutoff = now.AddSeconds(-VisibleWindowSeconds);
            while (_points.Count > 0 && (_points[0].Time < cutoff || _points.Count > MaxPoints))
                _points.RemoveAt(0);

            HasAnyData = true;
            CurrentText = Format(value);
            MaximumText = Format(_points.Max(point => point.Value));
            MinimumText = Format(_points.Min(point => point.Value));
            EndTime = now;
            Series = new[]
            {
                new ChartSeries
                {
                    Key = "sensor",
                    Name = name,
                    Unit = unit,
                    Color = Colors.DodgerBlue,
                    Points = _points.ToArray()
                }
            };
        });
    }

    private static string Format(double value) =>
        value.ToString(Math.Abs(value) < 10 ? "0.##" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
}
