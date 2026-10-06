#if IOS
using AVFoundation;
using CarScanner.Services.PerformanceVideo;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Photos;
using UIKit;

namespace CarScanner.Platforms.iOS.PerformanceVideo;

/// <summary>Exports an iPhone recording and saves the user-approved result to Photos.</summary>
public sealed class iOSPerformanceVideoExporter : IPerformanceVideoExporter
{
    public bool IsSupported => true;

    public async Task ComposeWithOverlayAsync(
        string inputPath,
        string outputPath,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style,
        IProgress<double>? progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("The recorded video was not found.", inputPath);

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var asset = AVAsset.FromUrl(NSUrl.FromFilename(inputPath));
        using var videoComposition = AVMutableVideoComposition.Create(asset)
            ?? throw new InvalidOperationException("The iPhone cannot prepare this video for telemetry.");

        var durationSeconds = Math.Max(0.1, asset.Duration.Seconds);
        using var overlayLayers = CreateOverlayLayers(
            videoComposition.RenderSize,
            durationSeconds,
            telemetry,
            style);
        videoComposition.AnimationTool = overlayLayers.AnimationTool;

        using var exporter = AVAssetExportSession.FromAsset(
            asset,
            (string?)AVAssetExportSessionPreset.HighestQuality.GetConstant() ?? "AVAssetExportPresetHighestQuality")
            ?? throw new InvalidOperationException("The iPhone cannot export this recording.");

        exporter.OutputUrl = NSUrl.FromFilename(outputPath);
        exporter.OutputFileType = AVFileTypes.Mpeg4.GetConstant();
        exporter.ShouldOptimizeForNetworkUse = true;
        exporter.VideoComposition = videoComposition;

        progress?.Report(0.05);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() =>
        {
            exporter.CancelExport();
            completion.TrySetCanceled(token);
        });

        exporter.ExportAsynchronously(() => completion.TrySetResult());
        while (!completion.Task.IsCompleted)
        {
            await Task.WhenAny(completion.Task, Task.Delay(150, token)).ConfigureAwait(false);
            progress?.Report(Math.Clamp(exporter.Progress, 0.05f, 0.98f));
        }
        await completion.Task.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        if (exporter.Status != AVAssetExportSessionStatus.Completed)
            throw new InvalidOperationException(
                exporter.Error?.LocalizedDescription ?? "The iPhone video export failed.");

        progress?.Report(1.0);

    }

    private static OverlayLayerSet CreateOverlayLayers(
        CGSize renderSize,
        double durationSeconds,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style)
    {
        var width = Math.Max(1, renderSize.Width);
        var height = Math.Max(1, renderSize.Height);
        var unit = Math.Min(width, height);
        var margin = unit * 0.045;

        var parent = new CALayer { Frame = new CGRect(0, 0, width, height) };
        var video = new CALayer { Frame = parent.Bounds };
        var overlay = new CALayer { Frame = parent.Bounds };
        parent.AddSublayer(video);
        parent.AddSublayer(overlay);

        var barHeight = unit * 0.14;
        var bar = new CALayer
        {
            Frame = new CGRect(margin, margin, width - margin * 2, barHeight),
            BackgroundColor = ParseColor(style.PanelColor),
            CornerRadius = (nfloat)(unit * 0.035)
        };
        overlay.AddSublayer(bar);

        var columnWidth = bar.Bounds.Width / PerformanceSplitTracker.Targets.Length;
        var splitSlots = new List<TextSlot>();
        for (var i = 0; i < PerformanceSplitTracker.Targets.Length; i++)
        {
            if (i > 0)
            {
                var divider = new CALayer
                {
                    Frame = new CGRect(columnWidth * i, barHeight * 0.22, Math.Max(1, unit * 0.002), barHeight * 0.56),
                    BackgroundColor = ParseColor(style.BorderColor)
                };
                bar.AddSublayer(divider);
            }

            var title = TextLayer(
                $"0–{PerformanceSplitTracker.Targets[i]}",
                new CGRect(columnWidth * i, barHeight * 0.53, columnWidth, barHeight * 0.32),
                unit * 0.03,
                style.SubTextColor,
                false);
            bar.AddSublayer(title);

            splitSlots.Add(new TextSlot(
                bar,
                new CGRect(columnWidth * i, barHeight * 0.12, columnWidth, barHeight * 0.42),
                unit * 0.048,
                style.AccentColor,
                true,
                CATextLayerAlignmentMode.Center));
        }

        var speedPanelWidth = unit * 0.39;
        var speedPanelHeight = unit * 0.22;
        var speedPanel = new CALayer
        {
            Frame = new CGRect(
                margin + unit * 0.025,
                margin + barHeight + unit * 0.035,
                speedPanelWidth,
                speedPanelHeight),
            BackgroundColor = ParseColor(style.PanelColor),
            CornerRadius = (nfloat)(unit * 0.04)
        };
        overlay.AddSublayer(speedPanel);

        var speedSlot = new TextSlot(
            speedPanel,
            new CGRect(unit * 0.025, unit * 0.015, speedPanelWidth * 0.66, speedPanelHeight * 0.86),
            unit * 0.17,
            style.TextColor,
            true,
            CATextLayerAlignmentMode.Left);

        speedPanel.AddSublayer(TextLayer(
            telemetry.SpeedUnit,
            new CGRect(speedPanelWidth * 0.67, unit * 0.035, speedPanelWidth * 0.30, speedPanelHeight * 0.42),
            unit * 0.045,
            style.SubTextColor,
            false,
            CATextLayerAlignmentMode.Left));

        var frames = telemetry.Snapshot();
        AddTimedText(
            speedSlot,
            frames,
            durationSeconds,
            frame => frame.Speed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—");

        for (var i = 0; i < PerformanceSplitTracker.Targets.Length; i++)
        {
            var splitIndex = i;
            AddTimedText(
                splitSlots[i],
                frames,
                durationSeconds,
                frame => splitIndex < frame.Splits.Length && frame.Splits[splitIndex] is { } seconds
                    ? $"{seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {telemetry.SecondsUnit}"
                    : "—");
        }

        var tool = AVVideoCompositionCoreAnimationTool.FromLayer(video, parent);
        return new OverlayLayerSet(parent, video, overlay, tool);
    }

    /// <summary>Value of AVFoundation's AVCoreAnimationBeginTimeAtZero constant.</summary>
    private const double AVCoreAnimationBeginTimeAtZero = 1e-100;

    private static CATextLayer TextLayer(
        string text,
        CGRect frame,
        double fontSize,
        string color,
        bool bold,
        CATextLayerAlignmentMode alignment = CATextLayerAlignmentMode.Center)
    {
        var layer = new CATextLayer
        {
            Frame = frame,
            String = text,
            FontSize = (nfloat)fontSize,
            ForegroundColor = ParseColor(color),
            TextAlignmentMode = alignment,
            ContentsScale = 2,
            Wrapped = false
        };
        layer.SetFont(bold ? "Helvetica-Bold" : "Helvetica");
        return layer;
    }

    /// <summary>Where one changing value (speed or a split) is drawn.</summary>
    private sealed record TextSlot(
        CALayer Parent,
        CGRect Frame,
        double FontSize,
        string Color,
        bool Bold,
        CATextLayerAlignmentMode Alignment);

    /// <summary>
    /// Shows the value that was live at each moment of the video.
    ///
    /// CATextLayer.string is not animatable, so a key-frame animation on
    /// "string" is ignored by AVFoundation's export and the saved video keeps
    /// the first value (the "frozen speed" bug). Instead every distinct text
    /// gets its own layer, and only its opacity (which is animatable) is
    /// switched on for the time ranges in which that text was live.
    /// </summary>
    private static void AddTimedText(
        TextSlot slot,
        PerformanceOverlayFrame[] sourceFrames,
        double durationSeconds,
        Func<PerformanceOverlayFrame, string> valueSelector)
    {
        var frames = sourceFrames.Length == 0
            ? new[] { new PerformanceOverlayFrame(0, null, new double?[PerformanceSplitTracker.Targets.Length]) }
            : sourceFrames;

        // Consecutive (start time, text) segments on a 0..1 timeline.
        var segments = new List<(double Start, string Text)>();
        var durationMs = Math.Max(1d, durationSeconds * 1000d);
        foreach (var frame in frames)
        {
            var start = segments.Count == 0 ? 0d : Math.Clamp(frame.TimeMs / durationMs, 0d, 1d);
            var text = valueSelector(frame);
            if (segments.Count > 0 && string.Equals(segments[^1].Text, text, StringComparison.Ordinal))
                continue;
            if (segments.Count > 0 && start >= 1d)
                break;
            if (segments.Count > 0 && start <= segments[^1].Start)
            {
                // Same instant: the later sample wins.
                segments[^1] = (segments[^1].Start, text);
                if (segments.Count > 1 && string.Equals(segments[^2].Text, text, StringComparison.Ordinal))
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add((start, text));
        }

        // Only one value for the whole video: a plain static layer.
        if (segments.Count == 1)
        {
            slot.Parent.AddSublayer(TextLayer(segments[0].Text, slot.Frame, slot.FontSize, slot.Color, slot.Bold, slot.Alignment));
            return;
        }

        // Visible ranges per distinct text.
        var ranges = new Dictionary<string, List<(double Start, double End)>>(StringComparer.Ordinal);
        for (var i = 0; i < segments.Count; i++)
        {
            var end = i + 1 < segments.Count ? segments[i + 1].Start : 1d;
            if (end <= segments[i].Start)
                continue;
            if (!ranges.TryGetValue(segments[i].Text, out var list))
                ranges[segments[i].Text] = list = new List<(double, double)>();
            list.Add((segments[i].Start, end));
        }

        foreach (var (text, list) in ranges)
        {
            // Discrete key frames: N opacity values need N + 1 key times (0 … 1).
            var values = new List<float>();
            var times = new List<double>();
            void Push(double time, float opacity)
            {
                if (values.Count > 0 && values[^1] == opacity)
                    return;
                if (times.Count > 0 && time <= times[^1])
                {
                    values[^1] = opacity;
                    if (values.Count > 1 && values[^2] == opacity)
                    {
                        values.RemoveAt(values.Count - 1);
                        times.RemoveAt(times.Count - 1);
                    }
                    return;
                }
                values.Add(opacity);
                times.Add(time);
            }

            Push(0, 0f);
            foreach (var (start, end) in list)
            {
                Push(start, 1f);
                Push(end, 0f);
            }
            times.Add(1d);

            var layer = TextLayer(text, slot.Frame, slot.FontSize, slot.Color, slot.Bold, slot.Alignment);
            // Model value = what is visible at the first video frame.
            layer.Opacity = values[0];
            slot.Parent.AddSublayer(layer);

            if (values.Count == 1)
                continue;

            var nsValues = values.Select(value => (NSObject)NSNumber.FromFloat(value)).ToArray();
            var nsTimes = times.Select(time => NSNumber.FromDouble(Math.Clamp(time, 0d, 1d))).ToArray();

            var animation = CAKeyFrameAnimation.FromKeyPath("opacity");
            animation.SetValues(nsValues);
            animation.KeyTimes = nsTimes;
            animation.CalculationMode = CAAnimation.AnimationDiscrete;
            // In a video export, BeginTime 0 means "now" (CACurrentMediaTime);
            // AVCoreAnimationBeginTimeAtZero (1e-100) anchors it to the video start.
            animation.BeginTime = AVCoreAnimationBeginTimeAtZero;
            animation.Duration = durationSeconds;
            animation.RemovedOnCompletion = false;
            animation.FillMode = CAFillMode.Both;
            layer.AddAnimation(animation, "gcar360-telemetry");

            foreach (var value in nsValues)
                value.Dispose();
            foreach (var time in nsTimes)
                time.Dispose();
            animation.Dispose();
        }
    }

    private static CGColor ParseColor(string hex)
    {
        var value = hex.TrimStart('#');
        byte a = 255;
        var offset = 0;
        if (value.Length == 8)
        {
            a = Convert.ToByte(value[..2], 16);
            offset = 2;
        }
        if (value.Length - offset != 6)
            return UIColor.Black.CGColor;
        return UIColor.FromRGBA(
            Convert.ToByte(value.Substring(offset, 2), 16),
            Convert.ToByte(value.Substring(offset + 2, 2), 16),
            Convert.ToByte(value.Substring(offset + 4, 2), 16),
            a).CGColor;
    }

    private sealed class OverlayLayerSet : IDisposable
    {
        public OverlayLayerSet(
            CALayer parent,
            CALayer video,
            CALayer overlay,
            AVVideoCompositionCoreAnimationTool animationTool)
        {
            Parent = parent;
            Video = video;
            Overlay = overlay;
            AnimationTool = animationTool;
        }

        public CALayer Parent { get; }
        public CALayer Video { get; }
        public CALayer Overlay { get; }
        public AVVideoCompositionCoreAnimationTool AnimationTool { get; }

        public void Dispose()
        {
            AnimationTool.Dispose();
            Overlay.Dispose();
            Video.Dispose();
            Parent.Dispose();
        }
    }

    public async Task<bool> EnsureSavePermissionAsync()
    {
        var status = PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.AddOnly);
        if (status is PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited)
            return true;
        if (status is PHAuthorizationStatus.Denied or PHAuthorizationStatus.Restricted)
            return false;

        var completion = new TaskCompletionSource<PHAuthorizationStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PHPhotoLibrary.RequestAuthorization(
            PHAccessLevel.AddOnly,
            result => completion.TrySetResult(result));
        status = await completion.Task.ConfigureAwait(false);
        return status is PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited;
    }

    public async Task SaveToGalleryAsync(
        string filePath,
        string displayName,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await EnsureSavePermissionAsync().ConfigureAwait(false))
            throw new UnauthorizedAccessException("Permission to save video to Photos was denied.");

        var completion = new TaskCompletionSource<NSError?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => PHAssetChangeRequest.FromVideo(NSUrl.FromFilename(filePath)),
            (success, error) => completion.TrySetResult(success ? null : error));

        var saveError = await completion.Task.ConfigureAwait(false);
        if (saveError is not null)
            throw new InvalidOperationException(saveError.LocalizedDescription);
    }
}
#endif
