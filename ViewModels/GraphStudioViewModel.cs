using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CarScanner.Controls;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.OBD;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One parameter shown in Graph Studio.</summary>
public sealed partial class GraphChannel : ObservableObject
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public Color Color { get; init; } = Colors.Blue;

    [ObservableProperty] private double? threshold;
    [ObservableProperty] private string valueText = "—";
    [ObservableProperty] private string minText = "—";
    [ObservableProperty] private string avgText = "—";
    [ObservableProperty] private string maxText = "—";
    [ObservableProperty] private string rateText = "";
    [ObservableProperty] private bool isOverLimit;
    [ObservableProperty] private IReadOnlyList<ChartSeries> series = Array.Empty<ChartSeries>();

    /// <summary>Legend chip of the comparison graph: "RPM 2 340".</summary>
    public string LegendText => $"{Name} {ValueText}";

    partial void OnValueTextChanged(string value) => OnPropertyChanged(nameof(LegendText));
}

/// <summary>A parameter that can be added in "What to show".</summary>
public sealed partial class GraphChannelOption : ObservableObject
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string thresholdText = string.Empty;
}

/// <summary>One-tap set of parameters.</summary>
public sealed record GraphPreset(string Icon, string Title, string Subtitle, int[] Pids);

/// <summary>A saved recording (CSV in the app folder).</summary>
public sealed record GraphRecording(string Path, string Title, string Detail);

/// <summary>
/// Graph Studio: separate windows, comparison, grid and X–Y views over the
/// same 10-minute buffer, with one shared cursor, limit lines, marks,
/// pause / scroll back, recordings and CSV export.
/// </summary>
public partial class GraphStudioViewModel : BaseViewModel
{
    private const int MaxChannels = 8;
    private static readonly TimeSpan HistoryLength = TimeSpan.FromMinutes(10);
    private static readonly Color[] Palette =
    {
        Color.FromArgb("#1D5BD6"), Color.FromArgb("#D0701F"), Color.FromArgb("#C2372B"), Color.FromArgb("#1E8A4C"),
        Color.FromArgb("#8E44AD"), Color.FromArgb("#0E8C9A"), Color.FromArgb("#B7791F"), Color.FromArgb("#5D636B")
    };
    private static readonly Dictionary<int, double> DefaultLimits = new()
    {
        [0x05] = 105, [0x5C] = 130, [0x0F] = 60
    };

    private readonly LiveDataEngine _engine;
    private readonly OBDService _obd;
    private readonly LocalizationService _localization;
    private readonly Dictionary<int, List<ChartPoint>> _history = new();
    private readonly Dictionary<int, (string Name, string Unit)> _known = new();
    private readonly List<ChartMarker> _markers = new();
    private readonly object _gate = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _refreshQueued;
    private bool _startedEngine;
    private bool _pageVisible;
    private bool _subscribed;
    private DateTime? _pausedAt;
    private DateTime? _recordStart;
    private string? _openedRecording;

    public ObservableCollection<GraphChannel> Channels { get; } = new();
    public ObservableCollection<GraphChannel> CompareChannels { get; } = new();
    public ObservableCollection<GraphChannel> GridChannels { get; } = new();
    public ObservableCollection<GraphChannelOption> Options { get; } = new();
    public ObservableCollection<GraphRecording> Recordings { get; } = new();
    public IReadOnlyList<GraphPreset> Presets { get; private set; } = Array.Empty<GraphPreset>();

    [ObservableProperty] private string selectedView = "separate";
    [ObservableProperty] private bool isSeparateView = true;
    [ObservableProperty] private bool isCompareView;
    [ObservableProperty] private bool isGridView;
    [ObservableProperty] private bool isXyView;
    [ObservableProperty] private double windowSeconds = 30;
    [ObservableProperty] private DateTime endTime = DateTime.MinValue;
    [ObservableProperty] private DateTime? cursorTime;
    [ObservableProperty] private bool isPaused;
    [ObservableProperty] private double scrollBackSeconds;
    [ObservableProperty] private double scrollBackMaximum = 1;
    [ObservableProperty] private string pauseText = "";
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private string recordingText = "";
    [ObservableProperty] private bool isFullScreen;
    [ObservableProperty] private IReadOnlyList<ChartMarker> markers = Array.Empty<ChartMarker>();
    [ObservableProperty] private IReadOnlyList<ChartSeries> compareSeries = Array.Empty<ChartSeries>();
    [ObservableProperty] private ChartScaleMode compareScale = ChartScaleMode.Own;
    [ObservableProperty] private string analysisText = "";
    [ObservableProperty] private int gridSize = 8;
    [ObservableProperty] private GraphChannel? expandedChannel;
    [ObservableProperty] private bool hasExpandedChannel;
    [ObservableProperty] private GraphChannel? xChannel;
    [ObservableProperty] private GraphChannel? yChannel;
    [ObservableProperty] private IReadOnlyList<Point> xyPoints = Array.Empty<Point>();
    [ObservableProperty] private string xyCaption = "";
    [ObservableProperty] private string rateText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool hasChannels;
    [ObservableProperty] private string selectionCountText = "";
    [ObservableProperty] private bool isRecordingOpen;
    [ObservableProperty] private string openedRecordingText = "";
    [ObservableProperty] private bool hasRecordings;

    // Presentation helpers for the page (chips and sizes).
    public bool IsNotFullScreen => !IsFullScreen;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    public double ChartHeight => IsFullScreen ? 520 : 300;
    public double WindowChartHeight => IsFullScreen ? 240 : 96;
    public string PauseButtonText => IsPaused ? "▶" : "⏸";
    public bool IsWindow10 => Math.Abs(WindowSeconds - 10) < 0.5;
    public bool IsWindow30 => Math.Abs(WindowSeconds - 30) < 0.5;
    public bool IsWindow60 => Math.Abs(WindowSeconds - 60) < 0.5;
    public bool IsWindow300 => Math.Abs(WindowSeconds - 300) < 0.5;
    public bool IsScaleOwn => CompareScale == ChartScaleMode.Own;
    public bool IsScalePercent => CompareScale == ChartScaleMode.Percent;
    public bool IsScaleShared => CompareScale == ChartScaleMode.Shared;
    public bool IsGrid4 => GridSize == 4;
    public bool IsGrid6 => GridSize == 6;
    public bool IsGrid8 => GridSize == 8;

    partial void OnIsFullScreenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotFullScreen));
        OnPropertyChanged(nameof(ChartHeight));
        OnPropertyChanged(nameof(WindowChartHeight));
    }

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(PauseButtonText));

    partial void OnWindowSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(IsWindow10));
        OnPropertyChanged(nameof(IsWindow30));
        OnPropertyChanged(nameof(IsWindow60));
        OnPropertyChanged(nameof(IsWindow300));
    }

    partial void OnCompareScaleChanged(ChartScaleMode value)
    {
        OnPropertyChanged(nameof(IsScaleOwn));
        OnPropertyChanged(nameof(IsScalePercent));
        OnPropertyChanged(nameof(IsScaleShared));
    }

    /// <summary>The page turned sideways: graph on the whole screen.</summary>
    public void SetLandscape(bool landscape)
    {
        if (landscape != IsFullScreen)
            IsFullScreen = landscape;
    }

    public GraphStudioViewModel(LiveDataEngine engine, OBDService obd, LocalizationService localization)
    {
        _engine = engine;
        _obd = obd;
        _localization = localization;
        Title = "Graph Studio";
        BuildPresets();
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            BuildPresets();
            OnPropertyChanged(nameof(Presets));
            RefreshTexts();
        });

        var saved = Preferences.Default.Get("graph_studio_pids", "0C,0D,05,04");
        var pids = saved.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(text => int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid) ? pid : -1)
            .Where(pid => pid >= 0)
            .ToArray();
        ApplyChannels(pids.Length > 0 ? pids : new[] { 0x0C, 0x0D, 0x05, 0x04 });
        LoadRecordings();
        RefreshTexts();
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    // =========================================================
    // PAGE LIFE
    // =========================================================

    public async Task OnAppearingAsync()
    {
        _pageVisible = true;
        if (!_subscribed)
        {
            _engine.LiveValuesUpdated += OnValues;
            _subscribed = true;
        }

        _engine.SetGraphPids(Channels.Select(channel => channel.Pid));
        RefreshTexts();
        if (_openedRecording is not null)
            return;

        try
        {
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();
            if (!_engine.IsRunning)
            {
                await _engine.StartAsync();
                _startedEngine = true;
            }

            // The previous page (e.g. live data) may stop polling a moment
            // after this page appeared: make sure it runs again.
            await Task.Delay(700);
            if (_pageVisible && !_engine.IsRunning)
            {
                await _engine.StartAsync();
                _startedEngine = true;
            }
        }
        catch
        {
            StatusText = L("ადაპტერი არ არის დაკავშირებული — ჩანს მხოლოდ ჩანაწერები.", "Adapter not connected — only recordings can be shown.");
        }
    }

    public async Task OnDisappearingAsync()
    {
        _pageVisible = false;
        _engine.SetGraphPids(null);
        if (_startedEngine)
        {
            _startedEngine = false;
            await _engine.StopAsync();
        }
    }

    // =========================================================
    // DATA
    // =========================================================

    private void OnValues(object? sender, IReadOnlyList<SensorValue> values)
    {
        if (_openedRecording is not null)
            return;

        var now = DateTime.Now;
        lock (_gate)
        {
            foreach (var value in values)
            {
                if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                    continue;

                _known[value.Pid] = (value.Name, value.Unit);
                if (!_history.TryGetValue(value.Pid, out var list))
                    _history[value.Pid] = list = new List<ChartPoint>();
                list.Add(new ChartPoint(now, number));

                var oldest = now - HistoryLength;
                var drop = 0;
                while (drop < list.Count && list[drop].Time < oldest) drop++;
                if (drop > 0) list.RemoveRange(0, drop);
            }
        }

        QueueRefresh();
    }

    /// <summary>At most ~6 redraws per second, whatever the polling rate.</summary>
    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        var wait = TimeSpan.FromMilliseconds(160) - (DateTime.UtcNow - _lastRefresh);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait);
            _refreshQueued = false;
            _lastRefresh = DateTime.UtcNow;
            Refresh();
        });
    }

    private DateTime VisibleEnd()
    {
        if (_pausedAt is { } paused)
            return paused.AddSeconds(-ScrollBackSeconds);
        return DateTime.Now;
    }

    private void Refresh()
    {
        var end = VisibleEnd();
        var start = end.AddSeconds(-WindowSeconds);
        EndTime = _pausedAt is null && _openedRecording is null ? DateTime.MinValue : end;

        foreach (var channel in Channels)
        {
            ChartPoint[] all, visible;
            lock (_gate)
            {
                all = _history.TryGetValue(channel.Pid, out var list) ? list.ToArray() : Array.Empty<ChartPoint>();
            }

            visible = all.Where(p => p.Time >= start.AddSeconds(-1) && p.Time <= end.AddSeconds(1)).ToArray();
            channel.Series = new[]
            {
                new ChartSeries { Key = channel.Pid.ToString("X2"), Name = channel.Name, Unit = channel.Unit, Color = channel.Color, Points = visible, Threshold = channel.Threshold }
            };

            var inWindow = visible.Where(p => p.Time >= start).Select(p => p.Value).ToArray();
            channel.MinText = inWindow.Length > 0 ? Format(inWindow.Min()) : "—";
            channel.AvgText = inWindow.Length > 0 ? Format(inWindow.Average()) : "—";
            channel.MaxText = inWindow.Length > 0 ? Format(inWindow.Max()) : "—";

            double? shown = CursorTime is { } cursor
                ? channel.Series[0].ValueAt(cursor)
                : visible.Length > 0 ? visible[^1].Value : null;
            channel.ValueText = shown is { } v ? Format(v) : "—";
            channel.IsOverLimit = channel.Threshold is { } limit && visible.Length > 0 && visible[^1].Value >= limit;

            var recent = all.Where(p => p.Time >= end.AddSeconds(-5) && p.Time <= end).Count();
            channel.RateText = recent > 0 ? $"{recent / 5d:0.#} Hz" : string.Empty;
        }

        CompareSeries = CompareChannels.SelectMany(channel => channel.Series).ToArray();
        Markers = _markers.ToArray();
        RateText = Channels.Count > 0 && Channels.Any(c => c.RateText.Length > 0)
            ? $"{Channels.Where(c => c.RateText.Length > 0).Select(c => double.Parse(c.RateText.Split(' ')[0], CultureInfo.InvariantCulture)).Min():0.#} Hz"
            : string.Empty;

        if (_pausedAt is { } pausedAt)
        {
            DateTime oldest;
            lock (_gate)
                oldest = _history.Values.Where(l => l.Count > 0).Select(l => l[0].Time).DefaultIfEmpty(pausedAt).Min();
            ScrollBackMaximum = Math.Max(1, (pausedAt - oldest).TotalSeconds - WindowSeconds);
            PauseText = ScrollBackSeconds >= 1
                ? $"⏸ −{TimeSpan.FromSeconds(ScrollBackSeconds):mm\\:ss}"
                : L("⏸ პაუზა", "⏸ paused");
        }

        AnalysisText = BuildAnalysis(start, end);
        RefreshXy(start, end);
        RecordingText = IsRecording && _recordStart is { } since
            ? $"● {(DateTime.Now - since):mm\\:ss}"
            : string.Empty;
    }

    private string BuildAnalysis(DateTime start, DateTime end)
    {
        if (CompareChannels.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append($"{start:HH:mm:ss} → {end:HH:mm:ss} · {WindowSeconds:0} {L("წამი", "s")}");
        foreach (var channel in CompareChannels)
        {
            var points = channel.Series.FirstOrDefault()?.Points.Where(p => p.Time >= start && p.Time <= end).ToArray() ?? Array.Empty<ChartPoint>();
            if (points.Length == 0) continue;
            sb.Append('\n').Append($"{channel.Name}: {Format(points.Min(p => p.Value))} → {Format(points.Max(p => p.Value))} {channel.Unit} · {L("საშ.", "avg")} {Format(points.Average(p => p.Value))}");
        }

        return sb.ToString();
    }

    private void RefreshXy(DateTime start, DateTime end)
    {
        if (XChannel is null || YChannel is null)
        {
            XyPoints = Array.Empty<Point>();
            return;
        }

        var xs = XChannel.Series.FirstOrDefault()?.Points ?? Array.Empty<ChartPoint>();
        var ys = YChannel.Series.FirstOrDefault()?.Points ?? Array.Empty<ChartPoint>();
        var pairs = new List<Point>();
        foreach (var y in ys)
        {
            if (y.Time < start || y.Time > end) continue;
            if (XChannel.Series[0].ValueAt(y.Time) is { } x)
                pairs.Add(new Point(x, y.Value));
        }

        XyPoints = pairs;
        XyCaption = $"X: {XChannel.Name} ({XChannel.Unit}) · Y: {YChannel.Name} ({YChannel.Unit}) · {pairs.Count} {L("წერტილი", "points")}";
    }

    private static string Format(double value) =>
        Math.Abs(value) >= 1000 ? value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ')
        : Math.Abs(value) >= 100 ? value.ToString("0", CultureInfo.InvariantCulture)
        : Math.Abs(value) >= 10 ? value.ToString("0.#", CultureInfo.InvariantCulture)
        : value.ToString("0.##", CultureInfo.InvariantCulture);

    partial void OnCursorTimeChanged(DateTime? value) => QueueRefresh();
    partial void OnScrollBackSecondsChanged(double value) => QueueRefresh();
    partial void OnXChannelChanged(GraphChannel? value) => QueueRefresh();
    partial void OnYChannelChanged(GraphChannel? value) => QueueRefresh();
    partial void OnGridSizeChanged(int value)
    {
        OnPropertyChanged(nameof(IsGrid4));
        OnPropertyChanged(nameof(IsGrid6));
        OnPropertyChanged(nameof(IsGrid8));
        RebuildSubsets();
    }

    // =========================================================
    // CHANNELS
    // =========================================================

    private void ApplyChannels(IEnumerable<int> pids)
    {
        var list = pids.Distinct().Take(MaxChannels).ToList();
        Channels.Clear();
        for (var i = 0; i < list.Count; i++)
        {
            var pid = list[i];
            var (name, unit) = Describe(pid);
            var limitKey = $"graph_limit_{pid:X2}";
            var savedLimit = Preferences.Default.Get(limitKey, string.Empty);
            double? limit = double.TryParse(savedLimit, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : string.IsNullOrEmpty(savedLimit) && DefaultLimits.TryGetValue(pid, out var preset) ? preset : null;
            Channels.Add(new GraphChannel { Pid = pid, Name = name, Unit = unit, Color = Palette[i % Palette.Length], Threshold = limit });
        }

        Preferences.Default.Set("graph_studio_pids", string.Join(",", list.Select(pid => pid.ToString("X2"))));
        if (_pageVisible)
            _engine.SetGraphPids(list);
        HasChannels = Channels.Count > 0;
        XChannel = Channels.FirstOrDefault(c => c.Pid == 0x0C) ?? Channels.ElementAtOrDefault(0);
        YChannel = Channels.FirstOrDefault(c => c != XChannel && c.Pid == 0x10) ?? Channels.FirstOrDefault(c => c != XChannel);
        RebuildSubsets();
        QueueRefresh();
    }

    private void RebuildSubsets()
    {
        CompareChannels.Clear();
        foreach (var channel in Channels.Take(4))
            CompareChannels.Add(channel);

        GridChannels.Clear();
        foreach (var channel in Channels.Take(GridSize))
            GridChannels.Add(channel);

        if (ExpandedChannel is not null && !Channels.Contains(ExpandedChannel))
            ExpandedChannel = null;
        HasExpandedChannel = ExpandedChannel is not null;
    }

    private (string Name, string Unit) Describe(int pid)
    {
        lock (_gate)
            if (_known.TryGetValue(pid, out var known))
                return known;

        var info = PidRegistry.Get(pid);
        return info is null ? ($"PID {pid:X2}", string.Empty) : (info.Name, info.Unit);
    }

    // =========================================================
    // COMMANDS — view, window, pause, marks
    // =========================================================

    [RelayCommand]
    private void SelectView(string? view)
    {
        SelectedView = view ?? "separate";
        IsSeparateView = SelectedView == "separate";
        IsCompareView = SelectedView == "compare";
        IsGridView = SelectedView == "grid";
        IsXyView = SelectedView == "xy";
        QueueRefresh();
    }

    [RelayCommand]
    private void SetWindow(string? seconds)
    {
        if (double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0)
            WindowSeconds = value;
        QueueRefresh();
    }

    [RelayCommand]
    private void SetCompareScale(string? mode)
    {
        CompareScale = mode switch
        {
            "percent" => ChartScaleMode.Percent,
            "shared" => ChartScaleMode.Shared,
            _ => ChartScaleMode.Own
        };
    }

    [RelayCommand]
    private void SetGridSize(string? size)
    {
        if (int.TryParse(size, out var value))
            GridSize = Math.Clamp(value, 2, MaxChannels);
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (_openedRecording is not null)
            return;

        if (IsPaused)
        {
            IsPaused = false;
            _pausedAt = null;
            ScrollBackSeconds = 0;
            CursorTime = null;
            PauseText = string.Empty;
        }
        else
        {
            IsPaused = true;
            _pausedAt = DateTime.Now;
            ScrollBackSeconds = 0;
        }

        QueueRefresh();
    }

    [RelayCommand]
    private void AddMarker()
    {
        var time = CursorTime ?? (IsPaused ? VisibleEnd() : DateTime.Now);
        _markers.Add(new ChartMarker(time, $"⚑ {time:HH:mm:ss}", Color.FromArgb("#1E8A4C")));
        QueueRefresh();
    }

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    [RelayCommand]
    private void ToggleExpand(GraphChannel? channel)
    {
        ExpandedChannel = ExpandedChannel == channel ? null : channel;
        HasExpandedChannel = ExpandedChannel is not null;
    }

    [RelayCommand]
    private void ClearHistory()
    {
        lock (_gate)
            _history.Clear();
        _markers.Clear();
        CursorTime = null;
        QueueRefresh();
    }

    [RelayCommand]
    private Task OpenChannels()
    {
        BuildOptions();
        return Shell.Current.GoToAsync(nameof(Views.GraphChannelsPage));
    }

    // =========================================================
    // WHAT TO SHOW
    // =========================================================

    private void BuildPresets()
    {
        Presets = new[]
        {
            new GraphPreset("⚙", L("ძრავის ჯანმრთელობა", "Engine health"), L("ბრუნები · დატვირთვა · კორექციები · MAF", "RPM · load · fuel trims · MAF"), new[] { 0x0C, 0x04, 0x06, 0x07, 0x10 }),
            new GraphPreset("🌡", L("ტემპერატურები", "Temperatures"), L("გამაგრილებელი · ზეთი · ჰაერი · კატალიზატორი", "Coolant · oil · intake air · catalyst"), new[] { 0x05, 0x5C, 0x0F, 0x46, 0x3C }),
            new GraphPreset("⛽", L("საწვავი და ჟანგბადი", "Fuel and oxygen"), L("MAF · O2 სენსორები · მოკლე/გრძელი კორექცია", "MAF · O2 sensors · short/long trim"), new[] { 0x10, 0x14, 0x15, 0x06, 0x07 }),
            new GraphPreset("⚡", L("ბატარეა და ძაბვა", "Battery and voltage"), L("ძაბვა · ჰიბრიდული ბატარეის % · დატვირთვა", "Voltage · hybrid battery % · load"), new[] { 0x42, 0x5B, 0x04, 0x0D }),
            new GraphPreset("🏁", L("აჩქარება", "Acceleration"), L("სიჩქარე · ბრუნები · დროსელი", "Speed · RPM · throttle"), new[] { 0x0D, 0x0C, 0x11 })
        };
    }

    private void BuildOptions()
    {
        var supported = _obd.SupportedPids.Select(pid => pid.Pid).ToHashSet();
        var core = new[] { 0x0C, 0x0D, 0x05, 0x04, 0x11, 0x42 };
        var pids = supported.Count > 0
            ? supported.Union(core)
            : PidRegistry.All.Select(pid => pid.Pid);
        var selected = Channels.Select(c => c.Pid).ToHashSet();

        Options.Clear();
        foreach (var pid in pids.OrderBy(pid => selected.Contains(pid) ? 0 : 1).ThenBy(pid => pid))
        {
            if (pid is 0x00 or 0x20 or 0x40 or 0x60 or 0x80 or 0xA0 or 0xC0 or 0x01 or 0x02 or 0x03 or 0x12 or 0x13 or 0x1C or 0x1D or 0x1E or 0x41 or 0x51)
                continue; // bit fields / status words, not graphable numbers

            var (name, unit) = Describe(pid);
            var limit = Channels.FirstOrDefault(c => c.Pid == pid)?.Threshold;
            Options.Add(new GraphChannelOption
            {
                Pid = pid,
                Name = name,
                Unit = unit,
                Source = supported.Count == 0 || supported.Contains(pid) || core.Contains(pid) ? $"PID {pid:X2}" : L("შეიძლება არ იყოს", "may be unsupported"),
                IsSelected = selected.Contains(pid),
                ThresholdText = limit?.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty
            });
        }

        UpdateSelectionCount();
    }

    public void UpdateSelectionCount() =>
        SelectionCountText = L($"ჩვენება ({Options.Count(o => o.IsSelected)})", $"Show ({Options.Count(o => o.IsSelected)})");

    [RelayCommand]
    private void ToggleOption(GraphChannelOption? option)
    {
        if (option is null) return;
        if (!option.IsSelected && Options.Count(o => o.IsSelected) >= MaxChannels)
        {
            StatusText = L("მაქსიმუმ 8 პარამეტრი.", "Up to 8 parameters.");
            return;
        }

        option.IsSelected = !option.IsSelected;
        UpdateSelectionCount();
    }

    [RelayCommand]
    private async Task ApplyPreset(GraphPreset? preset)
    {
        if (preset is null) return;
        var supported = _obd.SupportedPids.Select(pid => pid.Pid).ToHashSet();
        var pids = supported.Count == 0 ? preset.Pids : preset.Pids.Where(pid => supported.Contains(pid) || pid is 0x0C or 0x0D or 0x05 or 0x04 or 0x11 or 0x42).ToArray();
        ApplyChannels(pids.Length > 0 ? pids : preset.Pids);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task ApplyOptions()
    {
        foreach (var option in Options)
        {
            var key = $"graph_limit_{option.Pid:X2}";
            if (double.TryParse(option.ThresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit))
                Preferences.Default.Set(key, limit.ToString(CultureInfo.InvariantCulture));
            else if (option.IsSelected)
                Preferences.Default.Set(key, "none");
        }

        ApplyChannels(Options.Where(o => o.IsSelected).Select(o => o.Pid));
        await Shell.Current.GoToAsync("..");
    }

    // =========================================================
    // RECORDINGS AND EXPORT
    // =========================================================

    private static string RecordingFolder
    {
        get
        {
            var folder = Path.Combine(FileSystem.AppDataDirectory, "graph_recordings");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (_openedRecording is not null)
            return;

        if (!IsRecording)
        {
            IsRecording = true;
            _recordStart = DateTime.Now;
            QueueRefresh();
            return;
        }

        IsRecording = false;
        var start = _recordStart ?? DateTime.Now;
        _recordStart = null;
        var csv = BuildCsv(start, DateTime.Now);
        if (csv is null)
        {
            StatusText = L("ჩანაწერი ცარიელია.", "The recording is empty.");
            return;
        }

        var path = Path.Combine(RecordingFolder, $"graph_{start:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(path, csv, Encoding.UTF8);
        StatusText = L("ჩანაწერი შენახულია.", "Recording saved.");
        LoadRecordings();
        RecordingText = string.Empty;
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        var end = VisibleEnd();
        var csv = BuildCsv(end.AddSeconds(-WindowSeconds), end);
        if (csv is null)
            return;

        var path = Path.Combine(FileSystem.CacheDirectory, $"gcar360_graph_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(path, csv, Encoding.UTF8);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "GCar360 Graph Studio", File = new ShareFile(path) });
    }

    /// <summary>CSV: time + one column per parameter (values on a shared time line).</summary>
    private string? BuildCsv(DateTime start, DateTime end)
    {
        var channels = Channels.ToArray();
        Dictionary<int, ChartPoint[]> data;
        lock (_gate)
            data = channels.ToDictionary(c => c.Pid, c => _history.TryGetValue(c.Pid, out var l) ? l.Where(p => p.Time >= start && p.Time <= end).ToArray() : Array.Empty<ChartPoint>());

        var times = data.Values.SelectMany(points => points.Select(p => p.Time)).Distinct().OrderBy(t => t).ToArray();
        if (times.Length == 0)
            return null;

        var sb = new StringBuilder();
        sb.Append("time");
        foreach (var c in channels)
            sb.Append(',').Append($"\"{c.Pid:X2} {c.Name} ({c.Unit})\"");
        sb.AppendLine();
        foreach (var time in times)
        {
            sb.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            foreach (var c in channels)
            {
                var point = data[c.Pid].FirstOrDefault(p => p.Time == time);
                sb.Append(',');
                if (point.Time == time)
                    sb.Append(point.Value.ToString(CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void LoadRecordings()
    {
        Recordings.Clear();
        try
        {
            foreach (var file in Directory.GetFiles(RecordingFolder, "*.csv").OrderByDescending(f => f))
            {
                var info = new FileInfo(file);
                var lines = File.ReadLines(file).Skip(1).Count();
                Recordings.Add(new GraphRecording(file, info.CreationTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), $"{lines} {L("ჩანაწერი", "rows")} · {info.Length / 1024d:0.#} KB"));
            }
        }
        catch
        {
            // Unreadable folder: show no recordings.
        }

        HasRecordings = Recordings.Count > 0;
    }

    [RelayCommand]
    private Task ShareRecording(GraphRecording? recording) =>
        recording is null
            ? Task.CompletedTask
            : Share.Default.RequestAsync(new ShareFileRequest { Title = "GCar360 Graph Studio", File = new ShareFile(recording.Path) });

    [RelayCommand]
    private void DeleteRecording(GraphRecording? recording)
    {
        if (recording is null) return;
        try { File.Delete(recording.Path); } catch { }
        LoadRecordings();
    }

    /// <summary>Opens a saved recording in the studio (paused, can be scrolled and compared).</summary>
    [RelayCommand]
    private async Task OpenRecording(GraphRecording? recording)
    {
        if (recording is null) return;

        var lines = File.ReadAllLines(recording.Path);
        if (lines.Length < 2) return;

        var header = lines[0].Split(',');
        var pids = new List<int>();
        for (var i = 1; i < header.Length; i++)
        {
            var text = header[i].Trim('"');
            var id = text.Split(' ')[0];
            var parsedPid = int.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid);
            pids.Add(parsedPid ? pid : -1);
            var nameUnit = text.Length > 3 ? text[3..] : text;
            var open = nameUnit.LastIndexOf('(');
            if (parsedPid && open > 0)
                lock (_gate)
                    _known[pid] = (nameUnit[..open].Trim(), nameUnit[(open + 1)..].TrimEnd(')'));
        }

        lock (_gate)
        {
            _history.Clear();
            foreach (var line in lines.Skip(1))
            {
                var cells = line.Split(',');
                if (!DateTime.TryParseExact(cells[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    continue;
                for (var i = 1; i < cells.Length && i - 1 < pids.Count; i++)
                {
                    var pid = pids[i - 1];
                    if (pid < 0 || !double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
                    if (!_history.TryGetValue(pid, out var list)) _history[pid] = list = new List<ChartPoint>();
                    list.Add(new ChartPoint(time, value));
                }
            }
        }

        _openedRecording = recording.Path;
        IsRecordingOpen = true;
        OpenedRecordingText = L($"ჩანაწერი: {recording.Title}", $"Recording: {recording.Title}");
        DateTime last;
        lock (_gate)
            last = _history.Values.Where(l => l.Count > 0).Select(l => l[^1].Time).DefaultIfEmpty(DateTime.Now).Max();
        _pausedAt = last;
        IsPaused = true;
        ScrollBackSeconds = 0;
        ApplyChannels(pids.Where(pid => pid >= 0));
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private void BackToLive()
    {
        _openedRecording = null;
        IsRecordingOpen = false;
        _pausedAt = null;
        IsPaused = false;
        ScrollBackSeconds = 0;
        lock (_gate)
            _history.Clear();
        _markers.Clear();
        _ = OnAppearingAsync();
        QueueRefresh();
    }

    private void RefreshTexts()
    {
        if (string.IsNullOrEmpty(StatusText) || StatusText.Contains("ადაპტერი") || StatusText.Contains("Adapter"))
            StatusText = _engine.IsRunning || _openedRecording is not null
                ? string.Empty
                : StatusText;
    }
}
