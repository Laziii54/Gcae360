using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.PerformanceVideo;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One 0–X cell of the bottom split bar.</summary>
public sealed partial class PerformanceSplitCell : ObservableObject
{
    [ObservableProperty] private string label = string.Empty;
    [ObservableProperty] private string valueText = "—";
    [ObservableProperty] private bool isDone;
    [ObservableProperty] private bool showSeparator = true;
}

/// <summary>
/// Performance Video: camera preview with live OBD speed and 0–20/50/80/100
/// splits, recording, and saving with / without the burned-in data.
/// </summary>
public partial class PerformanceVideoViewModel : BaseViewModel
{
    private readonly AccelerationMeasurementService _acceleration;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private readonly IPerformanceVideoExporter _exporter;
    private readonly AccelerationLeaderboardService _leaderboard;

    private readonly PerformanceSplitTracker _tracker = new();
    private readonly object _gate = new();
    private IPerformanceCameraController? _camera;
    private PerformanceVideoTelemetry? _telemetry;
    private DateTime _recordingStartedUtc;
    private DateTime? _lastSampleAt;
    private int? _lastSpeed;
    private string? _pendingVideoPath;
    private IDispatcherTimer? _recordTimer;
    private CancellationTokenSource? _saveCancellation;
    private bool _previewStarted;
    private bool _attaching;
    private IDispatcherTimer? _speedWatchdog;
    private int _speedRetries;

    public ObservableCollection<PerformanceSplitCell> Splits { get; } = new();

    [ObservableProperty] private string speedText = "—";
    [ObservableProperty] private string speedUnit = "km/h";
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private string recordTimeText = "00:00";
    [ObservableProperty] private bool isSavePanelVisible;
    [ObservableProperty] private bool isProcessing;
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string hintText = string.Empty;

    // Localized texts (from PerformanceVideoStrings resources).
    [ObservableProperty] private string titleText = string.Empty;
    [ObservableProperty] private string recordButtonText = string.Empty;
    [ObservableProperty] private string recText = string.Empty;
    [ObservableProperty] private string closeText = string.Empty;
    [ObservableProperty] private string saveTitleText = string.Empty;
    [ObservableProperty] private string saveHintText = string.Empty;
    [ObservableProperty] private string saveWithDataText = string.Empty;
    [ObservableProperty] private string saveWithoutDataText = string.Empty;
    [ObservableProperty] private string saveBothText = string.Empty;
    [ObservableProperty] private string discardText = string.Empty;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public bool HasHint => !string.IsNullOrWhiteSpace(HintText) && !IsRecording;
    public bool CanUseRecordButton => !IsSavePanelVisible && !IsProcessing;
    public bool AreSaveButtonsEnabled => !IsProcessing;

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnHintTextChanged(string value) => OnPropertyChanged(nameof(HasHint));
    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasHint));
        RecordButtonText = PerformanceVideoText.Get(value ? PerformanceVideoText.StopRecording : PerformanceVideoText.StartRecording);
    }
    partial void OnIsSavePanelVisibleChanged(bool value) => OnPropertyChanged(nameof(CanUseRecordButton));
    partial void OnIsProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseRecordButton));
        OnPropertyChanged(nameof(AreSaveButtonsEnabled));
    }

    public PerformanceVideoViewModel(
        AccelerationMeasurementService acceleration,
        UnitSystemService units,
        LocalizationService localization,
        IPerformanceVideoExporter exporter,
        AccelerationLeaderboardService leaderboard)
    {
        _acceleration = acceleration;
        _units = units;
        _localization = localization;
        _exporter = exporter;
        _leaderboard = leaderboard;

        for (var i = 0; i < PerformanceSplitTracker.Targets.Length; i++)
        {
            Splits.Add(new PerformanceSplitCell
            {
                Label = $"0–{PerformanceSplitTracker.Targets[i]}",
                ShowSeparator = i > 0
            });
        }

        ApplyTexts();
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyTexts);
    }

    public bool IsSupported => _exporter.IsSupported;

    private void ApplyTexts()
    {
        TitleText = PerformanceVideoText.Get(PerformanceVideoText.Title);
        RecordButtonText = PerformanceVideoText.Get(IsRecording ? PerformanceVideoText.StopRecording : PerformanceVideoText.StartRecording);
        RecText = PerformanceVideoText.Get(PerformanceVideoText.Rec);
        CloseText = PerformanceVideoText.Get(PerformanceVideoText.Close);
        SaveTitleText = PerformanceVideoText.Get(PerformanceVideoText.SaveTitle);
        SaveHintText = PerformanceVideoText.Get(PerformanceVideoText.SaveHint);
        SaveWithDataText = PerformanceVideoText.Get(PerformanceVideoText.SaveWithData);
        SaveWithoutDataText = PerformanceVideoText.Get(PerformanceVideoText.SaveWithoutData);
        SaveBothText = PerformanceVideoText.Get(PerformanceVideoText.SaveBoth);
        DiscardText = PerformanceVideoText.Get(PerformanceVideoText.Discard);
        SpeedUnit = _units.SpeedUnit;
        RefreshSpeedAndSplits();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    public async Task OnAppearingAsync()
    {
        SpeedUnit = _units.SpeedUnit;
        _acceleration.StateChanged -= OnAccelerationStateChanged;
        _acceleration.StateChanged += OnAccelerationStateChanged;

        await EnsureSpeedPreviewAsync();
        OnAccelerationStateChanged(this, EventArgs.Empty);

        // The Performance Lab page stops its own preview while this page
        // opens; if live speed does not arrive, start it again.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            _speedWatchdog ??= dispatcher.CreateTimer();
            _speedWatchdog.Interval = TimeSpan.FromSeconds(3);
            _speedWatchdog.Tick -= OnSpeedWatchdogTick;
            _speedWatchdog.Tick += OnSpeedWatchdogTick;
            _speedWatchdog.Start();
        }
    }

    private async void OnSpeedWatchdogTick(object? sender, EventArgs e)
    {
        var snapshot = _acceleration.GetSnapshot();
        if (snapshot.HasCurrentSpeed && snapshot.LastSpeedAt is { } at && DateTime.UtcNow - at < TimeSpan.FromSeconds(3))
        {
            _speedRetries = 0;
            return;
        }

        // A few retries only — without an OBD connection there is nothing to start.
        if (_speedRetries++ >= 3)
            return;

        await EnsureSpeedPreviewAsync();
    }

    private async Task EnsureSpeedPreviewAsync()
    {
        try
        {
            // The camera overlay needs the freshest possible speed sample.
            // Skip RPM/throttle polling on this page so PID 01 0D can be
            // published as soon as the ECU answers.
            await _acceleration.StartPreviewAsync(speedOnly: true);
        }
        catch
        {
            // No OBD connection: the camera still works, speed shows "—".
        }
    }

    public async Task OnDisappearingAsync()
    {
        _acceleration.StateChanged -= OnAccelerationStateChanged;
        _speedWatchdog?.Stop();
        _speedRetries = 0;

        if (IsRecording)
        {
            try { await StopRecordingCoreAsync(); } catch { }
        }

        _recordTimer?.Stop();
        _saveCancellation?.Cancel();

        if (!IsProcessing)
            DeletePendingVideo();

        _camera?.Release();
        _camera = null;
        _previewStarted = false;

        try { await _acceleration.StopPreviewAsync(); } catch { }
    }

    /// <summary>Shows an unexpected camera error on the page instead of closing the app.</summary>
    public void ReportCameraError(Exception ex) =>
        StatusText = PerformanceVideoText.Format(PerformanceVideoText.CameraError, ex.Message);

    /// <summary>Called by the page once the native camera view is ready.</summary>
    public async Task AttachCameraAsync(IPerformanceCameraController controller)
    {
        if (!ReferenceEquals(_camera, controller))
            _previewStarted = false;

        _camera = controller;
        if (_previewStarted || _attaching)
            return;

        _attaching = true;
        try
        {
            await AttachCameraCoreAsync(controller);
        }
        finally
        {
            _attaching = false;
        }
    }

    private async Task AttachCameraCoreAsync(IPerformanceCameraController controller)
    {
        var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
            permission = await Permissions.RequestAsync<Permissions.Camera>();

        if (permission != PermissionStatus.Granted)
        {
            StatusText = PerformanceVideoText.Get(PerformanceVideoText.CameraPermission);
            return;
        }

        try
        {
            await controller.StartPreviewAsync();
            _previewStarted = true;
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = PerformanceVideoText.Format(PerformanceVideoText.CameraError, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Speed + splits
    // ------------------------------------------------------------------

    private void OnAccelerationStateChanged(object? sender, EventArgs e)
    {
        var snapshot = _acceleration.GetSnapshot();

        lock (_gate)
        {
            if (snapshot.HasCurrentSpeed && snapshot.LastSpeedAt is { } at && at != _lastSampleAt)
            {
                _lastSampleAt = at;
                var display = _units.Convert(snapshot.CurrentSpeed, "km/h").Value;
                _lastSpeed = (int)Math.Round(display, MidpointRounding.AwayFromZero);
                _tracker.AddSample(at, display);

                _telemetry?.Add(new PerformanceOverlayFrame(
                    (at - _recordingStartedUtc).TotalMilliseconds,
                    _lastSpeed,
                    _tracker.Snapshot()));
            }
            else if (!snapshot.HasCurrentSpeed)
            {
                _lastSpeed = null;
            }
        }

        MainThread.BeginInvokeOnMainThread(RefreshSpeedAndSplits);
    }

    private void RefreshSpeedAndSplits()
    {
        int? speed;
        double?[] splits;
        bool armed;
        lock (_gate)
        {
            speed = _lastSpeed;
            splits = _tracker.Snapshot();
            armed = _tracker.IsArmed;
        }

        SpeedText = speed?.ToString(CultureInfo.InvariantCulture) ?? "—";
        SpeedUnit = _units.SpeedUnit;

        var seconds = PerformanceVideoText.Get(PerformanceVideoText.Seconds);
        for (var i = 0; i < Splits.Count && i < splits.Length; i++)
        {
            Splits[i].IsDone = splits[i].HasValue;
            Splits[i].ValueText = splits[i] is { } value
                ? $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {seconds}"
                : "—";
        }

        HintText = speed is null
            ? PerformanceVideoText.Get(PerformanceVideoText.WaitingSpeed)
            : !armed && splits.All(value => value is null)
                ? PerformanceVideoText.Get(PerformanceVideoText.ReadyHint)
                : string.Empty;
    }

    // ------------------------------------------------------------------
    // Recording
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task ToggleRecording()
    {
        if (_camera is null || IsProcessing || IsSavePanelVisible)
            return;

        if (IsRecording)
        {
            await StopRecordingCoreAsync();
            return;
        }

        try
        {
            var mic = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            if (mic != PermissionStatus.Granted)
                mic = await Permissions.RequestAsync<Permissions.Microphone>();

            DeletePendingVideo();
            var path = Path.Combine(FileSystem.CacheDirectory, $"gcar360_perf_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            var telemetry = new PerformanceVideoTelemetry
            {
                SpeedUnit = _units.SpeedUnit,
                SecondsUnit = PerformanceVideoText.Get(PerformanceVideoText.Seconds)
            };

            await _camera.StartRecordingAsync(path, mic == PermissionStatus.Granted);

            lock (_gate)
            {
                _recordingStartedUtc = DateTime.UtcNow;
                telemetry.Add(new PerformanceOverlayFrame(0, _lastSpeed, _tracker.Snapshot()));
                _telemetry = telemetry;
            }

            _pendingVideoPath = path;
            IsRecording = true;
            StatusText = string.Empty;
            StartRecordTimer();
        }
        catch (Exception ex)
        {
            IsRecording = false;
            StatusText = PerformanceVideoText.Format(PerformanceVideoText.RecordError, ex.Message);
        }
    }

    private async Task StopRecordingCoreAsync()
    {
        _recordTimer?.Stop();

        string? path = null;
        try
        {
            path = await _camera!.StopRecordingAsync();
        }
        catch (Exception ex)
        {
            StatusText = PerformanceVideoText.Format(PerformanceVideoText.RecordError, ex.Message);
        }
        finally
        {
            IsRecording = false;
        }

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0)
        {
            _pendingVideoPath = path;
            IsSavePanelVisible = true;
        }
        else
        {
            DeletePendingVideo();
        }
    }

    private void StartRecordTimer()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _recordTimer ??= dispatcher.CreateTimer();
        _recordTimer.Interval = TimeSpan.FromMilliseconds(500);
        _recordTimer.Tick -= OnRecordTimerTick;
        _recordTimer.Tick += OnRecordTimerTick;
        RecordTimeText = "00:00";
        _recordTimer.Start();
    }

    private void OnRecordTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.UtcNow - _recordingStartedUtc;
        RecordTimeText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    // ------------------------------------------------------------------
    // Saving
    // ------------------------------------------------------------------

    [RelayCommand]
    private Task SaveWithData() => SaveAsync(withData: true, clean: false);

    [RelayCommand]
    private Task SaveWithoutData() => SaveAsync(withData: false, clean: true);

    [RelayCommand]
    private Task SaveBoth() => SaveAsync(withData: true, clean: true);

    [RelayCommand]
    private void Discard()
    {
        if (IsProcessing)
            return;

        DeletePendingVideo();
        IsSavePanelVisible = false;
        StatusText = PerformanceVideoText.Get(PerformanceVideoText.Discarded);
    }

    private async Task SaveAsync(bool withData, bool clean)
    {
        var source = _pendingVideoPath;
        if (IsProcessing || string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return;

        if (!await _exporter.EnsureSavePermissionAsync())
        {
            StatusText = PerformanceVideoText.Get(PerformanceVideoText.StoragePermission);
            return;
        }

        _saveCancellation?.Dispose();
        _saveCancellation = new CancellationTokenSource();
        var token = _saveCancellation.Token;
        var baseName = $"GCar360_{DateTime.Now:yyyyMMdd_HHmmss}";
        string? composed = null;

        try
        {
            IsProcessing = true;
            StatusText = string.Empty;

            if (withData)
            {
                composed = Path.Combine(FileSystem.CacheDirectory, baseName + "_data.mp4");
                var telemetry = _telemetry ?? new PerformanceVideoTelemetry { SpeedUnit = _units.SpeedUnit };
                var progress = new Progress<double>(value =>
                    ProgressText = PerformanceVideoText.Format(PerformanceVideoText.Processing, (int)Math.Round(value * 100)));
                ProgressText = PerformanceVideoText.Format(PerformanceVideoText.Processing, 0);

                await _exporter.ComposeWithOverlayAsync(source, composed, telemetry, CurrentOverlayStyle(), progress, token);

                ProgressText = PerformanceVideoText.Get(PerformanceVideoText.Saving);
                await _exporter.SaveToGalleryAsync(composed, baseName + "_data.mp4", token);
            }

            if (clean)
            {
                ProgressText = PerformanceVideoText.Get(PerformanceVideoText.Saving);
                await _exporter.SaveToGalleryAsync(source, baseName + ".mp4", token);
            }

            // Only a video actually recorded and saved by this app grants the
            // video-verified badge to a matching recent 0–100 run.
            await _leaderboard.AttachVideoToLatestEligibleRunAsync(_recordingStartedUtc);

            DeletePendingVideo();
            IsSavePanelVisible = false;
            StatusText = PerformanceVideoText.Get(withData && clean ? PerformanceVideoText.SavedBoth : PerformanceVideoText.Saved);
        }
        catch (Exception ex)
        {
            StatusText = PerformanceVideoText.Format(PerformanceVideoText.SaveFailed, ex.Message);
        }
        finally
        {
            if (composed is not null)
            {
                try { File.Delete(composed); } catch { }
            }

            ProgressText = string.Empty;
            IsProcessing = false;
        }
    }

    private static PerformanceOverlayStyle CurrentOverlayStyle()
    {
        static string Hex(string key, string fallback, float alpha = 1f)
        {
            var color = Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c
                ? c
                : Color.FromArgb(fallback);
            return color.WithAlpha(alpha).ToArgbHex(true);
        }

        return new PerformanceOverlayStyle(
            Hex("CardBg", "#121A2A", 0.78f),
            Hex("TextColor", "#F8FAFC"),
            Hex("SubTextColor", "#A8B7CC"),
            "#FF38BDF8",
            Hex("BorderColor", "#26334A"));
    }

    private void DeletePendingVideo()
    {
        var path = _pendingVideoPath;
        _pendingVideoPath = null;
        lock (_gate)
            _telemetry = null;

        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cache files are cleaned by the OS anyway.
        }
    }

    [RelayCommand]
    private async Task Close()
    {
        if (IsProcessing)
            return;

        if (IsRecording)
        {
            await StopRecordingCoreAsync();
            return;
        }

        DeletePendingVideo();
        IsSavePanelVisible = false;

        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch
        {
            // Page already closed.
        }
    }
}
